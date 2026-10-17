using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// A rigid-jointed 3D Euler-Bernoulli frame element (spec 2.7). DOF order per node: ux, uy, uz, rx, ry, rz,
    /// node i then node j. Local x runs i to j; local y = normalize(globalZ x localX) (global Y when the member
    /// is parallel to Z); local z = localX x localY. Bending in the local x-z plane uses Iy.
    /// </summary>
    public static class FrameElement
    {
        /// <summary>Rows are local x, y, z.</summary>
        public static Mat3 LocalAxes(Vec3 a, Vec3 b)
        {
            Vec3 x = (b - a).Normalized();
            Vec3 y = Vec3.Cross(Vec3.UnitZ, x);
            y = y.Length < 1e-9 ? Vec3.UnitY : y.Normalized();
            Vec3 z = Vec3.Cross(x, y);
            return Mat3.FromRows(x, y, z);
        }

        /// <summary>The 12 x 12 local stiffness matrix, row-major.</summary>
        public static double[] LocalStiffness(SectionProperties s, double L)
        {
            var k = new double[144];
            double ea = s.E * s.A / L, gj = s.G * s.J / L;
            double z12 = 12 * s.E * s.Iz / (L * L * L), z6 = 6 * s.E * s.Iz / (L * L), z4 = 4 * s.E * s.Iz / L, z2 = 2 * s.E * s.Iz / L;
            double y12 = 12 * s.E * s.Iy / (L * L * L), y6 = 6 * s.E * s.Iy / (L * L), y4 = 4 * s.E * s.Iy / L, y2 = 2 * s.E * s.Iy / L;
            void S(int i, int j, double v) { k[i * 12 + j] = v; k[j * 12 + i] = v; }

            S(0, 0, ea); S(0, 6, -ea); S(6, 6, ea);
            S(3, 3, gj); S(3, 9, -gj); S(9, 9, gj);
            // Bending in the local x-y plane (v, rz) uses Iz.
            S(1, 1, z12); S(1, 5, z6); S(1, 7, -z12); S(1, 11, z6);
            S(5, 5, z4); S(5, 7, -z6); S(5, 11, z2);
            S(7, 7, z12); S(7, 11, -z6); S(11, 11, z4);
            // Bending in the local x-z plane (w, ry) uses Iy.
            S(2, 2, y12); S(2, 4, -y6); S(2, 8, -y12); S(2, 10, -y6);
            S(4, 4, y4); S(4, 8, y6); S(4, 10, y2);
            S(8, 8, y12); S(8, 10, y6); S(10, 10, y4);
            return k;
        }

        /// <summary>Global 12 x 12 stiffness, T^T k T, for an element from a to b (bridge-world positions).</summary>
        public static double[] WorldStiffness(SectionProperties s, Vec3 a, Vec3 b)
        {
            double L = Vec3.Distance(a, b);
            if (!(L > 0))
                throw new ArgumentException("Zero-length element");
            var kl = LocalStiffness(s, L);
            var r = LocalAxes(a, b);
            var kg = new double[144];
            for (int bi = 0; bi < 4; bi++)
            {
                for (int bj = 0; bj < 4; bj++)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        for (int col = 0; col < 3; col++)
                        {
                            double sum = 0;
                            for (int p = 0; p < 3; p++)
                            {
                                for (int q = 0; q < 3; q++)
                                    sum += r[p, row] * kl[(bi * 3 + p) * 12 + bj * 3 + q] * r[q, col];
                            }
                            kg[(bi * 3 + row) * 12 + bj * 3 + col] = sum;
                        }
                    }
                }
            }
            return kg;
        }

        /// <summary>End forces in local axes, from the element's 12 global displacements: f = k (T u).</summary>
        public static MemberForce EndForces(int memberId, SectionProperties s, Vec3 a, Vec3 b, double[] globalDisplacements)
        {
            double L = Vec3.Distance(a, b);
            var kl = LocalStiffness(s, L);
            var r = LocalAxes(a, b);
            var local = new double[12];
            for (int blk = 0; blk < 4; blk++)
            {
                for (int row = 0; row < 3; row++)
                {
                    double sum = 0;
                    for (int c = 0; c < 3; c++)
                        sum += r[row, c] * globalDisplacements[blk * 3 + c];
                    local[blk * 3 + row] = sum;
                }
            }
            var f = new double[12];
            for (int i = 0; i < 12; i++)
            {
                double sum = 0;
                for (int j = 0; j < 12; j++)
                    sum += kl[i * 12 + j] * local[j];
                f[i] = sum;
            }
            return new MemberForce
            {
                MemberId = memberId,
                Axial = f[6],                  // force on end B along local x: tension positive
                ShearLocalYAtA = f[1],
                ShearLocalZAtA = f[2],
                MomentLocalYAtA = f[4],
                MomentLocalZAtA = f[5],
                ShearLocalYAtB = f[7],
                ShearLocalZAtB = f[8],
                MomentLocalYAtB = f[10],
                MomentLocalZAtB = f[11],
            };
        }
    }
}
