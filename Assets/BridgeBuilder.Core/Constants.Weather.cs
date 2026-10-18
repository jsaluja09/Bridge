namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>Weather for the test phases. Bridge-world SI units.</summary>
        public static class Weather
        {
            /// <summary>Crosswind in the wind phase, m/s: a strong gale. GUESS.</summary>
            public const double WindSpeed = 20.0;

            /// <summary>
            /// Snow on the road, Pa: about 1 kN/m^2, a moderate design snow load (roughly 30 cm of settled snow).
            /// GUESS.
            /// </summary>
            public const double SnowLoadPa = 1000.0;

            /// <summary>Tire grip on packed snow. GUESS.</summary>
            public const double SnowGrip = 0.3;
        }
    }
}
