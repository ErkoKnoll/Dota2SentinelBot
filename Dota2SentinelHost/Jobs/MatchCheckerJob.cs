using Dota2SentinelDomain;
using Dota2SentinelDomain.DataTypes;
using Dota2SentinelDomain.Helpers;
using log4net;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dota2SentinelHost.Jobs {
    public class MatchCheckerJob {
        private ILog _logger = LogManager.GetLogger(typeof(MatchCheckerJob));
        private Timer _matchChecker;
        private Timer _matchHistoryRequester;
        private DbContextOptions<Repository> _dbOptions;
        private bool _checkingMatch = false;
        private bool _finalizingMatch = false;
        private DateTime? _lastCheckTime = null;
        private DateTime? _lastHistoryRequest = null;
        private ulong _currentHistoryRequest = 0;
        private ulong _matchId = 0;
        private IDictionary<string, List<string>> _playerMatches;
        private OngoingMatch _match;

        public MatchCheckerJob() {
            _dbOptions = new DbContextOptionsBuilder<Repository>().UseNpgsql(Startup.Config.ConnectionString).Options;
            StartMatchHistoryRequester();
        }

        public void StartJob() {
            _matchChecker = new Timer((x) => {
                try {
                    if (!_checkingMatch) {
                        GetMatchToCheck();
                    } else if (_lastCheckTime.HasValue && _lastCheckTime.Value.AddMinutes(2) < DateTime.UtcNow) {
                        StopMatchChecking();
                    }
                } catch (Exception e) {
                    _logger.Error("Error while running match checker job", e);
                    //_checkingMatch = false;
                }
            }, null, 0, 1000);
        }

        private void StartMatchHistoryRequester() {
            _matchHistoryRequester = new Timer((x) => {
                try {
                    if (_checkingMatch == true && _playerMatches != null && (_currentHistoryRequest == 0 || (_lastHistoryRequest.HasValue && DateTime.UtcNow > _lastHistoryRequest.Value.AddSeconds(5)))) {
                        bool playerFound = false;
                        foreach (var playerMatch in _playerMatches) {
                            if (playerMatch.Value == null) {
                                //_logger.Info("Request history for - Account ID: " + playerMatch.Key);
                                _currentHistoryRequest = ulong.Parse(playerMatch.Key);
                                _lastHistoryRequest = DateTime.UtcNow;
                                Startup.Bot.DotaClient.GetPlayerMatchHistory(SteamHelper.ConvertIDToUint32(_currentHistoryRequest));
                                playerFound = true;
                                break;
                            }
                        }
                        if (!playerFound && !_finalizingMatch) {
                            _finalizingMatch = true;
                            TryFinalizeMatch();
                        }
                    }
                } catch (Exception e) {
                    _logger.Error("Error while requesting player history", e);
                }
            }, null, 0, 100);
        }

        private void GetMatchToCheck() {
            _checkingMatch = true;
            using (Repository repository = new Repository(_dbOptions)) {
                var match = repository.OngoingMatches.Include(m => m.Players).Where(m => m.Started.AddMinutes(Startup.Config.MatchCheckMinDuration) < DateTime.UtcNow && m.LastCheck.AddMinutes(1) < DateTime.UtcNow).ToList().OrderBy(m => m.LastCheck).FirstOrDefault();
                if (match != null) {
                    _match = match;
                    _lastCheckTime = DateTime.UtcNow;
                    _playerMatches = new Dictionary<string, List<string>>();
                    foreach (var player in match.Players) {
                        if (!_playerMatches.ContainsKey(player.AccountId)) {
                            _playerMatches.Add(player.AccountId, null);
                        }
                    }
                    //_logger.Info("Checking lobby: " + match?.LobbyId);
                } else {
                    _checkingMatch = false;
                }
            }
        }

        public void OnGetPlayerMatchHistoryResponse(ClientGCMsgProtobuf<CMsgDOTAGetPlayerMatchHistoryResponse> response) {
            try {
                _playerMatches[_currentHistoryRequest.ToString()] = response.Body.matches.Select(m => m.match_id.ToString()).ToList();
                //_logger.Info("Match history - Account ID: " + _currentHistoryRequest + " - Matches: " + response.Body.matches.Count);
                _currentHistoryRequest = 0;
            } catch (Exception e) {
                _logger.Error("Error occurred while processing player history response", e);
            }
        }

        public void OnMatchDetailsResponse(ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse> response) {
            try {
                //_logger.Info("Received match details - Match ID: " + response?.Body?.match?.match_id);
                if (response.Body.match.match_id != _matchId) {
                    throw new Exception(String.Format("Match ID mismatch - Original ID: {0} - Returned ID: {1}", _matchId, response?.Body?.match?.match_id));
                }
                //var map = Startup.Config.Games.Where(g => m.CustomMapName == _match.CustomMapName).SingleOrDefault();
                var game = Startup.Config.Games.Where(g => g.Maps.Any(m => m.MapId == _match.CustomMapName)).SingleOrDefault();
                var map = game.Maps.Where(m => m.MapId == _match.CustomMapName).SingleOrDefault();
                try {
                    using (IRepository repository = new Repository(_dbOptions)) {
                        bool saveRequired = false;
                        foreach (var player in response.Body.match.players) {
                            var accountId = SteamHelper.ConvertIDToUint64(player.account_id);
                            var account = repository.Accounts.Include(a => a.AccountNames).Where(a => a.AccountId == accountId.ToString()).FirstOrDefault();
                            if (account == null) {
                                account = new Account() {
                                    AccountId = accountId.ToString(),
                                    NewUser = false,
                                    AccountNames = new List<AccountName>(),
                                    Bans = new List<Ban>(),
                                    Matches = new List<AccountMatch>(),
                                };
                                repository.AddAccount(account);
                                saveRequired = true;
                            }
                            if (!String.IsNullOrEmpty(player.player_name)) {
                                if (!account.AccountNames.Any(n => n.Name == player.player_name)) {
                                    account.AccountNames.Add(new AccountName {
                                        Name = player.player_name
                                    });
                                    saveRequired = true;
                                }
                            }
                        }
                        if (saveRequired) {
                            repository.SaveChanges();
                        }
                    }
                } catch (Exception e) {
                    _logger.Error("Error while processing finished match accounts - Match ID: " + response?.Body?.match?.match_id, e);
                }
                try {
                    using (IRepository repository = new Repository(_dbOptions)) {
                        var match = new Match() {
                            CustomMapName = _match.CustomMapName,
                            Registered = _match.Started,
                            Closed = DateTime.UtcNow,
                            MatchId = response.Body.match.match_id.ToString(),
                            RequestedBy = repository.Accounts.Where(a => a.AccountId == _match.RequestedBy).FirstOrDefault(),
                            Bans = new List<Ban>(),
                            Players = new List<AccountMatch>()
                        };
                        repository.AddMatch(match);
                        var bannedUsers = new List<string>();
                        var totalBans = response.Body.match.players.Where(p => p.leaver_status != 0).Count();
                        var teamGoldSpent = new Dictionary<uint, List<uint>>();
                        foreach (var player in response.Body.match.players) {
                            var team = player.custom_game_data.dota_team;
                            if (map.Solo) {
                                team = 1;
                            }
                            List<uint> goldSpentList;
                            if (!teamGoldSpent.TryGetValue(team, out goldSpentList)) {
                                goldSpentList = new List<uint>();
                                teamGoldSpent[team] = goldSpentList;
                            }
                            goldSpentList.Add(player.gold_spent);
                        }
                        var teamGoldSpentTopAverage = new Dictionary<uint, float>();
                        foreach (var team in teamGoldSpent) {
                            var half = Convert.ToInt32(Math.Ceiling((double)(team.Value.Count / 2)));
                            var topAverageSpents = team.Value.OrderByDescending(v => v).Take(half);
                            float average = topAverageSpents.Sum(v => v) / half;
                            teamGoldSpentTopAverage[team.Key] = average;
                            _logger.Info("Team gold spent average - Team: " + team.Key + " - Average: " + average);
                        }
                        foreach (var player in response.Body.match.players) {
                            var accountId = SteamHelper.ConvertIDToUint64(player.account_id);
                            var account = repository.Accounts.Include(a => a.Bans).Where(a => a.AccountId == accountId.ToString()).SingleOrDefault();
                            match.Players.Add(new AccountMatch() {
                                Account = account,
                                Match = match
                            });
                            if (totalBans < Math.Floor(map.MaxPlayers * 0.75)) {
                                if (player.leaver_status != 0 && !Startup.Config.Admins.Contains(accountId)) {
                                    var banDuration = game.BanDurationLeave;
                                    float banSeverity = 1;
                                    float teamGoldSpentAverage = 0;
                                    var team = player.custom_game_data.dota_team;
                                    if (map.Solo) {
                                        team = 1;
                                    }
                                    if (game.VariableLeaveBan) {
                                        teamGoldSpentAverage = teamGoldSpentTopAverage[team];
                                        banSeverity = 1 - (player.gold_spent / teamGoldSpentAverage);
                                        if (banSeverity < 0) {
                                            banSeverity = 0;
                                        }
                                        if (banSeverity > 1) {
                                            throw new Exception("Ban severity calculation error");
                                        }
                                    }
                                    if (!game.VariableLeaveBan || banSeverity > game.VariableLeaveBanThreshold) {
                                        banDuration = Convert.ToInt32((float)banDuration * banSeverity);
                                        _logger.Info("Banned leaver - Name: " + player.player_name + " - Severity: " + banSeverity + " - Duration: " + banDuration + " hours - Gold spent: " + player.gold_spent + " - Team gold spent average: " + teamGoldSpentAverage + " - Team: " + team + " - ID: " + accountId);
                                        bannedUsers.Add(player.player_name);
                                        var previousBan = account.Bans.Where(b => b.Expires > DateTime.UtcNow).OrderByDescending(b => b.Expires).FirstOrDefault();
                                        repository.AddBan(new Ban() {
                                            Account = account,
                                            Match = match,
                                            Duration = banDuration,
                                            Expires = previousBan != null ? previousBan.Expires.AddHours(banDuration) : DateTime.UtcNow.AddHours(banDuration),
                                            Severity = banSeverity,
                                            Set = DateTime.UtcNow,
                                            Type = BanTypes.LEAVE,
                                            Reason = String.Format("Abandoned {0} match {1}", game.Name, response?.Body?.match?.match_id)
                                        });
                                    } else if (banSeverity <= game.VariableLeaveBanThreshold) {
                                        _logger.Info("Ban severity below threashold - Name: " + player.player_name + " - Severity: " + banSeverity + " - Gold spent: " + player.gold_spent + " - Team gold spent average: " + teamGoldSpentAverage + " - Team: " + team);
                                    }
                                }
                            }
                            if (player.feeding_detected) {
                                _logger.Info("FEEDER: " + player.player_name);
                            }
                            /*else if ((int)player.deaths - ((int)player.kills + (int)player.assists) >= map.FeedingScore) {
                                _logger.Info(String.Format("Banned Feeder - Player: {0} - KDA: ({1}/{2}/{3}) - Feeding Score: {4}", player.player_name, player.kills, player.deaths, player.assists, (int)player.deaths - ((int)player.kills + (int)player.assists)));
                                var previousBan = account.Bans.Where(b => b.Expires > DateTime.UtcNow).OrderByDescending(b => b.Expires).FirstOrDefault();
                                repository.AddBan(new Ban() {
                                    Account = account,
                                    Match = match,
                                    Duration = map.BanDurationLeave,
                                    Expires = previousBan != null ? previousBan.Expires.AddHours(map.BanDurationLeave) : DateTime.UtcNow.AddHours(map.BanDurationLeave),
                                    Set = DateTime.UtcNow,
                                    Type = BanTypes.LEAVE,
                                    Reason = "Feeding in match " + response?.Body?.match?.match_id,
                                });
                            }*/
                        }
                        DeleteOngoingMatch(repository, _match.LobbyId);
                        repository.SaveChanges();
                        _logger.Info("Finalized lobby: " + _match.LobbyId);
                        var channelId = Startup.Bot.Channels.Where(c => c.Value == Startup.Config.PrivateChat).FirstOrDefault().Key;
                        var gameName = game.Name;
                        if (game.Maps.Count > 1) {
                            gameName += " " + map.Name;
                        }
                        var message = String.Format("Finalized {0} match: {1}", gameName, _matchId);
                        if (bannedUsers.Count == 0) {
                            if (totalBans < Math.Floor(map.MaxPlayers * 0.75)) {
                                message += " - No users to ban.";
                            } else {
                                message += " - Voided due to mass leaving.";
                            }
                        } else {
                            message += " - Banned leavers (" + bannedUsers.Count + "): " + String.Join(", ", bannedUsers) + ".";
                        }
                        Startup.Bot.DotaClient.QueueChatMessage(channelId, message, false);
                    }
                } catch (Exception e) {
                    _logger.Error("Error while processing finished match details - Match ID: " + response?.Body?.match?.match_id, e);
                    PostponeLobbyCheck(_match.LobbyId);
                }
                StopMatchChecking();
            } catch (Exception e) {
                _logger.Error("Error while processing match details response - Match ID: " + response?.Body?.match?.match_id, e);
            }
        }

        private void TryFinalizeMatch() {
            try {
                //_logger.Info("Try finalizing match");
                var matchDifferences = new Dictionary<string, int>();
                foreach (var player in _playerMatches) {
                    try {
                        var matchPlayer = _match.Players.Where(p => p.AccountId == player.Key).SingleOrDefault();
                        using (IRepository repository = new Repository(_dbOptions)) {
                            //var oldMatchIds = matchPlayer.PlayerMatches.Select(m => m.MatchId);
                            var oldMatchIds = repository.PlayerMatch.Where(pm => pm.Player.AccountId == matchPlayer.AccountId).Select(pm => pm.MatchId).ToList(); //EF Core currently does not support loading eagerly multiple levels down collections so we'll load them up again here
                            var nonMatchingMatches = player.Value.Where(m => !oldMatchIds.Contains(m));
                            foreach (var match in nonMatchingMatches) {
                                if (matchDifferences.ContainsKey(match)) {
                                    matchDifferences[match]++;
                                } else {
                                    matchDifferences[match] = 1;
                                }
                            }
                        }
                    } catch (Exception e) {
                        _logger.Error("Error while checking player in match finalization - Account ID: " + player.Key, e);
                    }
                }
                //_logger.Info(JsonConvert.SerializeObject(matchDifferences.OrderByDescending(m => m.Value)));
                var game = Startup.Config.Games.Where(g => g.Maps.Any(m => m.MapId == _match.CustomMapName)).SingleOrDefault();
                var map = game.Maps.Where(m => m.MapId == _match.CustomMapName).SingleOrDefault();
                var aboveTresholdMatches = matchDifferences.Where(m => m.Value >= map.MaxPlayers / 4 + 1).ToList();
                if (aboveTresholdMatches.Count() == 0) {
                    //_logger.Info("Found no match - Lobby ID: " + _match.LobbyId);
                    if (_match.Started.AddMinutes(Startup.Config.MatchCheckMaxDuration) > DateTime.UtcNow) {
                        PostponeLobbyCheck(_match.LobbyId);
                    } else {
                        DeleteOngoingMatch(_match.LobbyId);
                        _logger.Info("Cancelled ongoing match - Lobby ID: " + _match.LobbyId);
                    }
                    StopMatchChecking();
                } else if (aboveTresholdMatches.Count() > 1) {
                    _logger.Info("Found too many matches - Lobby ID: " + _match.LobbyId + " - Count: " + aboveTresholdMatches.Count());
                    DeleteOngoingMatch(_match.LobbyId);
                    StopMatchChecking();
                } else {
                    _logger.Info("Found match - Match ID: " + aboveTresholdMatches.First().Key + " - Hits: " + aboveTresholdMatches.First().Value);
                    _matchId = ulong.Parse(aboveTresholdMatches.First().Key);
                    using (IRepository repository = new Repository(_dbOptions)) {
                        if (repository.Matches.Any(m => m.MatchId == _matchId.ToString())) {
                            _logger.Info("Match already exists, cancelling - Match ID: " + aboveTresholdMatches.First().Key);
                            DeleteOngoingMatch(_match.LobbyId);
                            StopMatchChecking();
                        } else {
                            Startup.Bot.DotaClient.GetMatchDetails(_matchId);
                        }
                    }
                }
            } catch (Exception e) {
                _logger.Error("Error while trying to finalize match - Lobby ID: " + _match?.LobbyId, e);
                StopMatchChecking();
            }
        }

        private void DeleteOngoingMatch(string lobbyId) {
            using (IRepository repository = new Repository(_dbOptions)) {
                DeleteOngoingMatch(repository, lobbyId);
                repository.SaveChanges();
            }
        }

        private void DeleteOngoingMatch(IRepository repository, string lobbyId) {
            var match = repository.OngoingMatches.Include(m => m.Players).Where(m => m.LobbyId == lobbyId).FirstOrDefault();
            foreach (var player in match.Players) {
                foreach (var playerMatch in repository.PlayerMatch.Where(pm => pm.Player.Id == player.Id)) { //Currently the only way to get second level relationships
                    repository.DeletePlayerMatch(playerMatch);
                }
                repository.DeletePlayer(player);
            }
            repository.DeleteOngoingMatch(match);
        }

        private void PostponeLobbyCheck(string lobbyId) {
            try {
                using (IRepository repository = new Repository(_dbOptions)) {
                    var match = repository.OngoingMatches.Where(m => m.LobbyId == lobbyId).FirstOrDefault();
                    match.LastCheck = DateTime.UtcNow;
                    repository.SaveChanges();
                }
            } catch (Exception e) {
                _logger.Error("Error while postponing lobby check - Lobby ID " + lobbyId, e);
            }
        }

        private void StopMatchChecking() {
            try {
                //_logger.Info("Stopping match checking");
                _checkingMatch = false;
                _finalizingMatch = false;
                _lastCheckTime = null;
                _lastHistoryRequest = null;
                _currentHistoryRequest = 0;
                _matchId = 0;
                _playerMatches = null;
                _match = null;
            } catch (Exception e) {
                _logger.Error("Error while stopping match checking", e);
            }
        }
    }
}
