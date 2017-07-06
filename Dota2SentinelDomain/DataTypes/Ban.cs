using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.DataTypes {
    public class Ban {
        [Key]
        public int Id { get; set; }
        public int Type { get; set; }
        public int Duration { get; set; }
        public DateTime Set { get; set; }
        public DateTime Expires { get; set; }
        public string Reason { get; set; }
        public float Severity { get; set; }
        public Account Account { get; set; }
        public Match Match { get; set; }
    }
}
