using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.ComponentModel.DataAnnotations;

namespace Dota2SentinelDomain.DataTypes {
    public class Account {
        [Key]
        public int Id { get; set; }
        [StringLength(20)]
        public string AccountId { get; set; }
        public bool NewUser { get; set; }
        public List<AccountName> AccountNames { get; set; }
        public List<AccountMatch> Matches { get; set; }
        public List<Ban> Bans { get; set; } 
        public List<Match> RequestedMatches { get; set; }
    }
}
