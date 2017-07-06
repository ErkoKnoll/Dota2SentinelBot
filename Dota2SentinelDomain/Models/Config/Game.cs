using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dota2SentinelDomain.Models.Config {
    public class Game {
        public string Name { get; set; }
        public string Chat { get; set; }
        public uint GameMode { get; set; }
        public ulong CustomGameMode { get; set; }
        public ulong CustomGameCrc { get; set; }
        public uint CustomGameTimestamp { get; set; }
        public uint Teams { get; set; }
        public uint ServerId { get; set; }
        public uint LobbyTimeout { get; set; }
        public int BanDurationLeave { get; set; }
        public bool VariableLeaveBan { get; set; }
        public float VariableLeaveBanThreshold { get; set; }
        public uint PlayerMinGamesRequired { get; set; }
        public string LobbyInfoMessage { get; set; }
        public string NewAccountKickMessage { get; set; }
        public string BannedAccountKickMessage { get; set; }
        public string LobbyTimeoutMessage { get; set; }
        public string BotLeaveMessage { get; set; }
        public string AccountStatsMessage { get; set; }
        public string HelpMessage { get; set; }
        public string CommandNotAllowedMessage { get; set; }
        public ulong RequestedBy { get; set; }
        public List<Map> Maps { get; set; }
    }
}
