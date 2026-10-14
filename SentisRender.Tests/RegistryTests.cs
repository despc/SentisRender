using System;
using System.Collections.Generic;
using System.Linq;
using SentisRender.Net;
using VRageMath;
using Xunit;

namespace SentisRender.Tests
{
    public class RegistryTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

        private static long TokenOf(ulong steamId) => 1000 + (long)steamId;

        private static Viewer Player(ulong steamId, double x = 0, long faction = 0, bool body = true) =>
            new Viewer { SteamId = steamId, IdentityId = (long)steamId * 10, FactionId = faction, Position = new Vector3D(x, 0, 0), HasPosition = body };

        private static ShapeData Sphere(string id, float radius = 10, uint color = 1) =>
            new ShapeData { Id = id, Kind = ShapeKind.Sphere, Radius = radius, Color = color };

        private static List<ServerBatch> For(Outgoing outgoing, ulong steamId) =>
            outgoing.Batches.Where(b => b.Key == steamId).Select(b => b.Value).ToList();

        private static List<string> Upserted(Outgoing outgoing, ulong steamId) =>
            For(outgoing, steamId).SelectMany(b => b.Upserts ?? new List<ShapeData>()).Select(s => s.Id).ToList();

        private static List<string> Removed(Outgoing outgoing, ulong steamId) =>
            For(outgoing, steamId).SelectMany(b => b.Removes ?? new List<string>()).ToList();

        [Fact]
        public void A_client_without_the_mod_is_sent_nothing_and_gets_notices_the_games_way()
        {
            var registry = new Registry();
            registry.Upsert(Sphere("a"));
            registry.Notice(Audience.All, new NoticeData { Text = "hi" });
            var outgoing = registry.Collect(new[] { Player(1) }, T0);
            Assert.Empty(outgoing.Batches);
            Assert.Equal("hi", outgoing.Fallback.Single().Value.Text);
            Assert.Equal(1UL, outgoing.Fallback.Single().Key.SteamId);
        }

        [Fact]
        public void A_hello_is_answered_with_a_reset_and_everything_to_see()
        {
            var registry = new Registry();
            registry.Upsert(Sphere("a"));
            registry.Upsert(Sphere("b"));
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            var outgoing = registry.Collect(new[] { Player(1) }, T0);
            var batch = For(outgoing, 1).Single();
            Assert.True(batch.Reset);
            Assert.Equal(new[] { "a", "b" }, batch.Upserts.Select(s => s.Id).OrderBy(x => x));
        }

        [Fact]
        public void An_empty_world_still_answers_a_hello_with_a_reset()
        {
            var registry = new Registry();
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            Assert.True(For(registry.Collect(new[] { Player(1) }, T0), 1).Single().Reset);
        }

        [Fact]
        public void Nothing_is_sent_again_until_something_changes()
        {
            var registry = new Registry();
            registry.Upsert(Sphere("a"));
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            registry.Collect(new[] { Player(1) }, T0);
            Assert.False(registry.Due(T0.AddMilliseconds(500)));
            Assert.True(registry.Due(T0.AddSeconds(1)));
            Assert.Empty(registry.Collect(new[] { Player(1) }, T0.AddSeconds(1)).Batches);

            registry.Upsert(Sphere("a"));
            Assert.False(registry.Due(T0.AddMilliseconds(1100)), "the same shape again is not a change");
        }

        [Fact]
        public void A_change_sends_only_the_shape_that_changed()
        {
            var registry = new Registry();
            registry.Upsert(Sphere("a"));
            registry.Upsert(Sphere("b"));
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            registry.Collect(new[] { Player(1) }, T0);

            Assert.True(registry.Change("a", d => d.Color = 99));
            var outgoing = registry.Collect(new[] { Player(1) }, T0.AddMilliseconds(16));
            var batch = For(outgoing, 1).Single();
            Assert.False(batch.Reset);
            Assert.Equal(99u, batch.Upserts.Single(s => s.Id == "a").Color);
            Assert.Single(batch.Upserts);
            Assert.False(registry.Change("missing", d => d.Color = 1));
        }

        [Fact]
        public void A_removed_shape_is_removed_on_the_client()
        {
            var registry = new Registry();
            registry.Upsert(Sphere("zone:1"));
            registry.Upsert(Sphere("zone:2"));
            registry.Upsert(Sphere("other"));
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            registry.Collect(new[] { Player(1) }, T0);

            Assert.Equal(2, registry.ClearPrefix("zone:"));
            Assert.Equal(new[] { "zone:1", "zone:2" }, Removed(registry.Collect(new[] { Player(1) }, T0.AddSeconds(1)), 1).OrderBy(x => x));
            Assert.Equal(new[] { "other" }, registry.Ids());
        }

        [Fact]
        public void A_shape_shown_near_a_point_comes_and_goes_with_the_player()
        {
            var registry = new Registry();
            registry.Upsert(Sphere("zone"));
            registry.SetAudience("zone", Audience.Near(Vector3D.Zero, 1000));
            registry.Hello(1, RenderNet.Version, TokenOf(1));

            Assert.Empty(Upserted(registry.Collect(new[] { Player(1, x: 5000) }, T0), 1));
            Assert.Equal(new[] { "zone" }, Upserted(registry.Collect(new[] { Player(1, x: 500) }, T0.AddSeconds(1)), 1));
            Assert.Equal(new[] { "zone" }, Removed(registry.Collect(new[] { Player(1, x: 500, body: false) }, T0.AddSeconds(2)), 1));
        }

        [Fact]
        public void A_shape_shown_to_a_faction_is_seen_by_its_members_only()
        {
            var registry = new Registry();
            registry.Upsert(Sphere("base"));
            registry.SetAudience("base", Audience.Faction(7));
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            registry.Hello(2, RenderNet.Version, TokenOf(2));
            var outgoing = registry.Collect(new[] { Player(1, faction: 7), Player(2, faction: 8) }, T0);
            Assert.Equal(new[] { "base" }, Upserted(outgoing, 1));
            Assert.Empty(Upserted(outgoing, 2));
        }

        [Fact]
        public void A_shape_with_a_lifetime_goes_on_the_server_and_tells_the_client_what_is_left()
        {
            var registry = new Registry();
            registry.Upsert(Sphere("beam"));
            registry.SetExpiry("beam", T0.AddSeconds(3));
            registry.Hello(1, RenderNet.Version, TokenOf(1));

            var sent = For(registry.Collect(new[] { Player(1) }, T0.AddSeconds(1)), 1).Single().Upserts.Single();
            Assert.Equal(2000, sent.LifetimeMs);

            registry.Collect(new[] { Player(1) }, T0.AddSeconds(3));
            Assert.Empty(registry.Ids());
        }

        [Fact]
        public void Many_shapes_go_in_several_messages_the_first_with_the_reset()
        {
            var registry = new Registry();
            for (var i = 0; i < Registry.MaxUpsertsPerBatch * 2 + 5; i++) registry.Upsert(Sphere("s" + i));
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            var batches = For(registry.Collect(new[] { Player(1) }, T0), 1);
            Assert.Equal(3, batches.Count);
            Assert.True(batches[0].Reset);
            Assert.False(batches[1].Reset);
            Assert.Equal(Registry.MaxUpsertsPerBatch * 2 + 5, batches.Sum(b => b.Upserts.Count));
        }

        [Fact]
        public void A_second_hello_starts_the_client_over()
        {
            var registry = new Registry();
            registry.Upsert(Sphere("a"));
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            registry.Collect(new[] { Player(1) }, T0);
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            var batch = For(registry.Collect(new[] { Player(1) }, T0.AddMilliseconds(16)), 1).Single();
            Assert.True(batch.Reset);
            Assert.Equal("a", batch.Upserts.Single().Id);
        }

        [Fact]
        public void A_notice_goes_to_its_audience_only()
        {
            var registry = new Registry();
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            registry.Hello(2, RenderNet.Version, TokenOf(2));
            registry.Collect(new[] { Player(1), Player(2) }, T0);
            registry.Notice(Audience.Player(2), new NoticeData { Id = "p", Text = "45%", Ms = 1000 });
            var outgoing = registry.Collect(new[] { Player(1), Player(2) }, T0.AddMilliseconds(16));
            Assert.Empty(For(outgoing, 1));
            Assert.Equal("45%", For(outgoing, 2).Single().Notices.Single().Text);
            Assert.Empty(registry.Collect(new[] { Player(1), Player(2) }, T0.AddSeconds(2)).Batches);
        }

        [Fact]
        public void A_player_who_left_is_sent_nothing_and_a_new_upsert_keeps_the_audience()
        {
            var registry = new Registry();
            registry.Upsert(Sphere("a"));
            registry.SetAudience("a", Audience.Player(1));
            registry.Upsert(Sphere("a", radius: 20));
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            registry.Hello(2, RenderNet.Version, TokenOf(2));
            var outgoing = registry.Collect(new[] { Player(1), Player(2) }, T0);
            Assert.Equal(new[] { "a" }, Upserted(outgoing, 1));
            Assert.Empty(Upserted(outgoing, 2));

            registry.Left(1);
            registry.Change("a", d => d.Radius = 30);
            Assert.Empty(registry.Collect(new[] { Player(2) }, T0.AddSeconds(1)).Batches);
        }
    
        [Fact]
        public void Every_batch_carries_the_token_of_its_clients_hello()
        {
            var registry = new Registry();
            for (var i = 0; i < Registry.MaxUpsertsPerBatch + 3; i++) registry.Upsert(Sphere("s" + i));
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            registry.Hello(2, RenderNet.Version, TokenOf(2));
            var outgoing = registry.Collect(new[] { Player(1), Player(2) }, T0);
            Assert.Equal(2, For(outgoing, 1).Count);
            Assert.All(For(outgoing, 1), b => Assert.Equal(TokenOf(1), b.Token));
            Assert.All(For(outgoing, 2), b => Assert.Equal(TokenOf(2), b.Token));

            registry.Hello(1, RenderNet.Version, 777);
            Assert.All(For(registry.Collect(new[] { Player(1) }, T0.AddSeconds(1)), 1), b => Assert.Equal(777, b.Token));
        }
    
        [Fact]
        public void A_solid_and_wire_shape_without_a_wire_color_gets_one_half_as_see_through()
        {
            var registry = new Registry();
            var color = new Color(40, 220, 60, 80);
            registry.Upsert(new ShapeData { Id = "both", Kind = ShapeKind.Sphere, Radius = 10, Style = ShapeStyle.SolidAndWire, Color = RenderNet.Pack(color) });
            registry.Upsert(new ShapeData { Id = "own", Kind = ShapeKind.Sphere, Radius = 10, Style = ShapeStyle.SolidAndWire, Color = RenderNet.Pack(color), WireColor = 5 });
            registry.Upsert(new ShapeData { Id = "wire", Kind = ShapeKind.Sphere, Radius = 10, Style = ShapeStyle.Wire, Color = RenderNet.Pack(color) });
            registry.Hello(1, RenderNet.Version, TokenOf(1));
            var sent = For(registry.Collect(new[] { Player(1) }, T0), 1).Single().Upserts.ToDictionary(u => u.Id);

            var wire = RenderNet.Unpack(sent["both"].WireColor);
            Assert.Equal(168, wire.A);
            Assert.Equal(new[] { 40, 220, 60 }, new[] { (int)wire.R, wire.G, wire.B });
            Assert.Equal(5u, sent["own"].WireColor);
            Assert.Equal(0u, sent["wire"].WireColor);
            Assert.Equal(0u, registry.Get("both").WireColor);
        }
    }
}
