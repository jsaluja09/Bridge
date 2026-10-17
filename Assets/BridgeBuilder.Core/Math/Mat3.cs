namespace BridgeBuilder.Core
{
    /// <summary>A 3x3 double matrix, row-major. Used for member local axes (rows are local x, y, z).</summary>
    public readonly struct Mat3
    {
        public readonly double M00, M01, M02, M10, M11, M12, M20, M21, M22;

        public Mat3(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
        {
            M00 = m00; M01 = m01; M02 = m02;
            M10 = m10; M11 = m11; M12 = m12;
            M20 = m20; M21 = m21; M22 = m22;
        }

        public static Mat3 FromRows(Vec3 r0, Vec3 r1, Vec3 r2) =>
            new Mat3(r0.X, r0.Y, r0.Z, r1.X, r1.Y, r1.Z, r2.X, r2.Y, r2.Z);

        public static readonly Mat3 Identity = new Mat3(1, 0, 0, 0, 1, 0, 0, 0, 1);

        public double this[int row, int column] => (row * 3 + column) switch
        {
            0 => M00, 1 => M01, 2 => M02,
            3 => M10, 4 => M11, 5 => M12,
            6 => M20, 7 => M21, _ => M22,
        };

        public static Vec3 operator *(Mat3 m, Vec3 v) => new Vec3(
            m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z,
            m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z,
            m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z);

        public Mat3 Transposed() => new Mat3(M00, M10, M20, M01, M11, M21, M02, M12, M22);
    }
}
