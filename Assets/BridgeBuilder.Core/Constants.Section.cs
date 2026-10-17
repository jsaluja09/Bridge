namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>
        /// The one cross-section (spec 2.7), calibrated so fixture 1 fails and fixture 5 holds (spec 5, 8).
        /// All GUESS; only these four are tuned (W2-07). Material comes from Constants.Human.
        /// </summary>
        public static class Section
        {
            public const double Area = 4.0e-3;               // m^2, GUESS, spec 2.7 and 8
            public const double SecondMoment = 1.6e-5;       // m^4, GUESS, spec 2.7 and 8
            public const double OuterFiberDistance = 0.1;    // m, GUESS, spec 2.7 and 8
            public const double YieldStrength = 250e6;       // Pa, GUESS, spec 2.7 and 8

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
