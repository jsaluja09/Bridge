namespace BridgeBuilder.Core
{
    /// <summary>
    /// W4-09: Ready-made <see cref="ChallengeConfig"/> instances for the three stress-test presets.
    /// Each preset drives a <see cref="SuspensionCar"/> via <see cref="ChallengeConfig.ToRunConfiguration"/>.
    /// Dimensions are from <see cref="Constants.CarPresets"/>; suspension is sized to the same
    /// <see cref="Constants.Car.StaticSag"/> as the standard car so the ride height is comparable.
    /// </summary>
    public static class CarPresets
    {
        // ── CarParameters factories ──────────────────────────────────────────

        /// <summary>
        /// <see cref="CarParameters"/> for the sports car preset.
        /// Springs are sized from <see cref="Constants.CarPresets.SportsSpringStiffness"/> directly
        /// (not from static sag) so the sports car feels stiffer than standard.
        /// </summary>
        public static CarParameters SportsCarParameters() => new CarParameters
        {
            MassKg            = Constants.CarPresets.SportsMassKg,
            WheelCount        = 4,
            Wheelbase         = Constants.CarPresets.SportsWheelbase,
            Track             = Constants.CarPresets.SportsTrack,
            SpringStiffness   = Constants.CarPresets.SportsSpringStiffness,
            DamperCoefficient = Constants.CarPresets.SportsDamperCoefficient,
            RestLength        = Constants.Car.RestLength,
        };

        /// <summary>
        /// <see cref="CarParameters"/> for the sedan preset.
        /// Matches the standard car sizing from <see cref="Constants.Car.Standard"/> but uses
        /// the preset's explicit wheelbase and track.
        /// </summary>
        public static CarParameters SedanParameters() => new CarParameters
        {
            MassKg            = Constants.CarPresets.SedanMassKg,
            WheelCount        = 4,
            Wheelbase         = Constants.CarPresets.SedanWheelbase,
            Track             = Constants.CarPresets.SedanTrack,
            SpringStiffness   = Constants.CarPresets.SedanSpringStiffness,
            DamperCoefficient = Constants.CarPresets.SedanDamperCoefficient,
            RestLength        = Constants.Car.RestLength,
        };

        /// <summary>
        /// <see cref="CarParameters"/> for the heavy truck preset.
        /// Very high spring stiffness keeps the truck from bottoming out on the deck.
        /// </summary>
        public static CarParameters TruckParameters() => new CarParameters
        {
            MassKg            = Constants.CarPresets.TruckMassKg,
            WheelCount        = 4,
            Wheelbase         = Constants.CarPresets.TruckWheelbase,
            Track             = Constants.CarPresets.TruckTrack,
            SpringStiffness   = Constants.CarPresets.TruckSpringStiffness,
            DamperCoefficient = Constants.CarPresets.TruckDamperCoefficient,
            RestLength        = Constants.Car.RestLength,
        };

        // ── ChallengeConfig factories ────────────────────────────────────────

        /// <summary>
        /// A <see cref="ChallengeConfig"/> that runs the sports car preset.
        /// The <see cref="ChallengeConfig.CarWeightKg"/> is set to the preset mass so
        /// <see cref="ChallengeConfig.ToRunConfiguration"/> produces a consistent <see cref="RunConfiguration"/>.
        /// </summary>
        public static ChallengeConfig SportsCar() => new ChallengeConfig
        {
            Name        = "Sports car",
            Brief       = "A light, stiff sports car. Fast and nimble.",
            CarWeightKg = Constants.CarPresets.SportsMassKg,
        };

        /// <summary>A <see cref="ChallengeConfig"/> for the sedan preset.</summary>
        public static ChallengeConfig Sedan() => new ChallengeConfig
        {
            Name        = "Sedan",
            Brief       = "An everyday car. The standard crossing.",
            CarWeightKg = Constants.CarPresets.SedanMassKg,
        };

        /// <summary>A <see cref="ChallengeConfig"/> for the heavy truck preset.</summary>
        public static ChallengeConfig HeavyTruck() => new ChallengeConfig
        {
            Name        = "Heavy truck",
            Brief       = "A 12 tonne truck. Spread its weight.",
            CarWeightKg = Constants.CarPresets.TruckMassKg,
        };
    }
}
