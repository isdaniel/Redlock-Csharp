using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace shopping_web.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WebStoreController : ControllerBase
    {
        private readonly ILogger<WebStoreController> _logger;

        public WebStoreController(ILogger<WebStoreController> logger)
        {
            _logger = logger;
        }

        [HttpGet()]
        public string Get()
        {
            return $"Hello { System.Net.Dns.GetHostName()}";
        }
    }
}
