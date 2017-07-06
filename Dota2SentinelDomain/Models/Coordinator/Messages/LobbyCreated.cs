using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Coordinator.Messages {
    public class LobbyCreated {
        public string CustomMapName { get; set; }
        public uint SeverId { get; set; }
        public ulong LobbyId { get; set; }
    }
}
