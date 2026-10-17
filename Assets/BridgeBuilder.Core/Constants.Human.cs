namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Values only a human may change. Agents read them and never edit this file.</summary>
        public static class Human
        {
            /// <summary>Spec 5, fixture 1. HUMAN_OWNED. Bare-cantilever worst-member utilization must be at least this. Do not loosen.</summary>
            public const double Fixture1MinimumUtilization = 1.0;

            /// <summary>Spec 5, fixture 5. HUMAN_OWNED. Truss worst-member utilization must stay below this. A human may lower it; agents must not raise it.</summary>
            public const double Fixture5MaximumUtilization = 1.0;

            /// <summary>Spec 2.12 Performance and section 8. HUMAN_OWNED. Hidden hard node cap, pending the headset benchmark.</summary>
            public const int NodeCap = 150;

            /// <summary>Spec 2.7 and section 8. HUMAN_OWNED. Young's modulus, Pa. Placeholder; the spec does not state it.</summary>
            public const double YoungsModulus = 2.0e11;

            /// <summary>Spec 2.7 and section 8. HUMAN_OWNED. Shear modulus, Pa. Placeholder; the spec does not state it.</summary>
            public const double ShearModulus = 7.692307692307693e10;

            /// <summary>Spec 2.7 and section 8. HUMAN_OWNED. Material density, kg/m^3. Placeholder; the spec does not state it.</summary>
            public const double Density = 7850.0;

            /// <summary>Spec 2.7 and section 8. HUMAN_OWNED. Torsion constant J, m^4. Placeholder; the spec does not state it.</summary>
            public const double TorsionConstant = 1.0e-6;

            /// <summary>Spec 2.8 and 2.9. HUMAN_OWNED. Standard car mass, kg. Placeholder; the spec does not state it.</summary>
            public const double CarMassKg = 1500.0;

            /// <summary>Spec 2.9. HUMAN_OWNED. Distance tolerance, m, for the car reaching the far surface. Placeholder; the spec does not state it.</summary>
            public const double ReachTolerance = 0.25;
        }
    }
}
