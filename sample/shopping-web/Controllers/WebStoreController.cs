using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Redlock_Csharp;
using StackExchange.Redis;

namespace shopping_web.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WebStoreController : ControllerBase
    {
        private readonly ILogger<WebStoreController> _logger;
        private string _productId = "pid:1";
        public WebStoreController(ILogger<WebStoreController> logger)
        {
            _logger = logger;
        }

        public static ConnectionMultiplexer RedisConnection
        {
            get
            {
                return lazyConnection.Value;
            }
        }

        private static Lazy<ConnectionMultiplexer> lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
        {
            var config = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                ConnectRetry = 10,
                ReconnectRetryPolicy = new ExponentialRetry(5000)
            };
            config.EndPoints.Add("redis:6379");
            return ConnectionMultiplexer.Connect(config);
        });

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
    }
}
