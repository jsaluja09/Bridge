namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Values only a human may change. Agents read them and never edit this file.</summary>
        public static class Human
        {
            /// <summary>Spec 5, fixture 1. HUMAN_OWNED (HG-fixture-margins). Bare-cantilever worst-member utilization must be at least this. 1.6 = 4 W / 2.5 W: two 8 m cantilevers with half the car at each tip carry 1.6 times the root moment of one fixed-fixed 20 m span with the car at midspan, so two lines that meet in the middle also fail under the car alone. Do not loosen.</summary>
            public const double Fixture1MinimumUtilization = 1.6;

            /// <summary>Spec 5, fixture 5, and 2.10. HUMAN_OWNED (HG-fixture-margins). Truss worst-member utilization must stay below this, the default green-to-yellow color cutoff, which leaves room for dynamic impact and self-weight that the static calibration leaves out. A human may lower it; agents must not raise it.</summary>
            public const double Fixture5MaximumUtilization = 0.5;

            /// <summary>Spec 2.12 Performance and section 8. HUMAN_OWNED. Hidden hard node cap, pending the headset benchmark.</summary>
            public const int NodeCap = 150;

            /// <summary>Spec 2.6, 2.7, and section 8. HUMAN_OWNED (HG-material). Young's modulus of structural steel, Pa.</summary>
            public const double YoungsModulus = 2.0e11;

            /// <summary>Spec 2.6, 2.7, and section 8. HUMAN_OWNED (HG-material). Shear modulus of structural steel, Pa: E / (2 * (1 + 0.3)).</summary>
            public const double ShearModulus = 7.692307692307693e10;

            /// <summary>Spec 2.6, 2.7, and section 8. HUMAN_OWNED (HG-material). Density of structural steel, kg/m^3.</summary>
            public const double Density = 7850.0;

            /// <summary>Spec 2.7 and section 8. HUMAN_OWNED (HG-material). Torsion constant J, m^4: 2 * I of the CHS 168.3 x 5.0 round tube that HG-material recommends to W2-07. If W2-07 settles on another second moment, a human resets this to 2 * Constants.Section.SecondMoment.</summary>
            public const double TorsionConstant = 1.71e-5;

            /// <summary>Spec 2.8 and 2.9. HUMAN_OWNED (HG-car-and-wind). Standard car mass, kg: a mid-size sedan with its driver.</summary>
            public const double CarMassKg = 1500.0;

            /// <summary>Spec 2.9. HUMAN_OWNED (HG-reach). Reach tolerance, bridge-world m: how far the car reference point may sit beyond either end of the far edge, or below it, and still count as on the far surface.</summary>
            public const double ReachTolerance = 0.25;
        }
    }
}
