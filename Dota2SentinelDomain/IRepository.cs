using Dota2SentinelDomain.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain {
    public interface IRepository : IDisposable {
        IQueryable<Account> Accounts { get; }
        IQueryable<AccountName> AccountNames { get; }
        IQueryable<OngoingMatch> OngoingMatches { get; }
        IQueryable<PlayerMatch> PlayerMatch { get; }
        IQueryable<Match> Matches { get; }
        IQueryable<Ban> Bans { get; }

        void AddAccount(Account account);
        void AddAccountName(AccountName accountName);
        void AddOngoingMatch(OngoingMatch ongoingMatch);
        void AddMatch(Match match);
        void AddBan(Ban ban);

        void DeleteOngoingMatch(OngoingMatch ongoingMatch);
        void DeletePlayer(Player player);
        void DeletePlayerMatch(PlayerMatch playerMatch);

        void SaveChanges();
    }
}
