using System;

namespace BridgeBuilder.Core
{
    /// <summary>The five-pointed star drawn for the result, as 2D points: outer tips and inner notches alternating.</summary>
    public static class StarShape
    {
        /// <summary>Notch radius as a share of the tip radius: a classic five-pointed star.</summary>
        public const double InnerRatio = 0.45;

        /// <summary>Ten (x, y) points around (0, 0), the first tip straight up, going clockwise.</summary>
        public static (double x, double y)[] Points(double radius)
        {
            var points = new (double, double)[10];
            for (int i = 0; i < 10; i++)
            {
                double r = i % 2 == 0 ? radius : radius * InnerRatio;
                double angle = Math.PI / 2 - i * Math.PI / 5;
                points[i] = (r * Math.Cos(angle), r * Math.Sin(angle));
            }
            return points;
        }
    }

    /// <summary>
    /// How the earned stars appear, one after another: each grows from nothing, overshoots a little and settles
    /// (a "pop"). Unearned stars stay as empty outlines.
    /// </summary>
    public static class StarReveal
    {
        /// <summary>Pause before the first star.</summary>
        public const double DelaySeconds = 0.4;
        /// <summary>Time from one star's pop to the next.</summary>
        public const double StepSeconds = 0.45;
        /// <summary>How long one star takes to pop in.</summary>
        public const double PopSeconds = 0.35;
        /// <summary>The pop's peak size, relative to the settled star.</summary>
        public const double Overshoot = 1.25;

        /// <summary>When star `index` (0-based) starts to pop, seconds after the result is shown.</summary>
        public static double StartTime(int index) => DelaySeconds + index * StepSeconds;

        /// <summary>
        /// Size of star `index`'s fill at `seconds` after the result: 0 before its turn (and always 0 for an
        /// unearned star), rising past 1 to Overshoot, then settling at 1.
        /// </summary>
        public static double Fill(int index, int earned, double seconds)
        {
            if (index >= earned)
                return 0;
            double t = (seconds - StartTime(index)) / PopSeconds;
            if (t <= 0)
                return 0;
            if (t >= 1)
                return 1;
            // Up to the overshoot by 70% of the pop, then back down to 1.
            return t < 0.7 ? Overshoot * t / 0.7 : Overshoot - (Overshoot - 1) * (t - 0.7) / 0.3;
        }

        /// <summary>True during the step a star's pop starts (to play its sound once).</summary>
        public static bool StartsBetween(int index, int earned, double from, double to) =>
            index < earned && StartTime(index) > from && StartTime(index) <= to;
    }
}
