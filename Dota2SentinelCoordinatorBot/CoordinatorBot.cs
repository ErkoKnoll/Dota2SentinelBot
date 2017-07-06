using Dota2Client;
using Dota2SentinelDomain;
using Dota2SentinelDomain.Helpers;
using Dota2SentinelDomain.Models.Config;
using Dota2SentinelDomain.Models.Coordinator;
using Dota2SentinelDomain.Models.Coordinator.Messages;
using log4net;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dota2SentinelCoordinatorBot {
    public class CoordinatorBot {
        public Queue<Message> OutboundMessages { get; set; }
        public List<LobbyState> LobbyStates { get; set; }
        public Action OnShutdown { get; set; }
        public Action OnStartup { get; set; }
        public Action<ClientGCMsgProtobuf<CMsgDOTAGetPlayerMatchHistoryResponse>> OnGetPlayerMatchHistoryResponse { get; set; }
        public Action<ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>> OnMatchDetailsResponse { get; set; }
        public DotaClient DotaClient { get; set; }
        public IDictionary<ulong, string> Channels = new Dictionary<ulong, string>();
        private ILog _logger = LogManager.GetLogger(typeof(CoordinatorBot));
        private DbContextOptions<Repository> _dbOptions;
        private Config _config;

        public CoordinatorBot(Config config) {
            _config = config;
            _dbOptions = new DbContextOptionsBuilder<Repository>().UseNpgsql(_config.ConnectionString).Options;
        }

        public void Connect() {
            DotaClient = new DotaClient();
            DotaClient.OnDota2Launched = OnDota2Startup;
            DotaClient.OnChatChannelJoined = OnChatChannelJoin;
            DotaClient.OnChatMessage = OnChatMessage;
            if (OnGetPlayerMatchHistoryResponse != null) {
                DotaClient.OnPlayerMatchHistoryResponse = OnGetPlayerMatchHistoryResponse;
            }
            if (OnMatchDetailsResponse != null) {
                DotaClient.OnMatchDetailsResponse = OnMatchDetailsResponse;
            }
            DotaClient.OnClientConnectionStatus = OnClientConnectionStatus;
            DotaClient.OnDisconnected = OnDisconnected;
            DotaClient.Connect(_config.CoordinatorBot.UserName, _config.CoordinatorBot.Password);
        }

        private void OnDota2Startup() {
            DotaClient.JoinChat(_config.PrivateChat, DOTAChatChannelType_t.DOTAChannelType_Custom);
            foreach (var map in _config.Games) {
                ulong id;
                if (ulong.TryParse(map.Chat, out id)) {
                    DotaClient.JoinChat(map.CustomGameMode.ToString(), DOTAChatChannelType_t.DOTAChannelType_CustomGame);
                } else {
                    DotaClient.JoinChat(map.Chat, DOTAChatChannelType_t.DOTAChannelType_Custom);
                }
            }
            OnStartup?.Invoke();
        }

        private void OnClientConnectionStatus() {
            DotaClient.Disconnect(false);
        }

        private void OnDisconnected() {
            Channels.Clear();
        }

        private void OnChatChannelJoin(ClientGCMsgProtobuf<CMsgDOTAJoinChatChannelResponse> response) {
            if (response.Body.result == CMsgDOTAJoinChatChannelResponse.Result.JOIN_SUCCESS) {
                Channels[response.Body.channel_id] = response.Body.channel_name;
                _logger.Info("Joined channel - Name " + response.Body.channel_name + " ID: " + response.Body.channel_id);
            } else {
                _logger.Info("Failed to join channel - Name " + response.Body.channel_name + " Result: " + response.Body.result);
            }
        }

        private void OnChatMessage(ClientGCMsgProtobuf<CMsgDOTAChatMessage> response) {
            if (!string.IsNullOrEmpty(response.Body.persona_name)) {
                _logger.Info("Incoming chat message - Person: " + response.Body.persona_name + " - Message: " + response.Body.text);
                var accountId = SteamHelper.ConvertIDToUint64(response.Body.account_id);
                switch (response.Body.text.Trim().ToLower().Split(' ')[0]) {
                    case "!shutdown":
                        if (_config.Admins.Contains(accountId)) {
                            _logger.Info("Shutdown requested");
                            DotaClient.Disconnect(true);
                            OnShutdown?.Invoke();
                        } else {
                            _logger.Warn("Shutdown not allowed for Steam ID: " + accountId);
                        }
                        break;
                    case "!host":
                        ProcessLobbyCommand(response.Body.channel_id, accountId, response.Body.persona_name, response.Body.text);
                        break;
                    case "!lobbies":
                        ProcessLobbiesCommand(response.Body.channel_id, response.Body.persona_name);
                        break;
                    case "!me":
                        ProcessMeCommand(response.Body.channel_id, accountId, response.Body.persona_name);
                        break;
                    case "!help":
                        DotaClient.QueueChatMessage(response.Body.channel_id, String.Format(_config.HelpMessage, response.Body.persona_name), false);
                        break;
                }
            }
        }

        private void ProcessLobbyCommand(ulong channelId, ulong accountId, string name, string text) {
            try {
                var commandParams = text.Split(' ');
                var channelName = Channels[channelId];
                var game = _config.Games.Where(m => m.Chat.ToLower() == channelName.ToLower()).SingleOrDefault();
                if (channelName == _config.PrivateChat) {
                    DotaClient.QueueChatMessage(channelId, String.Format("{0}, requesting a lobby in this channel is not allowed, please request a lobby in custom game's public channel.", name, GetValidServerRegions()), false);
                } else if (commandParams.Length < 2) {
                    DotaClient.QueueChatMessage(channelId, String.Format("{0}, please specify server region. Regions: {1}.", name, GetValidServerRegions()), false);
                    if (game.Maps.Count > 1) {
                        DotaClient.QueueChatMessage(channelId, String.Format(_config.SpecifyMapMessage, name, GetValidMaps(game), _config.ServerRegions.FirstOrDefault().ShortName, game.Maps.FirstOrDefault().ChatId), false);
                    }
                } else {
                    var region = _config.ServerRegions.Where(sr => sr.ShortName == commandParams[1].Trim().ToLower()).SingleOrDefault();
                    if (region == null) {
                        DotaClient.QueueChatMessage(channelId, String.Format("{0}, invalid server region. Valid regions: {1}.", name, GetValidServerRegions()), false);
                    } else {
                        string mapName = null;
                        if (commandParams.Length > 2 && game.Maps.Count > 1) {
                            mapName = commandParams[2];
                        }
                        Map map = null;
                        bool mapOk = true;
                        if (mapName == null) {
                            map = game.Maps.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
                        } else {
                            map = game.Maps.Where(m => m.ChatId == mapName.Trim().ToLower()).FirstOrDefault();
                            if (map == null && game.Maps.Count > 1) {
                                mapOk = false;
                                DotaClient.QueueChatMessage(channelId, String.Format(_config.MapNotFoundMessage, name, GetValidMaps(game)), false);
                            }
                        }
                        if (mapOk) {
                            using (IRepository repository = new Repository(_dbOptions)) {
                                var banned = repository.Bans.Any(b => b.Account.AccountId == accountId.ToString() && b.Expires > DateTime.UtcNow);
                                if (!banned || _config.Admins.Contains(accountId)) {
                                    try {
                                        var timedoutLobbies = LobbyStates.Where(ls => ls.Created.AddMinutes(_config.Games.Where(g => g.Maps.Any(m => m.MapId == ls.CustomMapName)).SingleOrDefault().LobbyTimeout + 1) < DateTime.UtcNow);
                                        foreach (var timedoutLobby in timedoutLobbies.ToList()) {
                                            _logger.Info("Removed timed out lobby - Game: " + timedoutLobby.CustomMapName + " Server ID: " + timedoutLobby.CustomMapName);
                                            LobbyStates.Remove(timedoutLobby);
                                        }
                                    } catch (Exception e) {
                                        _logger.Error("Error while checking timed out lobbies", e);
                                    }
                                    var myExistingLobby = LobbyStates.Where(ls => ls.RequesterAccountId == accountId).FirstOrDefault();
                                    if (myExistingLobby != null) {
                                        DotaClient.QueueChatMessage(channelId, String.Format(_config.LobbyRequesterHasHost, name, _config.Games.Where(g => g.Maps.Any(m => m.MapId == myExistingLobby.CustomMapName)).SingleOrDefault().Name), false);
                                    } else {
                                        var existingLobby = LobbyStates.Where(ls => game.Maps.Any(m => m.MapId == ls.CustomMapName) && ls.ServerId == region.ServerId).FirstOrDefault();
                                        if (existingLobby != null) {
                                            if (existingLobby.LobbyId == 0) {
                                                DotaClient.QueueChatMessage(channelId, String.Format(_config.LobbyAlreadyQueued, name, region.LongName), false);
                                            } else {
                                                DotaClient.QueueChatMessage(channelId, String.Format(_config.LobbyAlreadyHosted, name, region.LongName), true);
                                                DotaClient.QueueLobbyShare(channelId, existingLobby.LobbyId, game.CustomGameMode, true);
                                            }
                                        } else {
                                            if (LobbyStates.Count < _config.LobbyBotsPool.Count) {
                                                DotaClient.QueueChatMessage(channelId, String.Format("{0}, hosting your lobby, just a sec...", name), false);
                                                game.ServerId = region.ServerId;
                                                game.RequestedBy = accountId;
                                                _logger.Info("Queue lobby - Game: " + game.Name + " - Map: " + map.Name + " - Server ID: " + game.ServerId);
                                                OutboundMessages.Enqueue(new Message() {
                                                    Id = MessageIds.Out.CREATE_LOBBY,
                                                    Payload = JsonConvert.SerializeObject(new LobbyHost() {
                                                        Game = game,
                                                        Map = map
                                                    })
                                                });
                                                lock (LobbyStates) { //Just in case should something here be multithreaded
                                                    LobbyStates.Add(new LobbyState() {
                                                        CustomMapName = map.MapId,
                                                        ServerId = game.ServerId,
                                                        RequesterAccountId = accountId,
                                                        Created = DateTime.UtcNow
                                                    });
                                                }
                                            } else {
                                                DotaClient.QueueChatMessage(channelId, String.Format(_config.BusyMessage, name), false);
                                            }
                                        }
                                    }
                                } else if (banned) {
                                    DotaClient.QueueChatMessage(channelId, String.Format(_config.LobbyRequesterBanned, name), false);
                                }
                            }
                        }
                    }
                }
            } catch (Exception e) {
                _logger.Error(String.Format("Error while processing !host command: {0}", text), e);
            }
        }

        private void ProcessLobbiesCommand(ulong channelId, string name) {
            try {
                var channelName = Channels[channelId];
                if (_config.PrivateChat == channelName) {
                    DotaClient.QueueChatMessage(channelId, String.Format(_config.LobbiesCommandNotAllowed, name), false);
                } else {
                    var lobbies = LobbyStates.Where(l => _config.Games.Any(g => g.Chat == channelName && g.Maps.Any(m => m.MapId == l.CustomMapName)));
                    if (lobbies.Count() == 0) {
                        DotaClient.QueueChatMessage(channelId, String.Format(_config.HostingNoLobbiesMessage, name), false);
                    } else {
                        foreach (var lobby in lobbies) {
                            var region = _config.ServerRegions.Where(r => r.ServerId == lobby.ServerId).SingleOrDefault();
                            var game = _config.Games.Where(g => g.Maps.Any(m => m.MapId == lobby.CustomMapName)).SingleOrDefault();
                            var lobbyName = game.Name;
                            if (game.Maps.Count > 1) {
                                lobbyName += " " + game.Maps.Where(m => m.MapId == lobby.CustomMapName).FirstOrDefault().Name;
                            }
                            if (lobby.LobbyId != 0) {
                                DotaClient.QueueChatMessage(channelId, String.Format(_config.HostingLobby, lobbyName, region.LongName), true);
                                DotaClient.QueueLobbyShare(channelId, lobby.LobbyId, game.CustomGameMode, true);
                            } else {
                                DotaClient.QueueChatMessage(channelId, String.Format(_config.LobbyQueuedMessage, lobbyName, region.LongName), true);
                            }
                        }
                    }
                }
            } catch (Exception e) {
                _logger.Error("Error while processing !lobbies command", e);
            }
        }

        private void ProcessMeCommand(ulong channelId, ulong accountId, string name) {
            try {
                using (IRepository repository = new Repository(_dbOptions)) {
                    var account = repository.Accounts.Include(a => a.Bans).Include(a => a.Matches).Where(a => a.AccountId == accountId.ToString()).FirstOrDefault();
                    if (account == null) {
                        DotaClient.QueueChatMessage(channelId, String.Format(_config.NoAccountMessage, name), false);
                    } else {
                        var message = String.Format(_config.AccountStatsMessage, name, account.Matches.Count, account.Bans.Count);
                        if (account.NewUser) {
                            message += _config.NewAccountMessage;
                        } else {
                            var ban = account.Bans.Where(b => b.Expires > DateTime.UtcNow).OrderByDescending(b => b.Expires).FirstOrDefault();
                            if (ban != null) {
                                message += String.Format(_config.AccountBannedMessage, (ban.Expires.ToString("d") + " " + ban.Expires.ToString("t") + " UTC"), ban.Reason);
                            }
                        }
                        DotaClient.QueueChatMessage(channelId, message, false);
                    }
                }
            } catch (Exception e) {
                _logger.Error("Error while processing !me command", e);
            }
        }

        private string GetValidServerRegions() {
            return String.Join(", ", _config.ServerRegions.Select(sr => String.Format("{0} = {1}", sr.ShortName, sr.LongName)));
        }

        private string GetValidMaps(Game game) {
            return String.Join(", ", game.Maps.Select(sr => String.Format("{0} = {1}", sr.ChatId, sr.Name)));
        }
    }
}
