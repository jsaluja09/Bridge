using System;
using System.Collections.Generic;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W1-04: equal-segment snapping.</summary>
    public class EqualSegmentsTests
    {
        static void Equal12(double a, double b) => Assert.True(Math.Abs(a - b) <= 1e-12, $"{a} vs {b}");

        [Fact]
        public void EqualSegments_Length0_25_RoundsAwayTo3()
        {
            var a = Vec3.Zero;
            var b = new Vec3(0.25, 0, 0);
            var pts = EqualSegments.Divide(a, b, 0.10);
            Assert.Equal(3, pts.Length - 1);
            for (int i = 0; i + 1 < pts.Length; i++)
                Equal12(Vec3.Distance(pts[i], pts[i + 1]), 0.25 / 3);
            Assert.Equal(a, pts[0]);
            Assert.Equal(b, pts[3]);
        }

        [Fact] public void EqualSegments_Length0_14_IsOneSegment() => Assert.Equal(1, EqualSegments.Count(0.14, 0.10));

        [Fact]
        public void EqualSegments_Length0_15_IsTwoSegments()
        {
            Assert.Equal(2, EqualSegments.Count(0.15, 0.10));
            var pts = EqualSegments.Divide(Vec3.Zero, new Vec3(0.15, 0, 0), 0.10);
            Equal12(Vec3.Distance(pts[0], pts[1]), 0.075);
        }

        [Fact] public void EqualSegments_Length0_10_IsOneSegment() => Assert.Equal(1, EqualSegments.Count(0.10, 0.10));
        [Fact] public void EqualSegments_PositiveLength_AtLeastOneSegment() => Assert.Equal(1, EqualSegments.Count(0.02, 0.10));

        [Fact]
        public void EqualSegments_PointsAreColinear()
        {
            var b = new Vec3(0.25, 0.25, 0.25) / Math.Sqrt(3);
            var pts = EqualSegments.Divide(Vec3.Zero, b, 0.10);
            Assert.Equal(3, pts.Length - 1);
            foreach (var p in pts)
                Assert.True(Geometry.DistanceToSegment(p, Vec3.Zero, b) <= 1e-12);
            Assert.Equal(b, pts[3]);
        }

        [Fact]
        public void EqualSegments_ZeroLength_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(0, 0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(-1, 0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => EqualSegments.Count(1, 0));
        }

        [Fact]
        public void EqualSegments_DoesNotSplitExistingMember()
        {
            var model = new BridgeModel();
            int a = model.AddFixedNode(Vec3.Zero);
            var first = model.TryAddLine(new[] { LineVertex.Existing(a), LineVertex.New(new Vec3(1, 0, 0)) });
            Assert.True(first.Ok);
            var m0 = model.Members[0];
            var existing = new List<Vec3> { Vec3.Zero, new Vec3(1, 0, 0) };

            var up = EqualSegments.SegmentPolyline(new[] { Vec3.Zero, new Vec3(0, 0.25, 0) }, existing, 0.10);
            foreach (var p in up)
                Assert.False(p.Y == 0 && p.X > 0 && p.X < 1, "inserted a point on the existing member");

            var retrace = EqualSegments.SegmentPolyline(new[] { Vec3.Zero, new Vec3(1, 0, 0) }, existing, 0.10);
            Assert.Equal(11, retrace.Count);
            Assert.Single(model.Members);
            Assert.Equal(m0.NodeA, model.Members[0].NodeA);
            Assert.Equal(m0.NodeB, model.Members[0].NodeB);

            Assert.Throws<ArgumentException>(() => EqualSegments.SegmentPolyline(new[] { new Vec3(5, 5, 5), new Vec3(6, 5, 5) }, existing, 0.10));
        }

        [Fact]
        public void EqualSegments_DefaultSpacing_ReadsConstant()
        {
            Assert.Equal(0.10, Constants.Snapping.TargetSpacingMeters);
            Assert.Equal(EqualSegments.Count(0.25, Constants.Snapping.TargetSpacingMeters), EqualSegments.Count(0.25));
        }
    }

    /// <summary>W1-05: room-to-bridge scale adapter.</summary>
    public class RoomBridgeAdapterTests
    {
        [Fact] public void Scale_MultipliesCoordinates() => Assert.Equal(new Vec3(20, 40, 60), RoomBridgeAdapter.ToBridge(new Vec3(1, 2, 3), 20));

        [Fact]
        public void Scale_DividesDisplacements()
        {
            var r = RoomBridgeAdapter.DisplacementToRoom(new Vec3(0.2, 0.2, 0.2), 20);
            Assert.True(Math.Abs(r.X - 0.01) <= 1e-15 && Math.Abs(r.Y - 0.01) <= 1e-15 && Math.Abs(r.Z - 0.01) <= 1e-15);
        }

        [Fact] public void Scale_SectionAreaUnchanged() => Assert.Equal(1.0e-3, RoomBridgeAdapter.SectionArea(1.0e-3));
        [Fact] public void Scale_ForceUnchanged() => Assert.Equal(1000, RoomBridgeAdapter.Force(1000));

        [Fact]
        public void DefaultScale_ReadsConstant()
        {
            Assert.Equal(20.0, Constants.Scale.RoomToBridge);
            Assert.Equal(RoomBridgeAdapter.ToBridge(new Vec3(1, 2, 3), Constants.Scale.RoomToBridge), RoomBridgeAdapter.ToBridge(new Vec3(1, 2, 3)));
        }

        [Fact]
        public void Scale_FiniteInput_NoNaNOrInfinity()
        {
            Assert.True(RoomBridgeAdapter.ToBridge(new Vec3(1e3, -2, 0.5)).IsFinite);
            Assert.True(RoomBridgeAdapter.DisplacementToRoom(new Vec3(1e3, -2, 0.5)).IsFinite);
        }

        [Fact]
        public void Scale_NonPositive_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridge(Vec3.UnitX, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomBridgeAdapter.ToBridge(Vec3.UnitX, -20));
        }
    }
}
