namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>
        /// The standard car and wind model. Spec 2.8 and 2.9 define neither; HG-car-and-wind is open, so all
        /// GUESS (mass comes from Human.CarMassKg). Bridge-world SI units.
        /// </summary>
        public static class Car
        {
            public const double Wheelbase = 2.7;            // m, GUESS
            public const double Track = 1.6;                // m, GUESS
            public const double Length = 4.5;               // m, GUESS (for drawing the model)
            public const double RestLength = 0.6;           // m spring rest length, GUESS
            public const double StaticSag = 0.12;           // m spring compression at rest, GUESS
            public const double DampingRatio = 0.5;         // of critical, GUESS
            public const double CruiseSpeed = 8.0;          // m/s, GUESS
            public const double DefaultGrip = 0.9;          // tire friction, GUESS
            public const double SideArea = 6.0;             // m^2 facing a crosswind, GUESS
            public const double FrontArea = 2.5;            // m^2, GUESS
            public const double DragCoefficient = 1.0;      // GUESS
            public const double MemberDragCoefficient = 1.2;// GUESS, round tube in cross-flow
            public const double MemberDiameter = 0.2;       // m exposed to wind, GUESS (matches the section's 0.1 m fiber)
            public const double AirDensity = 1.225;         // kg/m^3, DERIVED (sea level)
            public const double FallSeconds = 0.5;          // airborne this long counts as falling, GUESS
            public const double TimeoutSeconds = 30;        // GUESS

            /// <summary>Car parameters for a vehicle of this mass, springs sized to the static sag.</summary>
            public static CarParameters Standard(double massKg)
            {
                double k = massKg * Loads.Gravity / (4 * StaticSag);
                return new CarParameters
                {
                    MassKg = massKg,
                    WheelCount = 4,
                    Wheelbase = Wheelbase,
                    Track = Track,
                    SpringStiffness = k,
                    DamperCoefficient = 2 * DampingRatio * System.Math.Sqrt(k * massKg / 4),
                    RestLength = RestLength,
                };
            }
        }
    }
}
