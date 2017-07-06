using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Config {
    public class Credentials {
        public string UserName { get; set; }
        public string Password { get; set; }
        public ulong MagicNumber { get; set; }
    }
}
