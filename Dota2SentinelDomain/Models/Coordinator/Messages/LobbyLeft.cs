using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Coordinator.Messages {
    public class LobbyLeft {
        public string CustomMapName { get; set; }
        public uint? ServerId { get; set; }
    }
}
