using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class ParallelAssistTests
    {
        const double Top = 0.75;

        static BridgeModel Model() => new BridgeModel(new BudgetPolicy
        {
            CostPerMeter = 1, CostPerSquareMeter = 1, MaterialBudget = 500, NodeCap = 500,
        });

        /// <summary>Adds a straight line from a to b with dots every ~10 cm, as the game would.</summary>
        static int AddStraight(BridgeModel m, Vec3 a, Vec3 b) =>
            Add(m, EqualSegments.Divide(a, b).ToList());

        static int Add(BridgeModel m, List<Vec3> points)
        {
            var r = m.TryAddFreeLine(points.Select(LineVertex.New).ToList());
            Assert.True(r.Ok);
            return r.Id;
        }

        /// <summary>The resolved arc's path divided into ~10 cm pieces.</summary>
        static List<Vec3> ArcDots(ResolvedShape arc)
        {
            var path = arc.Path(0.001);
            int n = EqualSegments.Count(Math.Abs(arc.Sweep) * arc.Radius);
            return Enumerable.Range(0, n + 1).Select(i => path[(int)Math.Round(i * (path.Count - 1) / (double)n)]).ToList();
        }

        static List<Vec3> Noisy(int seed, IEnumerable<Vec3> pts, double noise = 0.002)
        {
            var rng = new Random(seed);
            return pts.Select(p => p + new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * (2 * noise)).ToList();
        }

        /// <summary>A stroke of the given length centered at mid, turned by skew degrees about the vertical.</summary>
        static ResolvedShape DrawnLine(int seed, Vec3 mid, double skewDegrees, double length = 0.9)
        {
            double a = skewDegrees * Math.PI / 180;
            Vec3 dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var pts = Enumerable.Range(0, 181).Select(i => mid + dir * (length * (i / 180.0 - 0.5)));
            var s = ShapeResolver.Resolve(Noisy(seed, pts));
            Assert.Equal(ResolvedKind.Line, s.Kind);
            return s;
        }

        static List<Vec3> ArcPoints(Vec3 center, Vec3 u, Vec3 v, double r, double fromDeg, double toDeg, int n = 200) =>
            Enumerable.Range(0, n + 1).Select(i =>
            {
                double a = (fromDeg + (toDeg - fromDeg) * i / n) * Math.PI / 180;
                return center + u * (r * Math.Cos(a)) + v * (r * Math.Sin(a));
            }).ToList();

        [Fact]
        public void EightDegreeSkew_SnapsExactlyParallel_AndMakesADeck()
        {
            var m = Model();
            int rail = AddStraight(m, new Vec3(0, Top, -0.1), new Vec3(1, Top, -0.1));
            var result = ParallelAssist.Snap(DrawnLine(1, new Vec3(0.5, Top, 0.1), 8), m);

            Assert.True(result.Snapped);
            Assert.Equal(rail, result.PartnerLineId);
            var s = result.Shape;
            Assert.Equal(ResolvedKind.Line, s.Kind);
            Vec3 dir = (s.Points[1] - s.Points[0]).Normalized();
            Assert.Equal(1, Vec3.Dot(dir, Vec3.UnitX), 9);                       // exactly parallel
            Assert.Equal(0, s.Points[0].X, 9);                                   // same span as the partner
            Assert.Equal(1, s.Points[1].X, 9);
            Assert.Equal(s.Points[0].Z, s.Points[1].Z, 9);                       // constant offset
            Assert.InRange(s.Points[0].Z - -0.1, 0.18, 0.22);

            // The snapped line pairs with its partner under AutoDeck's own rules.
            int second = AddStraight(m, s.Points[0], s.Points[1]);
            var deck = Assert.Single(AutoDeck.Find(m));
            Assert.Equal(new[] { rail, second }.OrderBy(x => x), new[] { deck.LineA, deck.LineB }.OrderBy(x => x));
        }

        [Fact]
        public void DrawnBackwards_KeepsItsDirection()
        {
            var m = Model();
            AddStraight(m, new Vec3(0, Top, -0.1), new Vec3(1, Top, -0.1));
            var result = ParallelAssist.Snap(DrawnLine(2, new Vec3(0.5, Top, 0.1), 180 + 5), m);
            Assert.True(result.Snapped);
            Assert.Equal(1, result.Shape.Points[0].X, 9);
            Assert.Equal(0, result.Shape.Points[1].X, 9);
        }

        [Fact]
        public void TwentyFiveDegreeSkew_DoesNotSnap()
        {
            var m = Model();
            AddStraight(m, new Vec3(0, Top, -0.1), new Vec3(1, Top, -0.1));
            var drawn = DrawnLine(3, new Vec3(0.5, Top, 0.1), 25);
            var result = ParallelAssist.Snap(drawn, m);
            Assert.False(result.Snapped);
            Assert.Equal(-1, result.PartnerLineId);
            Assert.Same(drawn, result.Shape);
        }

        [Theory]
        [InlineData(0.05)]     // closer than a car fits
        [InlineData(0.45)]     // farther than a deck spans
        public void OffsetOutsideTheDrivableRange_DoesNotSnap(double spacing)
        {
            var m = Model();
            AddStraight(m, new Vec3(0, Top, 0), new Vec3(1, Top, 0));
            Assert.False(ParallelAssist.Snap(DrawnLine(4, new Vec3(0.5, Top, spacing), 3), m).Snapped);
        }

        [Fact]
        public void FarAway_DoesNotSnap()
        {
            var m = Model();
            AddStraight(m, new Vec3(0, Top, 0), new Vec3(1, Top, 0));
            Assert.False(ParallelAssist.Snap(DrawnLine(5, new Vec3(2.5, Top, 0.2), 2), m).Snapped);
        }

        [Fact]
        public void SideBySideArches_SnapToTheSameArch_Shifted()
        {
            var m = Model();
            // An arch in the plane z = -0.1: center (0.5, 0.5), radius 0.4, from 30 to 150 degrees.
            var partnerShape = ShapeResolver.Resolve(ArcPoints(new Vec3(0.5, 0.5, -0.1), Vec3.UnitX, Vec3.UnitY, 0.4, 30, 150));
            Assert.Equal(ResolvedKind.Arc, partnerShape.Kind);
            int rail = Add(m, ArcDots(partnerShape));

            var drawn = ShapeResolver.Resolve(Noisy(6, ArcPoints(new Vec3(0.5, 0.5, 0.1), Vec3.UnitX, Vec3.UnitY, 0.41, 33, 148)));
            Assert.Equal(ResolvedKind.Arc, drawn.Kind);
            var result = ParallelAssist.Snap(drawn, m);

            Assert.True(result.Snapped);
            Assert.Equal(rail, result.PartnerLineId);
            var partner = ShapeResolver.Resolve(m.LineNodes(rail).Select(id => m.GetNode(id).Position).ToList());
            var s = result.Shape;
            Assert.Equal(partner.Radius, s.Radius, 9);                          // same radius
            Assert.InRange(Math.Abs(Vec3.Dot(s.Center - partner.Center, partner.Normal)), 0.18, 0.22);
            Assert.Equal(Math.Abs(partner.Sweep), Math.Abs(s.Sweep), 9);        // same span
            Assert.All(s.Path(0.01), p => Assert.True(p.IsFinite));
        }

        [Fact]
        public void CurvedRoad_SnapsToAConcentricArc()
        {
            var m = Model();
            // A flat curve: center (0.5, Top, -0.5), radius 0.6, in the horizontal plane.
            var partnerShape = ShapeResolver.Resolve(ArcPoints(new Vec3(0.5, Top, -0.5), Vec3.UnitX, Vec3.UnitZ, 0.6, 50, 130));
            int rail = Add(m, ArcDots(partnerShape));
            var drawn = ShapeResolver.Resolve(Noisy(7, ArcPoints(new Vec3(0.5, Top, -0.5), Vec3.UnitX, Vec3.UnitZ, 0.8, 55, 128)));
            Assert.Equal(ResolvedKind.Arc, drawn.Kind);
            var result = ParallelAssist.Snap(drawn, m);

            Assert.True(result.Snapped);
            Assert.Equal(rail, result.PartnerLineId);
            var partner = ShapeResolver.Resolve(m.LineNodes(rail).Select(id => m.GetNode(id).Position).ToList());
            Assert.True(Vec3.Distance(result.Shape.Center, partner.Center) < 0.01);   // concentric
            Assert.InRange(result.Shape.Radius - partner.Radius, 0.17, 0.23);
        }

        [Fact]
        public void CrookedSecondRail_BetweenPads_StillMakesARoad()
        {
            // The game's flow (W4-04): resolve, snap parallel, then FreeDraw onto the pads.
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Rooms.Draw(m, pads, Rooms.P(0, Top, -0.1), Rooms.P(1, Top, -0.1));
            var drawn = ShapeResolver.Resolve(Noisy(8, Enumerable.Range(0, 181).Select(i =>
                Vec3.Lerp(new Vec3(0.03, Top + 0.02, 0.06), new Vec3(0.97, Top - 0.01, 0.16), i / 180.0))));
            var snapped = ParallelAssist.Snap(drawn, m);
            Assert.True(snapped.Snapped);

            var ctx = new FreeDraw.Context
            {
                Nodes = m.Nodes,
                Joined = (x, y) => m.Members.Any(k => (k.NodeA == x && k.NodeB == y) || (k.NodeA == y && k.NodeB == x)),
                Pads = pads,
            };
            foreach (var chain in FreeDraw.Build(snapped.Shape.Path(), ctx, out _))
            {
                var vertices = chain.Select(v => v.IsExisting ? LineVertex.Existing(v.ExistingId)
                    : v.NewSupport ? LineVertex.Existing(m.AddFixedNode(v.Position, v.SurfaceId, v.Ground)) : LineVertex.New(v.Position)).ToList();
                Assert.True(m.TryAddFreeLine(vertices).Ok);
            }
            var bridge = BridgeCompiler.Compile(m, pads, Constants.Scale.RoomToBridge);
            Assert.Single(bridge.Decks);
            Assert.True(bridge.Road.Ok, bridge.Road.Message);
        }

        [Fact]
        public void PolylineBesideAStraightLine_AndInvalidShapes_PassThrough()
        {
            var m = Model();
            AddStraight(m, new Vec3(0, Top, 0), new Vec3(1, Top, 0));
            var poly = new ResolvedShape { Kind = ResolvedKind.Polyline, Points = { new Vec3(0, Top, 0.2), new Vec3(0.5, Top + 0.2, 0.2), new Vec3(1, Top, 0.2) } };
            Assert.False(ParallelAssist.Snap(poly, m).Snapped);
            Assert.False(ParallelAssist.Snap(ResolvedShape.Invalid(InvalidReason.TooNoisy), m).Snapped);
            Assert.False(ParallelAssist.Snap(null, m).Snapped);
        }

        /// <summary>A hand stroke through the corners: ~5 mm between samples, seeded jitter.</summary>
        static List<Vec3> Through(int seed, params Vec3[] corners)
        {
            var pts = new List<Vec3>();
            for (int c = 0; c + 1 < corners.Length; c++)
            {
                int n = Math.Max(2, (int)(Vec3.Distance(corners[c], corners[c + 1]) / 0.005));
                for (int i = c == 0 ? 0 : 1; i <= n; i++)
                    pts.Add(Vec3.Lerp(corners[c], corners[c + 1], i / (double)n));
            }
            return Noisy(seed, pts);
        }

        /// <summary>A truss chord zigzag in the vertical plane at z, nudged by (dx, dy).</summary>
        static Vec3[] Zigzag(double z, double dx = 0, double dy = 0) => new[]
        {
            new Vec3(0.2 + dx, 1.1 + dy, z), new Vec3(0.3 - dx, 0.95, z), new Vec3(0.4 + dx, 1.1 - dy, z),
            new Vec3(0.5 - dx, 0.95 + dy, z), new Vec3(0.6 + dx, 1.1, z),
        };

        /// <summary>FreeDraw's single chain for a raw stroke, resolved the way the game resolves it for the assist.</summary>
        static ResolvedShape DrawnShape(BridgeModel m, Pad[] pads, List<Vec3> raw)
        {
            var chain = Assert.Single(FreeDraw.Build(raw, Rooms.Context(m, pads), out var kind));
            Assert.Equal(StrokeKind.Polyline, kind);
            return ShapeResolver.Resolve(chain.Select(v => v.Position).ToList());
        }

        [Fact]
        public void ZigzagBesideTheSameZigzag_SnapsToItsCornersShiftedSideways()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            int rail = Assert.Single(Rooms.DrawStroke(m, pads, Through(21, Zigzag(-0.1))));
            var drawn = DrawnShape(m, pads, Through(22, Zigzag(0.1, 0.012, 0.01)));
            Assert.Equal(ResolvedKind.Polyline, drawn.Kind);

            var result = ParallelAssist.Snap(drawn, m);
            Assert.True(result.Snapped);
            Assert.Equal(rail, result.PartnerLineId);
            var partner = ShapeResolver.Resolve(m.LineNodes(rail).Select(id => m.GetNode(id).Position).ToList());
            var s = result.Shape;
            Assert.Equal(ResolvedKind.Polyline, s.Kind);
            Assert.Equal(partner.Points.Count, s.Points.Count);
            Vec3 offset = s.Points[0] - partner.Points[0];
            for (int i = 0; i < s.Points.Count; i++)
                Assert.True(Vec3.Distance(s.Points[i] - partner.Points[i], offset) < 1e-9);   // the same corners, one constant shift
            Assert.True(Math.Abs(offset.X) < 0.005 && Math.Abs(offset.Y) < 0.005, $"offset {offset.X:F5} {offset.Y:F5} {offset.Z:F5}");   // straight across, same height
            Assert.InRange(offset.Z, 0.18, 0.22);
            Assert.All(s.Points, p => Assert.True(p.IsFinite));
        }

        [Fact]
        public void ZigzagDrawnBackwards_KeepsItsDirection()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Rooms.DrawStroke(m, pads, Through(23, Zigzag(-0.1)));
            var drawn = DrawnShape(m, pads, Through(24, Zigzag(0.1).Reverse().ToArray()));
            var result = ParallelAssist.Snap(drawn, m);
            Assert.True(result.Snapped);
            Assert.InRange(result.Shape.Points[0].X, 0.59, 0.61);
            Assert.InRange(result.Shape.Points[result.Shape.Points.Count - 1].X, 0.19, 0.21);
        }

        [Theory]
        [InlineData(0.05)]     // closer than a car fits
        [InlineData(0.45)]     // farther than a deck spans
        public void ZigzagOutsideTheDrivableRange_DoesNotSnap(double spacing)
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Rooms.DrawStroke(m, pads, Through(25, Zigzag(0)));
            Assert.False(ParallelAssist.Snap(DrawnShape(m, pads, Through(26, Zigzag(spacing))), m).Snapped);
        }

        [Fact]
        public void ZigzagWithMoreCorners_DoesNotSnap()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Rooms.DrawStroke(m, pads, Through(27, Zigzag(-0.1)));
            var more = new[]
            {
                new Vec3(0.2, 1.1, 0.1), new Vec3(0.27, 0.95, 0.1), new Vec3(0.33, 1.1, 0.1), new Vec3(0.4, 0.95, 0.1),
                new Vec3(0.47, 1.1, 0.1), new Vec3(0.53, 0.95, 0.1), new Vec3(0.6, 1.1, 0.1),
            };
            var drawn = DrawnShape(m, pads, Through(28, more));
            Assert.Equal(ResolvedKind.Polyline, drawn.Kind);
            Assert.False(ParallelAssist.Snap(drawn, m).Snapped);
        }

        [Fact]
        public void CopiedZigzag_BuiltSideBySide_MatchesTheOtherSideDotForDot()
        {
            // The game's flow (W4-04): FreeDraw shapes the stroke, the assist snaps it, and each side of the copy goes
            // through FreeDraw on its own.
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            int rail = Assert.Single(Rooms.DrawStroke(m, pads, Through(29, Zigzag(-0.1))));
            var result = ParallelAssist.Snap(DrawnShape(m, pads, Through(30, Zigzag(0.1, -0.01, 0.012))), m);
            Assert.True(result.Snapped);

            var before = m.Nodes.Select(n => n.Id).ToHashSet();
            var corners = result.Shape.Points;
            for (int i = 0; i + 1 < corners.Count; i++)
            {
                foreach (var chain in FreeDraw.Build(new[] { corners[i], corners[i + 1] }, Rooms.Context(m, pads), out _))
                    Assert.True(m.TryAddFreeLine(chain.Select(v => v.IsExisting ? LineVertex.Existing(v.ExistingId) : LineVertex.New(v.Position)).ToList()).Ok);
            }

            var added = m.Nodes.Where(n => !before.Contains(n.Id)).Select(n => n.Position).ToList();
            var expected = m.LineNodes(rail).Select(id => m.GetNode(id).Position + result.Offset).ToList();
            Assert.Equal(expected.Count, added.Count);
            Assert.All(expected, p => Assert.Contains(added, q => Vec3.Distance(p, q) < 1e-6));
            Assert.All(added, p => Assert.True(p.IsFinite));
        }
    }
}
