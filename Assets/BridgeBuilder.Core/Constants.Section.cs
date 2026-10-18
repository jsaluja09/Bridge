namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>
        /// The one cross-section (spec 2.7), calibrated so fixture 1 fails and fixture 5 holds (spec 5, 8):
        /// a CHS 168.3 x 5.0 S355 round steel tube (W2-07, from bridge-core). Constants.Human.TorsionConstant
        /// is 2 * SecondMoment. Material comes from Constants.Human.
        /// </summary>
        public static class Section
        {
            public const double Area = 2.5651e-3;            // m^2, GUESS, spec 2.7 and 8
            public const double SecondMoment = 8.5585e-6;    // m^4, GUESS, spec 2.7 and 8
            public const double OuterFiberDistance = 0.08415; // m, half the 168.3 mm diameter, GUESS
            public const double YieldStrength = 355e6;       // Pa, S355 steel, GUESS, spec 2.7 and 8

            /// <summary>The standard section as solver data, id 0.</summary>
            public static SectionProperties Standard(int id = 0) => new SectionProperties
            {
                Id = id,
                E = Human.YoungsModulus,
                G = Human.ShearModulus,
                A = Area,
                Iy = SecondMoment,
                Iz = SecondMoment,
                J = Human.TorsionConstant,
                YieldStrength = YieldStrength,
                Density = Human.Density,
                OuterFiberY = OuterFiberDistance,
                OuterFiberZ = OuterFiberDistance,
            };
        }
    }
}
