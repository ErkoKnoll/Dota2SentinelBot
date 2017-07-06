using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Config {
    public class ServerRegion {
        public string LongName { get; set; }
        public string ShortName { get; set; }
        public uint ServerId { get; set; }
    }
}
