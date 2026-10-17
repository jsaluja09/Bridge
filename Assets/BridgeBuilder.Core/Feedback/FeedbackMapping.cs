using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>Utilization color bucket (spec 2.10). Values rise with severity, so Red is worse than Yellow, which is worse than Green.</summary>
    public enum UtilizationColor
    {
        Green = 0,
        Yellow = 1,
        Red = 2,
    }

    /// <summary>
    /// Pure mappings from a caller-supplied utilization (0 unloaded, 1 or more failed) to the
    /// feedback cues of spec section 2.10. Overloads without thresholds read <see cref="Constants.Feedback"/>.
    /// A NaN utilization or threshold throws <see cref="ArgumentOutOfRangeException"/>. Utilizations
    /// outside 0 to 1, including infinities, clamp to the ends of each cue's range.
    /// </summary>
    public static class FeedbackMapping
    {
        public static UtilizationColor Color(double utilization)
        {
            return Color(utilization, Constants.Feedback.YellowUtilization, Constants.Feedback.RedUtilization);
        }

        /// <summary>Green below <paramref name="yellowThreshold"/>, Red at or above <paramref name="redThreshold"/>, Yellow in between.</summary>
        public static UtilizationColor Color(double utilization, double yellowThreshold, double redThreshold)
        {
            RequireNotNaN(utilization, nameof(utilization));
            RequireThresholds(yellowThreshold, redThreshold);
            return Bucket(utilization, yellowThreshold, redThreshold);
        }

        /// <summary>Returns the worse of two colors: Red, then Yellow, then Green.</summary>
        public static UtilizationColor Worse(UtilizationColor a, UtilizationColor b)
        {
            return a >= b ? a : b;
        }

        public static double StripeDensity(double utilization)
        {
            return StripeDensity(utilization, Constants.Feedback.StripeDensityMin, Constants.Feedback.StripeDensityMax);
        }

        /// <summary><c>minDensity + (maxDensity - minDensity) * clamp(utilization, 0, 1)</c>.</summary>
        public static double StripeDensity(double utilization, double minDensity, double maxDensity)
        {
            RequireNotNaN(utilization, nameof(utilization));
            RequireEndpoints(minDensity, maxDensity, nameof(minDensity), nameof(maxDensity));
            return Lerp(minDensity, maxDensity, utilization);
        }

        public static bool Pulse(double utilization)
        {
            return Pulse(utilization, Constants.Feedback.PulseUtilization);
        }

        /// <summary>True exactly when <paramref name="utilization"/> is at or above <paramref name="threshold"/>.</summary>
        public static bool Pulse(double utilization, double threshold)
        {
            RequireNotNaN(utilization, nameof(utilization));
            RequireNotNaN(threshold, nameof(threshold));
            return utilization >= threshold;
        }

        public static double BowAmplitude(double bucklingUtilization)
        {
            return BowAmplitude(bucklingUtilization, Constants.Feedback.MaxBowAmplitudeMeters);
        }

        /// <summary>
        /// <c>maxBowAmplitudeMeters * clamp(bucklingUtilization, 0, 1)</c>, where
        /// <paramref name="bucklingUtilization"/> is the member's compression over its buckling load.
        /// </summary>
        public static double BowAmplitude(double bucklingUtilization, double maxBowAmplitudeMeters)
        {
            RequireNotNaN(bucklingUtilization, nameof(bucklingUtilization));
            RequireFinite(maxBowAmplitudeMeters, nameof(maxBowAmplitudeMeters));
            if (maxBowAmplitudeMeters < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBowAmplitudeMeters), maxBowAmplitudeMeters, "The bow amplitude must not be negative.");
            }

            return maxBowAmplitudeMeters * Clamp01(bucklingUtilization);
        }

        public static double CreakPitch(double worstUtilization)
        {
            return CreakPitch(worstUtilization, Constants.Feedback.CreakPitchMinHz, Constants.Feedback.CreakPitchMaxHz);
        }

        /// <summary><c>minPitchHz + (maxPitchHz - minPitchHz) * clamp(worstUtilization, 0, 1)</c>.</summary>
        public static double CreakPitch(double worstUtilization, double minPitchHz, double maxPitchHz)
        {
            RequireNotNaN(worstUtilization, nameof(worstUtilization));
            RequireEndpoints(minPitchHz, maxPitchHz, nameof(minPitchHz), nameof(maxPitchHz));
            return Lerp(minPitchHz, maxPitchHz, worstUtilization);
        }

        public static UtilizationColor JointColor(IEnumerable<double> attachedMemberUtilizations)
        {
            return JointColor(attachedMemberUtilizations, Constants.Feedback.YellowUtilization, Constants.Feedback.RedUtilization);
        }

        /// <summary>The color of the worst attached member. Needs at least one utilization.</summary>
        public static UtilizationColor JointColor(IEnumerable<double> attachedMemberUtilizations, double yellowThreshold, double redThreshold)
        {
            RequireThresholds(yellowThreshold, redThreshold);
            UtilizationColor? worst = WorstColor(attachedMemberUtilizations, nameof(attachedMemberUtilizations), yellowThreshold, redThreshold);
            return worst ?? throw new ArgumentException("A joint needs at least one attached member utilization.", nameof(attachedMemberUtilizations));
        }

        public static UtilizationColor FillColor(IEnumerable<double> bracingUtilizations, IEnumerable<double> frameUtilizations)
        {
            return FillColor(bracingUtilizations, frameUtilizations, Constants.Feedback.YellowUtilization, Constants.Feedback.RedUtilization);
        }

        /// <summary>The worst color among the fill's bracing and surrounding frame members. Either list may be empty, but not both.</summary>
        public static UtilizationColor FillColor(
            IEnumerable<double> bracingUtilizations,
            IEnumerable<double> frameUtilizations,
            double yellowThreshold,
            double redThreshold)
        {
            if (bracingUtilizations == null)
            {
                throw new ArgumentNullException(nameof(bracingUtilizations));
            }

            if (frameUtilizations == null)
            {
                throw new ArgumentNullException(nameof(frameUtilizations));
            }

            RequireThresholds(yellowThreshold, redThreshold);
            UtilizationColor? bracing = WorstColor(bracingUtilizations, nameof(bracingUtilizations), yellowThreshold, redThreshold);
            UtilizationColor? frame = WorstColor(frameUtilizations, nameof(frameUtilizations), yellowThreshold, redThreshold);
            if (bracing.HasValue && frame.HasValue)
            {
                return Worse(bracing.Value, frame.Value);
            }

            return bracing ?? frame ?? throw new ArgumentException("A fill needs at least one bracing or frame utilization.", nameof(frameUtilizations));
        }

        private static UtilizationColor Bucket(double utilization, double yellowThreshold, double redThreshold)
        {
            if (utilization >= redThreshold)
            {
                return UtilizationColor.Red;
            }

            return utilization >= yellowThreshold ? UtilizationColor.Yellow : UtilizationColor.Green;
        }

        /// <summary>Worst color of the list, or null when it is empty. Every entry is checked for NaN, even after a Red.</summary>
        private static UtilizationColor? WorstColor(IEnumerable<double> utilizations, string paramName, double yellowThreshold, double redThreshold)
        {
            if (utilizations == null)
            {
                throw new ArgumentNullException(paramName);
            }

            UtilizationColor? worst = null;
            int index = 0;
            foreach (double utilization in utilizations)
            {
                if (double.IsNaN(utilization))
                {
                    throw new ArgumentOutOfRangeException(paramName, utilization, "Utilization at index " + index + " is NaN.");
                }

                UtilizationColor color = Bucket(utilization, yellowThreshold, redThreshold);
                worst = worst.HasValue ? Worse(worst.Value, color) : color;
                index++;
            }

            return worst;
        }

        private static double Clamp01(double value)
        {
            return Math.Min(1.0, Math.Max(0.0, value));
        }

        private static double Lerp(double min, double max, double utilization)
        {
            return min + (max - min) * Clamp01(utilization);
        }

        private static void RequireNotNaN(double value, string paramName)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Must not be NaN.");
            }
        }

        private static void RequireFinite(double value, string paramName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Must be finite.");
            }
        }

        private static void RequireThresholds(double yellowThreshold, double redThreshold)
        {
            RequireNotNaN(yellowThreshold, nameof(yellowThreshold));
            RequireNotNaN(redThreshold, nameof(redThreshold));
            if (yellowThreshold > redThreshold)
            {
                throw new ArgumentOutOfRangeException(nameof(redThreshold), redThreshold, "The red threshold must not be below the yellow threshold.");
            }
        }

        private static void RequireEndpoints(double min, double max, string minName, string maxName)
        {
            RequireFinite(min, minName);
            RequireFinite(max, maxName);
            if (min > max)
            {
                throw new ArgumentOutOfRangeException(maxName, max, "The maximum must not be below the minimum.");
            }
        }
    }
}
