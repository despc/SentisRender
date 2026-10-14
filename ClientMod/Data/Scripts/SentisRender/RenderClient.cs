using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using SentisRender.Net;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using VRageRender;

// The client half of SentisRender: draws what the server's SentisRender plugin sends. The server never loads this
// mod (Torch hands it only to the clients, "Client Only Mods" of SGI), and on a server or in one's own game it does
// nothing. C# 6 and the mod whitelist only: the game compiles it on the client.
namespace SentisRender.Client
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class RenderClient : MySessionComponentBase
    {
        /// <summary>Frames between hellos until the server answers (a hello can go before the server listens).</summary>
        private const int HelloRetryFrames = 600;

        private static readonly MyStringId FaceDefault = MyStringId.GetOrCompute("Square");
        private static readonly MyStringId WireDefault = MyStringId.GetOrCompute("GizmoDrawLine");
        private static readonly MyStringId BeamDefault = MyStringId.GetOrCompute("WeaponLaser");
        private static readonly MyStringId PointDefault = MyStringId.GetOrCompute("WhiteDot");

        private class Live
        {
            public ShapeData Data;
            public DateTime? Until;
            /// <summary>Where and how the shape stands as sent; Matrix is it turned by the spin at this frame.</summary>
            public MatrixD BaseMatrix;
            public MatrixD Matrix;
            public Vector3D SpinAxis;
            public double SpinRadians;
            public DateTime SpinFrom;
            public Vector3 BaseDirection;
            public Vector3D LineStart;
            /// <summary>The color as sent (for the HUD marker).</summary>
            public Color Straight;
            /// <summary>Premultiplied by its alpha, as the transparent geometry wants it.</summary>
            public Color Color;
            /// <summary>The wire's color, premultiplied; the same as Color unless the shape has one of its own.</summary>
            public Color WireColor;
            public bool OwnWireColor;
            public Vector4 Color4;
            public MyStringId Face;
            public MyStringId Wire;
            public Vector3D Center;
            public double Reach;
            public float Length;
            public Vector3 Direction;
            public IMyGps Gps;
        }

        private readonly Dictionary<string, Live> _shapes = new Dictionary<string, Live>();
        private readonly Dictionary<string, IMyHudNotification> _notices = new Dictionary<string, IMyHudNotification>();
        private readonly List<string> _drop = new List<string>();
        private Action<ushort, byte[], ulong, bool> _handler;
        private bool _active;
        private bool _answered;
        private int _helloIn = 60;
        private bool _drawFailed;
        private long _token;
        private bool _rejectLogged;

        public override void LoadData()
        {
            if (MyAPIGateway.Session == null || MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer) return;
            _active = true;
            _token = NewToken();
            _handler = OnMessage;
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(RenderNet.Channel, _handler);
        }

        protected override void UnloadData()
        {
            if (!_active) return;
            _active = false;
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(RenderNet.Channel, _handler);
            RemoveAll();
            foreach (var note in _notices.Values) note.Hide();
            _notices.Clear();
        }

        public override void UpdateAfterSimulation()
        {
            if (!_active || _answered) return;
            if (--_helloIn > 0) return;
            if (MyAPIGateway.Session.Player == null)
            {
                _helloIn = 60;
                return;
            }
            _helloIn = HelloRetryFrames;
            var hello = MyAPIGateway.Utilities.SerializeToBinary(new ClientHello { Version = RenderNet.Version, Token = _token });
            MyAPIGateway.Multiplayer.SendMessageToServer(RenderNet.Channel, hello);
        }

        private void OnMessage(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            if (!fromServer || data == null) return;
            try
            {
                var batch = MyAPIGateway.Utilities.SerializeFromBinary<ServerBatch>(data);
                if (batch == null) return;
                if (batch.Token != _token)
                {
                    // not the server's answer to our hello: another client's broadcast, relayed by the server
                    if (!_rejectLogged) MyLog.Default.WriteLine("SentisRender: a message without this session's token was dropped");
                    _rejectLogged = true;
                    return;
                }
                Apply(batch);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine("SentisRender: a message from the server was not read: " + e.Message);
            }
        }

        private static Color Premultiplied(Color c)
        {
            var a = c.A / 255f;
            return new Color((int)(c.R * a), (int)(c.G * a), (int)(c.B * a), c.A);
        }

        private static long NewToken()
        {
            var token = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
            return token != 0 ? token : 1;
        }

        private void Apply(ServerBatch batch)
        {
            if (batch.Reset)
            {
                _answered = true;
                RemoveAll();
            }
            if (batch.ClearPrefixes != null)
                foreach (var prefix in batch.ClearPrefixes)
                {
                    _drop.Clear();
                    foreach (var id in _shapes.Keys)
                        if (id.StartsWith(prefix, StringComparison.Ordinal)) _drop.Add(id);
                    foreach (var id in _drop) Remove(id);
                }
            if (batch.Removes != null)
                foreach (var id in batch.Removes) Remove(id);
            if (batch.Upserts != null)
                foreach (var shape in batch.Upserts) Set(shape);
            if (batch.Notices != null)
                foreach (var notice in batch.Notices) Show(notice);
        }

        private void Set(ShapeData d)
        {
            if (d == null || string.IsNullOrEmpty(d.Id)) return;
            Live live;
            if (!_shapes.TryGetValue(d.Id, out live))
            {
                live = new Live();
                _shapes[d.Id] = live;
            }
            live.Data = d;
            live.Until = d.LifetimeMs > 0 ? DateTime.UtcNow.AddMilliseconds(d.LifetimeMs) : (DateTime?)null;
            // the game blends transparent faces and lines as premultiplied: color channels times alpha, or an alpha of
            // 80 still draws the color at full strength (an all but opaque sphere)
            var straight = RenderNet.Unpack(d.Color);
            var alpha = straight.A / 255f;
            live.Straight = straight;
            live.Color = new Color((int)(straight.R * alpha), (int)(straight.G * alpha), (int)(straight.B * alpha), straight.A);
            live.Color4 = live.Color.ToVector4() * (d.Intensity > 0 ? d.Intensity : 1f);
            live.OwnWireColor = d.WireColor != 0;
            live.WireColor = live.OwnWireColor ? Premultiplied(RenderNet.Unpack(d.WireColor)) : live.Color;
            var custom = string.IsNullOrEmpty(d.Material) ? MyStringId.NullOrEmpty : MyStringId.GetOrCompute(d.Material);
            live.Face = custom != MyStringId.NullOrEmpty ? custom : FaceDefault;
            live.Wire = custom != MyStringId.NullOrEmpty ? custom
                : d.Kind == ShapeKind.Line ? BeamDefault
                : d.Kind == ShapeKind.Point ? PointDefault
                : WireDefault;

            var position = d.Position;
            switch (d.Kind)
            {
                case ShapeKind.Box:
                    live.Matrix = Oriented(d, position);
                    live.Center = position;
                    live.Reach = d.Size.Length();
                    break;
                case ShapeKind.Cylinder:
                case ShapeKind.Capsule:
                case ShapeKind.Line:
                    var axis = d.To - position;
                    var length = axis.Length();
                    var direction = length > 1e-6 ? axis / length : Vector3D.Up;
                    live.Length = (float)length;
                    live.Direction = (Vector3)direction;
                    live.Center = position + axis * 0.5;
                    live.Matrix = MatrixD.CreateWorld(live.Center, Vector3D.CalculatePerpendicularVector(direction), direction);
                    live.Reach = length * 0.5 + d.Radius;
                    break;
                default:
                    live.Matrix = Oriented(d, position);
                    live.Center = position;
                    live.Reach = d.Radius;
                    break;
            }
            live.BaseMatrix = live.Matrix;
            live.BaseDirection = live.Direction;
            live.LineStart = position;

            // a spin that did not change keeps its phase: a shape sent again (a new color) does not jump
            var speed = MathHelper.ToRadians((double)d.SpinSpeed);
            var spinAxis = (Vector3D)d.SpinAxis;
            spinAxis = spinAxis.LengthSquared() > 1e-12 ? Vector3D.Normalize(spinAxis) : live.BaseMatrix.Up;
            if (speed != live.SpinRadians || spinAxis != live.SpinAxis) live.SpinFrom = DateTime.UtcNow;
            live.SpinRadians = speed;
            live.SpinAxis = spinAxis;

            if (d.Kind == ShapeKind.Marker) SetMarker(live);
            else if (live.Gps != null) RemoveMarker(live);
        }

        /// <summary>At the position, turned by the shape's forward/up when it has them.</summary>
        private static MatrixD Oriented(ShapeData d, Vector3D position)
        {
            var forward = d.Forward;
            var up = d.Up;
            return forward.LengthSquared() > 0 && up.LengthSquared() > 0
                ? MatrixD.CreateWorld(position, forward, up)
                : MatrixD.CreateTranslation(position);
        }

        /// <summary>Turns the shape about its center by its spin for this frame.</summary>
        private static void Turn(Live live, DateTime now)
        {
            if (live.SpinRadians == 0) return;
            var angle = live.SpinRadians * (now - live.SpinFrom).TotalSeconds % (2 * Math.PI);
            var rotation = MatrixD.CreateFromAxisAngle(live.SpinAxis, angle);
            var turned = live.BaseMatrix;
            turned.Translation = Vector3D.Zero;
            turned = turned * rotation;
            turned.Translation = live.Center;
            live.Matrix = turned;
            if (live.Data.Kind == ShapeKind.Line)
            {
                live.Direction = (Vector3)Vector3D.TransformNormal((Vector3D)live.BaseDirection, rotation);
                live.LineStart = live.Center - (Vector3D)live.Direction * (live.Length * 0.5);
            }
        }

        private void SetMarker(Live live)
        {
            var d = live.Data;
            var name = d.Text ?? "";
            var gps = live.Gps;
            if (gps != null && gps.Name == name && gps.Coords == d.Position && gps.GPSColor == live.Straight) return;
            if (gps != null) RemoveMarker(live);
            gps = MyAPIGateway.Session.GPS.Create(name, "", d.Position, true, true);
            gps.GPSColor = live.Straight;
            MyAPIGateway.Session.GPS.AddLocalGps(gps);
            live.Gps = gps;
        }

        private static void RemoveMarker(Live live)
        {
            if (live.Gps == null) return;
            MyAPIGateway.Session.GPS.RemoveLocalGps(live.Gps);
            live.Gps = null;
        }

        private void Remove(string id)
        {
            Live live;
            if (!_shapes.TryGetValue(id, out live)) return;
            RemoveMarker(live);
            _shapes.Remove(id);
        }

        private void RemoveAll()
        {
            foreach (var live in _shapes.Values) RemoveMarker(live);
            _shapes.Clear();
        }

        private void Show(NoticeData n)
        {
            if (n == null) return;
            var ms = n.Ms > 0 ? n.Ms : 2000;
            var font = string.IsNullOrEmpty(n.Font) ? "White" : n.Font;
            if (string.IsNullOrEmpty(n.Id))
            {
                if (!string.IsNullOrEmpty(n.Text)) MyAPIGateway.Utilities.ShowNotification(n.Text, ms, font);
                return;
            }
            IMyHudNotification note;
            if (string.IsNullOrEmpty(n.Text))
            {
                if (_notices.TryGetValue(n.Id, out note))
                {
                    note.Hide();
                    _notices.Remove(n.Id);
                }
                return;
            }
            if (!_notices.TryGetValue(n.Id, out note))
            {
                note = MyAPIGateway.Utilities.CreateNotification(n.Text, ms, font);
                _notices[n.Id] = note;
            }
            else
            {
                // the HUD copies the text when the notification is added: a shown one is re-added to show a new text
                if (note.Text != n.Text || note.Font != font) note.Hide();
                note.Text = n.Text;
                note.Font = font;
                note.AliveTime = ms;
                note.ResetAliveTime();
            }
            note.Show();
        }

        public override void Draw()
        {
            if (!_active || _shapes.Count == 0) return;
            var camera = MyAPIGateway.Session.Camera;
            if (camera == null) return;
            var eye = camera.WorldMatrix.Translation;
            var now = DateTime.UtcNow;
            _drop.Clear();
            foreach (var pair in _shapes)
            {
                var live = pair.Value;
                if (live.Until.HasValue && now > live.Until.Value)
                {
                    _drop.Add(pair.Key);
                    continue;
                }
                var d = live.Data;
                if (d.Kind == ShapeKind.Marker) continue;
                if (d.DrawDistance > 0 && Vector3D.Distance(eye, live.Center) - live.Reach > d.DrawDistance) continue;
                try
                {
                    Turn(live, now);
                    DrawOne(live);
                }
                catch (Exception e)
                {
                    if (_drawFailed) continue;
                    _drawFailed = true;
                    MyLog.Default.WriteLine("SentisRender: " + d.Kind + " " + d.Id + " was not drawn: " + e);
                }
            }
            foreach (var id in _drop) Remove(id);
        }

        /// <summary>
        /// Segments of a sphere, capsule or cylinder. The game steps a sphere by 360 / n whole degrees: over 360 the step
        /// is 0 and its loop never ends (the client hangs), and n*n/2 faces a frame grow fast; so 4..90, 0 the default.
        /// </summary>
        private static int Segments(int detail, int fallback)
        {
            if (detail <= 0) return fallback;
            return detail < 4 ? 4 : detail > RenderNet.MaxDetail ? RenderNet.MaxDetail : detail;
        }

        private static void DrawOne(Live live)
        {
            var d = live.Data;
            var blend = d.Additive ? MyBillboard.BlendTypeEnum.AdditiveBottom : MyBillboard.BlendTypeEnum.Standard;
            var intensity = d.Intensity > 0 ? d.Intensity : 1f;
            var rasterizer = d.Style == ShapeStyle.Wire ? MySimpleObjectRasterizer.Wireframe
                : d.Style == ShapeStyle.SolidAndWire ? MySimpleObjectRasterizer.SolidAndWireframe
                : MySimpleObjectRasterizer.Solid;
            switch (d.Kind)
            {
                case ShapeKind.Sphere:
                {
                    var thickness = d.Thickness > 0 ? d.Thickness : Math.Max(0.05f, d.Radius / 500f);
                    var segments = Segments(d.Detail, 20);
                    if (live.OwnWireColor && rasterizer != MySimpleObjectRasterizer.Solid)
                    {
                        // the game draws a sphere's faces and wire in one color: two passes for two
                        if (rasterizer == MySimpleObjectRasterizer.SolidAndWireframe)
                            MySimpleObjectDraw.DrawTransparentSphere(ref live.Matrix, d.Radius, ref live.Color, MySimpleObjectRasterizer.Solid,
                                segments, live.Face, live.Wire, thickness, -1, null, blend, intensity);
                        MySimpleObjectDraw.DrawTransparentSphere(ref live.Matrix, d.Radius, ref live.WireColor, MySimpleObjectRasterizer.Wireframe,
                            segments, live.Face, live.Wire, thickness, -1, null, blend, intensity);
                    }
                    else
                        MySimpleObjectDraw.DrawTransparentSphere(ref live.Matrix, d.Radius, ref live.Color, rasterizer,
                            segments, live.Face, live.Wire, thickness, -1, null, blend, intensity);
                    break;
                }
                case ShapeKind.Box:
                {
                    var size = d.Size;
                    var box = new BoundingBoxD(-size, size);
                    var thickness = d.Thickness > 0 ? d.Thickness : Math.Max(0.02f, size.AbsMax() / 200f);
                    if (live.OwnWireColor && rasterizer != MySimpleObjectRasterizer.Solid)
                    {
                        if (rasterizer == MySimpleObjectRasterizer.SolidAndWireframe)
                            MySimpleObjectDraw.DrawTransparentBox(ref live.Matrix, ref box, ref live.Color, MySimpleObjectRasterizer.Solid, 1,
                                thickness, live.Face, live.Wire, false, -1, blend, intensity);
                        MySimpleObjectDraw.DrawTransparentBox(ref live.Matrix, ref box, ref live.WireColor, MySimpleObjectRasterizer.Wireframe, 1,
                            thickness, live.Face, live.Wire, false, -1, blend, intensity);
                    }
                    else
                        MySimpleObjectDraw.DrawTransparentBox(ref live.Matrix, ref box, ref live.Color, rasterizer, 1, thickness,
                            live.Face, live.Wire, false, -1, blend, intensity);
                    break;
                }
                case ShapeKind.Cylinder:
                {
                    var thickness = d.Thickness > 0 ? d.Thickness : Math.Max(0.02f, d.Radius / 100f);
                    MySimpleObjectDraw.DrawTransparentCylinder(ref live.Matrix, d.Radius, d.Radius, live.Length, ref live.Color4,
                        true, Segments(d.Detail, 24), thickness, live.Wire);
                    break;
                }
                case ShapeKind.Capsule:
                    MySimpleObjectDraw.DrawTransparentCapsule(ref live.Matrix, d.Radius, live.Length, ref live.Color,
                        Segments(d.Detail, 12), live.Face, -1, blend);
                    break;
                case ShapeKind.Line:
                    MyTransparentGeometry.AddLineBillboard(live.Wire, live.Color4, live.LineStart, live.Direction, live.Length,
                        d.Thickness > 0 ? d.Thickness : 0.1f, blend, -1, intensity);
                    break;
                case ShapeKind.Point:
                    MyTransparentGeometry.AddPointBillboard(live.Wire, live.Color4, d.Position, d.Radius > 0 ? d.Radius : 1f, 0f, -1, blend);
                    break;
            }
        }
    }
}
