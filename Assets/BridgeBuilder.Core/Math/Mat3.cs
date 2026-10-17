using System;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Immutable 3x3 matrix of doubles. <c>Mij</c> is row i, column j. Vectors are columns, so
    /// <c>m * v</c> transforms v. A NaN entry is rejected on construction with
    /// <see cref="ArgumentException"/>. <c>default(Mat3)</c> equals <see cref="Zero"/>.
    /// </summary>
    public readonly struct Mat3 : IEquatable<Mat3>
    {
        public Mat3(
            double m00, double m01, double m02,
            double m10, double m11, double m12,
            double m20, double m21, double m22)
        {
            RequireNotNaN(m00, nameof(m00));
            RequireNotNaN(m01, nameof(m01));
            RequireNotNaN(m02, nameof(m02));
            RequireNotNaN(m10, nameof(m10));
            RequireNotNaN(m11, nameof(m11));
            RequireNotNaN(m12, nameof(m12));
            RequireNotNaN(m20, nameof(m20));
            RequireNotNaN(m21, nameof(m21));
            RequireNotNaN(m22, nameof(m22));
            M00 = m00;
            M01 = m01;
            M02 = m02;
            M10 = m10;
            M11 = m11;
            M12 = m12;
            M20 = m20;
            M21 = m21;
            M22 = m22;
        }

        public double M00 { get; }

        public double M01 { get; }

        public double M02 { get; }

        public double M10 { get; }

        public double M11 { get; }

        public double M12 { get; }

        public double M20 { get; }

        public double M21 { get; }

        public double M22 { get; }

        public static Mat3 Zero => default;

        public static Mat3 Identity => new Mat3(
            1.0, 0.0, 0.0,
            0.0, 1.0, 0.0,
            0.0, 0.0, 1.0);

        public double this[int row, int column]
        {
            get
            {
                if (row < 0 || row > 2)
                {
                    throw new ArgumentOutOfRangeException(nameof(row));
                }

                if (column < 0 || column > 2)
                {
                    throw new ArgumentOutOfRangeException(nameof(column));
                }

                switch (row * 3 + column)
                {
                    case 0: return M00;
                    case 1: return M01;
                    case 2: return M02;
                    case 3: return M10;
                    case 4: return M11;
                    case 5: return M12;
                    case 6: return M20;
                    case 7: return M21;
                    default: return M22;
                }
            }
        }

        public double Determinant =>
            M00 * (M11 * M22 - M12 * M21)
            - M01 * (M10 * M22 - M12 * M20)
            + M02 * (M10 * M21 - M11 * M20);

        /// <summary>
        /// Builds a matrix whose rows are the given vectors. With orthonormal local axes as rows,
        /// <c>m * global</c> gives local components and <c>m.Transpose() * local</c> gives global ones.
        /// </summary>
        public static Mat3 FromRows(Vec3 row0, Vec3 row1, Vec3 row2)
        {
            return new Mat3(
                row0.X, row0.Y, row0.Z,
                row1.X, row1.Y, row1.Z,
                row2.X, row2.Y, row2.Z);
        }

        public static Mat3 FromColumns(Vec3 column0, Vec3 column1, Vec3 column2)
        {
            return new Mat3(
                column0.X, column1.X, column2.X,
                column0.Y, column1.Y, column2.Y,
                column0.Z, column1.Z, column2.Z);
        }

        public Vec3 Row(int index)
        {
            return new Vec3(this[index, 0], this[index, 1], this[index, 2]);
        }

        public Vec3 Column(int index)
        {
            return new Vec3(this[0, index], this[1, index], this[2, index]);
        }

        public Mat3 Transpose()
        {
            return new Mat3(
                M00, M10, M20,
                M01, M11, M21,
                M02, M12, M22);
        }

        public static Vec3 operator *(Mat3 m, Vec3 v)
        {
            return new Vec3(
                m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z,
                m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z,
                m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z);
        }

        public static Mat3 operator *(Mat3 a, Mat3 b)
        {
            return new Mat3(
                a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
                a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
                a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
                a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
                a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
                a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
                a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
                a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
                a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22);
        }

        public static Mat3 operator *(Mat3 m, double scale)
        {
            return new Mat3(
                m.M00 * scale, m.M01 * scale, m.M02 * scale,
                m.M10 * scale, m.M11 * scale, m.M12 * scale,
                m.M20 * scale, m.M21 * scale, m.M22 * scale);
        }

        public static Mat3 operator *(double scale, Mat3 m)
        {
            return m * scale;
        }

        public static Mat3 operator +(Mat3 a, Mat3 b)
        {
            return new Mat3(
                a.M00 + b.M00, a.M01 + b.M01, a.M02 + b.M02,
                a.M10 + b.M10, a.M11 + b.M11, a.M12 + b.M12,
                a.M20 + b.M20, a.M21 + b.M21, a.M22 + b.M22);
        }

        public static Mat3 operator -(Mat3 a, Mat3 b)
        {
            return new Mat3(
                a.M00 - b.M00, a.M01 - b.M01, a.M02 - b.M02,
                a.M10 - b.M10, a.M11 - b.M11, a.M12 - b.M12,
                a.M20 - b.M20, a.M21 - b.M21, a.M22 - b.M22);
        }

        public static bool operator ==(Mat3 a, Mat3 b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Mat3 a, Mat3 b)
        {
            return !a.Equals(b);
        }

        public bool Equals(Mat3 other)
        {
            return M00.Equals(other.M00) && M01.Equals(other.M01) && M02.Equals(other.M02)
                && M10.Equals(other.M10) && M11.Equals(other.M11) && M12.Equals(other.M12)
                && M20.Equals(other.M20) && M21.Equals(other.M21) && M22.Equals(other.M22);
        }

        public override bool Equals(object obj)
        {
            return obj is Mat3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(M00);
            hash.Add(M01);
            hash.Add(M02);
            hash.Add(M10);
            hash.Add(M11);
            hash.Add(M12);
            hash.Add(M20);
            hash.Add(M21);
            hash.Add(M22);
            return hash.ToHashCode();
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[({0}, {1}, {2}), ({3}, {4}, {5}), ({6}, {7}, {8})]",
                M00, M01, M02, M10, M11, M12, M20, M21, M22);
        }

        private static void RequireNotNaN(double value, string paramName)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentException("Mat3 entries must not be NaN.", paramName);
            }
        }
    }
}
