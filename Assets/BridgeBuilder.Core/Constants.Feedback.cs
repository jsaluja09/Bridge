namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Feedback thresholds (spec 2.10). The spec gives no color cutoffs, so all GUESS.</summary>
        public static class Feedback
        {
            public const double YellowUtilization = 0.50;     // GUESS, spec 2.10
            public const double RedUtilization = 0.80;        // GUESS, spec 2.10
            public const double PulseUtilization = 0.90;      // GUESS, spec 2.10 and 8
            public const double StripeDensityMin = 1;         // GUESS, spec 2.10 and 7
            public const double StripeDensityMax = 8;         // GUESS, spec 2.10 and 7
            public const double MaxBowAmplitudeMeters = 0.05; // GUESS, spec 2.10
            public const double CreakPitchMinHz = 80;         // GUESS, spec 2.10
            public const double CreakPitchMaxHz = 400;        // GUESS, spec 2.10
        }
    }
}
