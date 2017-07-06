using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.DataTypes {
    public class PlayerMatch {
        [Key]
        public int Id { get; set; }
        [StringLength(20)]
        public string MatchId { get; set; }
        public Player Player { get; set; }
    }
}
