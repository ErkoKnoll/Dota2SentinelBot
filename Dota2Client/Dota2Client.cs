using log4net;
using ProtoBuf;
using ProtoBuf.Meta;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.Dota.Internal;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static SteamKit2.SteamClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace Dota2Client {
    public class DotaClient {
        public Action OnDota2Launched { get; set; }
        public Action<ClientGCMsgProtobuf<CMsgDOTAJoinChatChannelResponse>> OnChatChannelJoined { get; set; }
        public Action<ClientGCMsgProtobuf<CMsgDOTAChatMessage>> OnChatMessage { get; set; }
        public Action<CSODOTALobby> OnLobbyUpdate { get; set; }
        public Action OnCacheUnsubscribed { get; set; }
        public Action<ClientGCMsgProtobuf<CMsgDOTAGetPlayerMatchHistoryResponse>> OnPlayerMatchHistoryResponse { get; set; }
        public Action<ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>> OnMatchDetailsResponse { get; set; }
        public Action<ClientGCMsgProtobuf<CMsgReadyUpStatus>> OnReadyUpStatus { get; set; }
        public Action OnClientConnectionStatus { get; set; }
        public Action OnDisconnected { get; set; }
        public static string UserName { get; set; }
        private const int APP_ID = 570;
        private ILog _logger = LogManager.GetLogger(typeof(DotaClient));
        private SteamClient _client;
        private SteamUser _user;
        private SteamGameCoordinator _gameCoordinator;
        private CallbackManager _callbackManager;
        private string _password { get; set; }
        private uint _clientVersion;
        private bool _disconnectRequested = false;
        private Queue<ClientGCMsgProtobuf<CMsgDOTAChatMessage>> _primaryChatQueue = new Queue<ClientGCMsgProtobuf<CMsgDOTAChatMessage>>();
        private Queue<ClientGCMsgProtobuf<CMsgDOTAChatMessage>> _secondaryChatQueue = new Queue<ClientGCMsgProtobuf<CMsgDOTAChatMessage>>();
        private Timer _chatQueueOffloadTimer;

        public void Connect(string userName, string password) {
            DotaClient.UserName = userName;
            _password = password;
            log4net.GlobalContext.Properties["user"] = new Log4NetUserNameProvider();
            _client = new SteamClient();
            _user = _client.GetHandler<SteamUser>();
            _gameCoordinator = _client.GetHandler<SteamGameCoordinator>();
            _callbackManager = new CallbackManager(_client);

            _callbackManager.Subscribe<SteamClient.ConnectedCallback>(EventOnConnected);
            _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(EventOnLoggedOn);
            _callbackManager.Subscribe<SteamGameCoordinator.MessageCallback>(EventOnGCMessage);
            _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(EventOnDisconnected);

            while (!LoadServersList()) {
                Thread.Sleep(1000);
            }

            StartChatQueueOffloadTimer();
            Connect();
            Wait();
        }

        private void StartChatQueueOffloadTimer() {
            _chatQueueOffloadTimer = new Timer((x) => {
                try {
                    if (_primaryChatQueue.Count > 0) {
                        var chatMessage = _primaryChatQueue.Dequeue();
                        SendChatMessage(chatMessage);
                    } else if (_secondaryChatQueue.Count > 0) {
                        var chatMessage = _secondaryChatQueue.Dequeue();
                        SendChatMessage(chatMessage);
                    }
                } catch (Exception e) {
                    _logger.Error("Error while offloading chat queue", e);
                }
            }, null, 0, 250);
        }

        private void Wait() {
            while (true) {
                _callbackManager.RunWaitCallbacks(TimeSpan.FromMilliseconds(1));
            }
        }

        private bool LoadServersList() {
            var loadServersTask = SteamDirectory.Initialize(0u);
            loadServersTask.Wait();

            if (loadServersTask.IsFaulted) {
                _logger.Info(String.Format("Error loading server list from directory: {0}", loadServersTask.Exception.Message));
                return false;
            }
            return true;
        }

        private void Connect() {
            _logger.Info("Initiating Steam connection");
            _client.Connect();
        }

        public void Disconnect(bool dontReconnect) {
            _disconnectRequested = dontReconnect;
            _user.LogOff();
            _client.Disconnect();
        }

        private void LogIn() {
            _user.LogOn(new SteamUser.LogOnDetails {
                Username = DotaClient.UserName,
                Password = _password,
            });
        }

        private void LaunchDota() {
            var playGame = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

            playGame.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed {
                game_id = new GameID(APP_ID),
            });

            _client.Send(playGame);

            Thread.Sleep(5000);

            var clientHello = new ClientGCMsgProtobuf<CMsgClientHello>((uint)EGCBaseClientMsg.k_EMsgGCClientHello);
            clientHello.Body.engine = ESourceEngine.k_ESE_Source2;
            _gameCoordinator.Send(clientHello, APP_ID);
        }

        public void JoinChat(string chatName, DOTAChatChannelType_t channelType) {
            var request = new ClientGCMsgProtobuf<CMsgDOTAJoinChatChannel>((uint)EDOTAGCMsg.k_EMsgGCJoinChatChannel);
            request.Body.channel_name = chatName;
            request.Body.channel_type = channelType;
            _gameCoordinator.Send(request, APP_ID);
            _logger.Info("Joining chat: " + chatName);
        }

        public void LeaveChat(ulong channelId) {
            var request = new ClientGCMsgProtobuf<CMsgDOTALeaveChatChannel>((uint)EDOTAGCMsg.k_EMsgGCLeaveChatChannel);
            request.Body.channel_id = channelId;
            _gameCoordinator.Send(request, APP_ID);
            _logger.Info("Leaving chat: " + channelId);
        }

        public void QueueChatMessage(ulong channelId, string message, bool primary) {
            var request = new ClientGCMsgProtobuf<CMsgDOTAChatMessage>((uint)EDOTAGCMsg.k_EMsgGCChatMessage);
            request.Body.channel_id = channelId;
            request.Body.persona_name = "";
            request.Body.text = message;
            request.Body.share_lobby_passkey = "";
            request.Body.suggest_invite_name = "";
            if (primary) {
                _primaryChatQueue.Enqueue(request);
            } else {
                _secondaryChatQueue.Enqueue(request);
            }
        }

        public void QueueLobbyShare(ulong channelId, ulong lobbyId, ulong customGameId, bool primary) {
            var request = new ClientGCMsgProtobuf<CMsgDOTAChatMessage>((uint)EDOTAGCMsg.k_EMsgGCChatMessage);
            request.Body.channel_id = channelId;
            request.Body.persona_name = "";
            request.Body.text = "";
            request.Body.share_lobby_id = lobbyId;
            request.Body.share_lobby_custom_game_id = customGameId;
            request.Body.share_lobby_passkey = "";
            request.Body.suggest_invite_name = "";
            if (primary) {
                _primaryChatQueue.Enqueue(request);
            } else {
                _secondaryChatQueue.Enqueue(request);
            }
        }

        private void SendChatMessage(ClientGCMsgProtobuf<CMsgDOTAChatMessage> message) {
            _gameCoordinator.Send(message, APP_ID);
            _logger.Info("Chat message sent - Channel ID: " + message.Body.channel_id + " - Message: " + message.Body.text + " - Share Lobby ID: " + message.Body.share_lobby_id);
        }

        public void SendChatMessage(ulong channelId, string message) {
            var request = new ClientGCMsgProtobuf<CMsgDOTAChatMessage>((uint)EDOTAGCMsg.k_EMsgGCChatMessage);
            request.Body.channel_id = channelId;
            request.Body.persona_name = "";
            request.Body.text = message;
            request.Body.share_lobby_passkey = "";
            request.Body.suggest_invite_name = "";
            request.Body.badge_level = 10;
            _gameCoordinator.Send(request, APP_ID);
            _logger.Info("Chat message sent - Channel ID: " + channelId + " - Message: " + message);
        }

        public void CreateLobby(uint serverRegion, uint gameMode, string customMapName, ulong customGameMode, ulong customGameCrc, uint customGameTimestamp, uint minPlayers, uint maxPlayers, uint teams) {
            var request = new ClientGCMsgProtobuf<CMsgPracticeLobbyCreate>((uint)EDOTAGCMsg.k_EMsgGCPracticeLobbyCreate);
            request.Body.client_version = _clientVersion;
            request.Body.search_key = "";
            request.Body.pass_key = "";
            request.Body.lobby_details = new CMsgPracticeLobbySetDetails() {
                server_region = serverRegion,
                game_mode = gameMode,
                allow_spectating = true,
                cm_pick = DOTA_CM_PICK.DOTA_CM_RANDOM,
                bot_difficulty_radiant = DOTABotDifficulty.BOT_DIFFICULTY_HARD,
                bot_difficulty_dire = DOTABotDifficulty.BOT_DIFFICULTY_HARD,
                game_version = DOTAGameVersion.GAME_VERSION_STABLE,
                dota_tv_delay = LobbyDotaTVDelay.LobbyDotaTV_120,
                lan = true,
                pass_key = "",
                custom_game_mode = customGameMode.ToString(),
                custom_map_name = customMapName,
                custom_game_id = customGameMode,
                custom_min_players = minPlayers,
                custom_max_players = maxPlayers,
                lan_host_ping_to_server_region = 12,
                visibility = DOTALobbyVisibility.DOTALobbyVisibility_Public,
                custom_game_crc = customGameCrc,
                custom_game_timestamp = customGameTimestamp,
                league_selection_priority_choice = SelectionPriorityType.UNDEFINED,
                league_non_selection_priority_choice = SelectionPriorityType.UNDEFINED,
                pause_setting = LobbyDotaPauseSetting.LobbyDotaPauseSetting_Limited,
                allchat = false,
                allow_cheats = false,
                custom_difficulty = 0,
                dire_series_wins = 0,
                fill_with_bots = false,
                game_name = "",
                intro_mode = false,
                leagueid = 0,
                league_game_id = 0,
                league_selection_priority_team = 0,
                league_series_id = 0,
                load_game_id = 0,
                lobby_id = 0,
                penalty_level_dire = 0,
                penalty_level_radiant = 0,
                previous_match_override = 0,
                radiant_series_wins = 0,
                series_type = 0,
                bot_radiant = 0,
                bot_dire = 0
            };
            for (var i = 0; i < teams; i++) {
                request.Body.lobby_details.team_details.Add(new CLobbyTeamDetails() {
                    team_name = "",
                    team_tag = "",
                    guild_name = "",
                    guild_tag = "",
                    guild_banner_logo = 0,
                    guild_base_logo = 0,
                    guild_id = 0,
                    guild_logo = 0,
                    is_home_team = false,
                    rank = 0,
                    rank_change = 0,
                    team_banner_logo = 0,
                    team_base_logo = 0,
                    team_complete = false,
                    team_id = 0,
                    team_logo = 0
                });
            }

            _gameCoordinator.Send(request, APP_ID);
        }

        public void LeaveLobby() {
            var request = new ClientGCMsgProtobuf<CMsgPracticeLobbyLeave>((uint)EDOTAGCMsg.k_EMsgGCPracticeLobbyLeave);
            _gameCoordinator.Send(request, APP_ID);
            _logger.Info("Leave current lobby");
        }

        public void DeclineReadyUp(ulong lobbyId, ulong userId, ulong magicNumber) {
            var request = new ClientGCMsgProtobuf<CMsgReadyUp>((uint)EDOTAGCMsg.k_EMsgGCReadyUp);
            request.Body.state = DOTALobbyReadyState.DOTALobbyReadyState_DECLINED;
            request.Body.ready_up_key = (lobbyId ^ ~(userId | (userId << 32))) + magicNumber;
            _gameCoordinator.Send(request, APP_ID);
            _logger.Info("Decline ready up");
        }

        public void GetPlayerMatchHistory(uint id) {
            var request = new ClientGCMsgProtobuf<CMsgDOTAGetPlayerMatchHistory>((uint)EDOTAGCMsg.k_EMsgDOTAGetPlayerMatchHistory);
            request.Body.account_id = id;
            request.Body.include_custom_games = true;
            request.Body.include_practice_matches = true;
            request.Body.matches_requested = 20;
            _gameCoordinator.Send(request, APP_ID);
            //_logger.Info("Requesting match history - Player ID " + id);
        }

        public void GetMatchDetails(ulong matchId) {
            var requestMatch = new ClientGCMsgProtobuf<CMsgGCMatchDetailsRequest>((uint)EDOTAGCMsg.k_EMsgGCMatchDetailsRequest);
            requestMatch.Body.match_id = matchId;
            _gameCoordinator.Send(requestMatch, APP_ID);
            _logger.Info("Requesting match details - Match ID: " + matchId);
        }

        public void KickLobbyMember(uint id) {
            var request = new ClientGCMsgProtobuf<CMsgPracticeLobbyKick>((uint)EDOTAGCMsg.k_EMsgGCPracticeLobbyKick);
            request.Body.account_id = id;
            _gameCoordinator.Send(request, APP_ID);
            _logger.Info("Kicked lobby member - Account ID: " + id);
        }

        private void EventOnConnected(ConnectedCallback callback) {
            if (callback.Result != EResult.OK) {
                _logger.Info(String.Format("Connection failed: {0}", callback.Result));
                Thread.Sleep(1000);
            } else {
                _logger.Info(String.Format("Connection successful - Logging in {0}", UserName));
                LogIn();
            }
        }

        private void EventOnDisconnected(SteamClient.DisconnectedCallback callback) {
            _logger.Info("Disconnected from Steam");
            OnDisconnected?.Invoke();
            if (!_disconnectRequested) {
                Thread.Sleep(1000);
                Connect();
            }
        }

        private void EventOnLoggedOn(SteamUser.LoggedOnCallback callback) {
            if (callback.Result != EResult.OK) {
                _logger.Info(String.Format("Login failed: {0}", callback.Result));
                Thread.Sleep(1000);
                LogIn();
            } else {
                _logger.Info("Login successful - Launching Dota 2");
                LaunchDota();
            }
        }

        private void EventOnClientWelcome(IPacketGCMsg packetMsg) {
            var response = new ClientGCMsgProtobuf<CMsgClientWelcome>(packetMsg);
            _clientVersion = response.Body.version;
            _logger.Info(String.Format("Dota 2 launch successful - Client version: {0}", _clientVersion));
            OnDota2Launched?.Invoke();
        }

        private void EventOnChatChannelJoin(IPacketGCMsg packetMsg) {
            if (OnChatChannelJoined != null) {
                var response = new ClientGCMsgProtobuf<CMsgDOTAJoinChatChannelResponse>(packetMsg);
                OnChatChannelJoined(response);
            }
        }

        private void EventOnChatMessage(IPacketGCMsg packetMsg) {
            if (OnChatMessage != null) {
                var response = new ClientGCMsgProtobuf<CMsgDOTAChatMessage>(packetMsg);
                OnChatMessage(response);
            }
        }

        private void EventOnReadyUpStatus(IPacketGCMsg packetMsg) {
            if (OnReadyUpStatus != null) {
                var response = new ClientGCMsgProtobuf<CMsgReadyUpStatus>(packetMsg);
                OnReadyUpStatus(response);
            }
        }

        private void EventOnUpdateMultiple(IPacketGCMsg packetMsg) {
            var msg = new ClientGCMsgProtobuf<CMsgSOMultipleObjects>(packetMsg);
            if (msg.Body.objects_modified.Count > 0) {
                var lobby = ReadExtraObject(msg.Body.objects_modified.First()) as CSODOTALobby;
                OnLobbyUpdate?.Invoke(lobby);
            }
        }

        private void EventOnMatchDetailsResponse(IPacketGCMsg packetMsg) {
            if (OnMatchDetailsResponse != null) {
                var response = new ClientGCMsgProtobuf<CMsgGCMatchDetailsResponse>(packetMsg);
                OnMatchDetailsResponse(response);
            }
        }

        private void EventOnCacheUnsubscribed(IPacketGCMsg packetMsg) {
            OnCacheUnsubscribed?.Invoke();
        }

        private void EventOnClientConnectionStatus(IPacketGCMsg packetMsg) {
            _logger.Info("Client connection status");
            OnClientConnectionStatus?.Invoke();
        }

        private void EventVoidMessage(IPacketGCMsg packetMsg) {
        }

        private void EventOnPlayerMatchHistoryResponse(IPacketGCMsg packetMsg) {
            if (OnPlayerMatchHistoryResponse != null) {
                var response = new ClientGCMsgProtobuf<CMsgDOTAGetPlayerMatchHistoryResponse>(packetMsg);
                OnPlayerMatchHistoryResponse(response);
            }
        }

        private void EventOnGCMessage(SteamGameCoordinator.MessageCallback callback) {
            var messageMap = new Dictionary<uint, Action<IPacketGCMsg>>
            {
                { ( uint )EGCBaseClientMsg.k_EMsgGCClientWelcome, EventOnClientWelcome },
                { ( uint )EDOTAGCMsg.k_EMsgGCMatchDetailsResponse, EventOnMatchDetailsResponse },
                { ( uint )ESOMsg.k_ESOMsg_CacheSubscribed, EventVoidMessage },
                { ( uint )ESOMsg.k_ESOMsg_CacheUnsubscribed, EventOnCacheUnsubscribed },
                { ( uint )ESOMsg.k_ESOMsg_UpdateMultiple, EventOnUpdateMultiple },
                { ( uint )EDOTAGCMsg.k_EMsgGCJoinChatChannelResponse, EventOnChatChannelJoin },
                { ( uint )EDOTAGCMsg.k_EMsgGCChatMessage, EventOnChatMessage },
                { ( uint )EDOTAGCMsg.k_EMsgGCReadyUpStatus, EventOnReadyUpStatus },
                { ( uint )EDOTAGCMsg.k_EMsgDOTAGetPlayerMatchHistoryResponse, EventOnPlayerMatchHistoryResponse },
                { ( uint )EDOTAGCMsg.k_EMsgGCOtherJoinedChannel, EventVoidMessage },
                { ( uint )EDOTAGCMsg.k_EMsgGCOtherLeftChannel, EventVoidMessage },
                { ( uint )EGCBaseClientMsg.k_EMsgGCClientConnectionStatus, EventOnClientConnectionStatus }
            };

            Action<IPacketGCMsg> func;
            if (!messageMap.TryGetValue(callback.EMsg, out func)) {
                _logger.Info("Unhandled message: " + callback.EMsg);
                return;
            }

            try {
                func(callback.Message);
            } catch (Exception e) {
                _logger.Error(String.Format("Error while handling message {0}", callback.EMsg), e);
            }
        }

        private object ReadExtraObject(CMsgSOMultipleObjects.SingleObject sharedObject) {
            try {
                using (var ms = new MemoryStream(sharedObject.object_data)) {
                    Type t;
                    if (Dota2SOHelper.SOTypes.TryGetValue(sharedObject.type_id, out t)) {
                        return RuntimeTypeModel.Default.Deserialize(ms, null, t);
                    }
                }
            } catch (ProtoException ex) {
                return "Error parsing SO data: " + ex.Message;
            } catch (EndOfStreamException ex) {
                return "Error parsing SO data: " + ex.Message;
            }

            return null;
        }
    }

    public class CustomResolver : DefaultContractResolver {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization) {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            property.ShouldSerialize = instance => {
                try {
                    PropertyInfo prop = (PropertyInfo)member;
                    if (prop.CanRead) {
                        prop.GetValue(instance, null);
                        return true;
                    }
                } catch {
                }
                return false;
            };

            return property;
        }
    }
}
