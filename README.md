# Redlock-Csharp

我之前有寫透過 [lock or CAS](https://isdaniel.github.io/high-concurrency-atomic-cas-algorithm/) 來防治，Racing condition 問題，但如果這個問題延深到多台服務器甚至是 micor-services 架構我們要怎麼處理資料問題呢?

下面程式在單體服務或應用程式不會出問題，但如果服務器有多台問題可就大了，因為下面的 lock 只限於單體 Server 上

```c#
private readonly static object _lock = new object();
[HttpGet()]
public string Get()
{
    int remainCount;
    lock (_lock)
    {
        
        remainCount = (int)RedisConnection.GetDatabase().StringGet(_productId);
        if (remainCount > 0)
        {
            RedisConnection.GetDatabase().StringSet(_productId, --remainCount);
        }
    }

    string productMsg = remainCount <= 0 ? "沒貨了賣完了" : $"還剩下 {remainCount} 商品";
    string result = $"{Dns.GetHostName()} 處理貨物狀態!! {productMsg}";
    _logger.LogInformation(result);
    return result;
}
```

如果有聽過 Redis 可能就會聽過 [RedLock.net](https://github.com/samcook/RedLock.net) 來處理，但您知道 RedLock.net 底層大致上怎麼實作的嗎?

本篇文章會帶領大家透過 distlock 算法時做出，自己的Redlock

> 本篇只介紹核心概念，細部防錯我沒有寫出來，所以建議本篇程式不要用在 prod 環境

我使用 docker-compose 建立問題架構，架構圖如下

![](https://i.imgur.com/nt8Q9Sv.png)

## How to Run

在根目錄使用 `docker-compose up -d` 跑起來後應該會有下面五個組件

![](https://i.imgur.com/7Zyji6N.png)

後面我們進入 Redis Server 利用命令建立一個商品和數量 `set pid:1 1000`

> `pid=1` 有 1000 個

```bash
$ docker exec -it b489eb20ab74 bash
root@b489eb20ab74:/data# redis-cli
127.0.0.1:6379> set pid:1 1000
OK
```

在查詢 `http://localhost:8080/WebStore` 可以獲得下圖代表組件建立完畢

![](https://i.imgur.com/nqxIxwD.png)

## Redlock 演算法

其實 [distlock](https://redis.io/topics/distlock) 算法說明在 Redis 官網有一篇專門來說明，

測試檔案已經準備好了 `/test/shopping.jmx`，我們使用 jmeter 來壓測，使用壓力測試程式，建議 Jmeter 使用 Thread 最好小於或等於 cpu core count (最好設定2的倍數)

ex: 我的電腦 8 core 我可以設定 8 thread concurrent

這邊有一個情境假設，有一個商品秒殺只有 200 個商品

這邊我們要怎麼防止賣超呢?

我們把 `lock` 部分替換成我們自己寫的 `RedLock` 物件．我有透過 Redis 算法實現一個簡單的分佈式鎖

```c#
[HttpGet()]
public string Get()
{
    RedLock redlock = new RedLock(RedisConnection);
    int remainCount;
    try
    {
        redlock.LockInstance("redkey", new TimeSpan(00, 00, 10), out var lockObject);
        remainCount = (int)RedisConnection.GetDatabase().StringGet(_productId);
        if (remainCount > 0)
        {
            RedisConnection.GetDatabase().StringSet(_productId, --remainCount);
        }

    }
    finally
    {
        redlock.UnlockInstance("redkey");
    }
    string productMsg = remainCount <= 0 ? "沒貨了賣完了" : $"還剩下 {remainCount} 商品";
    string result = $"{Dns.GetHostName()} 處理貨物狀態!! {productMsg}";
    _logger.LogInformation(result);
    return result;
}
```

透過 jmeter 壓力測試

```bash
$ .\jmeter -n -t .\shopping.jmx -l .\shopping.jtl
Creating summariser <summary>
Starting standalone 
Waiting for possible Shutdown/StopTestNow/HeapDump/ThreadDump message on port 4445
summary +      1 in 00:00:01 =    1.0/s Avg:    91 Min:    91 Max:    91 Err:     0 (0.00%) Active: 15 Started: 15 Finished: 0
summary +   1599 in 00:00:12 =  137.1/s Avg:    77 Min:     3 Max:  8921 Err:     0 (0.00%) Active: 0 Started: 16 Finished: 16
summary =   1600 in 00:00:13 =  126.8/s Avg:    77 Min:     3 Max:  8921 Err:     0 (0.00%)
Tidying up ...    @ Mon Oct 25 11:37:15 CST 2021 (1635133035562)
... end of run
```

結果如下：

在多台 server 上並沒有出現賣超

![](https://i.imgur.com/SGdamvC.png)

### RedLock 解說程式

裡面最核心程式是 `RedLock` 類別．建構子會傳入我們使用 `Redis` Connection

其中核心方式是

* LockInstance
* UnlockInstance

#### LockInstance

在 Lock 需要注意 Atomic 並且需要給一個 TTL 不然假如機器突然當機或跳電，會造成鎖不會解放其他人會無限期 blocking

下面這段話是來是官網

> since this is actually a viable solution in applications where a race condition from time to time is acceptable, and because locking into a single instance is the foundation we’ll use for the distributed algorithm described here.

```bash
SET resource_name my_random_value NX PX 30000
```

> NX -- Only set the key if it does not already exist.

Redis 操作命令是一個 single Thread 執行所以命令如果可以包在一包或一條來執行就可以保證 Atomic，[SET](https://redis.io/commands/set) `NX` 命令**不存在** key 建立數值 (具有 Atomic )

另外我為了避免一直在空轉，我會判斷假如目前有人佔有鎖我會自旋等待一個時間( lock TTL )，到了後在嘗試訪問（可以有更優的算法但我懶得寫了）避免浪費資源空轉

```C#
public class RedLock
{
    private IConnectionMultiplexer _connection;
    private string _keyId;

    public RedLock(IConnectionMultiplexer connection)
    {
        _connection = connection;
        _keyId = $"{Guid.NewGuid().ToString()}-Tid:{Thread.CurrentThread.ManagedThreadId}";
    }

    public bool LockInstance(string resource, TimeSpan ttl, out LockObject lockObject)
    {   
        bool result;
        lockObject = new LockObject(resource, _keyId, ttl);
        try
        {
        
            do
            {
            result = _connection.GetDatabase().StringSet(resource, _keyId, ttl, When.NotExists);
                
                if(!result)
                    WaitForLock(resource);

            } while (!result);
        }
        catch (Exception)
        {
            result = false;
        }

        return result;
    }

    private void WaitForLock(string resource)
    {
        var waitTime = _connection.GetDatabase().KeyTimeToLive(resource);

        if(waitTime.HasValue){
            SpinWait.SpinUntil(()=>true,waitTime.Value.Milliseconds);
        }
    }
}
```

#### UnlockInstance

我們必須讓 **查詢** 跟 **刪除** keyid 有 Atomic 所以有兩種做法

1. 透過 Lua 腳本讓命令連續
2. 使用 Redis transaction 模式

本次程式解鎖程式是靠 Lua 腳本來完成( Redis 官網推薦)

下面是官網的說明

> This is important in order to avoid removing a lock that was created by another client. For example a client may acquire the lock, get blocked in some operation for longer than the lock validity time (the time at which the key will expire), and later remove the lock, that was already acquired by some other client. Using just DEL is not safe as a client may remove the lock of another client. With the above script instead every lock is “signed” with a random string, so the lock will be removed only if it is still the one that was set by the client trying to remove it.

建議在 Keyid 那邊可以標示是由你產生的鎖避免刪除到別人 lock，所以我的程式 keyid 使用 GUID + ThreadId 來保證不會有人跟我產生一樣的 keyId

```c#
public class RedLock
{
    private IConnectionMultiplexer _connection;
    private string _keyId;
    const string UNLOCK_SCRIPT = @"
        if redis.call(""get"",KEYS[1]) == ARGV[1] then
            return redis.call(""del"",KEYS[1])
        else
            return 0
        end";

    public RedLock(IConnectionMultiplexer connection)
    {
        _connection = connection;
        _keyId = $"{Guid.NewGuid().ToString()}-Tid:{Thread.CurrentThread.ManagedThreadId}";
    }

    public void UnlockInstance(string resource)
    {
        RedisKey[] keys = { resource };
        RedisValue[] values = { _keyId };
        _connection.GetDatabase().ScriptEvaluate(
            UNLOCK_SCRIPT,
            keys,
            values
            );
    }
}
```

![](https://i.imgur.com/3w0cD8l.png)

## 小結

本次跟大家介紹 Redlock 算法帶著大家快速走過一遍，能發現實現 lock 算法其實不會很難，這邊留一個地方讓大家考慮一下

之前我有篇文章討論 [c# lock](https://isdaniel.github.io/lock-deepknow/) 原理，裡面有討論可重入鎖模式，假如給你實現可重入鎖你會實現嗎？

在實現的過程中你會發現原來 lock 核心是算法而不是實作，實作可以由許多方式來處理但算法概念不會變

本程式不建議在 Prod 上使用，因為我沒有實現多台 master Redis 同步 lock、鎖 TTL 到了 logic 還在執行需要延長等等問題...
