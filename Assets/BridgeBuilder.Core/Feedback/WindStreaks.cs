using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Wind made visible for the wind phase: short streaks blowing across the canyon (along its Across direction,
    /// the way the phase's wind blows), computed from time alone like Snowfall. Each streak has a fixed lane and
    /// height and wraps around when it leaves the diorama. Room units.
    /// </summary>
    public static class WindStreaks
    {
        public const double Speed = 0.7;            // m/s in the room
        public const double Length = 0.08;          // m
        public const double HeightAboveTops = 0.3;  // streaks fill the canyon up to this far above the higher end

        /// <summary>Streak i at `time`: its tail and head, head downwind.</summary>
        public static (Vec3 tail, Vec3 head) Streak(CanyonLayout canyon, int i, double time)
        {
            double width = 2 * canyon.HalfWidth;
            double top = Math.Max(canyon.NearTop, canyon.FarTop) + HeightAboveTops;
            double along = canyon.Span * Hash(i, 1);
            double y = canyon.FloorY + (top - canyon.FloorY) * Hash(i, 2);
            double travel = (time * Speed + Hash(i, 3) * width) % width;     // 0..width across the diorama
            double head = -canyon.HalfWidth + travel;
            double tail = Math.Max(-canyon.HalfWidth, head - Length);
            return (canyon.Point(along, tail, y), canyon.Point(along, head, y));
        }

        static double Hash(int i, int salt)
        {
            unchecked
            {
                uint h = (uint)i * 2246822519u ^ (uint)salt * 3266489917u;
                h ^= h >> 15;
                h *= 668265263u;
                h ^= h >> 13;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }
}
