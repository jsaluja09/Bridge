using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// The town the bridge connects, on the far clifftop, plus a few trees on the near one (room meters). Streets: the
    /// main road with sidewalks, a cross street, and a square with a fountain. Buildings: houses of one and two
    /// storeys in four wall colors with red or slate roofs, chimneys, doors and windows, and a church with a bell
    /// tower, spire and clock. Street lamps line the main road; windows, lamps and the clock light up when the town
    /// is connected. Positions are `along` past the far cliff edge and `across` from the road's centerline.
    /// </summary>
    static class CanyonTown
    {
        const double Lift = 0.0008;           // paving sits just above the clifftop so the two don't flicker
        const double Overhang = 0.006;        // roofs reach past the walls
        const double Proud = 0.001;           // windows and doors sit just off their wall
        const double TwoStoreys = 0.07;       // walls taller than this get a second row of windows
        const double Sidewalk = 0.02;
        const double StreetFrom = 0.17, StreetTo = 0.21;     // the cross street, along past the cliff edge

        internal static void Build(CanyonMesh.Builder b, double farTop, double nearTop)
        {
            double a = b.Layout.Span, road = b.RoadHalfWidth, y = farTop;

            Streets(b, a, y, road);
            Square(b, a, y);

            // North of the main road (across > 0): a row along the cliff edge, then behind the cross street.
            House(b, a + 0.075, 0.235, y, 0.08, 0.07, 0.050, ridgeAcross: true, CanyonSurface.House, CanyonSurface.Roof, chimney: true);
            House(b, a + 0.080, 0.340, y, 0.08, 0.075, 0.085, ridgeAcross: false, CanyonSurface.HouseBlue, CanyonSurface.RoofDark, chimney: false);
            House(b, a + 0.075, 0.455, y, 0.08, 0.085, 0.055, ridgeAcross: true, CanyonSurface.HouseYellow, CanyonSurface.Roof, chimney: true);
            House(b, a + 0.275, 0.235, y, 0.08, 0.07, 0.050, ridgeAcross: false, CanyonSurface.Brick, CanyonSurface.RoofDark, chimney: true);
            Church(b, a + 0.285, 0.420, y);

            // South of the main road (across < 0): a row along the cliff edge, then the square and one more house.
            House(b, a + 0.075, -0.235, y, 0.08, 0.07, 0.055, ridgeAcross: false, CanyonSurface.Brick, CanyonSurface.Roof, chimney: true);
            House(b, a + 0.080, -0.340, y, 0.08, 0.075, 0.090, ridgeAcross: true, CanyonSurface.House, CanyonSurface.RoofDark, chimney: false);
            House(b, a + 0.075, -0.455, y, 0.08, 0.08, 0.050, ridgeAcross: false, CanyonSurface.HouseBlue, CanyonSurface.Roof, chimney: true);
            House(b, a + 0.280, -0.455, y, 0.08, 0.08, 0.060, ridgeAcross: true, CanyonSurface.HouseYellow, CanyonSurface.Roof, chimney: true);

            foreach (double along in new[] { 0.06, 0.15, 0.27 })
            {
                Lamp(b, a + along, road + 0.013, y);
                Lamp(b, a + along, -road - 0.013, y);
            }

            Flagpole(b, a + 0.04, -road - 0.03, y);

            // Trees around the edge of the town, and a few on the near clifftop.
            Tree(b, a + 0.31, 0.56, y, 1.1);
            Tree(b, a + 0.08, 0.56, y, 0.9);
            Tree(b, a + 0.25, 0.56, y, 1.0);
            Tree(b, a + 0.31, -0.56, y, 1.0);
            Tree(b, a + 0.08, -0.56, y, 1.2);
            Tree(b, a + 0.22, -0.56, y, 0.85);
            Tree(b, a + 0.31, 0.30, y, 0.9);
            for (int i = 0; i < 4; i++)
            {
                double along = -0.10 - 0.07 * i;
                Tree(b, along, 0.30 + 0.07 * (i % 2), nearTop, 0.85 + 0.1 * i);
                Tree(b, along, -0.32 - 0.07 * ((i + 1) % 2), nearTop, 1.15 - 0.1 * i);
            }
            Tree(b, -0.30, 0.52, nearTop, 1.2);
            Tree(b, -0.27, -0.53, nearTop, 1.0);
        }

        /// <summary>Sidewalks along the main road (broken by the cross street) and the cross street itself.</summary>
        static void Streets(CanyonMesh.Builder b, double a, double y, double road)
        {
            double top = y + Lift, back = Constants.Canyon.ClifftopDepthMeters, w = b.Layout.HalfWidth;
            foreach (int side in new[] { 1, -1 })
            {
                double c0 = side * road, c1 = side * (road + Sidewalk);
                Flat(b, a, a + StreetFrom, c0, c1, top, CanyonSurface.Pavement);
                Flat(b, a + StreetTo, a + back, c0, c1, top, CanyonSurface.Pavement);
            }
            Flat(b, a + StreetFrom, a + StreetTo, -w, w, top, CanyonSurface.Road);
        }

        /// <summary>The town square south of the main road, with a fountain in the middle.</summary>
        static void Square(CanyonMesh.Builder b, double a, double y)
        {
            Flat(b, a + 0.23, a + 0.33, -0.36, -0.20, y + Lift, CanyonSurface.Pavement);
            Vec3 middle = b.P(a + 0.28, -0.28, y);
            b.Box(middle, 0.06, 0.06, 0.01, CanyonSurface.Pavement);
            Flat(b, a + 0.255, a + 0.305, -0.305, -0.255, y + 0.0105, CanyonSurface.FountainWater);
            b.Box(middle, 0.008, 0.008, 0.03, CanyonSurface.Pavement);
            b.Box(middle + Vec3.UnitY * 0.03, 0.022, 0.022, 0.004, CanyonSurface.Pavement);
        }

        /// <summary>
        /// A house standing at (along, across): walls, a pitched roof (its ridge running across the canyon or along
        /// it), gables, windows on the side facing the bridge (two rows if it has two storeys), a door and a window on
        /// the side facing the main road, and maybe a chimney.
        /// </summary>
        static void House(CanyonMesh.Builder b, double along, double across, double y, double sizeAlong, double sizeAcross,
                          double wallHeight, bool ridgeAcross, CanyonSurface wall, CanyonSurface roof, bool chimney)
        {
            Vec3 Pt(double da, double dc, double dy) => b.P(along + da, across + dc, y + dy);
            double sa = sizeAlong / 2, sc = sizeAcross / 2, ha = sa + Overhang, hc = sc + Overhang;
            double rise = Math.Min(sizeAlong, sizeAcross) * 0.45, ridge = wallHeight + rise;

            b.Box(Pt(0, 0, 0), sizeAlong, sizeAcross, wallHeight, wall);
            if (ridgeAcross)
            {
                b.Quad(Pt(-ha, -hc, wallHeight), Pt(-ha, hc, wallHeight), Pt(0, hc, ridge), Pt(0, -hc, ridge), roof);
                b.Quad(Pt(ha, -hc, wallHeight), Pt(ha, hc, wallHeight), Pt(0, hc, ridge), Pt(0, -hc, ridge), roof);
                b.Tri(Pt(-sa, -sc, wallHeight), Pt(sa, -sc, wallHeight), Pt(0, -sc, ridge), wall);
                b.Tri(Pt(-sa, sc, wallHeight), Pt(sa, sc, wallHeight), Pt(0, sc, ridge), wall);
            }
            else
            {
                b.Quad(Pt(-ha, -hc, wallHeight), Pt(ha, -hc, wallHeight), Pt(ha, 0, ridge), Pt(-ha, 0, ridge), roof);
                b.Quad(Pt(-ha, hc, wallHeight), Pt(ha, hc, wallHeight), Pt(ha, 0, ridge), Pt(-ha, 0, ridge), roof);
                b.Tri(Pt(-sa, -sc, wallHeight), Pt(-sa, sc, wallHeight), Pt(-sa, 0, ridge), wall);
                b.Tri(Pt(sa, -sc, wallHeight), Pt(sa, sc, wallHeight), Pt(sa, 0, ridge), wall);
            }

            int storeys = wallHeight > TwoStoreys ? 2 : 1;
            double storey = wallHeight / storeys, win = Math.Min(sizeAlong, sizeAcross) * 0.2;
            for (int s = 0; s < storeys; s++)
            {
                double z0 = storey * s + storey * 0.4, z1 = storey * s + storey * 0.75;
                foreach (double dc in new[] { -sc * 0.5, sc * 0.5 })
                    FrontWindow(b, Pt, -sa - Proud, dc, win, z0, z1, CanyonSurface.Window);
            }

            // The side facing the main road: a door at street level and a window beside it, on every storey.
            double roadSide = -Math.Sign(across) * (sc + Proud);
            SideWindow(b, Pt, roadSide, -sa * 0.4, win * 0.9, 0, storey * 0.62, CanyonSurface.Door);
            for (int s = 0; s < storeys; s++)
                SideWindow(b, Pt, roadSide, sa * 0.45, win, storey * s + storey * 0.4, storey * s + storey * 0.75, CanyonSurface.Window);

            if (chimney)
                b.Box(Pt(sa * 0.45, sc * 0.45, wallHeight), 0.012, 0.012, rise + 0.012, CanyonSurface.Brick);
        }

        /// <summary>A window or door on a face that looks along the canyon (at da), centered at dc.</summary>
        static void FrontWindow(CanyonMesh.Builder b, Func<double, double, double, Vec3> pt, double da, double dc, double width, double z0, double z1, CanyonSurface s) =>
            b.Quad(pt(da, dc - width / 2, z0), pt(da, dc + width / 2, z0), pt(da, dc + width / 2, z1), pt(da, dc - width / 2, z1), s);

        /// <summary>A window or door on a face that looks across the canyon (at dc), centered at da.</summary>
        static void SideWindow(CanyonMesh.Builder b, Func<double, double, double, Vec3> pt, double dc, double da, double width, double z0, double z1, CanyonSurface s) =>
            b.Quad(pt(da - width / 2, dc, z0), pt(da + width / 2, dc, z0), pt(da + width / 2, dc, z1), pt(da - width / 2, dc, z1), s);

        /// <summary>A church: a brick nave with a slate roof and tall windows, and a bell tower with a spire and a clock.</summary>
        static void Church(CanyonMesh.Builder b, double along, double across, double y)
        {
            House(b, along, across, y, 0.11, 0.07, 0.06, ridgeAcross: false, CanyonSurface.Brick, CanyonSurface.RoofDark, chimney: false);
            const double towerSize = 0.035, towerHeight = 0.14;
            double towerAlong = along - 0.055;
            Vec3 foot = b.P(towerAlong, across, y);
            b.Box(foot, towerSize, towerSize, towerHeight, CanyonSurface.Brick);
            b.Pyramid(foot + Vec3.UnitY * towerHeight, towerSize + 0.006, towerSize + 0.006, 0.07, CanyonSurface.RoofDark);
            Vec3 Pt(double da, double dc, double dy) => b.P(towerAlong + da, across + dc, y + dy);
            FrontWindow(b, Pt, -towerSize / 2 - Proud, 0, 0.016, 0.105, 0.121, CanyonSurface.Window);        // the clock
            FrontWindow(b, Pt, -towerSize / 2 - Proud, 0, 0.012, 0, 0.03, CanyonSurface.Door);
        }

        /// <summary>A street lamp: a thin pole with a lamp head that lights up with the windows.</summary>
        static void Lamp(CanyonMesh.Builder b, double along, double across, double y)
        {
            Vec3 foot = b.P(along, across, y);
            b.Box(foot, 0.003, 0.003, 0.055, CanyonSurface.Pole);
            b.Box(foot + Vec3.UnitY * 0.055, 0.009, 0.009, 0.006, CanyonSurface.Window);
        }

        static void Flagpole(CanyonMesh.Builder b, double along, double across, double y)
        {
            Vec3 foot = b.P(along, across, y);
            b.Box(foot, 0.006, 0.006, 0.16, CanyonSurface.Pole);
            b.SetFlagTop(foot + Vec3.UnitY * 0.16);
        }

        /// <summary>A low-poly pine: a trunk and two stacked crowns, sized by `scale`.</summary>
        static void Tree(CanyonMesh.Builder b, double along, double across, double y, double scale)
        {
            Vec3 foot = b.P(along, across, y);
            b.Box(foot, 0.006 * scale, 0.006 * scale, 0.02 * scale, CanyonSurface.Trunk);
            b.Pyramid(foot + Vec3.UnitY * (0.015 * scale), 0.04 * scale, 0.04 * scale, 0.045 * scale, CanyonSurface.Leaves);
            b.Pyramid(foot + Vec3.UnitY * (0.04 * scale), 0.03 * scale, 0.03 * scale, 0.04 * scale, CanyonSurface.Leaves);
        }

        /// <summary>A flat rectangle between two along and two across values at height y.</summary>
        static void Flat(CanyonMesh.Builder b, double a0, double a1, double c0, double c1, double y, CanyonSurface s) =>
            b.Quad(b.P(a0, c0, y), b.P(a1, c0, y), b.P(a1, c1, y), b.P(a0, c1, y), s);
    }
}
