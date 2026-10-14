using System;
using System.IO;
using System.Windows.Controls;
using NLog;
using SentisRender.GUI;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Plugins;
using Torch.API.Session;
using Torch.Session;

namespace SentisRender
{
    /// <summary>
    /// Draws on the clients what other plugins ask (<see cref="Render"/>): spheres, boxes, beams, points, HUD markers,
    /// notices. The drawing is done by the SentisRender client mod, which Torch hands to the clients only (the mod's id
    /// in "Client Only Mods" of SGI); this plugin keeps what is to be drawn and sends it.
    /// </summary>
    public class SentisRenderPlugin : TorchPluginBase, IWpfPlugin
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private static Persistent<RenderConfig> _config;

        public static RenderConfig Config => _config?.Data;
        public static SentisRenderPlugin Instance { get; private set; }

        internal Link Link { get; private set; }
        private IMultiplayerManagerBase _multiplayer;
        private ConfigGUI _control;
        private string _configPath;

        public UserControl GetControl() => _control ?? (_control = new ConfigGUI());

        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            Instance = this;
            _configPath = Path.Combine(StoragePath, "SentisRender.cfg");
            _config = Persistent<RenderConfig>.Load(_configPath);
            _config.Save();
            var sessions = Torch.Managers.GetManager<TorchSessionManager>();
            if (sessions != null) sessions.SessionStateChanged += OnSessionState;
        }

        private void OnSessionState(ITorchSession session, TorchSessionState state)
        {
            try
            {
                if (state == TorchSessionState.Loaded) Start(session);
                else if (state == TorchSessionState.Unloading) Stop();
            }
            catch (Exception e)
            {
                Log.Error(e, "SentisRender: " + state + " failed");
            }
        }

        private void Start(ITorchSession session)
        {
            if (!Config.Enabled)
            {
                Log.Info("SentisRender is disabled");
                return;
            }
            Link = new Link(Render.Registry, () => Config);
            Link.Start();
            _multiplayer = session.Managers.GetManager<IMultiplayerManagerBase>();
            if (_multiplayer != null) _multiplayer.PlayerLeft += OnPlayerLeft;
        }

        private void OnPlayerLeft(IPlayer player) => Link?.Left(player.SteamId);

        private void Stop()
        {
            if (_multiplayer != null) _multiplayer.PlayerLeft -= OnPlayerLeft;
            _multiplayer = null;
            Link?.Stop();
            Link = null;
            Render.Registry.Reset();
        }

        /// <summary>Game thread, every frame: sends what changed (and, once a second, what moved players now see or not).</summary>
        public override void Update()
        {
            var link = Link;
            if (link == null) return;
            try
            {
                Render.Registry.ReconcileInterval = TimeSpan.FromMilliseconds(Config.ReconcileMs);
                link.Tick(DateTime.UtcNow);
            }
            catch (Exception e)
            {
                Log.Error(e, "SentisRender: update failed");
            }
        }

        public string Describe()
        {
            var link = Link;
            if (link == null) return Config?.Enabled == false ? "Disabled." : "No world loaded.";
            var registry = Render.Registry;
            return $"Shapes: {registry.ShapeCount}; clients with the mod: {registry.Clients.Count}; " +
                   $"sent {link.SentMessages} messages, {link.SentBytes / 1024} KB; notices without the mod: {link.FallbackNotices}";
        }

        public override void Dispose()
        {
            Stop();
            _config?.Save(_configPath);
            base.Dispose();
        }
    }
}
