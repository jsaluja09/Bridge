namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Values only a human changes. Agents read these and never edit them (AGENTS.md).</summary>
        public static class Human
        {
            /// <summary>Spec 5 fixture 1: bare-cantilever utilization must be >= this. HUMAN_OWNED; do not loosen.</summary>
            public const double Fixture1MinimumUtilization = 1.0;

            /// <summary>Spec 5 fixture 5: truss utilization must be &lt; this. HUMAN_OWNED; a human may lower it, agents must not raise it.</summary>
            public const double Fixture5MaximumUtilization = 1.0;

            /// <summary>Spec 2.12 and 8: node cap, pending the headset benchmark. HUMAN_OWNED.</summary>
            public const int NodeCap = 150;

            /// <summary>Placeholder the spec does not state (steel). HUMAN_OWNED.</summary>
            public const double YoungsModulus = 2.0e11;

            /// <summary>Placeholder the spec does not state (steel). HUMAN_OWNED.</summary>
            public const double ShearModulus = 7.692307692307693e10;

            /// <summary>Placeholder the spec does not state (steel), kg/m^3. HUMAN_OWNED.</summary>
            public const double Density = 7850.0;

            /// <summary>Placeholder the spec does not state, m^4. HUMAN_OWNED.</summary>
            public const double TorsionConstant = 1.0e-6;

            /// <summary>Placeholder standard car mass the spec does not state, kg. HUMAN_OWNED.</summary>
            public const double CarMassKg = 1500.0;

            /// <summary>Placeholder far-surface reach tolerance the spec does not state, m. HUMAN_OWNED.</summary>
            public const double ReachTolerance = 0.25;
        }
    }
}
