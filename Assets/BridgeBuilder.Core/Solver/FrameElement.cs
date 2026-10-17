using System;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Euler-Bernoulli 3D frame element with rigid joints (spec 2.7): axial, torsion, and bending in
    /// both planes, with no shear deformation. Element degrees of freedom are ux, uy, uz, rx, ry, rz
    /// at end A, then the same six at end B. Local x runs from end A to end B. Local y is
    /// normalize(globalZ x local x) and local z is local x x local y, except that a member parallel
    /// to global Z (zero cross product) uses local y = global Y. Deflection along local z (bending
    /// about local y) uses <see cref="SectionProperties.Iy"/>; deflection along local y (bending
    /// about local z) uses <see cref="SectionProperties.Iz"/>.
    /// </summary>
    public static class FrameElement
    {
        /// <summary>Degrees of freedom of one element: two nodes.</summary>
        public const int DofCount = 2 * Constants.Solver.DofPerNode;

        private const int Ux = 0;
        private const int Uy = 1;
        private const int Uz = 2;
        private const int Rx = 3;
        private const int Ry = 4;
        private const int Rz = 5;
        private const int EndB = Constants.Solver.DofPerNode;

        /// <summary>Local axes x, y, z as the rows of a rotation matrix, so <c>axes * global</c> gives local components.</summary>
        /// <exception cref="ArgumentException">The positions are not finite or coincide.</exception>
        public static Mat3 LocalAxes(Vec3 positionA, Vec3 positionB)
        {
            RequireLength(positionA, positionB);
            Vec3 x = (positionB - positionA).Normalized();
            Vec3 horizontal = Vec3.Cross(Vec3.UnitZ, x);
            Vec3 y = horizontal.LengthSquared == 0.0 ? Vec3.UnitY : horizontal.Normalized();
            Vec3 z = Vec3.Cross(x, y);
            return Mat3.FromRows(x, y, z);
        }

        /// <summary>
        /// The 12 by 12 element stiffness in global axes, in the degree-of-freedom order of this class.
        /// The result is exactly symmetric.
        /// </summary>
        /// <exception cref="ArgumentException">The positions coincide or are not finite, or a section stiffness is not positive and finite.</exception>
        public static double[,] WorldStiffness(Vec3 positionA, Vec3 positionB, SectionProperties section)
        {
            RequireValidSection(section, nameof(section));
            double length = RequireLength(positionA, positionB);
            double[,] axes = ToArray(LocalAxes(positionA, positionB));
            double[,] local = LocalStiffness(section, length);

            // World = T^T * local * T, where T repeats the 3 by 3 axes on its four diagonal blocks.
            var world = new double[DofCount, DofCount];
            for (int row = 0; row < DofCount; row++)
            {
                int rowBlock = row - row % 3;
                for (int column = row; column < DofCount; column++)
                {
                    int columnBlock = column - column % 3;
                    double sum = 0.0;
                    for (int i = 0; i < 3; i++)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            sum += axes[i, row % 3] * local[rowBlock + i, columnBlock + j] * axes[j, column % 3];
                        }
                    }

                    world[row, column] = sum;
                    world[column, row] = sum;
                }
            }

            return world;
        }

        /// <summary>
        /// End actions in local axes for the element's 12 global displacements (the order of
        /// <see cref="WorldStiffness"/>): the forces and moments the joints apply to the member at
        /// each end. Axial force is tension positive. Torsion is not reported, and
        /// <see cref="MemberForce.Utilization"/> is left 0.
        /// </summary>
        /// <exception cref="ArgumentException">An argument is invalid or a displacement is not finite.</exception>
        public static MemberForce EndForces(int memberId, Vec3 positionA, Vec3 positionB, SectionProperties section, double[] worldDisplacements)
        {
            RequireValidSection(section, nameof(section));
            if (worldDisplacements == null)
            {
                throw new ArgumentNullException(nameof(worldDisplacements));
            }

            if (worldDisplacements.Length != DofCount)
            {
                throw new ArgumentException("An element has " + DofCount.ToString(CultureInfo.InvariantCulture) + " displacements.", nameof(worldDisplacements));
            }

            foreach (double value in worldDisplacements)
            {
                if (!double.IsFinite(value))
                {
                    throw new ArgumentException("Element displacements must be finite.", nameof(worldDisplacements));
                }
            }

            double length = RequireLength(positionA, positionB);
            double[,] axes = ToArray(LocalAxes(positionA, positionB));
            double[,] local = LocalStiffness(section, length);

            var localDisplacements = new double[DofCount];
            for (int dof = 0; dof < DofCount; dof++)
            {
                int block = dof - dof % 3;
                double sum = 0.0;
                for (int i = 0; i < 3; i++)
                {
                    sum += axes[dof % 3, i] * worldDisplacements[block + i];
                }

                localDisplacements[dof] = sum;
            }

            var end = new double[DofCount];
            for (int row = 0; row < DofCount; row++)
            {
                double sum = 0.0;
                for (int column = 0; column < DofCount; column++)
                {
                    sum += local[row, column] * localDisplacements[column];
                }

                end[row] = sum;
            }

            return new MemberForce
            {
                MemberId = memberId,
                Axial = end[EndB + Ux],
                ShearLocalYAtA = end[Uy],
                ShearLocalZAtA = end[Uz],
                MomentLocalYAtA = end[Ry],
                MomentLocalZAtA = end[Rz],
                ShearLocalYAtB = end[EndB + Uy],
                ShearLocalZAtB = end[EndB + Uz],
                MomentLocalYAtB = end[EndB + Ry],
                MomentLocalZAtB = end[EndB + Rz],
            };
        }

        internal static bool IsFinite(Vec3 v)
        {
            return double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        }

        private static void RequireValidSection(SectionProperties section, string paramName)
        {
            if (section == null)
            {
                throw new ArgumentNullException(paramName);
            }

            RequirePositive(section, section.E, "E", paramName);
            RequirePositive(section, section.G, "G", paramName);
            RequirePositive(section, section.A, "A", paramName);
            RequirePositive(section, section.Iy, "Iy", paramName);
            RequirePositive(section, section.Iz, "Iz", paramName);
            RequirePositive(section, section.J, "J", paramName);
        }

        private static double[,] LocalStiffness(SectionProperties section, double length)
        {
            var k = new double[DofCount, DofCount];

            double axial = section.E * section.A / length;
            SetSymmetric(k, Ux, Ux, axial);
            SetSymmetric(k, Ux, EndB + Ux, -axial);
            SetSymmetric(k, EndB + Ux, EndB + Ux, axial);

            double torsion = section.G * section.J / length;
            SetSymmetric(k, Rx, Rx, torsion);
            SetSymmetric(k, Rx, EndB + Rx, -torsion);
            SetSymmetric(k, EndB + Rx, EndB + Rx, torsion);

            // Deflection along local y turns the member about +z, so rz = +d(uy)/dx.
            AddBending(k, Uy, Rz, section.E * section.Iz, length, 1.0);

            // Deflection along local z turns the member about -y, so ry = -d(uz)/dx.
            AddBending(k, Uz, Ry, section.E * section.Iy, length, -1.0);
            return k;
        }

        private static void AddBending(double[,] k, int deflection, int rotation, double flexuralRigidity, double length, double rotationSign)
        {
            double shear = 12.0 * flexuralRigidity / (length * length * length);
            double coupling = rotationSign * 6.0 * flexuralRigidity / (length * length);
            double near = 4.0 * flexuralRigidity / length;
            double far = 2.0 * flexuralRigidity / length;

            int deflectionB = EndB + deflection;
            int rotationB = EndB + rotation;
            SetSymmetric(k, deflection, deflection, shear);
            SetSymmetric(k, deflection, rotation, coupling);
            SetSymmetric(k, deflection, deflectionB, -shear);
            SetSymmetric(k, deflection, rotationB, coupling);
            SetSymmetric(k, rotation, rotation, near);
            SetSymmetric(k, rotation, deflectionB, -coupling);
            SetSymmetric(k, rotation, rotationB, far);
            SetSymmetric(k, deflectionB, deflectionB, shear);
            SetSymmetric(k, deflectionB, rotationB, -coupling);
            SetSymmetric(k, rotationB, rotationB, near);
        }

        private static void SetSymmetric(double[,] k, int row, int column, double value)
        {
            k[row, column] = value;
            k[column, row] = value;
        }

        private static double RequireLength(Vec3 positionA, Vec3 positionB)
        {
            if (!IsFinite(positionA) || !IsFinite(positionB))
            {
                throw new ArgumentException("Frame element end positions must be finite.");
            }

            double length = Vec3.Distance(positionA, positionB);
            if (!(length > 0.0) || double.IsInfinity(length))
            {
                throw new ArgumentException("A frame element needs two distinct end positions.");
            }

            return length;
        }

        private static void RequirePositive(SectionProperties section, double value, string name, string paramName)
        {
            if (!(value > 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentException(
                    "Section " + section.Id.ToString(CultureInfo.InvariantCulture) + " needs a positive, finite " + name + ".",
                    paramName);
            }
        }

        private static double[,] ToArray(Mat3 m)
        {
            var a = new double[3, 3];
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    a[row, column] = m[row, column];
                }
            }

            return a;
        }
    }
}
