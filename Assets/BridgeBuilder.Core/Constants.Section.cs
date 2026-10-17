namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>
        /// The one cross-section every member uses (spec 2.7), calibrated so the bare cantilevers of
        /// fixture 1 fail and the truss of fixture 5 holds the standard car (spec 5 and section 8).
        /// Material stiffness, density, and the torsion constant are in <see cref="Human"/>.
        /// </summary>
        public static class Section
        {
            /// <summary>Spec 2.7 and section 8 ("Section size and material strength ... Not chosen yet"). GUESS. Cross-section area, m^2: a CHS 168.3 x 5.0 round steel tube.</summary>
            public const double Area = 2.5651e-3;

            /// <summary>Spec 2.7 and section 8. GUESS. Second moment of area about either bending axis, m^4: the CHS 168.3 x 5.0 tube. Constants.Human.TorsionConstant is 2 * this value.</summary>
            public const double SecondMoment = 8.5585e-6;

            /// <summary>Spec 2.7 and section 8. GUESS. Outer-fiber distance for bending about either axis, m: half the 168.3 mm outside diameter. Must stay positive for the bending check.</summary>
            public const double OuterFiberDistance = 0.08415;

            /// <summary>Spec 2.7 and section 8. GUESS. Yield strength, Pa: S355 structural steel, inside the 235 to 355 MPa range of real grades that HG-material sets.</summary>
            public const double YieldStrength = 355e6;
        }
    }
}
