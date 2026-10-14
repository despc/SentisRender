using System;
using System.Globalization;
using SentisRender.Net;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;
using VRageMath;

namespace SentisRender.Commands
{
    /// <summary>
    /// !render - draws test shapes around the admin who types it (ids "test:..."), to see the client mod at work.
    /// Colors: a name (green, red, gray, white, blue, yellow, cyan, orange, purple) or #RRGGBB / #RRGGBBAA.
    /// </summary>
    [Category("render")]
    public class RenderCommands : CommandModule
    {
        private const string Prefix = "test:";

        [Command("status", "Shapes, clients with the mod, what was sent.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Status()
        {
            Context.Respond(SentisRenderPlugin.Instance?.Describe() ?? "SentisRender is not loaded.");
            var ids = Render.Registry.Ids();
            if (ids.Count > 0) Context.Respond("Ids: " + string.Join(", ", ids.Count > 30 ? ids.GetRange(0, 30) : ids) + (ids.Count > 30 ? ", ..." : ""));
        }

        [Command("sphere", "!render sphere [radius=50] [color=green] [solid|wire|both] [alpha=80]: a sphere around you.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Sphere(float radius = 50, string color = "green", string style = "solid", int alpha = 80)
        {
            if (!Here(out var at, out _, out _)) return;
            Render.Sphere(Prefix + "sphere", at, radius, Paint(color, alpha), StyleOf(style));
            Context.Respond($"Sphere of {radius} m at you.");
        }

        [Command("box", "!render box [half size=10] [color=cyan] [solid|wire|both] [alpha=80]: a box around you, turned like you.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Box(float half = 10, string color = "cyan", string style = "wire", int alpha = 80)
        {
            if (!Here(out var at, out var forward, out var up)) return;
            Render.Box(Prefix + "box", at, new Vector3(half), Paint(color, alpha), StyleOf(style), forward, up);
            Context.Respond($"Box of {half * 2} m at you.");
        }

        [Command("cylinder", "!render cylinder [length=50] [radius=5] [color=yellow]: a wire cylinder up from you.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Cylinder(float length = 50, float radius = 5, string color = "yellow")
        {
            if (!Here(out var at, out _, out var up)) return;
            Render.Cylinder(Prefix + "cylinder", at, at + (Vector3D)up * length, radius, Paint(color, 255));
            Context.Respond($"Cylinder of {length} m up from you.");
        }

        [Command("capsule", "!render capsule [length=20] [radius=3] [color=purple] [alpha=100]: a capsule up from you.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Capsule(float length = 20, float radius = 3, string color = "purple", int alpha = 100)
        {
            if (!Here(out var at, out _, out var up)) return;
            Render.Capsule(Prefix + "capsule", at, at + (Vector3D)up * length, radius, Paint(color, alpha));
            Context.Respond($"Capsule of {length} m up from you.");
        }

        [Command("beam", "!render beam [length=200] [color=red] [thickness=0.3] [seconds=0]: a laser where you look (seconds: its lifetime, 0 stays).")]
        [Permission(MyPromoteLevel.Admin)]
        public void Beam(float length = 200, string color = "red", float thickness = 0.3f, float seconds = 0)
        {
            if (!Here(out var at, out var forward, out _)) return;
            var from = at + (Vector3D)forward * 2;
            var beam = Render.Line(Prefix + "beam", from, from + (Vector3D)forward * length, Paint(color, 255), thickness).Additive();
            if (seconds > 0) beam.Lifetime(TimeSpan.FromSeconds(seconds));
            Context.Respond($"Beam of {length} m where you look.");
        }

        [Command("point", "!render point [radius=2] [color=white] [distance=20]: a glowing point in front of you.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Point(float radius = 2, string color = "white", float distance = 20)
        {
            if (!Here(out var at, out var forward, out _)) return;
            Render.Point(Prefix + "point", at + (Vector3D)forward * distance, radius, Paint(color, 255)).Additive();
            Context.Respond("Point in front of you.");
        }

        [Command("marker", "!render marker [text=Marker] [color=orange]: a HUD marker where you stand.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Marker(string text = "Marker", string color = "orange")
        {
            if (!Here(out var at, out _, out _)) return;
            Render.Marker(Prefix + "marker", at, text, Paint(color, 255));
            Context.Respond("Marker where you stand.");
        }

        [Command("color", "!render color <id> <color> [alpha=80]: recolors a shape (ids: !render status).")]
        [Permission(MyPromoteLevel.Admin)]
        public void Recolor(string id, string color, int alpha = 80)
        {
            if (!Render.Exists(id) && Render.Exists(Prefix + id)) id = Prefix + id;
            Context.Respond(Render.Exists(id) ? "Recolored " + id + "." : "No shape " + id + ".");
            Render.Shape(id).Color(Paint(color, alpha));
        }

        [Command("notify", "!render notify <text> [ms=3000] [font=White] [all]: a line on your screen (all: on everyone's).")]
        [Permission(MyPromoteLevel.Admin)]
        public void Notify(string text, int ms = 3000, string font = "White", string who = "")
        {
            if (who.Equals("all", StringComparison.OrdinalIgnoreCase)) Render.Notify(Audience.All, text, ms, font);
            else if (Context.Player != null) Render.Notify(Context.Player.SteamUserId, text, ms, font);
            else
            {
                Context.Respond("From the console only to all: !render notify <text> <ms> <font> all");
                return;
            }
            Context.Respond("Sent.");
        }

        [Command("clear", "!render clear [prefix=test:]: removes the shapes whose ids start with the prefix (\"*\": all).")]
        [Permission(MyPromoteLevel.Admin)]
        public void Clear(string prefix = Prefix)
        {
            var removed = Render.Clear(prefix == "*" ? "" : prefix);
            Context.Respond($"Removed {removed} shapes.");
        }

        private bool Here(out Vector3D position, out Vector3 forward, out Vector3 up)
        {
            position = default;
            forward = Vector3.Forward;
            up = Vector3.Up;
            var character = Context.Player?.Character;
            if (character == null)
            {
                Context.Respond("Only for a player in the game (with a body).");
                return false;
            }
            var head = character.GetHeadMatrix(true);
            position = character.WorldMatrix.Translation;
            forward = (Vector3)head.Forward;
            up = (Vector3)character.WorldMatrix.Up;
            return true;
        }

        private static ShapeStyle StyleOf(string style)
        {
            switch ((style ?? "").ToLowerInvariant())
            {
                case "wire": return ShapeStyle.Wire;
                case "both": return ShapeStyle.SolidAndWire;
                default: return ShapeStyle.Solid;
            }
        }

        /// <summary>A color by name or #RRGGBB[AA]; <paramref name="alpha"/> unless the hex gives one.</summary>
        internal static Color Paint(string name, int alpha)
        {
            var a = (byte)MathHelper.Clamp(alpha, 0, 255);
            var s = (name ?? "").Trim().ToLowerInvariant();
            if (s.StartsWith("#") && (s.Length == 7 || s.Length == 9) &&
                uint.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                if (s.Length == 9) return new Color((int)(hex >> 24), (int)((hex >> 16) & 0xFF), (int)((hex >> 8) & 0xFF), (int)(hex & 0xFF));
                return new Color((int)(hex >> 16), (int)((hex >> 8) & 0xFF), (int)(hex & 0xFF), a);
            }
            switch (s)
            {
                case "red": return new Color(255, 40, 40, a);
                case "gray": case "grey": return new Color(150, 150, 150, a);
                case "white": return new Color(255, 255, 255, a);
                case "blue": return new Color(40, 90, 255, a);
                case "yellow": return new Color(255, 230, 40, a);
                case "cyan": return new Color(40, 230, 255, a);
                case "orange": return new Color(255, 140, 20, a);
                case "purple": return new Color(170, 60, 255, a);
                default: return new Color(40, 220, 60, a);
            }
        }
    }
}
