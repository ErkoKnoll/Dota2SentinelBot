using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Coordinator {
    public class Message {
        public uint Id { get; set; }
        public string Payload { get; set; }
    }
}
