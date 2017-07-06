using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.DataTypes {
    public class OngoingMatch {
        [Key]
        public int Id { get; set; }
        [StringLength(20)]
        public string LobbyId { get; set; }
        [StringLength(30)]
        public string CustomMapName { get; set; }
        [StringLength(20)]
        public string RequestedBy { get; set; }
        public DateTime Started { get; set; }
        public DateTime LastCheck { get; set; }
        public List<Player> Players { get; set; }
    }
}
