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

    /// <summary>The canyon diorama mesh: shape, budget, and colors for snow and the town's lights.</summary>
    public class CanyonMeshTests
    {
        static CanyonLayout Layout(double nearY = 0.9, double farY = 0.9)
        {
            var (near, far) = Pad.FromTaps(new Vec3(0, nearY, 0), new Vec3(1, farY, 0));
            return new CanyonLayout(near, far);
        }

        [Fact]
        public void Mesh_IsFlatShadedFiniteAndWithinBudget()
        {
            var m = CanyonMesh.Build(Layout());

            Assert.InRange(m.TriangleCount, 500, 3000);
            Assert.Equal(m.Positions.Count, m.Triangles.Count);                 // every triangle has its own vertices
            Assert.Equal(m.Positions.Count, m.Normals.Count);
            Assert.Equal(m.Positions.Count, m.Surfaces.Count);
            Assert.All(m.Positions, p => Assert.True(p.IsFinite));
            Assert.All(m.Normals, n => Assert.Equal(1, n.Length, 6));
            Assert.All(m.Normals, n => Assert.True(n.Y >= 0));
        }

        [Fact]
        public void Mesh_IsTheSameEveryTime()
        {
            var a = CanyonMesh.Build(Layout());
            var b = CanyonMesh.Build(Layout());
            Assert.Equal(a.Positions, b.Positions);
            Assert.Equal(a.Surfaces, b.Surfaces);
        }

        [Fact]
        public void Mesh_StaysInsideTheDiorama()
        {
            var c = Layout();
            var m = CanyonMesh.Build(c);
            double back = Constants.Canyon.ClifftopDepthMeters;
            foreach (var p in m.Positions)
            {
                var (along, across) = c.Local(p);
                Assert.InRange(along, -back - 1e-9, c.Span + back + 1e-9);
                Assert.InRange(across, -c.HalfWidth - 0.011, c.HalfWidth + 0.011);
                Assert.InRange(p.Y, c.FloorY - Constants.Canyon.BaseThicknessMeters - 1e-9, c.NearTop + 0.25);
            }
        }

        [Fact]
        public void Walls_NeverReachIntoTheCanyon()
        {
            var c = Layout();
            var m = CanyonMesh.Build(c);
            for (int i = 0; i < m.Positions.Count; i++)
            {
                var s = m.Surfaces[i];
                if (s != CanyonSurface.RockLight && s != CanyonSurface.RockDark)
                    continue;
                var (along, _) = c.Local(m.Positions[i]);
                Assert.True(along <= 1e-9 || along >= c.Span - 1e-9, $"rock at along {along}");
            }
        }

        [Fact]
        public void RoadRunsOntoBothEnds_AndTheFloorAndRiverAreInPlace()
        {
            var c = Layout(0.9, 0.85);
            var m = CanyonMesh.Build(c);
            var road = Enumerable.Range(0, m.Positions.Count).Where(i => m.Surfaces[i] == CanyonSurface.Road).Select(i => c.Local(m.Positions[i])).ToList();
            Assert.Contains(road, l => l.along < 0);
            Assert.Contains(road, l => l.along > c.Span);
            Assert.All(road, l => Assert.InRange(Math.Abs(l.across), 0, Constants.Surface.PadWidthMeters / 2 + 0.051));

            var (from, to) = c.River();
            for (int i = 0; i < m.Positions.Count; i++)
            {
                if (m.Surfaces[i] == CanyonSurface.River)
                {
                    Assert.True(m.Positions[i].Y < c.FloorY);
                    Assert.InRange(c.Local(m.Positions[i]).along, from - 1e-9, to + 1e-9);
                }
                if (m.Surfaces[i] == CanyonSurface.Floor && m.Normals[i].Y > 0.9)
                    Assert.Equal(c.FloorY, m.Positions[i].Y, 9);
            }
        }

        [Fact]
        public void TheTownStandsOnTheFarClifftop()
        {
            var c = Layout();
            var m = CanyonMesh.Build(c);
            var house = Enumerable.Range(0, m.Positions.Count).Where(i => m.Surfaces[i] == CanyonSurface.House || m.Surfaces[i] == CanyonSurface.Window).ToList();
            Assert.NotEmpty(house);
            Assert.All(house, i => Assert.True(c.Local(m.Positions[i]).along > c.Span));
            Assert.True(c.Local(m.FlagTop).along > c.Span);
            Assert.True(m.FlagTop.Y > c.FarTop + 0.1);
        }

        [Fact]
        public void Snow_WhitensOnlyTheTops()
        {
            var m = CanyonMesh.Build(Layout());
            var plain = new Rgb[m.Positions.Count];
            var snowy = new Rgb[m.Positions.Count];
            m.Colors(false, false, plain);
            m.Colors(true, false, snowy);
            for (int i = 0; i < plain.Length; i++)
            {
                bool changed = !plain[i].Equals(snowy[i]);
                bool top = m.Normals[i].Y > 0.7;
                if (changed)
                    Assert.True(top, $"{m.Surfaces[i]} changed but faces sideways");
                if (!top || m.Surfaces[i] == CanyonSurface.River || m.Surfaces[i] == CanyonSurface.Window)
                    Assert.Equal(plain[i], snowy[i]);
            }
            Assert.Contains(Enumerable.Range(0, plain.Length), i => m.Surfaces[i] == CanyonSurface.Clifftop && !plain[i].Equals(snowy[i]));
        }

        [Fact]
        public void Lights_ChangeOnlyTheWindows()
        {
            var m = CanyonMesh.Build(Layout());
            var dark = new Rgb[m.Positions.Count];
            var lit = new Rgb[m.Positions.Count];
            m.Colors(false, false, dark);
            m.Colors(false, true, lit);
            for (int i = 0; i < dark.Length; i++)
                Assert.Equal(m.Surfaces[i] == CanyonSurface.Window, !dark[i].Equals(lit[i]));
        }
    }
}
