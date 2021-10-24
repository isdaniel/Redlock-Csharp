using System;
using System.Threading;
using StackExchange.Redis;

namespace Redlock_Csharp
{
    public class RedLock
    {
        private IConnectionMultiplexer _connection;
        const string UNLOCK_SCRIPT = @"
            if redis.call(""get"",KEYS[1]) == ARGV[1] then
                return redis.call(""del"",KEYS[1])
            else
                return 0
            end";

        public RedLock(IConnectionMultiplexer connection)
        {
            _connection = connection;
        }
        private string GetUniqueId(){            
            return $"{Guid.NewGuid().ToString()}-Tid:{Thread.CurrentThread.ManagedThreadId}";
        }
        public bool LockInstance(string resource, TimeSpan ttl, out LockObject lockObject)
        {   
            bool result;
            string keyId = GetUniqueId();
            lockObject = new LockObject(resource,keyId,ttl);
            try
            {
                
                do
                {
                    result = _connection.GetDatabase().StringSet(resource, keyId, ttl, When.NotExists);
                    
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
                Thread.Sleep(waitTime.Value.Milliseconds);
            }
        }

        public void UnlockInstance(string resource, byte[] val)
        {
            RedisKey[] keys = { resource };
            RedisValue[] values = { val };
            _connection.GetDatabase().ScriptEvaluate(
                UNLOCK_SCRIPT,
                keys,
                values
                );
        }
    }
}
