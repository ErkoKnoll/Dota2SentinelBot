using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Config {
    public class Map {
        public string Name { get; set; }
        public string MapId { get; set; }
        public string ChatId { get; set; }
        public uint MinPlayers { get; set; }
        public uint MaxPlayers { get; set; }
        public bool Solo { get; set; }
    }
}
