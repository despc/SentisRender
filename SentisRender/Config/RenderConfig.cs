using SentisRender.GUI;
using Torch;

namespace SentisRender
{
    /// <summary>The plugin's settings (SentisRender.cfg in the instance folder).</summary>
    public class RenderConfig : ViewModel
    {
        private bool _enabled = true;
        private int _reconcileMs = 1000;
        private bool _fallbackNotifications = true;
        private bool _diagnosticLogs;

        [DisplayTab(Name = "Enabled", GroupName = "General", Tab = "General", Order = 0,
            Description = "Send shapes and notices to the client mod. Off: other plugins' calls do nothing. Takes effect at the next server start.")]
        public bool Enabled
        {
            get => _enabled;
            set => SetValue(ref _enabled, value);
        }

        [DisplayTab(Name = "Recheck every (ms)", GroupName = "General", Tab = "General", Order = 1, LiveUpdate = true,
            Description = "How often who sees what is looked at again when nothing changed: shapes shown by distance or to a faction " +
                          "appear and go as players move and change factions. A change made by a plugin is sent at the next frame anyway.")]
        public int ReconcileMs
        {
            get => _reconcileMs;
            set => SetValue(ref _reconcileMs, value < 100 ? 100 : value);
        }

        [DisplayTab(Name = "Notices without the mod", GroupName = "General", Tab = "General", Order = 2, LiveUpdate = true,
            Description = "A player whose client has no SentisRender mod (it failed to download, or the mod is not in \"Client Only Mods\" " +
                          "of SGI) gets the notices as the game's own notifications. Shapes are not drawn for them.")]
        public bool FallbackNotifications
        {
            get => _fallbackNotifications;
            set => SetValue(ref _fallbackNotifications, value);
        }

        [DisplayTab(Name = "Diagnostic logs", GroupName = "Logs", Tab = "Logs", Order = 0, LiveUpdate = true,
            Description = "Log every client mod that says hello.")]
        public bool DiagnosticLogs
        {
            get => _diagnosticLogs;
            set => SetValue(ref _diagnosticLogs, value);
        }
    }
}
