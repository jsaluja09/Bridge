using System;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Immutable 3D vector of doubles. Y is up. A NaN component is rejected on construction,
    /// so any arithmetic whose result would contain NaN throws <see cref="ArgumentException"/>.
    /// Infinite components are allowed. <c>default(Vec3)</c> equals <see cref="Zero"/>.
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public Vec3(double x, double y, double z)
        {
            RequireNotNaN(x, nameof(x));
            RequireNotNaN(y, nameof(y));
            RequireNotNaN(z, nameof(z));
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public static Vec3 Zero => default;

        public static Vec3 UnitX => new Vec3(1.0, 0.0, 0.0);

        public static Vec3 UnitY => new Vec3(0.0, 1.0, 0.0);

        public static Vec3 UnitZ => new Vec3(0.0, 0.0, 1.0);

        public double LengthSquared => X * X + Y * Y + Z * Z;

        public double Length => Math.Sqrt(LengthSquared);

        /// <summary>Returns the unit vector in this direction.</summary>
        /// <exception cref="InvalidOperationException">The length is zero or infinite.</exception>
        public Vec3 Normalized()
        {
            double length = Length;
            if (length == 0.0 || double.IsInfinity(length))
            {
                throw new InvalidOperationException("Cannot normalize a vector of length " + length.ToString(CultureInfo.InvariantCulture) + ".");
            }

            return new Vec3(X / length, Y / length, Z / length);
        }

        public static double Dot(Vec3 a, Vec3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        /// <summary>Right-handed cross product: <c>Cross(UnitX, UnitY) == UnitZ</c>.</summary>
        public static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
        }

        public static double Distance(Vec3 a, Vec3 b)
        {
            return (a - b).Length;
        }

        public static Vec3 operator +(Vec3 a, Vec3 b)
        {
            return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Vec3 operator -(Vec3 a, Vec3 b)
        {
            return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Vec3 operator -(Vec3 v)
        {
            return new Vec3(-v.X, -v.Y, -v.Z);
        }

        public static Vec3 operator *(Vec3 v, double scale)
        {
            return new Vec3(v.X * scale, v.Y * scale, v.Z * scale);
        }

        public static Vec3 operator *(double scale, Vec3 v)
        {
            return v * scale;
        }

        public static Vec3 operator /(Vec3 v, double divisor)
        {
            return new Vec3(v.X / divisor, v.Y / divisor, v.Z / divisor);
        }

        public static bool operator ==(Vec3 a, Vec3 b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Vec3 a, Vec3 b)
        {
            return !a.Equals(b);
        }

        public bool Equals(Vec3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is Vec3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
        }

        private static void RequireNotNaN(double value, string paramName)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentException("Vec3 components must not be NaN.", paramName);
            }
        }
    }
}
