using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.DataTypes {
    public class Player {
        [Key]
        public int Id { get; set; }
        [StringLength(20)]
        public string AccountId { get; set; }
        [StringLength(200)]
        public string Name { get; set; }
        [NotMapped]
        public bool Active { get; set; }
        [NotMapped]
        public bool Kicked { get; set; }
        public OngoingMatch Match { get; set; }
        public List<PlayerMatch> PlayerMatches { get; set; }
    }
}
