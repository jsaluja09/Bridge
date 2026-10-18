using System;
using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>Spec 11.1: strokes come out as clean lines, arcs, corner shapes or smooth curves; squiggles are rejected.</summary>
    public class FreeDrawShapeTests
    {
        static Vec3 P(double x, double y, double z) => new Vec3(x, y, z);

        /// <summary>A hand stroke along curve(t), t from 0 to 1, sampled about every 5 mm with seeded jitter.</summary>
        static List<Vec3> Stroke(Func<double, Vec3> curve, int seed, double jitter = 0.003)
        {
            double length = 0;
            for (int i = 1; i <= 400; i++)
                length += Vec3.Distance(curve((i - 1) / 400.0), curve(i / 400.0));
            int n = Math.Max(2, (int)Math.Ceiling(length / 0.005));
            var rng = new Random(seed);
            var raw = new List<Vec3>(n + 1);
            for (int i = 0; i <= n; i++)
                raw.Add(curve((double)i / n) + new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * jitter);
            return raw;
        }

        /// <summary>A hand stroke through the corners, with seeded jitter.</summary>
        static List<Vec3> Through(int seed, params Vec3[] corners)
        {
            var lengths = new double[corners.Length];
            for (int i = 1; i < corners.Length; i++)
                lengths[i] = lengths[i - 1] + Vec3.Distance(corners[i - 1], corners[i]);
            double total = lengths[corners.Length - 1];
            return Stroke(t =>
            {
                int c = 1;
                while (c < corners.Length - 1 && lengths[c] < t * total)
                    c++;
                double seg = lengths[c] - lengths[c - 1];
                return Vec3.Lerp(corners[c - 1], corners[c], seg > 0 ? (t * total - lengths[c - 1]) / seg : 0);
            }, seed);
        }

        static StrokeKind Kind(BridgeModel m, Pad[] pads, List<Vec3> raw)
        {
            FreeDraw.Build(raw, Rooms.Context(m, pads), out var kind, out var reason);
            Assert.Equal(RejectionReason.None, reason);
            return kind;
        }

        static List<Vec3> Positions(BridgeModel m, int line) => m.LineNodes(line).Select(id => m.GetNode(id).Position).ToList();

        static void AssertFinite(IEnumerable<Vec3> points) => Assert.All(points, p => Assert.True(p.IsFinite));

        static void AssertEqualSpacing(IReadOnlyList<Vec3> pts, int from, int to, double tolerance)
        {
            var gaps = Enumerable.Range(from, to - from).Select(i => Vec3.Distance(pts[i], pts[i + 1])).ToList();
            Assert.True(gaps.Max() - gaps.Min() <= tolerance, $"gaps {string.Join(", ", gaps.Select(g => g.ToString("F4")))}");
        }

        /// <summary>The centre of the circle through a, b and c.</summary>
        static Vec3 Circumcenter(Vec3 a, Vec3 b, Vec3 c)
        {
            Vec3 ab = b - a, ac = c - a, n = Vec3.Cross(ab, ac);
            return a + (Vec3.Cross(n, ab) * ac.LengthSquared + Vec3.Cross(ac, n) * ab.LengthSquared) / (2 * n.LengthSquared);
        }

        /// <summary>Asserts every point lies on the circle through a, b and c (in its plane, at its radius).</summary>
        static void AssertOnCircle(IEnumerable<Vec3> points, Vec3 a, Vec3 b, Vec3 c, double tolerance)
        {
            Vec3 center = Circumcenter(a, b, c), normal = Vec3.Cross(b - a, c - a).Normalized();
            double r = Vec3.Distance(center, a);
            Assert.All(points, p =>
            {
                Assert.True(Math.Abs(Vec3.Distance(center, p) - r) <= tolerance, $"off the circle by {Vec3.Distance(center, p) - r:E2}");
                Assert.True(Math.Abs(Vec3.Dot(p - center, normal)) <= tolerance);
            });
        }

        [Theory]
        [InlineData(0.30, 0.02, 1)]
        [InlineData(0.45, 0.03, 2)]
        [InlineData(0.60, 0.03, 3)]
        public void WobblyStroke_BetweenTwoDots_ComesOutStraight(double length, double wobble, int seed)
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Vec3 a = P(0.2, 1.1, 0), b = a + P(length, 0, 0);
            int ia = m.AddFixedNode(a), ib = m.AddFixedNode(b);
            // Starts and ends 2 to 3 cm off the dots, bowing and waving by up to `wobble` on the way.
            Vec3 from = a + P(-0.01, 0.015, 0.01), to = b + P(0.015, -0.01, 0.02);
            var raw = Stroke(t => Vec3.Lerp(from, to, t)
                + P(0, wobble * (0.6 * Math.Sin(Math.PI * t) + 0.4 * Math.Sin(2 * Math.PI * t)), wobble * 0.5 * Math.Sin(2 * Math.PI * t + 1)), seed);

            Assert.Equal(StrokeKind.Line, Kind(m, pads, raw));
            int line = Assert.Single(Rooms.DrawStroke(m, pads, raw));
            var nodes = m.LineNodes(line);
            var pts = Positions(m, line);
            AssertFinite(pts);
            Assert.Equal(ia, nodes[0]);
            Assert.Equal(ib, nodes[nodes.Count - 1]);
            Assert.All(pts, p => Assert.True(Geometry.DistanceToSegment(p, a, b) < 1e-9));
            Assert.Equal(EqualSegments.Count(length) + 1, pts.Count);
            AssertEqualSpacing(pts, 0, pts.Count - 1, 1e-9);
        }

        [Theory]
        [InlineData(0.15, 4)]
        [InlineData(0.30, 5)]
        [InlineData(0.50, 6)]
        public void IntendedArch_BetweenTwoDots_IsACleanArcWithEqualJoints(double rise, int seed)
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Vec3 a = P(0.2, 1.0, 0), b = P(0.8, 1.0, 0);
            int ia = m.AddFixedNode(a), ib = m.AddFixedNode(b);
            double chord = 0.6, sag = rise * chord, r = (chord * chord / 4 + sag * sag) / (2 * sag), half = Math.Asin(chord / 2 / r);
            Vec3 center = P(0.5, 1.0 + sag - r, 0), from = P(-0.015, 0.01, 0.01), to = P(0.02, -0.015, -0.01);
            // A hand arch: off the dots at both ends, wavering about 1 cm around the circle.
            var raw = Stroke(t =>
            {
                double th = -half + 2 * half * t, wave = 0.008 * Math.Sin(3 * Math.PI * t);
                return center + P((r + wave) * Math.Sin(th), (r + wave) * Math.Cos(th), 0.006 * Math.Sin(2 * Math.PI * t)) + Vec3.Lerp(from, to, t);
            }, seed);

            Assert.Equal(StrokeKind.Arc, Kind(m, pads, raw));
            int line = Assert.Single(Rooms.DrawStroke(m, pads, raw));
            var nodes = m.LineNodes(line);
            var pts = Positions(m, line);
            AssertFinite(pts);
            Assert.Equal(ia, nodes[0]);
            Assert.Equal(ib, nodes[nodes.Count - 1]);
            Vec3 top = pts[pts.Count / 2];
            AssertOnCircle(pts, a, b, top, 1e-5);
            AssertEqualSpacing(pts, 0, pts.Count - 1, 1e-5);
            Assert.InRange(Vec3.Distance(pts[0], pts[1]), 0.07, 0.13);

            double drawnTop = 1.0 + sag, apex = pts.Max(p => p.Y);
            Assert.InRange(apex, drawnTop - 0.02, drawnTop + 0.02);
        }

        [Fact]
        public void CrossingX_SplitsOnce_AndBothLinesStayStraight()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Vec3 a0 = P(0.25, 1.1, 0), a1 = P(0.75, 1.1, 0);
            int first = Assert.Single(Rooms.DrawStroke(m, pads, Stroke(t => Vec3.Lerp(a0, a1, t) + P(0, 0.02 * Math.Sin(Math.PI * t), 0.01 * Math.Sin(2 * Math.PI * t)), 7)));
            var before = m.LineNodes(first);
            var firstBefore = Positions(m, first);
            Vec3 mid = Vec3.Lerp(firstBefore[2], firstBefore[3], 0.5);

            // The second stroke wobbles, and its straight line passes 1.5 cm above the first where they cross.
            Vec3 s0 = mid + P(0.03, 0.015, -0.25), s1 = mid + P(-0.03, 0.015, 0.25);
            int second = Assert.Single(Rooms.DrawStroke(m, pads, Stroke(t => Vec3.Lerp(s0, s1, t) + P(0.02 * Math.Sin(Math.PI * t), 0.015 * Math.Sin(2 * Math.PI * t), 0), 8)));

            var after = m.LineNodes(first);
            var secondNodes = m.LineNodes(second);
            Assert.Equal(before.Count + 1, after.Count);
            int split = Assert.Single(after.Intersect(secondNodes));
            var p1 = Positions(m, first);
            var p2 = Positions(m, second);
            AssertFinite(p1);
            AssertFinite(p2);
            Assert.All(p1, p => Assert.True(Geometry.DistanceToSegment(p, firstBefore[0], firstBefore[firstBefore.Count - 1]) < 1e-9));

            Vec3 e0 = p2[0], e1 = p2[p2.Count - 1], at = m.GetNode(split).Position;
            int k = secondNodes.IndexOf(split);
            Assert.InRange(k, 1, p2.Count - 2);
            Assert.True(Geometry.DistanceToSegment(at, e0, e1) <= Constants.Draw.SnapRadiusMeters);
            for (int i = 0; i < p2.Count; i++)
                Assert.True(Geometry.DistanceToSegment(p2[i], i <= k ? e0 : at, i <= k ? at : e1) < 1e-9);
            AssertEqualSpacing(p2, 0, k, 1e-9);
            AssertEqualSpacing(p2, k, p2.Count - 1, 1e-9);
        }

        [Fact]
        public void SCurve_IsKeptAsASmoothCurve()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            Vec3 a = P(0.2, 1.1, 0), b = P(0.8, 1.1, 0);
            var raw = Stroke(t => Vec3.Lerp(a, b, t) + P(0, 0.09 * Math.Sin(2 * Math.PI * t), 0), 9);

            Assert.Equal(StrokeKind.Curve, Kind(m, pads, raw));
            int line = Assert.Single(Rooms.DrawStroke(m, pads, raw));
            var pts = Positions(m, line);
            AssertFinite(pts);
            Assert.Contains(pts, p => p.Y > 1.1 + 0.06);
            Assert.Contains(pts, p => p.Y < 1.1 - 0.06);
            Assert.All(Enumerable.Range(0, pts.Count - 1), i => Assert.InRange(Vec3.Distance(pts[i], pts[i + 1]), 0.08, 0.12));
        }

        [Fact]
        public void Squiggle_IsRejectedForItsShape_AndATinyStrokeForItsLength()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            var wavy = Stroke(t => P(0.2 + 0.5 * t, 1.1 + 0.06 * Math.Sin(2 * Math.PI * 2.3 * t) + 0.025 * Math.Sin(2 * Math.PI * 5.3 * t + 1), 0.01 * Math.Sin(7 * t)), 10);
            var loops = Stroke(t => { double th = 4 * Math.PI * t; return P(0.2 + 0.4 * t - 0.06 * Math.Sin(th), 1.1 + 0.06 * Math.Cos(th), 0); }, 11);
            var spiral = Stroke(t => { double th = 3 * Math.PI * t, r = 0.05 + 0.1 * t; return P(0.5 + r * Math.Cos(th), 1.1 + r * Math.Sin(th), 0); }, 12);
            foreach (var raw in new[] { wavy, loops, spiral })
            {
                var chains = FreeDraw.Build(raw, Rooms.Context(m, pads), out var kind, out var reason);
                Assert.Empty(chains);
                Assert.Equal(StrokeKind.Rejected, kind);
                Assert.Equal(RejectionReason.Unrecognized, reason);
            }

            var tiny = Stroke(t => P(0.3 + 0.02 * t, 1.1, 0), 13, 0.001);
            Assert.Empty(FreeDraw.Build(tiny, Rooms.Context(m, pads), out _, out var tooShort));
            Assert.Equal(RejectionReason.Degenerate, tooShort);
        }

        [Fact]
        public void Zigzag_KeepsItsCorners_WithStraightEvenSides()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            var corners = new[] { P(0.2, 1.1, 0), P(0.3, 0.95, 0), P(0.4, 1.1, 0), P(0.5, 0.95, 0), P(0.6, 1.1, 0) };
            var raw = Through(14, corners);

            Assert.Equal(StrokeKind.Polyline, Kind(m, pads, raw));
            int line = Assert.Single(Rooms.DrawStroke(m, pads, raw));
            var pts = Positions(m, line);
            AssertFinite(pts);
            var at = new List<int> { 0 };
            for (int c = 1; c + 1 < corners.Length; c++)
            {
                int nearest = Enumerable.Range(0, pts.Count).OrderBy(i => Vec3.Distance(pts[i], corners[c])).First();
                Assert.True(Vec3.Distance(pts[nearest], corners[c]) < 0.01);
                at.Add(nearest);
            }
            at.Add(pts.Count - 1);
            for (int s = 0; s + 1 < at.Count; s++)
            {
                for (int i = at[s]; i <= at[s + 1]; i++)
                    Assert.True(Geometry.DistanceToSegment(pts[i], pts[at[s]], pts[at[s + 1]]) < 1e-9);
                AssertEqualSpacing(pts, at[s], at[s + 1], 1e-9);
            }
        }

        [Fact]
        public void Triangle_OverABeam_KeepsItsApex()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            int beam = Assert.Single(Rooms.DrawStroke(m, pads, Through(15, P(0.2, 1.0, 0), P(0.8, 1.0, 0))));
            var dots = m.LineNodes(beam);
            Vec3 left = m.GetNode(dots[1]).Position, right = m.GetNode(dots[5]).Position, apex = Vec3.Lerp(left, right, 0.5) + P(0, 0.25, 0);

            var raw = Through(16, left + P(0.01, 0.01, 0), apex, right + P(-0.01, 0.015, 0.01));
            Assert.Equal(StrokeKind.Polyline, Kind(m, pads, raw));
            int line = Assert.Single(Rooms.DrawStroke(m, pads, raw));
            var nodes = m.LineNodes(line);
            var pts = Positions(m, line);
            AssertFinite(pts);
            Assert.Equal(dots[1], nodes[0]);
            Assert.Equal(dots[5], nodes[nodes.Count - 1]);
            int top = Enumerable.Range(0, pts.Count).OrderBy(i => Vec3.Distance(pts[i], apex)).First();
            Assert.True(Vec3.Distance(pts[top], apex) < 0.01);
            for (int i = 0; i < pts.Count; i++)
                Assert.True(Geometry.DistanceToSegment(pts[i], i <= top ? left : pts[top], i <= top ? pts[top] : right) < 1e-9);
        }

        [Fact]
        public void Arch_CrossingALine_StaysOnItsCircleOnBothSidesOfTheSplit()
        {
            var pads = Rooms.Pads();
            var m = Rooms.Model();
            int beam = Assert.Single(Rooms.DrawStroke(m, pads, Through(17, P(0.5, 1.0, -0.3), P(0.5, 1.0, 0.3))));
            var before = m.LineNodes(beam);

            // An arch in the plane z = 0.05 (between two of the beam's dots), its top passing 1.5 cm above the beam.
            double chord = 0.6, sag = 0.165, r = (chord * chord / 4 + sag * sag) / (2 * sag), half = Math.Asin(chord / 2 / r);
            Vec3 center = P(0.5, 1.015 - r, 0.05);
            var raw = Stroke(t =>
            {
                double th = -half + 2 * half * t, wave = 0.006 * Math.Sin(3 * Math.PI * t);
                return center + P((r + wave) * Math.Sin(th), (r + wave) * Math.Cos(th), 0.004 * Math.Sin(2 * Math.PI * t));
            }, 18);

            Assert.Equal(StrokeKind.Arc, Kind(m, pads, raw));
            int arch = Assert.Single(Rooms.DrawStroke(m, pads, raw));
            var nodes = m.LineNodes(arch);
            var pts = Positions(m, arch);
            AssertFinite(pts);
            Assert.Equal(before.Count + 1, m.LineNodes(beam).Count);
            int split = Assert.Single(m.LineNodes(beam).Intersect(nodes));
            int k = nodes.IndexOf(split);
            Assert.InRange(k, 2, pts.Count - 3);

            var onCircle = pts.Where((_, i) => i != k).ToList();
            AssertOnCircle(onCircle, pts[0], pts[pts.Count - 1], pts[1], 1e-5);
            Vec3 c = Circumcenter(pts[0], pts[pts.Count - 1], pts[1]);
            double radius = Vec3.Distance(c, pts[0]);
            Assert.True(Math.Abs(Vec3.Distance(c, pts[k]) - radius) <= Constants.Draw.SnapRadiusMeters);
            AssertEqualSpacing(pts, 0, k - 1, 1e-5);
            AssertEqualSpacing(pts, k + 1, pts.Count - 1, 1e-5);
        }
    }
}
