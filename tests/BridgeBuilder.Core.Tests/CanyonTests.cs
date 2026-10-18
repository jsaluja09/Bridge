using System;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>The canyon the bridge crosses: its frame, depth, lift rule and buildable floor dots.</summary>
    public class CanyonLayoutTests
    {
        static Vec3 P(double x, double y, double z) => new Vec3(x, y, z);

        static CanyonLayout Layout(Vec3 a, Vec3 b)
        {
            var (near, far) = Pad.FromTaps(a, b);
            return new CanyonLayout(near, far);
        }

        [Fact]
        public void Frame_RunsFromTheNearEndToTheFarEnd()
        {
            var c = Layout(P(0, 0.9, 0), P(1, 0.9, 0));
            Assert.Equal(Vec3.UnitX, c.Forward);
            Assert.Equal(1, c.Span, 9);
            Assert.Equal(0.9, c.NearTop, 9);
            Assert.Equal(0.9 - Constants.Canyon.DepthMeters, c.FloorY, 9);
            Assert.Equal(P(0.5, 0.6, 0.2), c.Point(0.5, -0.2, 0.6));     // Across is -Z for a gap along +X
            var (along, across) = c.Local(P(0.25, 3, -0.1));
            Assert.Equal(0.25, along, 9);
            Assert.Equal(0.1, across, 9);
        }

        [Fact]
        public void TiltedGap_IsFramedAlongTheGap()
        {
            var c = Layout(P(0.2, 1, 0.3), P(0.8, 1, 1.1));               // 0.6 by 0.8: a 1 m gap at an angle
            Assert.Equal(1, c.Span, 9);
            Assert.Equal(0.6, c.Forward.X, 9);
            Assert.Equal(0.8, c.Forward.Z, 9);
            Assert.Equal(0, Vec3.Dot(c.Forward, c.Across), 9);
        }

        [Fact]
        public void Floor_HangsBelowTheLowerEnd()
        {
            var c = Layout(P(0, 1.0, 0), P(1, 0.8, 0));
            Assert.Equal(0.8 - Constants.Canyon.DepthMeters, c.FloorY, 9);
            Assert.Equal(1.0, c.NearTop, 9);
            Assert.Equal(0.8, c.FarTop, 9);
        }

        [Fact]
        public void LowEnds_AreLiftedSoTheCanyonClearsTheRealFloor()
        {
            double below = Constants.Canyon.DepthMeters + Constants.Canyon.BaseThicknessMeters + Constants.Canyon.FloorClearanceMeters;
            Assert.Equal(0, CanyonLayout.LiftNeeded(P(0, 1, 0), P(1, 1.2, 0), 0));
            Assert.Equal(0, CanyonLayout.LiftNeeded(P(0, below, 0), P(1, 0.9, 0), 0), 9);
            Assert.Equal(below - 0.1, CanyonLayout.LiftNeeded(P(0, 0.1, 0), P(1, 0.5, 0), 0), 9);   // the lower end decides
            Assert.Equal(below, CanyonLayout.LiftNeeded(P(0, 0.2, 0), P(1, 0.2, 0), 0.2), 9);      // tapped on the floor
        }

        [Fact]
        public void LiftedEnds_GiveTheFullDepthAboveTheFloor()
        {
            var a = P(0, 0.05, 0);
            var b = P(1, 0.1, 0);
            double lift = CanyonLayout.LiftNeeded(a, b, 0);
            var c = Layout(a + Vec3.UnitY * lift, b + Vec3.UnitY * lift);
            Assert.Equal(Constants.Canyon.FloorClearanceMeters + Constants.Canyon.BaseThicknessMeters, c.FloorY, 9);
        }

        [Fact]
        public void FloorDots_CoverTheFloorBetweenTheWalls()
        {
            var c = Layout(P(0, 0.9, 0), P(1, 0.9, 0));
            var dots = c.FloorDots();

            Assert.All(dots, d => Assert.Equal(c.FloorY, d.Y, 9));
            var local = dots.Select(c.Local).ToList();
            Assert.All(local, l => Assert.InRange(l.along, 0.05, 0.95));                       // none on a wall
            Assert.All(local, l => Assert.InRange(l.across, -c.HalfWidth - 1e-9, c.HalfWidth + 1e-9));
            Assert.Equal(-c.HalfWidth, local.Min(l => l.across), 9);
            Assert.Equal(c.HalfWidth, local.Max(l => l.across), 9);
            Assert.Equal(9 * 13, dots.Count);                                                  // 10 cm grid, 1 m by 1.2 m
            var (from, to) = c.River();
            Assert.Contains(local, l => l.along > from && l.along < to);                       // river included
        }

        [Fact]
        public void FloorDots_FollowATiltedGap()
        {
            var c = Layout(P(0.2, 1, 0.3), P(0.8, 1, 1.1));
            foreach (var d in c.FloorDots())
            {
                var (along, across) = c.Local(d);
                Assert.InRange(along, 0, c.Span);
                Assert.InRange(Math.Abs(across), 0, c.HalfWidth + 1e-9);
            }
        }

        [Fact]
        public void FloorDots_RejectABadSpacing() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => Layout(P(0, 1, 0), P(1, 1, 0)).FloorDots(0));

        [Fact]
        public void River_IsAQuarterOfTheFloorInTheMiddle()
        {
            var (from, to) = Layout(P(0, 1, 0), P(0.8, 1, 0)).River();
            Assert.Equal(0.4, (from + to) / 2, 9);
            Assert.Equal(0.8 * Constants.Canyon.RiverShare, to - from, 9);
        }
    }
}
