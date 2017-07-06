using Dota2SentinelDomain.Models.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Coordinator.Messages {
    public class LobbyHost {
        public Game Game { get; set; }
        public Map Map { get; set; }
    }
}
