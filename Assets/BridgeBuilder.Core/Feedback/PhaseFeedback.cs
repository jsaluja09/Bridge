using System;

namespace BridgeBuilder.Core
{
    /// <summary>What happened in one phase, as far as the advice is concerned.</summary>
    public readonly struct PhaseResult
    {
        public readonly bool Passed;
        public readonly FailureMode FirstBreak;     // None: no member broke
        public readonly bool Stuck;                 // timed out with nothing fallen
        public readonly double PeakUtilization;

        public PhaseResult(bool passed, FailureMode firstBreak, bool stuck, double peakUtilization)
        {
            Passed = passed;
            FirstBreak = firstBreak;
            Stuck = stuck;
            PeakUtilization = peakUtilization;
        }
    }

    /// <summary>
    /// One line of advice after a phase, in plain words, about what that phase adds. The member-level reason
    /// ("this beam buckled") comes from FailureMessages; this says what to change for this kind of load.
    /// </summary>
    public static class PhaseFeedback
    {
        public static string Tip(PhaseKind phase, PhaseResult r)
        {
            if (r.Passed)
            {
                int percent = (int)Math.Round(Math.Min(1, r.PeakUtilization) * 100);
                return r.PeakUtilization >= Constants.Phases.CloseCallUtilization
                    ? $"Close call: one beam reached {percent}% of its strength. Strengthen it before the next test."
                    : $"Solid: no beam went past {percent}% of its strength.";
            }
            if (r.Stuck)
                return "The cars couldn't get across. Check the road reaches both ends with no steep or broken parts.";
            return r.FirstBreak != FailureMode.None ? AfterBreak(phase) : OffTheRoad(phase);
        }

        /// <summary>A member broke: what this phase's load asks of the bridge.</summary>
        static string AfterBreak(PhaseKind phase) => phase switch
        {
            PhaseKind.Calm => "Even one car was too much. Start with triangles under or over the road.",
            PhaseKind.Wind => "The wind pushes sideways. Brace the two sides together with an X across the top or under the road.",
            PhaseKind.Snow => "Snow adds weight over the whole road, so every beam works harder. Add more triangles along the span.",
            PhaseKind.Traffic => "Cars in both lanes load both sides at once. Make both sides equally strong (keep mirror on).",
            PhaseKind.Jam => "A jam is the heaviest load: the whole bridge full of cars. Make your triangles taller to carry it.",
            _ => "",
        };

        /// <summary>A car left the road with nothing broken.</summary>
        static string OffTheRoad(PhaseKind phase) => phase switch
        {
            PhaseKind.Wind => "The wind blew a car off the road. A wider road gives it room.",
            PhaseKind.Snow => "A car slid off the icy road. Make the road flatter and wider.",
            _ => "A car went off the road. Make sure the road is wide and joins both ends without gaps.",
        };
    }
}
