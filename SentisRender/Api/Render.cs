using System;
using SentisRender.Net;
using VRageMath;

namespace SentisRender
{
    /// <summary>
    /// Drawing on the clients from another plugin. A shape lives on until it is removed (or its lifetime runs out) and is
    /// sent to every client of its audience, a client that joins later included; changing it sends only it again.
    /// Ids are the caller's own: start them with the plugin's prefix ("zone:1") so <see cref="Clear"/> takes only its own.
    /// Any thread; what changed goes out at the next frame. Players without the client mod see nothing of it.
    /// <code>
    /// Render.Sphere("zone:1", center, 500, new Color(0, 255, 0, 60)).ShowTo(Audience.Near(center, 5000));
    /// Render.Shape("zone:1").Color(Color.Red);
    /// Render.Notify(Audience.Player(steamId), "CAPTURE 45%", 1000, id: "zone:progress");
    /// </code>
    /// </summary>
    public static class Render
    {
        /// <summary>The plugin's registry of shapes; the same for every caller.</summary>
        public static Registry Registry { get; } = new Registry();

        public static ShapeHandle Sphere(string id, Vector3D center, float radius, Color color, ShapeStyle style = ShapeStyle.Solid) =>
            Put(new ShapeData { Id = id, Kind = ShapeKind.Sphere, Position = center, Radius = radius, Color = RenderNet.Pack(color), Style = style });

        /// <summary>A box with half extents <paramref name="halfSize"/>; turned by forward/up when given, the world's axes otherwise.</summary>
        public static ShapeHandle Box(string id, Vector3D center, Vector3 halfSize, Color color, ShapeStyle style = ShapeStyle.Solid,
            Vector3? forward = null, Vector3? up = null) =>
            Put(new ShapeData
            {
                Id = id, Kind = ShapeKind.Box, Position = center, Size = halfSize, Color = RenderNet.Pack(color), Style = style,
                Forward = forward ?? Vector3.Zero, Up = up ?? Vector3.Zero
            });

        /// <summary>A cylinder of lines from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static ShapeHandle Cylinder(string id, Vector3D from, Vector3D to, float radius, Color color) =>
            Put(new ShapeData { Id = id, Kind = ShapeKind.Cylinder, Position = from, To = to, Radius = radius, Color = RenderNet.Pack(color) });

        public static ShapeHandle Capsule(string id, Vector3D from, Vector3D to, float radius, Color color) =>
            Put(new ShapeData { Id = id, Kind = ShapeKind.Capsule, Position = from, To = to, Radius = radius, Color = RenderNet.Pack(color) });

        /// <summary>A line or a beam (a laser: give it <see cref="ShapeHandle.Additive"/> and a short <see cref="ShapeHandle.Lifetime"/>).</summary>
        public static ShapeHandle Line(string id, Vector3D from, Vector3D to, Color color, float thickness = 0.1f) =>
            Put(new ShapeData { Id = id, Kind = ShapeKind.Line, Position = from, To = to, Color = RenderNet.Pack(color), Thickness = thickness });

        public static ShapeHandle Point(string id, Vector3D position, float radius, Color color) =>
            Put(new ShapeData { Id = id, Kind = ShapeKind.Point, Position = position, Radius = radius, Color = RenderNet.Pack(color) });

        /// <summary>A marker on the HUD, like a GPS point or a beacon's, named <paramref name="text"/>.</summary>
        public static ShapeHandle Marker(string id, Vector3D position, string text, Color color) =>
            Put(new ShapeData { Id = id, Kind = ShapeKind.Marker, Position = position, Text = text, Color = RenderNet.Pack(color) });

        /// <summary>An existing shape to change (calls on a shape that is gone do nothing).</summary>
        public static ShapeHandle Shape(string id) => new ShapeHandle(Registry, id);

        public static bool Exists(string id) => Registry.Contains(id);

        public static bool Remove(string id) => Registry.Remove(id);

        /// <summary>Removes every shape whose id starts with <paramref name="prefix"/>.</summary>
        public static int Clear(string prefix) => Registry.ClearPrefix(prefix);

        /// <summary>
        /// A line of text at the bottom of the screen for <paramref name="ms"/>. With an <paramref name="id"/> the same line
        /// is updated (a counter every second does not stack up). Fonts of the game: White, Red, Green, Blue, DarkBlue...
        /// A player without the client mod gets the game's own notification (if allowed in the settings).
        /// </summary>
        public static void Notify(Audience audience, string text, int ms = 2000, string font = "White", string id = null) =>
            Registry.Notice(audience, new NoticeData { Id = id, Text = text, Ms = ms, Font = font });

        public static void Notify(ulong steamId, string text, int ms = 2000, string font = "White", string id = null) =>
            Notify(Audience.Player(steamId), text, ms, font, id);

        /// <summary>A shape made anew keeps the audience and the lifetime of the one it replaces (a new one: everyone, no end).</summary>
        private static ShapeHandle Put(ShapeData data)
        {
            Registry.Upsert(data);
            return new ShapeHandle(Registry, data.Id);
        }
    }

    /// <summary>A shape to change; every call sends it again if it changed. The calls chain.</summary>
    public sealed class ShapeHandle
    {
        private readonly Registry _registry;

        public string Id { get; }

        internal ShapeHandle(Registry registry, string id)
        {
            _registry = registry;
            Id = id;
        }

        public bool Exists => _registry.Contains(Id);

        private ShapeHandle Change(Action<ShapeData> change)
        {
            _registry.Change(Id, change);
            return this;
        }

        public ShapeHandle ShowTo(Audience audience)
        {
            _registry.SetAudience(Id, audience);
            return this;
        }

        public ShapeHandle Color(Color color) => Change(d => d.Color = RenderNet.Pack(color));
        public ShapeHandle Position(Vector3D position) => Change(d => d.Position = position);
        public ShapeHandle To(Vector3D to) => Change(d => d.To = to);
        public ShapeHandle Radius(float radius) => Change(d => d.Radius = radius);
        public ShapeHandle HalfSize(Vector3 halfSize) => Change(d => d.Size = halfSize);
        public ShapeHandle Orientation(Vector3 forward, Vector3 up) => Change(d => { d.Forward = forward; d.Up = up; });
        public ShapeHandle Text(string text) => Change(d => d.Text = text);
        public ShapeHandle Style(ShapeStyle style) => Change(d => d.Style = style);
        /// <summary>Width of a line or of a wire, metres; 0 is the client's default.</summary>
        public ShapeHandle Thickness(float thickness) => Change(d => d.Thickness = thickness);
        /// <summary>A transparent material of the game ("Square", "WeaponLaser", "SafeZoneShield_Material", ...); empty is the default.</summary>
        public ShapeHandle Material(string material) => Change(d => d.Material = material);
        /// <summary>Not drawn farther than this from the camera; 0 is always.</summary>
        public ShapeHandle DrawDistance(float metres) => Change(d => d.DrawDistance = metres);
        /// <summary>
        /// Segments of a sphere, a capsule or a cylinder, 4..<see cref="RenderNet.MaxDetail"/>; 0 is the client's default
        /// (20 for a sphere). A sphere of n is about n*n/2 faces a frame; divisors of 360 come out even (24, 36, 40, 60).
        /// </summary>
        public ShapeHandle Detail(int segments) =>
            Change(d => d.Detail = segments <= 0 ? 0 : Math.Max(4, Math.Min(RenderNet.MaxDetail, segments)));
        public ShapeHandle Intensity(float intensity) => Change(d => d.Intensity = intensity);
        /// <summary>Additive blending: the shape glows (lasers, energy).</summary>
        public ShapeHandle Additive(bool additive = true) => Change(d => d.Additive = additive);

        /// <summary>The shape goes away by itself after this long, on the server and on the clients.</summary>
        public ShapeHandle Lifetime(TimeSpan lifetime)
        {
            _registry.SetExpiry(Id, DateTime.UtcNow + lifetime);
            return this;
        }

        public bool Remove() => _registry.Remove(Id);
    }
}
