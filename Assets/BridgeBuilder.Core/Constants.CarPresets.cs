namespace BridgeBuilder.Core
{
    public static partial class Constants
    {
        /// <summary>
        /// W4-09: Named constants for the three stress-test car presets.
        /// All values are GUESS; bridge-world SI units unless noted.
        /// Tuned so the truck produces higher peak deck utilization than the sports car on fixture 5.
        /// </summary>
        public static class CarPresets
        {
            // ── Sports car ──────────────────────────────────────────────────
            /// <summary>Sports car total mass, kg. GUESS.</summary>
            public const double SportsMassKg = 1200.0;

            /// <summary>Sports car wheelbase, m. GUESS.</summary>
            public const double SportsWheelbase = 2.4;

            /// <summary>Sports car track, m. GUESS.</summary>
            public const double SportsTrack = 1.5;

            /// <summary>Sports car spring stiffness (per wheel), N/m. GUESS – stiffer than standard for handling.</summary>
            public const double SportsSpringStiffness = 28000.0;

            /// <summary>Sports car damper coefficient (per wheel), N·s/m. GUESS.</summary>
            public const double SportsDamperCoefficient = 3000.0;

            // ── Sedan ───────────────────────────────────────────────────────
            /// <summary>Sedan total mass, kg. GUESS.</summary>
            public const double SedanMassKg = 1500.0;

            /// <summary>Sedan wheelbase, m. GUESS – same as the standard car (Constants.Car.Wheelbase).</summary>
            public const double SedanWheelbase = 2.7;

            /// <summary>Sedan track, m. GUESS.</summary>
            public const double SedanTrack = 1.6;

            /// <summary>Sedan spring stiffness (per wheel), N/m. GUESS.</summary>
            public const double SedanSpringStiffness = 22000.0;

            /// <summary>Sedan damper coefficient (per wheel), N·s/m. GUESS.</summary>
            public const double SedanDamperCoefficient = 2400.0;

            // ── Heavy truck ─────────────────────────────────────────────────
            /// <summary>Heavy truck total mass, kg. GUESS – about the load of spec "12 tonne truck" challenge.</summary>
            public const double TruckMassKg = 12000.0;

            /// <summary>Heavy truck wheelbase, m. GUESS.</summary>
            public const double TruckWheelbase = 4.2;

            /// <summary>Heavy truck track, m. GUESS.</summary>
            public const double TruckTrack = 2.0;

            /// <summary>Heavy truck spring stiffness (per wheel), N/m. GUESS – stiff to support the load.</summary>
            public const double TruckSpringStiffness = 200000.0;

            /// <summary>Heavy truck damper coefficient (per wheel), N·s/m. GUESS.</summary>
            public const double TruckDamperCoefficient = 20000.0;
        }
    }
}
