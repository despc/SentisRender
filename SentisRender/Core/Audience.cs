using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace SentisRender
{
    /// <summary>A player as the registry sees them when it decides what they are to be shown.</summary>
    public sealed class Viewer
    {
        public ulong SteamId;
        public long IdentityId;
        /// <summary>0 when not in a faction.</summary>
        public long FactionId;
        public Vector3D Position;
        /// <summary>False while the player has no body (respawn screen): a shape shown by distance is not shown then.</summary>
        public bool HasPosition;
    }

    /// <summary>
    /// Who is shown a shape or a notice: everyone, some players, a faction, or those near a point. <see cref="Within"/>
    /// adds a distance to any of them. Immutable: every method returns a new audience.
    /// </summary>
    public sealed class Audience
    {
        private readonly HashSet<ulong> _players;
        private readonly long _faction;
        private readonly Vector3D _center;
        private readonly double _range;

        private Audience(HashSet<ulong> players, long faction, Vector3D center, double range)
        {
            _players = players;
            _faction = faction;
            _center = center;
            _range = range;
        }

        /// <summary>Every player with the client mod.</summary>
        public static readonly Audience All = new Audience(null, 0, Vector3D.Zero, 0);

        public static Audience Player(ulong steamId) => new Audience(new HashSet<ulong> { steamId }, 0, Vector3D.Zero, 0);

        public static Audience Players(IEnumerable<ulong> steamIds) => new Audience(new HashSet<ulong>(steamIds), 0, Vector3D.Zero, 0);

        /// <summary>The members of a faction (by its id).</summary>
        public static Audience Faction(long factionId) => new Audience(null, factionId, Vector3D.Zero, 0);

        /// <summary>Everyone no farther than <paramref name="range"/> metres from <paramref name="center"/>.</summary>
        public static Audience Near(Vector3D center, double range) => new Audience(null, 0, center, range);

        /// <summary>This audience, only those of it no farther than <paramref name="range"/> metres from <paramref name="center"/>.</summary>
        public Audience Within(Vector3D center, double range) => new Audience(_players, _faction, center, range);

        public bool Sees(Viewer viewer)
        {
            if (_players != null && !_players.Contains(viewer.SteamId)) return false;
            if (_faction != 0 && viewer.FactionId != _faction) return false;
            if (_range > 0)
            {
                if (!viewer.HasPosition) return false;
                if (Vector3D.DistanceSquared(viewer.Position, _center) > _range * _range) return false;
            }
            return true;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (_players != null) parts.Add("players " + string.Join(",", _players.Take(5)) + (_players.Count > 5 ? ",..." : ""));
            if (_faction != 0) parts.Add("faction " + _faction);
            if (_range > 0) parts.Add("within " + _range.ToString("0") + " m");
            return parts.Count == 0 ? "all" : string.Join(", ", parts);
        }
    }
}
