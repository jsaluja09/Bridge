using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class EqualSegmentsTests
    {
        private const double Spacing = 0.10;
        private const double Tolerance = 1e-12;

        [Fact]
        public void EqualSegments_Length0_25_RoundsAwayTo3()
        {
            Assert.Equal(3, EqualSegments.Count(0.25, Spacing));

            var start = new Vec3(0, 0, 0);
            var end = new Vec3(0.25, 0, 0);
            Vec3[] points = EqualSegments.Points(start, end, Spacing);
            AssertFinite(points);
            Assert.Equal(4, points.Length);
            Assert.Equal(start, points[0]);
            Assert.Equal(end, points[3]);
            AssertSegmentLengths(points, 0.25 / 3);
        }

        [Fact]
        public void EqualSegments_Length0_14_IsOneSegment()
        {
            Assert.Equal(1, EqualSegments.Count(0.14, Spacing));

            Vec3[] points = EqualSegments.Points(Vec3.Zero, new Vec3(0.14, 0, 0), Spacing);
            AssertFinite(points);
            Assert.Equal(2, points.Length);
            AssertSegmentLengths(points, 0.14);
        }

        [Fact]
        public void EqualSegments_Length0_15_IsTwoSegments()
        {
            Assert.Equal(2, EqualSegments.Count(0.15, Spacing));

            var end = new Vec3(0.15, 0, 0);
            Vec3[] points = EqualSegments.Points(Vec3.Zero, end, Spacing);
            AssertFinite(points);
            Assert.Equal(3, points.Length);
            Assert.Equal(Vec3.Zero, points[0]);
            Assert.Equal(end, points[2]);
            AssertSegmentLengths(points, 0.075);
        }

        [Fact]
        public void EqualSegments_Length0_10_IsOneSegment()
        {
            Assert.Equal(1, EqualSegments.Count(0.10, Spacing));
            Assert.Equal(2, EqualSegments.Points(Vec3.Zero, new Vec3(0, 0.10, 0), Spacing).Length);
        }

        [Fact]
        public void EqualSegments_PositiveLength_AtLeastOneSegment()
        {
            Assert.Equal(1, EqualSegments.Count(0.02, Spacing));
            Assert.Equal(1, EqualSegments.Count(1e-30, Spacing));

            var end = new Vec3(0, 0, 0.02);
            Vec3[] points = EqualSegments.Points(Vec3.Zero, end, Spacing);
            AssertFinite(points);
            Assert.Equal(new[] { Vec3.Zero, end }, points);
        }

        [Fact]
        public void EqualSegments_PointsAreColinear()
        {
            var start = new Vec3(0, 0, 0);
            Vec3 end = new Vec3(0.25, 0.25, 0.25) / Math.Sqrt(3);
            Assert.Equal(3, EqualSegments.Count(Vec3.Distance(start, end), Spacing));

            Vec3[] points = EqualSegments.Points(start, end, Spacing);
            AssertFinite(points);
            Assert.Equal(4, points.Length);
            Assert.Equal(start, points[0]);
            Assert.Equal(end, points[3]);

            Vec3 direction = (end - start).Normalized();
            double previous = double.NegativeInfinity;
            foreach (Vec3 point in points)
            {
                double offChord = Vec3.Cross(point - start, direction).Length;
                Assert.True(offChord <= Tolerance, point + " is " + offChord + " off the chord.");
                double along = Vec3.Dot(point - start, direction);
                Assert.True(along > previous, "Points must run in order from start to end.");
                previous = along;
            }

            AssertSegmentLengths(points, Vec3.Distance(start, end) / 3);
        }

        [Fact]
        public void EqualSegments_ZeroLength_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(0.0, Spacing));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(-0.25, Spacing));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(0.25, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(0.25, -0.10));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(double.NaN, Spacing));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(double.PositiveInfinity, Spacing));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(0.25, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(0.25, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(0.25, 1e-30));

            var point = new Vec3(0.3, 0.2, 0.1);
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Points(point, point, Spacing));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Points(Vec3.Zero, point, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Points(Vec3.Zero, point, -0.10));
        }

        [Fact]
        public void EqualSegments_DoesNotSplitExistingMember()
        {
            var origin = new Vec3(0, 0, 0);
            var far = new Vec3(1, 0, 0);
            Node[] nodes =
            {
                new Node { Id = 0, Position = origin, Fixed = true, SurfaceId = 1 },
                new Node { Id = 1, Position = far },
            };
            var m0 = new Member { Id = 0, NodeA = 0, NodeB = 1, SectionId = 0, LineId = 0 };
            Member[] members = { m0 };

            var top = new Vec3(0, 0.25, 0);
            SnappedPolyline attached = EqualSegments.SnapPolyline(nodes, members, new[] { origin, top }, Spacing);
            Assert.Equal(4, attached.NodeIds.Length);
            Assert.Equal(0, attached.NodeIds[0]);
            Assert.Equal(3, attached.MemberIds.Length);
            Assert.Equal(3, attached.NewNodes.Length);
            Assert.Equal(3, attached.NewMembers.Length);
            Assert.DoesNotContain(m0.Id, attached.MemberIds);
            Assert.Equal(top, attached.NewNodes[2].Position);
            Assert.Equal(attached.NodeIds.Skip(1), attached.NewNodes.Select(n => n.Id));
            Assert.Equal(attached.MemberIds, attached.NewMembers.Select(m => m.Id));
            Assert.All(attached.NewNodes, n => Assert.DoesNotContain(n.Id, nodes.Select(e => e.Id)));
            Assert.All(attached.NewMembers, m => Assert.NotEqual(m0.Id, m.Id));
            Assert.All(attached.NewNodes, n => Assert.False(n.Fixed || n.GroundAnchor || n.SurfaceId.HasValue));
            for (int k = 0; k < attached.NewMembers.Length; k++)
            {
                Assert.Equal(attached.NodeIds[k], attached.NewMembers[k].NodeA);
                Assert.Equal(attached.NodeIds[k + 1], attached.NewMembers[k].NodeB);
            }

            AssertFinite(attached.NewNodes.Select(n => n.Position));
            Assert.All(attached.NewNodes, n => Assert.False(InOpenSegment(n.Position, origin, far), n.Position + " lies inside m0."));
            AssertSegmentLengths(new[] { origin }.Concat(attached.NewNodes.Select(n => n.Position)).ToArray(), 0.25 / 3);
            AssertUnchanged(nodes, members, m0);

            SnappedPolyline retraced = EqualSegments.SnapPolyline(nodes, members, new[] { origin, far }, Spacing);
            Assert.Equal(new[] { m0.Id }, retraced.MemberIds);
            Assert.Equal(new[] { 0, 1 }, retraced.NodeIds);
            Assert.Empty(retraced.NewNodes);
            Assert.Empty(retraced.NewMembers);
            AssertUnchanged(nodes, members, m0);

            SnappedPolyline reversed = EqualSegments.SnapPolyline(nodes, members, new[] { far, origin }, Spacing);
            Assert.Equal(new[] { m0.Id }, reversed.MemberIds);
            Assert.Equal(new[] { 1, 0 }, reversed.NodeIds);
            Assert.Empty(reversed.NewNodes);
            Assert.Empty(reversed.NewMembers);
            AssertUnchanged(nodes, members, m0);

            Assert.Throws<ArgumentException>(() => EqualSegments.SnapPolyline(nodes, members, new[] { new Vec3(0.5, 0, 0), new Vec3(0.5, 0.25, 0) }, Spacing));
            Assert.Throws<ArgumentException>(() => EqualSegments.SnapPolyline(nodes, members, new[] { new Vec3(2, 0, 0), new Vec3(2, 0.25, 0) }, Spacing));
            AssertUnchanged(nodes, members, m0);
        }

        [Fact]
        public void EqualSegments_DefaultSpacing_ReadsConstant()
        {
            Assert.Equal(0.10, Constants.Snapping.TargetSpacingMeters);
            Assert.Equal(EqualSegments.Count(0.25, Constants.Snapping.TargetSpacingMeters), EqualSegments.Count(0.25));

            var end = new Vec3(0.25, 0.1, -0.05);
            Assert.Equal(EqualSegments.Points(Vec3.Zero, end, Constants.Snapping.TargetSpacingMeters), EqualSegments.Points(Vec3.Zero, end));

            string source = File.ReadAllText(RepoPath("Assets/BridgeBuilder.Core/Snapping/EqualSegments.cs"));
            Assert.DoesNotMatch(@"(?<![\w.])0?\.10*(?!\d)", source);
        }

        private static void AssertSegmentLengths(Vec3[] points, double expected)
        {
            for (int i = 1; i < points.Length; i++)
            {
                double length = Vec3.Distance(points[i - 1], points[i]);
                Assert.True(double.IsFinite(length), "Segment " + i + " length is not finite.");
                Assert.Equal(expected, length, Tolerance);
            }
        }

        private static void AssertFinite(IEnumerable<Vec3> points)
        {
            foreach (Vec3 point in points)
            {
                Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z), point + " is not finite.");
            }
        }

        private static bool InOpenSegment(Vec3 point, Vec3 a, Vec3 b)
        {
            Vec3 axis = b - a;
            double t = Vec3.Dot(point - a, axis) / axis.LengthSquared;
            double offAxis = ((point - a) - axis * t).Length;
            return t > 0.0 && t < 1.0 && offAxis <= Tolerance;
        }

        private static void AssertUnchanged(Node[] nodes, Member[] members, Member m0)
        {
            Assert.Equal(2, nodes.Length);
            Assert.Same(m0, Assert.Single(members));
            Assert.Equal(0, m0.Id);
            Assert.Equal(0, m0.NodeA);
            Assert.Equal(1, m0.NodeB);
            Assert.Equal(new Vec3(0, 0, 0), nodes[0].Position);
            Assert.Equal(new Vec3(1, 0, 0), nodes[1].Position);
        }

        private static string RepoPath(string relativePath)
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "dotnet", "BridgeBuilder.sln")))
                {
                    return Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                }
            }

            throw new InvalidOperationException("No directory containing dotnet/BridgeBuilder.sln above " + AppContext.BaseDirectory);
        }
    }
}
