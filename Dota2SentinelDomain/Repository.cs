using Dota2SentinelDomain.DataTypes;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain {
    public class Repository : DbContext, IRepository {
        public Repository(DbContextOptions<Repository> options) : base(options) {
        }

        public DbSet<Account> Accounts { get; set; }
        public DbSet<AccountName> AccountNames { get; set; }
        public DbSet<OngoingMatch> OngoingMatches { get; set; }
        public DbSet<Player> Players { get; set; }
        public DbSet<PlayerMatch> PlayerMatches { get; set; } 
        public DbSet<Match> Matches { get; set; }
        public DbSet<Ban> Bans { get; set; }
        public DbSet<AccountMatch> AccountMatches { get; set; }

        IQueryable<Account> IRepository.Accounts
        {
            get { return Accounts; }
        }

        IQueryable<OngoingMatch> IRepository.OngoingMatches
        {
            get { return OngoingMatches; }
        }

        IQueryable<PlayerMatch> IRepository.PlayerMatch
        {
            get { return PlayerMatches; }
        }

        IQueryable<AccountName> IRepository.AccountNames
        {
            get { return AccountNames; }
        }

        IQueryable<Match> IRepository.Matches
        {
            get { return Matches; }
        }

        IQueryable<Ban> IRepository.Bans
        {
            get { return Bans; }
        }

        public void AddAccount(Account account) {
            Accounts.Add(account);
        }

        public void AddAccountName(AccountName accountName) {
            AccountNames.Add(accountName);
        }

        public void AddOngoingMatch(OngoingMatch ongoingMatch) {
            OngoingMatches.Add(ongoingMatch);
        }

        public void AddMatch(Match match) {
            Matches.Add(match);
        }

        public void AddBan(Ban ban) {
            Bans.Add(ban);
        }

        public void DeleteOngoingMatch(OngoingMatch ongoingMatch) {
            OngoingMatches.Remove(ongoingMatch);
        }

        public void DeletePlayer(Player player) {
            Players.Remove(player);
        }

        public void DeletePlayerMatch(PlayerMatch playerMatch) {
            PlayerMatches.Remove(playerMatch);
        }

        void IRepository.SaveChanges() {
            base.SaveChanges();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            //EF Core does not currently support automatic many-to-many mappings so intermediate table has to be created manually.
            modelBuilder.Entity<AccountMatch>().HasOne(am => am.Account).WithMany(a => a.Matches).HasForeignKey(am => am.AccountId);
            modelBuilder.Entity<AccountMatch>().HasOne(am => am.Match).WithMany(m => m.Players).HasForeignKey(am => am.MatchId);
        }
    }
}
