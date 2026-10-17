using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class SurfaceSetupTests
    {
        private const double Tolerance = 1e-12;
        private const double AngleToleranceDegrees = 1e-9;
        private const int FirstSurfaceId = 1;
        private const int SecondSurfaceId = 2;

        private static readonly Vec3 Gravity = new Vec3(0, -9.8, 0);

        [Fact]
        public void SnapDots_IncludeEndpoints()
        {
            Vec3[] points = SurfaceSetup.SnapDotPositions(Vec3.Zero, new Vec3(1, 0, 0), 0.25);
            Assert.Equal(5, points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                AssertFinite(points[i]);
                Assert.Equal(new Vec3(i * 0.25, 0, 0), points[i]);
            }

            var shortEnd = new Vec3(0.25, 0, 0);
            Vec3[] shortPoints = SurfaceSetup.SnapDotPositions(Vec3.Zero, shortEnd, 0.1);
            Assert.Equal(4, shortPoints.Length);
            Assert.Equal(Vec3.Zero, shortPoints[0]);
            Assert.Equal(shortEnd, shortPoints[3]);
            for (int i = 1; i < shortPoints.Length; i++)
            {
                AssertFinite(shortPoints[i]);
                Assert.Equal(0.25 / 3, Vec3.Distance(shortPoints[i - 1], shortPoints[i]), Tolerance);
            }

            SurfaceSetupResult result = SurfaceSetup.Build(
                new MarkedEdge(FirstSurfaceId, Vec3.Zero, new Vec3(1, 0, 0)),
                new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 2), new Vec3(1, 0, 2)),
                Gravity,
                0.25);
            AssertValidResult(result);
            Assert.Equal(points, result.First.SnapDots.Select(n => n.Position));
            Assert.Equal(points.Select(p => p + new Vec3(0, 0, 2)), result.Second.SnapDots.Select(n => n.Position));
        }

        [Fact]
        public void SnapDots_RoundHalfAwayFromZeroWithAtLeastOneSegment()
        {
            Vec3[] half = SurfaceSetup.SnapDotPositions(Vec3.Zero, new Vec3(0.15, 0, 0), 0.10);
            Assert.Equal(3, half.Length);
            Assert.Equal(0.075, Vec3.Distance(half[0], half[1]), Tolerance);
            Assert.Equal(0.075, Vec3.Distance(half[1], half[2]), Tolerance);

            Assert.Equal(2, SurfaceSetup.SnapDotPositions(Vec3.Zero, new Vec3(0.14, 0, 0), 0.10).Length);

            var tinyEnd = new Vec3(0, 0.02, 0);
            Vec3[] tiny = SurfaceSetup.SnapDotPositions(Vec3.Zero, tinyEnd, 0.10);
            Assert.Equal(new[] { Vec3.Zero, tinyEnd }, tiny);
        }

        [Fact]
        public void MaxRoadWidth_EqualsEdgeLength()
        {
            SurfaceSetupResult result = SurfaceSetup.Build(
                new MarkedEdge(FirstSurfaceId, Vec3.Zero, new Vec3(3, 0, 0)),
                new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 2), new Vec3(4, 0, 2)),
                Gravity,
                0.1);
            AssertValidResult(result);
            Assert.Equal(3.0, result.First.EdgeLength);
            Assert.Equal(3.0, result.First.MaxRoadWidth);
            Assert.Equal(4.0, result.Second.EdgeLength);
            Assert.Equal(4.0, result.Second.MaxRoadWidth);

            SurfaceSetupResult diagonal = SurfaceSetup.Build(
                new MarkedEdge(FirstSurfaceId, new Vec3(1, 0.5, 0), new Vec3(2.8, 0.5, 2.4)),
                new MarkedEdge(SecondSurfaceId, new Vec3(5, 1, 0), new Vec3(7.4, 1, 3.2)),
                Gravity,
                0.1);
            AssertValidResult(diagonal);
            Assert.Equal(3.0, diagonal.First.MaxRoadWidth, Tolerance);
            Assert.Equal(4.0, diagonal.Second.MaxRoadWidth, Tolerance);
        }

        [Fact]
        public void GapDirection_IsMidpointToMidpoint()
        {
            var firstEdge = new MarkedEdge(FirstSurfaceId, Vec3.Zero, new Vec3(1, 0, 0));
            var secondEdge = new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 2), new Vec3(1, 0, 2));
            SurfaceSetupResult result = SurfaceSetup.Build(firstEdge, secondEdge, Gravity, 0.25);
            AssertValidResult(result);
            Assert.Equal(new Vec3(0.5, 0, 0), result.First.EdgeMidpoint);
            Assert.Equal(new Vec3(0.5, 0, 2), result.Second.EdgeMidpoint);
            Assert.Equal(new Vec3(0, 0, 2), result.Gap);

            var reversedSecond = new MarkedEdge(SecondSurfaceId, new Vec3(1, 0, 2), new Vec3(0, 0, 2));
            SurfaceSetupResult reversed = SurfaceSetup.Build(firstEdge, reversedSecond, Gravity, 0.25);
            AssertValidResult(reversed);
            Assert.Equal(new Vec3(0, 0, 2), reversed.Gap);

            SurfaceSetupResult swapped = SurfaceSetup.Build(secondEdge, firstEdge, Gravity, 0.25);
            AssertValidResult(swapped);
            Assert.Equal(new Vec3(0, 0, -2), swapped.Gap);
        }

        [Fact]
        public void Up_IsOppositeGravity()
        {
            Assert.Equal(new Vec3(0, 1, 0), SurfaceSetup.UpFromGravity(Gravity));

            SurfaceSetupResult result = SurfaceSetup.Build(
                new MarkedEdge(FirstSurfaceId, Vec3.Zero, new Vec3(1, 0, 0)),
                new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 2), new Vec3(1, 0, 2)),
                Gravity,
                0.25);
            AssertValidResult(result);
            Assert.Equal(new Vec3(0, 1, 0), result.Up);

            Vec3 tilted = SurfaceSetup.UpFromGravity(new Vec3(3, -4, 0));
            AssertFinite(tilted);
            Assert.Equal(-0.6, tilted.X, Tolerance);
            Assert.Equal(0.8, tilted.Y, Tolerance);
            Assert.Equal(0.0, tilted.Z, Tolerance);
            Assert.Equal(1.0, tilted.Length, Tolerance);
        }

        [Fact]
        public void DifferentHeights_ArePreserved()
        {
            SurfaceSetupResult result = SurfaceSetup.Build(
                new MarkedEdge(FirstSurfaceId, new Vec3(0, 1.5, 0), new Vec3(1, 1.5, 0)),
                new MarkedEdge(SecondSurfaceId, new Vec3(0, 4, 2), new Vec3(1, 4, 2)),
                Gravity,
                0.25);
            AssertValidResult(result);
            Assert.All(result.First.SnapDots, dot => Assert.Equal(1.5, dot.Position.Y));
            Assert.All(result.Second.SnapDots, dot => Assert.Equal(4.0, dot.Position.Y));
            Assert.Equal(1.5, result.First.EdgeMidpoint.Y);
            Assert.Equal(4.0, result.Second.EdgeMidpoint.Y);
            Assert.Equal(2.5, result.Gap.Y);
            Assert.Equal(new Vec3(0, 2.5, 2), result.Gap);
        }

        [Fact]
        public void NonParallel_SetsFlagAndDoesNotDropDots()
        {
            Assert.Equal(5.0, Constants.Surface.NonParallelAngleDegrees);

            var edgeA = new MarkedEdge(FirstSurfaceId, Vec3.Zero, new Vec3(1, 0, 0));

            var angled = new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 5), new Vec3(Math.Sqrt(3), 0, 6));
            SurfaceSetupResult nonParallel = SurfaceSetup.Build(edgeA, angled, Gravity, 0.25);
            AssertValidResult(nonParallel);
            Assert.Equal(5, nonParallel.First.SnapDots.Count);
            Assert.Equal(9, nonParallel.Second.SnapDots.Count);
            Assert.Equal(14, nonParallel.SnapDots.Count);
            Assert.Equal(30.0, nonParallel.EdgeAngleDegrees, AngleToleranceDegrees);
            Assert.True(nonParallel.NonParallel);

            var parallel = new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 5), new Vec3(2, 0, 5));
            var parallelReversed = new MarkedEdge(SecondSurfaceId, new Vec3(2, 0, 5), new Vec3(0, 0, 5));
            foreach (MarkedEdge edgeB in new[] { parallel, parallelReversed })
            {
                SurfaceSetupResult result = SurfaceSetup.Build(edgeA, edgeB, Gravity, 0.25);
                AssertValidResult(result);
                Assert.Equal(5, result.First.SnapDots.Count);
                Assert.Equal(9, result.Second.SnapDots.Count);
                Assert.Equal(0.0, result.EdgeAngleDegrees, AngleToleranceDegrees);
                Assert.False(result.NonParallel);
            }

            double twoDegrees = 2.0 * Math.PI / 180.0;
            var slight = new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 5), new Vec3(2 * Math.Cos(twoDegrees), 0, 5 + 2 * Math.Sin(twoDegrees)));
            SurfaceSetupResult slightResult = SurfaceSetup.Build(edgeA, slight, Gravity, 0.25);
            AssertValidResult(slightResult);
            Assert.Equal(9, slightResult.Second.SnapDots.Count);
            Assert.Equal(2.0, slightResult.EdgeAngleDegrees, AngleToleranceDegrees);
            Assert.False(slightResult.NonParallel);
        }

        [Fact]
        public void SurfaceSetup_RejectsDegenerateInput()
        {
            var edgeA = new MarkedEdge(FirstSurfaceId, Vec3.Zero, new Vec3(1, 0, 0));
            var edgeB = new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 2), new Vec3(1, 0, 2));

            foreach (double spacing in new[] { 0.0, -0.25, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.Build(edgeA, edgeB, Gravity, spacing));
                Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.SnapDotPositions(Vec3.Zero, new Vec3(1, 0, 0), spacing));
            }

            var zeroA = new MarkedEdge(FirstSurfaceId, new Vec3(1, 2, 3), new Vec3(1, 2, 3));
            var zeroB = new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 2), new Vec3(0, 0, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.Build(zeroA, edgeB, Gravity, 0.25));
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.Build(edgeA, zeroB, Gravity, 0.25));
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.SnapDotPositions(Vec3.UnitX, Vec3.UnitX, 0.25));

            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.Build(edgeA, edgeB, Vec3.Zero, 0.25));
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.UpFromGravity(Vec3.Zero));

            var infinite = new Vec3(double.PositiveInfinity, 0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.Build(new MarkedEdge(FirstSurfaceId, Vec3.Zero, infinite), edgeB, Gravity, 0.25));
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceSetup.Build(edgeA, edgeB, infinite, 0.25));
        }

        [Fact]
        public void SnapDots_AreFixedSurfaceSupportsWithConsecutiveIds()
        {
            const int firstNodeId = 10;
            SurfaceSetupResult result = SurfaceSetup.Build(
                new MarkedEdge(FirstSurfaceId, Vec3.Zero, new Vec3(1, 0, 0)),
                new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 2), new Vec3(0.5, 0, 2)),
                Gravity,
                0.25,
                firstNodeId);
            AssertValidResult(result);
            Assert.Equal(FirstSurfaceId, result.First.SurfaceId);
            Assert.Equal(SecondSurfaceId, result.Second.SurfaceId);
            Assert.Equal(Enumerable.Range(firstNodeId, 8), result.SnapDots.Select(n => n.Id));

            SurfaceSetupResult defaultIds = SurfaceSetup.Build(
                new MarkedEdge(FirstSurfaceId, Vec3.Zero, new Vec3(1, 0, 0)),
                new MarkedEdge(SecondSurfaceId, new Vec3(0, 0, 2), new Vec3(0.5, 0, 2)),
                Gravity,
                0.25);
            AssertValidResult(defaultIds);
            Assert.Equal(Enumerable.Range(0, 8), defaultIds.SnapDots.Select(n => n.Id));

            Assert.Throws<ArgumentException>(() => SurfaceSetup.Build(
                new MarkedEdge(FirstSurfaceId, Vec3.Zero, new Vec3(1, 0, 0)),
                new MarkedEdge(FirstSurfaceId, new Vec3(0, 0, 2), new Vec3(1, 0, 2)),
                Gravity,
                0.25));
        }

        private static void AssertValidResult(SurfaceSetupResult result)
        {
            AssertFinite(result.Up);
            AssertFinite(result.Gap);
            AssertFinite(result.EdgeAngleDegrees);
            Assert.InRange(result.EdgeAngleDegrees, 0.0, 90.0);
            Assert.Equal(1.0, result.Up.Length, Tolerance);
            Assert.Equal(result.Second.EdgeMidpoint - result.First.EdgeMidpoint, result.Gap);
            Assert.Equal(result.EdgeAngleDegrees > Constants.Surface.NonParallelAngleDegrees, result.NonParallel);
            Assert.NotEqual(result.First.SurfaceId, result.Second.SurfaceId);
            AssertSurface(result.First);
            AssertSurface(result.Second);

            List<Node> expected = result.First.SnapDots.Concat(result.Second.SnapDots).ToList();
            Assert.Equal(expected.Count, result.SnapDots.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Same(expected[i], result.SnapDots[i]);
                Assert.Equal(result.SnapDots[0].Id + i, result.SnapDots[i].Id);
            }
        }

        private static void AssertSurface(MarkedSurface surface)
        {
            AssertFinite(surface.EdgeStart);
            AssertFinite(surface.EdgeEnd);
            AssertFinite(surface.EdgeMidpoint);
            AssertFinite(surface.EdgeLength);
            AssertFinite(surface.MaxRoadWidth);
            Assert.Equal(Vec3.Distance(surface.EdgeStart, surface.EdgeEnd), surface.EdgeLength);
            Assert.Equal(surface.EdgeLength, surface.MaxRoadWidth);

            IReadOnlyList<Node> dots = surface.SnapDots;
            Assert.True(dots.Count >= 2, "An edge needs at least its two end dots.");
            Assert.Equal(surface.EdgeStart, dots[0].Position);
            Assert.Equal(surface.EdgeEnd, dots[dots.Count - 1].Position);

            Vec3 direction = (surface.EdgeEnd - surface.EdgeStart).Normalized();
            double step = surface.EdgeLength / (dots.Count - 1);
            for (int i = 0; i < dots.Count; i++)
            {
                Node dot = dots[i];
                AssertFinite(dot.Position);
                Assert.True(dot.Fixed);
                Assert.False(dot.GroundAnchor);
                Assert.Equal(surface.SurfaceId, dot.SurfaceId);

                Vec3 offset = dot.Position - surface.EdgeStart;
                Assert.Equal(0.0, Vec3.Cross(offset, direction).Length, Tolerance);
                Assert.Equal(i * step, Vec3.Dot(offset, direction), Tolerance);
            }
        }

        private static void AssertFinite(Vec3 v)
        {
            AssertFinite(v.X);
            AssertFinite(v.Y);
            AssertFinite(v.Z);
        }

        private static void AssertFinite(double value)
        {
            Assert.False(double.IsNaN(value), "Value is NaN.");
            Assert.False(double.IsInfinity(value), "Value is infinite.");
        }
    }
}
