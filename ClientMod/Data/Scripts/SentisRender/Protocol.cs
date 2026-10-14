using System.Collections.Generic;
using ProtoBuf;
using VRageMath;

// The messages between the SentisRender plugin (server) and the SentisRender client mod. This one file is compiled
// into both: the mod (by the game's script compiler, so C# 6 and the mod whitelist only) and the plugin (linked from
// the plugin's project). Field numbers are the wire format: never reuse or renumber one, only add new ones.
namespace SentisRender.Net
{
    public static class RenderNet
    {
        /// <summary>The channel of RegisterSecureMessageHandler/SendMessageTo both sides use.</summary>
        public const ushort Channel = 47110;

        /// <summary>Bumped when a change needs both sides updated; an older peer is logged, not refused.</summary>
        public const int Version = 1;

        /// <summary>
        /// The most segments a sphere, capsule or cylinder is drawn with: the game steps a sphere by 360 / n whole
        /// degrees (over 360 it never ends) and draws about n*n/2 faces. Divisors of 360 come out even: 12, 18, 20,
        /// 24, 30, 36, 40, 45, 60, 72, 90.
        /// </summary>
        public const int MaxDetail = 90;

        /// <summary>A color as one number: R in the low byte, then G, B, A.</summary>
        public static uint Pack(Color c)
        {
            return c.R | ((uint)c.G << 8) | ((uint)c.B << 16) | ((uint)c.A << 24);
        }

        public static Color Unpack(uint v)
        {
            return new Color((int)(v & 0xFF), (int)((v >> 8) & 0xFF), (int)((v >> 16) & 0xFF), (int)((v >> 24) & 0xFF));
        }
    }

    public enum ShapeKind
    {
        None = 0,
        /// <summary>A sphere at Position with Radius.</summary>
        Sphere = 1,
        /// <summary>A box at Position, half extents Size, turned by Forward/Up (zero: the world's axes).</summary>
        Box = 2,
        /// <summary>A cylinder from Position to To with Radius (drawn as lines).</summary>
        Cylinder = 3,
        /// <summary>A capsule from Position to To with Radius (drawn as faces).</summary>
        Capsule = 4,
        /// <summary>A line or a beam from Position to To, Thickness wide.</summary>
        Line = 5,
        /// <summary>A glowing point at Position with Radius.</summary>
        Point = 6,
        /// <summary>A marker on the HUD at Position named Text (a GPS point of the client's own).</summary>
        Marker = 7
    }

    public enum ShapeStyle
    {
        Solid = 0,
        Wire = 1,
        SolidAndWire = 2
    }

    /// <summary>The client mod tells the server it is loaded and listening; the server answers with everything it is to see.</summary>
    [ProtoContract]
    public class ClientHello
    {
        [ProtoMember(1)] public int Version;
    }

    /// <summary>One thing to draw. The same Id again replaces it.</summary>
    [ProtoContract]
    public class ShapeData
    {
        [ProtoMember(1)] public string Id;
        [ProtoMember(2)] public ShapeKind Kind;
        [ProtoMember(3)] public double X;
        [ProtoMember(4)] public double Y;
        [ProtoMember(5)] public double Z;
        [ProtoMember(6)] public double ToX;
        [ProtoMember(7)] public double ToY;
        [ProtoMember(8)] public double ToZ;
        [ProtoMember(9)] public float Radius;
        [ProtoMember(10)] public float SizeX;
        [ProtoMember(11)] public float SizeY;
        [ProtoMember(12)] public float SizeZ;
        [ProtoMember(13)] public float ForwardX;
        [ProtoMember(14)] public float ForwardY;
        [ProtoMember(15)] public float ForwardZ;
        [ProtoMember(16)] public float UpX;
        [ProtoMember(17)] public float UpY;
        [ProtoMember(18)] public float UpZ;
        /// <summary>RenderNet.Pack of the color, alpha included.</summary>
        [ProtoMember(19)] public uint Color;
        [ProtoMember(20)] public ShapeStyle Style;
        /// <summary>Width of a line or of a wire; 0 is the client's default.</summary>
        [ProtoMember(21)] public float Thickness;
        /// <summary>A transparent material of the game; empty is the client's default for the kind.</summary>
        [ProtoMember(22)] public string Material;
        /// <summary>The marker's name.</summary>
        [ProtoMember(23)] public string Text;
        /// <summary>The client drops the shape this long after it got it unless it is sent again; 0 keeps it.</summary>
        [ProtoMember(24)] public int LifetimeMs;
        /// <summary>Not drawn farther than this from the camera; 0 is always.</summary>
        [ProtoMember(25)] public float DrawDistance;
        /// <summary>Segments of a sphere, capsule or cylinder, up to RenderNet.MaxDetail; 0 is the client's default (20, 12, 24).</summary>
        [ProtoMember(26)] public int Detail;
        /// <summary>Brightness of the faces and lines; 0 is 1.</summary>
        [ProtoMember(27)] public float Intensity;
        /// <summary>Additive blending (glows, adds light) instead of the standard one.</summary>
        [ProtoMember(28)] public bool Additive;

        public Vector3D Position
        {
            get { return new Vector3D(X, Y, Z); }
            set { X = value.X; Y = value.Y; Z = value.Z; }
        }

        public Vector3D To
        {
            get { return new Vector3D(ToX, ToY, ToZ); }
            set { ToX = value.X; ToY = value.Y; ToZ = value.Z; }
        }

        public Vector3 Size
        {
            get { return new Vector3(SizeX, SizeY, SizeZ); }
            set { SizeX = value.X; SizeY = value.Y; SizeZ = value.Z; }
        }

        public Vector3 Forward
        {
            get { return new Vector3(ForwardX, ForwardY, ForwardZ); }
            set { ForwardX = value.X; ForwardY = value.Y; ForwardZ = value.Z; }
        }

        public Vector3 Up
        {
            get { return new Vector3(UpX, UpY, UpZ); }
            set { UpX = value.X; UpY = value.Y; UpZ = value.Z; }
        }
    }

    /// <summary>A line of text on the screen. With an Id the same notification is updated instead of a new one shown.</summary>
    [ProtoContract]
    public class NoticeData
    {
        [ProtoMember(1)] public string Id;
        [ProtoMember(2)] public string Text;
        [ProtoMember(3)] public int Ms;
        /// <summary>A font of the game: White, Red, Green, Blue, DarkBlue, ...</summary>
        [ProtoMember(4)] public string Font;
    }

    /// <summary>What the server sends: applied in the order Reset, ClearPrefixes, Removes, Upserts, Notices.</summary>
    [ProtoContract]
    public class ServerBatch
    {
        /// <summary>Drop everything the client has before applying the rest.</summary>
        [ProtoMember(1)] public bool Reset;
        [ProtoMember(2)] public List<ShapeData> Upserts;
        [ProtoMember(3)] public List<string> Removes;
        /// <summary>Drop every shape whose Id starts with one of these.</summary>
        [ProtoMember(4)] public List<string> ClearPrefixes;
        [ProtoMember(5)] public List<NoticeData> Notices;
        [ProtoMember(6)] public int Version;

        public bool IsEmpty
        {
            get
            {
                return !Reset && (Upserts == null || Upserts.Count == 0) && (Removes == null || Removes.Count == 0)
                       && (ClearPrefixes == null || ClearPrefixes.Count == 0) && (Notices == null || Notices.Count == 0);
            }
        }
    }
}
