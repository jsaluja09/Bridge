using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    public enum LoadColor
    {
        Green,
        Yellow,
        Red,
    }

    /// <summary>
    /// Utilization to feedback (spec 2.10): color bucket, stripe density, pulse, bowing and creak pitch.
    /// Pure functions; shaders, audio and meshes live in the game.
    /// </summary>
    public static class FeedbackMapping
    {
        static double Check(double u) => double.IsNaN(u) ? throw new ArgumentOutOfRangeException(nameof(u), "Utilization is NaN") : u;
        static double Clamp01(double u) => Math.Min(1, Math.Max(0, Check(u)));

        public static LoadColor Color(double u, double yellow, double red)
        {
            Check(u);
            return u >= red ? LoadColor.Red : u >= yellow ? LoadColor.Yellow : LoadColor.Green;
        }

        public static LoadColor Color(double u) => Color(u, Constants.Feedback.YellowUtilization, Constants.Feedback.RedUtilization);

        public static double StripeDensity(double u, double min, double max) => min + (max - min) * Clamp01(u);
        public static double StripeDensity(double u) => StripeDensity(u, Constants.Feedback.StripeDensityMin, Constants.Feedback.StripeDensityMax);

        public static bool Pulse(double u, double threshold) => Check(u) >= threshold;
        public static bool Pulse(double u) => Pulse(u, Constants.Feedback.PulseUtilization);

        /// <summary>How far a member visibly bows as it approaches its buckling load.</summary>
        public static double BowAmplitude(double bucklingUtilization, double maxBow) => maxBow * Clamp01(bucklingUtilization);
        public static double BowAmplitude(double bucklingUtilization) => BowAmplitude(bucklingUtilization, Constants.Feedback.MaxBowAmplitudeMeters);

        public static double CreakPitch(double worstUtilization, double minHz, double maxHz) => minHz + (maxHz - minHz) * Clamp01(worstUtilization);
        public static double CreakPitch(double worstUtilization) => CreakPitch(worstUtilization, Constants.Feedback.CreakPitchMinHz, Constants.Feedback.CreakPitchMaxHz);

        /// <summary>A joint takes the color of its worst attached member.</summary>
        public static LoadColor JointColor(IEnumerable<double> memberUtilizations, double yellow, double red)
        {
            double worst = 0;
            foreach (double u in memberUtilizations)
                worst = Math.Max(worst, Check(u));
            return Color(worst, yellow, red);
        }

        public static LoadColor JointColor(IEnumerable<double> memberUtilizations) =>
            JointColor(memberUtilizations, Constants.Feedback.YellowUtilization, Constants.Feedback.RedUtilization);

        /// <summary>A fill takes its color from the worst of its bracing and surrounding frame.</summary>
        public static LoadColor FillColor(IEnumerable<double> bracing, IEnumerable<double> frame, double yellow, double red)
        {
            var all = new List<double>(bracing);
            all.AddRange(frame);
            return JointColor(all, yellow, red);
        }

        public static LoadColor FillColor(IEnumerable<double> bracing, IEnumerable<double> frame) =>
            FillColor(bracing, frame, Constants.Feedback.YellowUtilization, Constants.Feedback.RedUtilization);

        /// <summary>Plain words for students: why a member failed, and what helps (spec 1: see why it holds or fails).</summary>
        public static string Explain(FailureMode mode) => mode switch
        {
            FailureMode.Tension => "Snapped: it was pulled harder than the steel can stretch. Share the pull with more members.",
            FailureMode.Compression => "Crushed: it was squeezed harder than the steel can take. Add members to share the push.",
            FailureMode.Buckling => "Buckled: long, thin members bow sideways when squeezed. Make them shorter with triangles.",
            FailureMode.Bending => "Bent: a long beam sagged too far. Support it from below or above with triangles.",
            _ => "",
        };
    }
}
