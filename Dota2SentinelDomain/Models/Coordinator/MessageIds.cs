using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Coordinator {
    public static class MessageIds {
        public static class Out {
            public const uint CREATE_LOBBY = 0;
        }

        public static class In {
            public const uint LOBBY_CREATED = 0;
            public const uint LOBBY_LEFT = 1;
        }
    }
}
