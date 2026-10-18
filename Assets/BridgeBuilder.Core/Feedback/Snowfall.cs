using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Falling snow for the snow phase, computed from time alone (no stored state): each flake has a fixed spot
    /// in a box over the bridge and falls through it on a loop, drifting a little sideways. Room units.
    /// </summary>
    public static class Snowfall
    {
        public const double FallSpeed = 0.12;        // m/s in the room: a slow, gentle fall
        public const double DriftAmplitude = 0.015;  // m of sideways sway
        public const double DriftRate = 1.3;         // rad/s

        /// <summary>Where flake `i` is at `time`, inside the box from `min` to `max`.</summary>
        public static Vec3 Flake(int i, double time, Vec3 min, Vec3 max)
        {
            double fx = Hash(i, 1), fz = Hash(i, 2), phase = Hash(i, 3);
            double height = max.Y - min.Y;
            double fallen = (time * FallSpeed + phase * height) % height;
            double sway = Math.Sin(time * DriftRate + phase * 2 * Math.PI) * DriftAmplitude;
            return new Vec3(
                min.X + fx * (max.X - min.X) + sway,
                max.Y - fallen,
                min.Z + fz * (max.Z - min.Z) + sway * 0.5);
        }

        /// <summary>A fixed pseudo-random number in [0, 1) for flake i and a salt.</summary>
        static double Hash(int i, int salt)
        {
            unchecked
            {
                uint h = (uint)i * 2654435761u ^ (uint)salt * 40503u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }
}
