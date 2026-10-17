namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Defaults for the utilization coloring and second-cue mappings in <see cref="FeedbackMapping"/>.</summary>
        public static class Feedback
        {
            /// <summary>Spec 2.10. GUESS. Utilization at which a member turns from green to yellow. The spec names the colors but not the cutoffs.</summary>
            public const double YellowUtilization = 0.50;

            /// <summary>Spec 2.10. GUESS. Utilization at which a member turns from yellow to red. The spec names the colors but not the cutoffs.</summary>
            public const double RedUtilization = 0.80;

            /// <summary>Spec 2.10 and section 8 ("~90% pulse threshold"). GUESS. Utilization at or above which a member pulses.</summary>
            public const double PulseUtilization = 0.90;

            /// <summary>Spec 2.10 and section 7 (stripe pattern deferred). GUESS. Stripe density at zero utilization, in the renderer's stripe units.</summary>
            public const double StripeDensityMin = 1.0;

            /// <summary>Spec 2.10 and section 7 (stripe pattern deferred). GUESS. Stripe density at full utilization, in the renderer's stripe units.</summary>
            public const double StripeDensityMax = 8.0;

            /// <summary>Spec 2.10. GUESS. Drawn bow of a member at or past its buckling load, m.</summary>
            public const double MaxBowAmplitudeMeters = 0.05;

            /// <summary>Spec 2.10. GUESS. Creak pitch, Hz, when the worst member is unloaded.</summary>
            public const double CreakPitchMinHz = 80.0;

            /// <summary>Spec 2.10. GUESS. Creak pitch, Hz, when the worst member is at or past failure.</summary>
            public const double CreakPitchMaxHz = 400.0;
        }
    }
}
