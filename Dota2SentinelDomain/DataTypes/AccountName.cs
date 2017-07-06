using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.DataTypes {
    public class AccountName {
        [Key]
        public int Id { get; set; }
        [StringLength(200)]
        public string Name { get; set; }
        public Account Account { get; set; }
    }
}
