using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.DataTypes {
    public class Match {
        [Key]
        public int Id { get; set; }
        [StringLength(20)]
        public string MatchId { get; set; } //Rename to match ID
        [StringLength(30)]
        public string CustomMapName { get; set; }
        public Account RequestedBy { get; set; }
        public DateTime Registered { get; set; }
        public DateTime Closed { get; set; }
        public List<AccountMatch> Players { get; set; }
        public List<Ban> Bans { get; set; }
    }
}
