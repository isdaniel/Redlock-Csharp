using System;
using StackExchange.Redis;

namespace Redlock_Csharp
{
    public class LockObject
    {
        public LockObject(RedisKey resource, RedisValue keyId, TimeSpan validity)
        {
            Resource = resource;
            KeyId = keyId ;
            ValidityTime = validity;
        }
        public RedisKey Resource { get;private set; }

        public RedisValue KeyId { get;private set; }

        public TimeSpan ValidityTime { get;private set; }
    }
}
