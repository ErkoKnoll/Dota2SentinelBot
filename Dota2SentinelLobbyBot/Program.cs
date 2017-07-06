using Dota2Client;
using log4net;
using log4net.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json;
using Dota2SentinelDomain.Models.Coordinator;
using Dota2SentinelDomain.Models.Config;
using SteamKit2.GC.Dota.Internal;
using System.Text;
using Dota2SentinelDomain.Models.Coordinator.Messages;
using Dota2SentinelDomain.DataTypes;
using Dota2SentinelDomain.Helpers;
using SteamKit2.GC;
using Microsoft.EntityFrameworkCore;
using Dota2SentinelDomain;
using System.Reflection;

namespace Dota2SentinelLobbyBot {
    public class Program {
        private static DotaClient _dotaClient;
        private static ILog _logger = LogManager.GetLogger(typeof(DotaClient));
        private static Timer _coordinatorTaskPoller;
        private static Timer _matchHistoryRequester;
        private static Timer _lobbyTimeoutChecker;
        private static string _userName;
        private static string _coordinatorUrl;
        private static ulong _magicNumber;
        private static bool _test = false;
        private static bool _hostingLobby = false;
        private static bool _lobbyShared = false;
        private static bool _chatJoined = false;
        private static bool _reconnectRequested = false;
        private static bool _lobbyWasStarted = false;
        private static ulong _botUserId = 0;
        private static ulong _channelId;
        private static ulong _lobbyId;
        private static int _lobbyMembersCount;
        private static Game _game;
        private static Map _map;
        private static List<Player> _lobbyMembers;
        private static ulong _currentHistoryRequest = 0;
        private static int _infoMessagesSent = 0;
        private static DateTime? _lastHistoryRequest;
        private static DateTime? _lobbyCreated;
        private static DbContextOptions<Repository> _dbOptions;
        private static List<uint> _playersToKick = new List<uint>();

        public static void Main(string[] args) {
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo(args[2]));
            _userName = args[0];
            _coordinatorUrl = args[3];
            _test = bool.Parse(args[5]);
            _magicNumber = ulong.Parse(args[6]);
            _dbOptions = new DbContextOptionsBuilder<Repository>().UseNpgsql(args[4]).Options;
            _dotaClient = new DotaClient();
            _dotaClient.OnDota2Launched = () => {
                _dotaClient.LeaveLobby(); //Try leaving the current lobby on startup in case the client was shut down forcefully and couldn't leave the lobby before.
                StartCoordinatorTaskPoller();
                StartMatchHistoryRequester();
                StartLobbyTimeoutChecker();
            };
            _dotaClient.OnLobbyUpdate = OnLobbyUpdate;
            _dotaClient.OnChatMessage = OnChatMessage;
            _dotaClient.OnChatChannelJoined = OnChatChannelJoin;
            _dotaClient.OnPlayerMatchHistoryResponse = OnGetPlayerMatchHistoryResponse;
            _dotaClient.OnCacheUnsubscribed = OnCacheUnsubscribed;
            _dotaClient.OnReadyUpStatus = OnReadyUpStatus;
            _dotaClient.Connect(_userName, args[1]);
        }

        private static void StartCoordinatorTaskPoller() {
            _coordinatorTaskPoller = new Timer(async (x) => {
                try {
                    using (var httpClient = new HttpClient()) {
                        var response = await httpClient.GetStringAsync(String.Format("{0}/api/coordinator/{1}?hostingLobby={2}", _coordinatorUrl, _userName, _hostingLobby));
                        var messages = JsonConvert.DeserializeObject<List<Message>>(response);
                        foreach (var message in messages) {
                            _logger.Info("Received message from coordinator - ID: " + message.Id);
                            switch (message.Id) {
                                case MessageIds.Out.CREATE_LOBBY:
                                    HostLobby(JsonConvert.DeserializeObject<LobbyHost>(message.Payload));
                                    break;
                            }
                        }
                    }
                } catch (Exception e) {
                    _logger.Error("Error while polling coordinator tasks: " + e.Message);
                }
            }, null, 0, 500);
        }

        private static void StartMatchHistoryRequester() {
            _matchHistoryRequester = new Timer((x) => {
                try {
                    if (_lobbyMembers != null && _currentHistoryRequest == 0 || (_lastHistoryRequest.HasValue && DateTime.UtcNow > _lastHistoryRequest.Value.AddSeconds(5))) {
                        var player = _lobbyMembers.Skip(1).Where(m => m.Active && m.PlayerMatches == null).FirstOrDefault();
                        if (player != null) {
                            _currentHistoryRequest = ulong.Parse(player.AccountId);
                            _lastHistoryRequest = DateTime.UtcNow;
                            _dotaClient.GetPlayerMatchHistory(SteamHelper.ConvertIDToUint32(_currentHistoryRequest));
                        }
                    }
                } catch (Exception e) {
                    _logger.Error("Error while requesting a match", e);
                }
            }, null, 0, 100);
        }

        private static void StartLobbyTimeoutChecker() {
            _lobbyTimeoutChecker = new Timer((x) => {
                try {
                    if (_lobbyCreated.HasValue && _game != null && _lobbyCreated.Value.AddMinutes(_game.LobbyTimeout) < DateTime.UtcNow) {
                        _logger.Info("Lobby timed out");
                        if (_channelId != 0 && !_test) {
                            _dotaClient.SendChatMessage(_channelId, _game.LobbyTimeoutMessage);
                        }
                        SendLobbyLeftToCoordinator();
                        LeaveLobby();
                        _dotaClient.LeaveLobby();
                    }
                } catch (Exception e) {
                    _logger.Error("Error while checking lobby timeout", e);
                }
            }, null, 0, 5000);
        }

        private static void SendMessage(Message message) {
            using (var httpClient = new HttpClient()) {
                httpClient.PostAsync(String.Format("{0}/api/coordinator/{1}", _coordinatorUrl, _userName), new StringContent(JsonConvert.SerializeObject(message), Encoding.UTF8, "application/json")).Wait();
            }
        }

        private static void HostLobby(LobbyHost lobbyHost) {
            _hostingLobby = true;
            _game = lobbyHost.Game;
            _map = lobbyHost.Map;
            _lobbyMembers = new List<Player>();
            _playersToKick = new List<uint>();
            _logger.Info("Requesting lobby host - Game: " + _game.Name + " - Server ID: " + _game.ServerId);
            _lobbyCreated = DateTime.UtcNow;
            _dotaClient.CreateLobby(_game.ServerId, _game.GameMode, _map.MapId, _game.CustomGameMode, _game.CustomGameCrc, _game.CustomGameTimestamp, _map.MinPlayers, _map.MaxPlayers, _game.Teams);
        }

        private static void OnClientConnectionStatus() {
            if (!_hostingLobby) {
                _dotaClient.Disconnect(false);
            } else {
                _reconnectRequested = true;
            }
        }

        private static void OnLobbyUpdate(CSODOTALobby lobby) {
            try {
                if (!_hostingLobby) {
                    _dotaClient.LeaveLobby();
                    return;
                }
                _lobbyMembersCount = lobby.members.Count;
                if (!_lobbyShared) {
                    _lobbyShared = true;
                    _lobbyId = lobby.lobby_id;
                    SendMessage(new Message() {
                        Id = MessageIds.In.LOBBY_CREATED,
                        Payload = JsonConvert.SerializeObject(new LobbyCreated() {
                            CustomMapName = _map.MapId,
                            SeverId = _game.ServerId,
                            LobbyId = _lobbyId
                        })
                    });
                    _dotaClient.JoinChat("Lobby_" + lobby.lobby_id, DOTAChatChannelType_t.DOTAChannelType_Lobby);
                }
                if (lobby.members.Count == _map.MaxPlayers) {
                    _lobbyWasStarted = true;
                } else if (_lobbyWasStarted) {
                    _lobbyWasStarted = false;
                    foreach (var playerId in _playersToKick) {
                        try {
                            _logger.Info("Re-kicking player: " + playerId);
                            _dotaClient.KickLobbyMember(playerId);
                        } catch (Exception e) {
                            _logger.Error("Error while re-kicking player - Account ID: " + playerId, e);
                        }
                    }
                }
                lock (_lobbyMembers) { //Not sure if SteamKit is multithreaded under the hood and because of that hold a lock just in case
                    if (!_test) {
                        if (_infoMessagesSent == 0 && lobby.members.Count >= _map.MaxPlayers / 2) {
                            _infoMessagesSent++;
                            SendChatMessage(_game?.LobbyInfoMessage, true);
                        }
                        if (_infoMessagesSent == 2 && lobby.members.Count < _map.MaxPlayers) {
                            _infoMessagesSent = 1;
                        }
                        if (_infoMessagesSent == 1 && lobby.members.Count == _map.MaxPlayers) {
                            _infoMessagesSent++;
                            SendChatMessage(_game?.LobbyInfoMessage, true);
                            SendChatMessage(_game?.BotLeaveMessage, true);
                        }
                    }
                    foreach (var member in lobby.members) {
                        var player = _lobbyMembers.Where(m => m.AccountId == member.id.ToString()).SingleOrDefault();
                        if (player == null) {
                            _lobbyMembers.Add(new Player() {
                                AccountId = member.id.ToString(),
                                Name = member.name,
                                Active = true
                            });
                            _logger.Info("Lobby joined (" + lobby.members.Count + "/" + _map.MaxPlayers + "): " + member.name);
                        } else if (player.Active == false) {
                            player.Active = true;
                            _logger.Info("Lobby re-joined (" + lobby.members.Count + "/" + _map.MaxPlayers + "): " + member.name);
                            if (player.Kicked) {
                                var playerId = SteamHelper.ConvertIDToUint32(ulong.Parse(player.AccountId));
                                if (_lobbyMembersCount < _map.MaxPlayers) {
                                    _dotaClient.KickLobbyMember(playerId);
                                }
                                _playersToKick.Add(playerId);
                            }
                        }
                    }
                    var playerIds = lobby.members.Select(m => m.id.ToString());
                    var leftPlayers = _lobbyMembers.Where(m => !playerIds.Contains(m.AccountId) && m.Active == true);
                    foreach (var leftPlayer in leftPlayers) {
                        leftPlayer.Active = false;
                        _playersToKick.Remove(SteamHelper.ConvertIDToUint32(ulong.Parse(leftPlayer.AccountId)));
                        _logger.Info("Lobby left (" + lobby.members.Count + "/" + _map.MaxPlayers + "): " + leftPlayer.Name);
                    }
                    if (_botUserId == 0 && lobby.members.Count > 0) {
                        _botUserId = lobby.members.First().id;
                    }
                }
            } catch (Exception e) {
                _logger.Error("Error occurred while processing lobby update", e);
            }
        }

        private static void OnGetPlayerMatchHistoryResponse(ClientGCMsgProtobuf<CMsgDOTAGetPlayerMatchHistoryResponse> response) {
            var accountId = _currentHistoryRequest;
            try {
                var player = _lobbyMembers.Where(m => m.AccountId == _currentHistoryRequest.ToString()).SingleOrDefault();
                player.PlayerMatches = response.Body.matches.Select(m => new PlayerMatch() {
                    MatchId = m.match_id.ToString()
                }).ToList();
                _currentHistoryRequest = 0;
                //_logger.Info("Match history - Player: " + player.Name + " - Matches: " + player.PlayerMatches.Count);
                using (IRepository repository = new Repository(_dbOptions)) {
                    var account = repository.Accounts.Include(a => a.Bans).Where(m => m.AccountId == player.AccountId).FirstOrDefault();
                    var newUser = player.PlayerMatches.Count < _game.PlayerMinGamesRequired;
                    if (newUser && !_test) {
                        var playerId = SteamHelper.ConvertIDToUint32(ulong.Parse(player.AccountId));
                        if (_lobbyMembersCount < _map.MaxPlayers) {
                            player.Kicked = true;
                            SendChatMessage(String.Format(_game.NewAccountKickMessage, player.Name));
                            _dotaClient.KickLobbyMember(playerId);
                        }
                        _playersToKick.Add(playerId);
                    }
                    if (account == null) {
                        account = new Account() {
                            AccountId = player.AccountId,
                            NewUser = newUser,
                            AccountNames = new List<AccountName>() {
                                new AccountName() {
                                    Name = player.Name
                                }
                            },
                            Bans = new List<Ban>(),
                            Matches = new List<AccountMatch>()
                        };
                        repository.AddAccount(account);
                        repository.SaveChanges();
                    } else {
                        var ban = account.Bans.Where(b => b.Expires > DateTime.UtcNow).OrderByDescending(b => b.Expires).FirstOrDefault();
                        if (ban != null && !_test) {
                            var playerId = SteamHelper.ConvertIDToUint32(ulong.Parse(player.AccountId));
                            if (_lobbyMembersCount < _map.MaxPlayers) {
                                player.Kicked = true;
                                SendChatMessage(String.Format(_game.BannedAccountKickMessage, player.Name, ban.Reason, ban.Expires.ToString("d") + " " + ban.Expires.ToString("t")));
                                _dotaClient.KickLobbyMember(playerId);
                            }
                            _playersToKick.Add(playerId);
                        }
                        if (account.NewUser && !newUser) {
                            account.NewUser = false;
                            repository.SaveChanges();
                        }
                    }
                }
            } catch (Exception e) {
                _logger.Error("Error occurred while processing player history response - Account ID: " + accountId, e);
            }
        }

        private static void SendChatMessage(string chatMessage, bool primary) {
            if (_chatJoined) {
                _dotaClient.QueueChatMessage(_channelId, chatMessage, primary);
            }
        }

        private static void SendChatMessage(string chatMessage) {
            if (_chatJoined) {
                _dotaClient.SendChatMessage(_channelId, chatMessage);
            }
        }

        private static void OnChatMessage(ClientGCMsgProtobuf<CMsgDOTAChatMessage> response) {
            if (!string.IsNullOrEmpty(response.Body.persona_name)) {
                _logger.Info("Incoming chat message - Person: " + response.Body.persona_name + " - Message: " + response.Body.text);
                var accountId = SteamHelper.ConvertIDToUint64(response.Body.account_id);
                switch (response.Body.text.Trim().ToLower().Split(' ')[0]) {
                    case "!host":
                        SendChatMessage(String.Format(_game.CommandNotAllowedMessage, response.Body.persona_name), false);
                        break;
                    case "!lobbies":
                        SendChatMessage(String.Format(_game.CommandNotAllowedMessage, response.Body.persona_name), false);
                        break;
                    case "!me":
                        ProcessMeCommand(accountId, response.Body.persona_name);
                        break;
                    case "!help":
                        SendChatMessage(String.Format(_game.HelpMessage, response.Body.persona_name), false);
                        break;
                }
            }
        }

        private static void OnReadyUpStatus(ClientGCMsgProtobuf<CMsgReadyUpStatus> response) {
            if (response.Body.accepted_ids != null && response.Body.accepted_ids.Count == _map.MaxPlayers - 1) {
                _dotaClient.DeclineReadyUp(_lobbyId, _botUserId, _magicNumber);
            }
        }

        private static void OnCacheUnsubscribed() {
            try {
                if (_hostingLobby) {
                    using (IRepository repository = new Repository(_dbOptions)) {
                        repository.AddOngoingMatch(new OngoingMatch() {
                            LobbyId = _lobbyId.ToString(),
                            CustomMapName = _map.MapId,
                            Started = DateTime.UtcNow,
                            LastCheck = DateTime.UtcNow,
                            RequestedBy = _game.RequestedBy.ToString(),
                            Players = _lobbyMembers.Skip(1).Where(lm => lm.Active == true).ToList()
                        });
                        repository.SaveChanges();
                    }
                    SendLobbyLeftToCoordinator();
                    LeaveLobby();
                }
            } catch (Exception e) {
                _logger.Error("Error while finalizing lobby: " + _lobbyId, e);
            }
        }

        private static void SendLobbyLeftToCoordinator() {
            while (true) {
                try {
                    SendMessage(new Message() {
                        Id = MessageIds.In.LOBBY_LEFT,
                        Payload = JsonConvert.SerializeObject(new LobbyLeft() {
                            CustomMapName = _map?.MapId,
                            ServerId = _game?.ServerId
                        })
                    });
                    break;
                } catch (Exception e) {
                    _logger.Error("Error while sending lobby left command: ", e);
                }
                Thread.Sleep(1000);
            }
        }

        private static void LeaveLobby() {
            if (_channelId != 0) {
                _dotaClient.LeaveChat(_channelId);
            }
            _hostingLobby = false;
            _lobbyShared = false;
            _chatJoined = false;
            _lobbyWasStarted = false;
            _game = null;
            _map = null;
            _lobbyMembers = null;
            _playersToKick = null;
            _infoMessagesSent = 0;
            _currentHistoryRequest = 0;
            _lobbyMembersCount = 0;
            _channelId = 0;
            _lastHistoryRequest = null;
            _lobbyCreated = null;
            _logger.Info("Left lobby: " + _lobbyId);
            _lobbyId = 0;
            if (_reconnectRequested) {
                _reconnectRequested = false;
                _dotaClient.Disconnect(false);
            }
        }

        private static void OnChatChannelJoin(ClientGCMsgProtobuf<CMsgDOTAJoinChatChannelResponse> response) {
            if (response.Body.result == CMsgDOTAJoinChatChannelResponse.Result.JOIN_SUCCESS) {
                _channelId = response.Body.channel_id;
                _chatJoined = true;
                _logger.Info("Joined channel - Name " + response.Body.channel_name + " ID: " + response.Body.channel_id);
            } else {
                _logger.Info("Failed to join channel - Name " + response.Body.channel_name + " Result: " + response.Body.result);
            }
        }

        private static void ProcessMeCommand(ulong accountId, string name) {
            try {
                using (IRepository repository = new Repository(_dbOptions)) {
                    var account = repository.Accounts.Include(a => a.Bans).Include(a => a.Matches).Where(a => a.AccountId == accountId.ToString()).FirstOrDefault();
                    if (account == null) {
                        SendChatMessage(String.Format(_game.AccountStatsMessage, name, 0, 0), false);
                    } else {
                        SendChatMessage(String.Format(_game.AccountStatsMessage, name, account.Matches.Count, account.Bans.Count), false);
                    }
                }
            } catch (Exception e) {
                _logger.Error("Error while processing !me command", e);
            }
        }
    }
}
