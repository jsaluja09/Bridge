using System;

namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>
        /// Glulam timber, a square solid section (spec 2.7, bridge-world SI units): light, moderate in tension and
        /// compression, bends easily. Sized so fixture 1 fails and fixture 5 holds when built in wood (spec 5, 8).
        /// </summary>
        public static class Wood
        {
            public const double Width = 0.20;                    // m, side of the square, GUESS
            public const double YoungsModulus = 11.5e9;          // Pa, GUESS
            public const double ShearModulus = 0.65e9;           // Pa, GUESS
            public const double Density = 450.0;                 // kg/m^3, GUESS
            public const double BendingStrength = 24e6;          // Pa, GUESS
            public const double TensionStrength = 19e6;          // Pa, GUESS
            public const double CompressionStrength = 24e6;      // Pa, GUESS
            public const double DeckCostPerSquareMeter = 40.0;   // per room m^2 of wood deck (planks), GUESS

            public const double Area = Width * Width;                                       // m^2, DERIVED
            public const double SecondMoment = Width * Width * Width * Width / 12;          // m^4, DERIVED
            public const double TorsionConstant = 0.1406 * Width * Width * Width * Width;   // m^4, DERIVED: solid square

            /// <summary>Wood as solver data, id BridgeMaterial.Wood.</summary>
            public static SectionProperties Section() => new SectionProperties
            {
                Id = (int)BridgeMaterial.Wood,
                E = YoungsModulus,
                G = ShearModulus,
                A = Area,
                Iy = SecondMoment,
                Iz = SecondMoment,
                J = TorsionConstant,
                YieldStrength = BendingStrength,
                TensionStrength = TensionStrength,
                CompressionStrength = CompressionStrength,
                Density = Density,
                OuterFiberY = Width / 2,
                OuterFiberZ = Width / 2,
            };
        }

        /// <summary>
        /// Spiral steel strand (spec 2.7, bridge-world SI units): very strong in tension, almost no strength when
        /// pushed (a pushed cable fails and drops out like any broken member) and almost none in bending.
        /// </summary>
        public static class Cable
        {
            public const double Diameter = 0.04;                 // m, GUESS
            public const double StrandBendingFactor = 0.1;       // strand I over a solid bar's I: wires slide past each other, GUESS
            public const double YoungsModulus = 160e9;           // Pa, GUESS
            public const double Density = 7850.0;                // kg/m^3, GUESS
            public const double BendingStrength = 1570e6;        // Pa, GUESS
            public const double TensionStrength = 1570e6;        // Pa, GUESS
            public const double CompressionStrength = 5e6;       // Pa, near zero so a pushed cable fails, GUESS

            public const double ShearModulus = YoungsModulus / 2.6;                                                 // Pa, DERIVED: E / (2 (1 + 0.3))
            public const double Area = Math.PI * Diameter * Diameter / 4;                                           // m^2, DERIVED
            public const double SecondMoment = StrandBendingFactor * Math.PI * Diameter * Diameter * Diameter * Diameter / 64; // m^4, DERIVED
            public const double TorsionConstant = 2 * SecondMoment;                                                 // m^4, DERIVED: round

            /// <summary>Steel cable as solver data, id BridgeMaterial.Cable.</summary>
            public static SectionProperties Section() => new SectionProperties
            {
                Id = (int)BridgeMaterial.Cable,
                E = YoungsModulus,
                G = ShearModulus,
                A = Area,
                Iy = SecondMoment,
                Iz = SecondMoment,
                J = TorsionConstant,
                YieldStrength = BendingStrength,
                TensionStrength = TensionStrength,
                CompressionStrength = CompressionStrength,
                Density = Density,
                OuterFiberY = Diameter / 2,
                OuterFiberZ = Diameter / 2,
            };
        }

        /// <summary>
        /// Reinforced concrete, a square solid section (spec 2.7, bridge-world SI units): very strong in
        /// compression, weak in tension (only the rebar holds a pull), very heavy.
        /// </summary>
        public static class Concrete
        {
            public const double Width = 0.35;                    // m, side of the square, GUESS
            public const double YoungsModulus = 30e9;            // Pa, GUESS
            public const double ShearModulus = 12.5e9;           // Pa, GUESS
            public const double Density = 2500.0;                // kg/m^3, GUESS
            public const double BendingStrength = 12e6;          // Pa, effective for reinforced bending, GUESS
            public const double TensionStrength = 5e6;           // Pa, effective, carried by the rebar, GUESS
            public const double CompressionStrength = 30e6;      // Pa, GUESS
            public const double DeckCostPerSquareMeter = 200.0;  // per room m^2 of concrete deck, GUESS

            public const double Area = Width * Width;                                       // m^2, DERIVED
            public const double SecondMoment = Width * Width * Width * Width / 12;          // m^4, DERIVED
            public const double TorsionConstant = 0.1406 * Width * Width * Width * Width;   // m^4, DERIVED: solid square

            /// <summary>Concrete as solver data, id BridgeMaterial.Concrete.</summary>
            public static SectionProperties Section() => new SectionProperties
            {
                Id = (int)BridgeMaterial.Concrete,
                E = YoungsModulus,
                G = ShearModulus,
                A = Area,
                Iy = SecondMoment,
                Iz = SecondMoment,
                J = TorsionConstant,
                YieldStrength = BendingStrength,
                TensionStrength = TensionStrength,
                CompressionStrength = CompressionStrength,
                Density = Density,
                OuterFiberY = Width / 2,
                OuterFiberZ = Width / 2,
            };
        }
    }
}
