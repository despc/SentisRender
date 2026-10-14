using System;
using System.Collections.Generic;
using System.Linq;
using SentisRender.Net;

namespace SentisRender
{
    /// <summary>What <see cref="Registry.Collect"/> decided to send: a batch per client with the mod, and the notices of
    /// players without it (shown to them by the game's own notification).</summary>
    public sealed class Outgoing
    {
        public readonly List<KeyValuePair<ulong, ServerBatch>> Batches = new List<KeyValuePair<ulong, ServerBatch>>();
        public readonly List<KeyValuePair<Viewer, NoticeData>> Fallback = new List<KeyValuePair<Viewer, NoticeData>>();
    }

    /// <summary>
    /// Every shape that is to be drawn now, who is to see it, and what each client with the mod has already been sent.
    /// The game is not touched here: <see cref="Link"/> gives the online players and sends what <see cref="Collect"/>
    /// returns. Safe from any thread.
    /// </summary>
    public sealed class Registry
    {
        /// <summary>Shapes in one message at most; more go in the messages after it.</summary>
        public const int MaxUpsertsPerBatch = 64;

        private sealed class Entry
        {
            public ShapeData Data;
            public Audience Audience;
            public long Version;
            public DateTime? ExpiresUtc;
        }

        private sealed class ClientState
        {
            public readonly Dictionary<string, long> Has = new Dictionary<string, long>(StringComparer.Ordinal);
            public bool NeedsReset = true;
            public int Version;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<ulong, ClientState> _clients = new Dictionary<ulong, ClientState>();
        private readonly List<KeyValuePair<Audience, NoticeData>> _notices = new List<KeyValuePair<Audience, NoticeData>>();
        private long _version;
        private bool _dirty;
        private DateTime _nextReconcile;

        /// <summary>How often who-sees-what is looked at again without a change (players move, change factions).</summary>
        public TimeSpan ReconcileInterval = TimeSpan.FromSeconds(1);

        public int ShapeCount
        {
            get { lock (_lock) return _entries.Count; }
        }

        /// <summary>The players whose client mod said hello this session.</summary>
        public List<ulong> Clients
        {
            get { lock (_lock) return _clients.Keys.ToList(); }
        }

        public bool HasClient(ulong steamId)
        {
            lock (_lock) return _clients.ContainsKey(steamId);
        }

        public List<string> Ids(string prefix = null)
        {
            lock (_lock)
                return _entries.Keys.Where(id => prefix == null || id.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(id => id).ToList();
        }

        /// <summary>Adds the shape or replaces the one with its id.</summary>
        public void Set(ShapeData data, Audience audience, DateTime? expiresUtc = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrEmpty(data.Id)) throw new ArgumentException("A shape needs an id", nameof(data));
            lock (_lock)
            {
                _entries.TryGetValue(data.Id, out var old);
                var copy = data.Clone();
                var audienceOf = audience ?? Audience.All;
                if (old != null && old.Data.SameAs(copy) && old.Audience == audienceOf && old.ExpiresUtc == expiresUtc) return;
                _entries[data.Id] = new Entry { Data = copy, Audience = audienceOf, Version = ++_version, ExpiresUtc = expiresUtc };
                _dirty = true;
            }
        }

        /// <summary>
        /// Adds the shape, or replaces the data of the one with its id keeping its audience and lifetime: a caller that
        /// builds its shape anew every second does not send it again unless it changed.
        /// </summary>
        public void Upsert(ShapeData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrEmpty(data.Id)) throw new ArgumentException("A shape needs an id", nameof(data));
            lock (_lock)
            {
                if (_entries.TryGetValue(data.Id, out var old))
                {
                    if (old.Data.SameAs(data)) return;
                    old.Data = data.Clone();
                    old.Version = ++_version;
                }
                else _entries[data.Id] = new Entry { Data = data.Clone(), Audience = Audience.All, Version = ++_version };
                _dirty = true;
            }
        }

        /// <summary>Changes a shape in place; false when there is no shape with that id. Sent only if it really changed.</summary>
        public bool Change(string id, Action<ShapeData> change)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(id, out var entry)) return false;
                var copy = entry.Data.Clone();
                change(copy);
                copy.Id = id;
                if (copy.SameAs(entry.Data)) return true;
                entry.Data = copy;
                entry.Version = ++_version;
                _dirty = true;
                return true;
            }
        }

        public bool SetAudience(string id, Audience audience)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(id, out var entry)) return false;
                entry.Audience = audience ?? Audience.All;
                _dirty = true;
                return true;
            }
        }

        /// <summary>The shape goes away by itself at this time (on the server and, if the server goes quiet, on the client).</summary>
        public bool SetExpiry(string id, DateTime? expiresUtc)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(id, out var entry)) return false;
                if (entry.ExpiresUtc == expiresUtc) return true;
                entry.ExpiresUtc = expiresUtc;
                entry.Version = ++_version;
                _dirty = true;
                return true;
            }
        }

        public bool Contains(string id)
        {
            lock (_lock) return _entries.ContainsKey(id);
        }

        public ShapeData Get(string id)
        {
            lock (_lock) return _entries.TryGetValue(id, out var entry) ? entry.Data.Clone() : null;
        }

        public bool Remove(string id)
        {
            lock (_lock)
            {
                if (!_entries.Remove(id)) return false;
                _dirty = true;
                return true;
            }
        }

        /// <summary>Removes every shape whose id starts with <paramref name="prefix"/> (a plugin's own, as "zone:").</summary>
        public int ClearPrefix(string prefix)
        {
            lock (_lock)
            {
                var ids = _entries.Keys.Where(id => id.StartsWith(prefix ?? "", StringComparison.Ordinal)).ToList();
                foreach (var id in ids) _entries.Remove(id);
                if (ids.Count > 0) _dirty = true;
                return ids.Count;
            }
        }

        /// <summary>A line on the screen of everyone in <paramref name="audience"/>, at the next send.</summary>
        public void Notice(Audience audience, NoticeData notice)
        {
            lock (_lock)
            {
                _notices.Add(new KeyValuePair<Audience, NoticeData>(audience ?? Audience.All, notice));
                _dirty = true;
            }
        }

        /// <summary>A client's mod is loaded: it gets everything it is to see from scratch.</summary>
        public void Hello(ulong steamId, int version)
        {
            lock (_lock)
            {
                if (!_clients.TryGetValue(steamId, out var client)) _clients[steamId] = client = new ClientState();
                client.NeedsReset = true;
                client.Version = version;
                _dirty = true;
            }
        }

        public void Left(ulong steamId)
        {
            lock (_lock) _clients.Remove(steamId);
        }

        /// <summary>The world is unloaded: nothing is drawn and no client is known any more.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _entries.Clear();
                _clients.Clear();
                _notices.Clear();
                _dirty = false;
            }
        }

        /// <summary>Something changed, or it is time to look at who sees what again.</summary>
        public bool Due(DateTime now)
        {
            lock (_lock) return _dirty || now >= _nextReconcile;
        }

        /// <summary>What each client is to be sent so that it has exactly what it is to see now.</summary>
        public Outgoing Collect(IReadOnlyList<Viewer> online, DateTime now)
        {
            var result = new Outgoing();
            lock (_lock)
            {
                _dirty = false;
                _nextReconcile = now + ReconcileInterval;

                var expired = _entries.Where(e => e.Value.ExpiresUtc.HasValue && e.Value.ExpiresUtc.Value <= now).Select(e => e.Key).ToList();
                foreach (var id in expired) _entries.Remove(id);

                var viewers = new Dictionary<ulong, Viewer>();
                foreach (var v in online) viewers[v.SteamId] = v;

                var noticesOf = new Dictionary<ulong, List<NoticeData>>();
                foreach (var pair in _notices)
                    foreach (var v in online)
                    {
                        if (!pair.Key.Sees(v)) continue;
                        if (_clients.ContainsKey(v.SteamId))
                        {
                            if (!noticesOf.TryGetValue(v.SteamId, out var list)) noticesOf[v.SteamId] = list = new List<NoticeData>();
                            list.Add(pair.Value);
                        }
                        else result.Fallback.Add(new KeyValuePair<Viewer, NoticeData>(v, pair.Value));
                    }
                _notices.Clear();

                foreach (var pair in _clients)
                {
                    if (!viewers.TryGetValue(pair.Key, out var viewer)) continue;
                    var client = pair.Value;
                    var first = new ServerBatch { Version = RenderNet.Version };
                    if (client.NeedsReset)
                    {
                        first.Reset = true;
                        client.Has.Clear();
                        client.NeedsReset = false;
                    }

                    List<string> removes = null;
                    foreach (var has in client.Has.ToList())
                    {
                        if (_entries.TryGetValue(has.Key, out var entry) && entry.Audience.Sees(viewer)) continue;
                        (removes ?? (removes = new List<string>())).Add(has.Key);
                        client.Has.Remove(has.Key);
                    }
                    first.Removes = removes;

                    var upserts = new List<ShapeData>();
                    foreach (var entry in _entries.Values)
                    {
                        if (!entry.Audience.Sees(viewer)) continue;
                        if (client.Has.TryGetValue(entry.Data.Id, out var sent) && sent == entry.Version) continue;
                        upserts.Add(ForClient(entry, now));
                        client.Has[entry.Data.Id] = entry.Version;
                    }
                    noticesOf.TryGetValue(pair.Key, out var notices);
                    first.Notices = notices;

                    var batch = first;
                    for (var i = 0; i < upserts.Count; i += MaxUpsertsPerBatch)
                    {
                        if (batch == null) batch = new ServerBatch { Version = RenderNet.Version };
                        batch.Upserts = upserts.GetRange(i, Math.Min(MaxUpsertsPerBatch, upserts.Count - i));
                        result.Batches.Add(new KeyValuePair<ulong, ServerBatch>(pair.Key, batch));
                        batch = null;
                    }
                    if (batch != null && !batch.IsEmpty) result.Batches.Add(new KeyValuePair<ulong, ServerBatch>(pair.Key, batch));
                }
            }
            return result;
        }

        private static ShapeData ForClient(Entry entry, DateTime now)
        {
            var data = entry.Data.Clone();
            if (entry.ExpiresUtc.HasValue)
                data.LifetimeMs = (int)Math.Max(1, Math.Min(int.MaxValue, (entry.ExpiresUtc.Value - now).TotalMilliseconds));
            return data;
        }
    }
}
