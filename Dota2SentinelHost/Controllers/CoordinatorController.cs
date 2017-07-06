using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Dota2SentinelDomain.Models.Coordinator;
using log4net;
using Dota2SentinelDomain.Models.Coordinator.Messages;
using Newtonsoft.Json;

namespace Dota2SentinelHost.Controllers {
    [Route("api/[controller]")]
    public class CoordinatorController : Controller {
        private ILog _logger = LogManager.GetLogger(typeof(CoordinatorController));
        public static Queue<Message> OutboundMessages = new Queue<Message>();
        public static List<LobbyState> LobbyStates = new List<LobbyState>();

        [HttpGet("{id}")]
        public List<Message> Get(string id, [FromQuery] bool hostingLobby) {
            try {
                if (!hostingLobby) {
                    lock (OutboundMessages) {
                        if (OutboundMessages.Count > 0) {
                            var message = OutboundMessages.Dequeue();
                            _logger.Info("Assigned task - Bot: " + id + " - Message ID: " + message.Id);
                            return new List<Message> { message };
                        } else {
                            return new List<Message>();
                        }
                    }
                }
            } catch (Exception e) {
                _logger.Error(String.Format("Failed to process outgoing coordinator message - Bot ID: " + id), e);
            }
            return new List<Message>();
        }

        [HttpPost("{id}")]
        public void Post(string id, [FromBody] Message message) {
            try {
                _logger.Info("Incoming message - Bot: " + id + " - Message ID: " + message.Id);
                switch (message.Id) {
                    case MessageIds.In.LOBBY_CREATED:
                        ProcessLobbyCreated(JsonConvert.DeserializeObject<LobbyCreated>(message.Payload));
                        break;
                    case MessageIds.In.LOBBY_LEFT:
                        ProcessLobbyLeft(id, JsonConvert.DeserializeObject<LobbyLeft>(message.Payload));
                        break;
                }
            } catch (Exception e) {
                _logger.Error(String.Format("Failed to process incoming coordinator message - Message ID: ", message?.Id), e);
            }
        }

        private void ProcessLobbyCreated(LobbyCreated message) {
            var game = Startup.Config.Games.Where(g => g.Maps.Any(m => m.MapId == message.CustomMapName)).SingleOrDefault();
            if (game == null) {
                throw new Exception("Game with name not found: " + message.CustomMapName);
            }
            var channelId = Startup.Bot.Channels.Where(c => c.Value == game.Chat).SingleOrDefault().Key;
            var region = Startup.Config.ServerRegions.Where(sr => sr.ServerId == message.SeverId).SingleOrDefault().LongName;
            var name = game.Name;
            if (game.Maps.Count > 1) {
                name += " " + game.Maps.Where(m => m.MapId == message.CustomMapName).SingleOrDefault().Name;
            }
            Startup.Bot.DotaClient.QueueChatMessage(channelId, String.Format("New {0} lobby hosted in {1}", name, region), true);
            Startup.Bot.DotaClient.QueueLobbyShare(channelId, message.LobbyId, game.CustomGameMode, true);
            lock (LobbyStates) {
                var lobbyState = LobbyStates.Where(ls => ls.CustomMapName == message.CustomMapName && ls.ServerId == message.SeverId).FirstOrDefault();
                if (lobbyState != null) {
                    lobbyState.LobbyId = message.LobbyId;
                }
            }
        }

        private void ProcessLobbyLeft(string id, LobbyLeft lobbyLeft) {
            lock (LobbyStates) {
                var lobbyState = LobbyStates.Where(ls => ls.CustomMapName == lobbyLeft.CustomMapName && ls.ServerId == lobbyLeft.ServerId).FirstOrDefault();
                if (lobbyState != null) {
                    LobbyStates.Remove(lobbyState);
                }
            }
        }
    }
}
