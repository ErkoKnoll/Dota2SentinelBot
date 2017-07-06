using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Config {
    public class Config {
        public string ConnectionString { get; set; }
        public string PrivateChat { get; set; }
        public Credentials CoordinatorBot { get; set; }
        public string CoordinatorUrl { get; set; }
        public string LobbyBotPath { get; set; }
        public uint MatchCheckMinDuration { get; set; }
        public uint MatchCheckMaxDuration { get; set; }
        public List<Credentials> LobbyBotsPool { get; set; }
        public List<Game> Games { get; set; }
        public List<ServerRegion> ServerRegions { get; set; }
        public List<ulong> Admins { get; set; }
        public string BusyMessage { get; set; }
        public string LobbyAlreadyQueued { get; set; }
        public string LobbyAlreadyHosted { get; set; }
        public string LobbyRequesterBanned { get; set; }
        public string LobbyRequesterHasHost { get; set; }
        public string HostingNoLobbiesMessage { get; set; }
        public string HostingLobby { get; set; }
        public string LobbyQueuedMessage { get; set; }
        public string NoAccountMessage { get; set; }
        public string AccountStatsMessage { get; set; }
        public string AccountBannedMessage { get; set; }
        public string NewAccountMessage { get; set; }
        public string HelpMessage { get; set; }
        public string LobbiesCommandNotAllowed { get; set; }
        public string MapNotFoundMessage { get; set; }
        public string SpecifyMapMessage { get; set; }
    }
}
