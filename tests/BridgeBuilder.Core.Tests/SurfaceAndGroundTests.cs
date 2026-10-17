using System;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W1-08: surface setup math.</summary>
    public class SurfaceSetupTests
    {
        static readonly Vec3 G = new Vec3(0, -9.8, 0);
        static Vec3 V(double x, double y, double z) => new Vec3(x, y, z);

        [Fact]
        public void SnapDots_IncludeEndpoints()
        {
            var r = SurfaceSetup.Compute(V(0, 0, 0), V(1, 0, 0), V(0, 0, 2), V(1, 0, 2), G, 0.25);
            Assert.Equal(5, r.DotsA.Count);
            for (int i = 0; i < 5; i++)
                Assert.True(Vec3.Distance(r.DotsA[i].Position, V(i * 0.25, 0, 0)) < 1e-12);
            Assert.All(r.DotsA, d => Assert.True(d.Fixed && !d.GroundAnchor));
            Assert.Equal(4, SurfaceSetup.Compute(V(0, 0, 0), V(0.25, 0, 0), V(0, 0, 2), V(0.25, 0, 2), G, 0.1).DotsA.Count);
        }

        [Fact]
        public void MaxRoadWidth_EqualsEdgeLength()
        {
            var r = SurfaceSetup.Compute(V(0, 0, 0), V(3, 0, 0), V(0, 0, 5), V(4, 0, 5), G, 0.5);
            Assert.Equal(3, r.MaxRoadWidthA, 12);
            Assert.Equal(4, r.MaxRoadWidthB, 12);
        }

        [Fact]
        public void GapDirection_IsMidpointToMidpoint() =>
            Assert.Equal(V(0, 0, 2), SurfaceSetup.Compute(V(0, 0, 0), V(1, 0, 0), V(0, 0, 2), V(1, 0, 2), G, 0.25).GapDirection);

        [Fact]
        public void Up_IsOppositeGravity() =>
            Assert.Equal(V(0, 1, 0), SurfaceSetup.Compute(V(0, 0, 0), V(1, 0, 0), V(0, 0, 2), V(1, 0, 2), G, 0.25).Up);

        [Fact]
        public void DifferentHeights_ArePreserved()
        {
            var r = SurfaceSetup.Compute(V(0, 1.5, 0), V(1, 1.5, 0), V(0, 4, 2), V(1, 4, 2), G, 0.25);
            Assert.All(r.DotsA, d => Assert.Equal(1.5, d.Position.Y));
            Assert.All(r.DotsB, d => Assert.Equal(4, d.Position.Y));
            Assert.Equal(2.5, r.GapDirection.Y, 12);
        }

        [Fact]
        public void NonParallel_SetsFlagAndDoesNotDropDots()
        {
            var skew = SurfaceSetup.Compute(V(0, 0, 0), V(1, 0, 0), V(0, 0, 5), V(Math.Sqrt(3), 0, 6), G, 0.25);
            Assert.Equal(5, skew.DotsA.Count);
            Assert.Equal(9, skew.DotsB.Count);
            Assert.True(skew.NonParallel);

            var parallel = SurfaceSetup.Compute(V(0, 0, 0), V(1, 0, 0), V(2, 0, 5), V(0, 0, 5), G, 0.25);
            Assert.Equal(9, parallel.DotsB.Count);
            Assert.False(parallel.NonParallel);

            double a = 2 * Math.PI / 180;
            Assert.False(SurfaceSetup.Compute(V(0, 0, 0), V(1, 0, 0), V(0, 0, 5), V(Math.Cos(a), 0, 5 + Math.Sin(a)), G, 0.25).NonParallel);
            Assert.Equal(5.0, Constants.Surface.NonParallelAngleDegrees);
        }

        [Fact]
        public void SurfaceSetup_RejectsDegenerateInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.Compute(V(0, 0, 0), V(1, 0, 0), V(0, 0, 2), V(1, 0, 2), G, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.Compute(V(0, 0, 0), V(0, 0, 0), V(0, 0, 2), V(1, 0, 2), G, 0.25));
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.Compute(V(0, 0, 0), V(1, 0, 0), V(0, 0, 2), V(1, 0, 2), Vec3.Zero, 0.25));
        }
    }

    /// <summary>W1-09: ground-plane snap grid.</summary>
    public class GroundPlaneTests
    {
        static Vec3 V(double x, double y, double z) => new Vec3(x, y, z);

        [Fact]
        public void Grid_SitsAtFloorHeight()
        {
            var nodes = GroundPlane.Grid(V(0, 2, 0), V(1, 2, 0), V(0, 2, 1), V(1, 2, 1), 0.5);
            Assert.All(nodes, n => Assert.True(n.Position.Y == 0.5 && n.Fixed && n.GroundAnchor));
            Assert.Equal(0.10, Constants.Ground.GridSpacingMeters);
            var explicitSpacing = GroundPlane.Grid(V(0, 2, 0), V(1, 2, 0), V(0, 2, 1), V(1, 2, 1), 0.5, null, Constants.Ground.GridSpacingMeters);
            Assert.Equal(explicitSpacing.Select(n => n.Position), nodes.Select(n => n.Position));
        }

        [Fact]
        public void Grid_OverrideReplacesHeight()
        {
            Assert.All(GroundPlane.Grid(V(0, 2, 0), V(1, 2, 0), V(0, 2, 1), V(1, 2, 1), 0, -1.5), n => Assert.Equal(-1.5, n.Position.Y));
            Assert.All(GroundPlane.Grid(V(0, 2, 0), V(1, 2, 0), V(0, 2, 1), V(1, 2, 1), 2, -1.5), n => Assert.Equal(-1.5, n.Position.Y));
        }

        [Fact]
        public void Grid_LimitedToFootprint()
        {
            var expected = (from x in new[] { 0.0, 1, 2 } from z in new[] { 0.0, 1, 2, 3 } select V(x, 0, z)).ToList();
            Assert.Equal(expected, GroundPlane.Grid(V(0, 0, 0), V(2, 0, 0), V(0, 0, 3), V(2, 0, 3), 0, null, 1).Select(n => n.Position));
            Assert.Equal(expected, GroundPlane.Grid(V(0, 0, 0), V(2, 0, 3), V(2, 0, 0), V(0, 0, 3), 0, null, 1).Select(n => n.Position));
        }

        [Fact]
        public void Grid_EmptyWhenSpacingNonPositive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.Grid(V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1), 0, null, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => GroundPlane.Grid(V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1), 0, null, -1));
        }

        [Fact]
        public void Grid_IsDeterministic() =>
            Assert.Equal(GroundPlane.Grid(V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1), 0).Select(n => n.Position),
                         GroundPlane.Grid(V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1), 0).Select(n => n.Position));
    }
}
