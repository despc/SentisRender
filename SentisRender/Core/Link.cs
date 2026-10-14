using System;
using System.Collections.Generic;
using NLog;
using Sandbox.Game;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SentisRender.Net;
using VRage.Game.ModAPI;

namespace SentisRender
{
    /// <summary>
    /// The game's side of the registry: hears the client mods' hellos, tells the registry who is online and where,
    /// and sends what it decides. Game thread only (Start, Stop, Tick).
    /// </summary>
    internal sealed class Link
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private readonly Registry _registry;
        private readonly Func<RenderConfig> _config;
        private readonly Action<ushort, byte[], ulong, bool> _handler;
        private readonly List<Viewer> _online = new List<Viewer>();
        private bool _started;

        /// <summary>Messages and bytes sent since the start, for the status line.</summary>
        public long SentMessages, SentBytes, FallbackNotices;

        public Link(Registry registry, Func<RenderConfig> config)
        {
            _registry = registry;
            _config = config;
            _handler = OnMessage;
        }

        public void Start()
        {
            if (_started) return;
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(RenderNet.Channel, _handler);
            _started = true;
            Log.Info("SentisRender: listening for the client mod on channel " + RenderNet.Channel);
        }

        public void Stop()
        {
            if (!_started) return;
            MyAPIGateway.Multiplayer?.UnregisterSecureMessageHandler(RenderNet.Channel, _handler);
            _started = false;
        }

        private void OnMessage(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            if (fromServer || data == null || sender == 0) return;
            try
            {
                var hello = MyAPIGateway.Utilities.SerializeFromBinary<ClientHello>(data);
                if (hello == null) return;
                _registry.Hello(sender, hello.Version);
                if (hello.Version != RenderNet.Version)
                    Log.Warn($"SentisRender: the client mod of {sender} speaks version {hello.Version}, the plugin {RenderNet.Version}");
                else if (_config().DiagnosticLogs)
                    Log.Info($"SentisRender: client mod of {sender} is loaded");
            }
            catch (Exception e)
            {
                Log.Warn("SentisRender: a message from " + sender + " was not read: " + e.Message);
            }
        }

        public void Left(ulong steamId) => _registry.Left(steamId);

        public void Tick(DateTime now)
        {
            if (!_started || !_registry.Due(now)) return;
            FillOnline();
            var outgoing = _registry.Collect(_online, now);
            foreach (var pair in outgoing.Batches)
            {
                var bytes = MyAPIGateway.Utilities.SerializeToBinary(pair.Value);
                MyAPIGateway.Multiplayer.SendMessageTo(RenderNet.Channel, bytes, pair.Key);
                SentMessages++;
                SentBytes += bytes.Length;
            }
            if (outgoing.Fallback.Count > 0 && _config().FallbackNotifications)
                foreach (var pair in outgoing.Fallback)
                {
                    var n = pair.Value;
                    if (string.IsNullOrEmpty(n.Text)) continue;
                    MyVisualScriptLogicProvider.ShowNotification(n.Text, n.Ms > 0 ? n.Ms : 2000, string.IsNullOrEmpty(n.Font) ? "White" : n.Font, pair.Key.IdentityId);
                    FallbackNotices++;
                }
        }

        private void FillOnline()
        {
            _online.Clear();
            var session = MySession.Static;
            if (session?.Players == null) return;
            foreach (var player in session.Players.GetOnlinePlayers())
            {
                if (player == null || !player.IsRealPlayer || player.Id.SteamId == 0) continue;
                var identityId = player.Identity?.IdentityId ?? 0;
                var controlled = player.Controller?.ControlledEntity?.Entity;
                _online.Add(new Viewer
                {
                    SteamId = player.Id.SteamId,
                    IdentityId = identityId,
                    FactionId = session.Factions.GetPlayerFaction(identityId)?.FactionId ?? 0,
                    HasPosition = controlled != null,
                    Position = controlled != null ? ((IMyPlayer)player).GetPosition() : default
                });
            }
        }
    }
}
