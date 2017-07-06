using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.DataTypes {
    public class AccountMatch {
        [Key]
        public int Id { get; set; }
        public int AccountId { get; set; }
        public Account Account { get; set; }
        public int MatchId { get; set; }
        public Match Match { get; set; }
    }
}
