using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Coordinator {
    public class LobbyState {
        public string CustomMapName { get; set; }
        public uint ServerId { get; set; }
        public ulong LobbyId { get; set; }
        public ulong RequesterAccountId { get; set; }
        public DateTime Created { get; set; }
    }
}
