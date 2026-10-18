using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class ShapeResolverTests
    {
        /// <summary>A hand-drawn stroke through the corners: ~5 mm between samples, seeded jitter.</summary>
        static List<Vec3> Stroke(int seed, double noise, params Vec3[] corners)
        {
            var rng = new Random(seed);
            var pts = new List<Vec3>();
            for (int c = 0; c + 1 < corners.Length; c++)
            {
                int n = Math.Max(2, (int)(Vec3.Distance(corners[c], corners[c + 1]) / 0.005));
                for (int i = c == 0 ? 0 : 1; i <= n; i++)
                    pts.Add(Vec3.Lerp(corners[c], corners[c + 1], i / (double)n) + Jitter(rng, noise));
            }
            return pts;
        }

        static Vec3 Jitter(Random rng, double noise) =>
            new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * (2 * noise);

        /// <summary>An arc in the vertical plane z = 0.2 around (0.5, 1.0), radius r.</summary>
        static List<Vec3> ArcStroke(int seed, double noise, double r, double sweepDegrees)
        {
            var rng = new Random(seed);
            double sweep = sweepDegrees * Math.PI / 180;
            int n = (int)(r * sweep / 0.005);
            var pts = new List<Vec3>();
            for (int i = 0; i <= n; i++)
            {
                double a = Math.PI / 2 + sweep / 2 - sweep * i / n;
                pts.Add(new Vec3(0.5 + r * Math.Cos(a), 1.0 + r * Math.Sin(a), 0.2) + Jitter(rng, noise));
            }
            return pts;
        }

        static Vec3 P(double x, double y, double z = 0) => new Vec3(x, y, z);

        static void AssertFinite(ResolvedShape s)
        {
            Assert.True(double.IsFinite(s.Residual) && double.IsFinite(s.Radius) && double.IsFinite(s.Sweep));
            Assert.True(s.Center.IsFinite && s.Normal.IsFinite);
            Assert.All(s.Points, p => Assert.True(p.IsFinite));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void NoisyLine_ResolvesToALine(int seed)
        {
            var a = P(0, 1, 0);
            var b = P(0.6, 1.05, 0.1);
            var s = ShapeResolver.Resolve(Stroke(seed, 0.003, a, b));
            Assert.Equal(ResolvedKind.Line, s.Kind);
            Assert.Equal(2, s.Points.Count);
            Assert.True(Vec3.Distance(s.Points[0], a) < 0.01 && Vec3.Distance(s.Points[1], b) < 0.01);
            AssertFinite(s);
        }

        [Theory]
        [InlineData(30)]
        [InlineData(60)]
        [InlineData(90)]
        [InlineData(135)]
        [InlineData(180)]
        public void NoisyArc_ResolvesToAnArc(double sweep)
        {
            const double r = 0.3;
            var s = ShapeResolver.Resolve(ArcStroke((int)sweep, 0.002, r, sweep));
            Assert.Equal(ResolvedKind.Arc, s.Kind);
            // A shallow arc bulges only ~1 cm under 2 mm of hand noise, so its radius is loosely pinned.
            double tolerance = sweep < 60 ? 0.08 : 0.02;
            Assert.InRange(s.Radius, r - tolerance, r + tolerance);
            if (sweep >= 60)
                Assert.True(Vec3.Distance(s.Center, P(0.5, 1.0, 0.2)) < 0.03);
            Assert.InRange(Math.Abs(Math.Abs(s.Sweep) * 180 / Math.PI - sweep), 0, sweep < 60 ? 8 : 4);
            AssertFinite(s);

            // The clean path lies exactly on the circle, end to end.
            var path = s.Path(0.01);
            Assert.All(path, p => Assert.Equal(s.Radius, Vec3.Distance(p, s.Center), 9));
            Assert.Equal(s.Points[0], path[0]);
            Assert.Equal(s.Points[1], path[path.Count - 1]);
        }

        [Fact]
        public void NoisyTriangle_ResolvesToAThreePiecePolyline()
        {
            var s = ShapeResolver.Resolve(Stroke(4, 0.003, P(0, 0), P(0.4, 0), P(0.2, 0.32), P(0, 0)));
            Assert.Equal(ResolvedKind.Polyline, s.Kind);
            Assert.Equal(3, s.Segments);
            Assert.True(Vec3.Distance(s.Points[1], P(0.4, 0)) < 0.015);
            Assert.True(Vec3.Distance(s.Points[2], P(0.2, 0.32)) < 0.015);
            AssertFinite(s);
        }

        [Fact]
        public void NoisyZigzag_ResolvesWithTheRightCornerCount()
        {
            var s = ShapeResolver.Resolve(Stroke(5, 0.003, P(0, 0), P(0.15, 0.2), P(0.3, 0), P(0.45, 0.2), P(0.6, 0), P(0.75, 0.2)));
            Assert.Equal(ResolvedKind.Polyline, s.Kind);
            Assert.Equal(4, s.Corners);
            AssertFinite(s);
        }

        [Fact]
        public void NoisyW_ResolvesToThreeCorners()
        {
            var corners = new[] { P(0, 0.3), P(0.15, 0), P(0.3, 0.2), P(0.45, 0), P(0.6, 0.3) };
            var s = ShapeResolver.Resolve(Stroke(6, 0.003, corners));
            Assert.Equal(ResolvedKind.Polyline, s.Kind);
            Assert.Equal(3, s.Corners);
            for (int i = 1; i < 4; i++)
                Assert.True(Vec3.Distance(s.Points[i], corners[i]) < 0.015, $"corner {i} at {s.Points[i]}");
            AssertFinite(s);
        }

        /// <summary>A random walk in the plane z = 0 from start: no shape fits it.</summary>
        internal static List<Vec3> Scribble(int seed, Vec3 start, int steps = 400)
        {
            var rng = new Random(seed);
            var pts = new List<Vec3> { start };
            Vec3 heading = Vec3.UnitX;
            for (int i = 0; i < steps; i++)
            {
                double turn = (rng.NextDouble() - 0.5) * 2.5;
                heading = new Vec3(heading.X * Math.Cos(turn) - heading.Y * Math.Sin(turn), heading.X * Math.Sin(turn) + heading.Y * Math.Cos(turn), 0);
                pts.Add(pts[pts.Count - 1] + heading * 0.006);
            }
            return pts;
        }

        [Fact]
        public void Scribble_IsInvalid()
        {
            var s = ShapeResolver.Resolve(Scribble(7, P(0.3, 1, 0)));
            Assert.Equal(ResolvedKind.Invalid, s.Kind);
            Assert.Contains(s.Reason, new[] { InvalidReason.SelfIntersecting, InvalidReason.TooNoisy });
            Assert.True(double.IsFinite(s.Residual));
        }

        [Fact]
        public void Spiral_IsInvalid()
        {
            var pts = new List<Vec3>();
            for (int i = 0; i <= 600; i++)
            {
                double t = i / 600.0, a = t * 4 * Math.PI, r = 0.05 + 0.2 * t;
                pts.Add(P(0.5 + r * Math.Cos(a), 1 + r * Math.Sin(a), 0.1));
            }
            var s = ShapeResolver.Resolve(pts);
            Assert.Equal(ResolvedKind.Invalid, s.Kind);
            Assert.NotEqual(InvalidReason.None, s.Reason);
        }

        [Fact]
        public void Helix_IsNonPlanar()
        {
            var pts = new List<Vec3>();
            for (int i = 0; i <= 400; i++)
            {
                double t = i / 400.0, a = t * 3 * Math.PI;
                pts.Add(P(0.15 * Math.Cos(a), 1 + 0.15 * Math.Sin(a), 0.4 * t));
            }
            var s = ShapeResolver.Resolve(pts);
            Assert.Equal(ResolvedKind.Invalid, s.Kind);
            Assert.Equal(InvalidReason.NonPlanar, s.Reason);
        }

        [Fact]
        public void BowTie_SelfIntersects()
        {
            var s = ShapeResolver.Resolve(Stroke(8, 0.002, P(0, 0), P(0.4, 0.3), P(0.4, 0), P(0, 0.3)));
            Assert.Equal(ResolvedKind.Invalid, s.Kind);
            Assert.Equal(InvalidReason.SelfIntersecting, s.Reason);
        }

        [Fact]
        public void ZeroLengthAndDuplicatePoints_AreTooShort()
        {
            Assert.Equal(InvalidReason.TooShort, ShapeResolver.Resolve(new List<Vec3>()).Reason);
            Assert.Equal(InvalidReason.TooShort, ShapeResolver.Resolve(null).Reason);
            Assert.Equal(InvalidReason.TooShort, ShapeResolver.Resolve(new[] { P(0.1, 1, 0) }).Reason);
            var same = Enumerable.Repeat(P(0.1, 1, 0), 50).ToList();
            var s = ShapeResolver.Resolve(same);
            Assert.Equal(ResolvedKind.Invalid, s.Kind);
            Assert.Equal(InvalidReason.TooShort, s.Reason);
            Assert.True(double.IsFinite(s.Residual));
            var tiny = ShapeResolver.Resolve(Stroke(9, 0.0005, P(0, 1, 0), P(0.02, 1, 0)));
            Assert.Equal(InvalidReason.TooShort, tiny.Reason);
        }

        [Fact]
        public void SameInput_SameOutput()
        {
            var stroke = Stroke(10, 0.004, P(0, 0.3), P(0.15, 0), P(0.3, 0.2), P(0.45, 0), P(0.6, 0.3));
            var x = ShapeResolver.Resolve(stroke);
            var y = ShapeResolver.Resolve(stroke.ToList());
            Assert.Equal(x.Kind, y.Kind);
            Assert.Equal(x.Reason, y.Reason);
            Assert.Equal(x.Residual, y.Residual);
            Assert.Equal(x.Points, y.Points);
        }

        [Fact]
        public void ResolvedW_BuiltSideBySide_HasExactSharedCorners()
        {
            // What the game does (W4-04): each side of the polyline goes through FreeDraw on its own.
            var m = new BridgeModel(new BudgetPolicy { CostPerMeter = 1, CostPerSquareMeter = 1, MaterialBudget = 500, NodeCap = 500 });
            var s = ShapeResolver.Resolve(Stroke(12, 0.003, P(0, 1.3), P(0.15, 1.0), P(0.3, 1.2), P(0.45, 1.0), P(0.6, 1.3)));
            Assert.Equal(ResolvedKind.Polyline, s.Kind);
            for (int i = 0; i + 1 < s.Points.Count; i++)
            {
                var ctx = new FreeDraw.Context { Nodes = m.Nodes, Joined = (x, y) => false };
                foreach (var chain in FreeDraw.Build(new[] { s.Points[i], s.Points[i + 1] }, ctx, out var kind))
                {
                    Assert.Equal(StrokeKind.Line, kind);
                    Assert.True(m.TryAddFreeLine(chain.Select(v => v.IsExisting ? LineVertex.Existing(v.ExistingId) : LineVertex.New(v.Position)).ToList()).Ok);
                }
            }
            Assert.Equal(4, m.Lines.Count);
            foreach (var corner in s.Points.Skip(1).Take(3))
            {
                var at = m.Nodes.Where(n => Vec3.Distance(n.Position, corner) < 1e-9).ToList();
                Assert.Single(at);                                            // one shared dot, exactly on the corner
                Assert.Equal(2, m.Members.Count(x => x.NodeA == at[0].Id || x.NodeB == at[0].Id));
            }
        }

        [Fact]
        public void ResolvedLine_DividesIntoEqualSegments()
        {
            var s = ShapeResolver.Resolve(Stroke(11, 0.003, P(0, 1, 0), P(0.5, 1, 0)));
            var dots = EqualSegments.Divide(s.Points[0], s.Points[1]);
            Assert.Equal(EqualSegments.Count(Vec3.Distance(s.Points[0], s.Points[1])) + 1, dots.Length);
            Assert.All(dots, d => Assert.True(d.IsFinite));
        }
    }
}
