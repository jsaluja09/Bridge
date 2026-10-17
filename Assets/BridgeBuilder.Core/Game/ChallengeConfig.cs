using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// An authored challenge, shown before building like a design brief (spec 2.9): car weight,
    /// wind speed, grip, and material budget, plus an optional ground-depth override (section 2.5).
    /// The values are fixed when the challenge is authored. Free play with player-chosen
    /// conditions is a later upgrade (section 9) and has no entry point here.
    /// </summary>
    public sealed class ChallengeConfig
    {
        /// <exception cref="ArgumentOutOfRangeException">
        /// The car mass is not positive, a speed, grip, or budget is negative, or any value is NaN or infinite.
        /// </exception>
        public ChallengeConfig(double carMassKg, double windSpeed, double grip, double materialBudget, double? groundHeightOverride = null)
        {
            if (!(carMassKg > 0.0) || double.IsInfinity(carMassKg))
            {
                throw new ArgumentOutOfRangeException(nameof(carMassKg), carMassKg, "Car mass must be positive and finite.");
            }

            RequireNonNegativeFinite(windSpeed, nameof(windSpeed));
            RequireNonNegativeFinite(grip, nameof(grip));
            RequireNonNegativeFinite(materialBudget, nameof(materialBudget));
            if (groundHeightOverride.HasValue && !IsFinite(groundHeightOverride.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(groundHeightOverride), groundHeightOverride.Value, "Ground height override must be finite.");
            }

            CarMassKg = carMassKg;
            WindSpeed = windSpeed;
            Grip = grip;
            MaterialBudget = materialBudget;
            GroundHeightOverride = groundHeightOverride;
        }

        /// <summary>The challenge's car weight, as a mass in kg.</summary>
        public double CarMassKg { get; }

        /// <summary>Wind speed, m/s. Kept as a speed; the dynamics decide the force it puts on the car.</summary>
        public double WindSpeed { get; }

        /// <summary>Tire-to-deck friction coefficient.</summary>
        public double Grip { get; }

        public double MaterialBudget { get; }

        /// <summary>
        /// Absolute ground-plane height (room units, Y) that replaces the tracked floor height,
        /// or null to use the floor (section 2.5).
        /// </summary>
        public double? GroundHeightOverride { get; }

        /// <summary>
        /// Builds the configuration for one test run. <paramref name="car"/> supplies the wheel layout
        /// and suspension; the result holds a copy with its mass set to <see cref="CarMassKg"/>, and
        /// <paramref name="car"/> itself is left unchanged.
        /// </summary>
        /// <param name="windDirection">Unit vector of the direction the wind blows toward.</param>
        /// <param name="scaleFactor">Room-to-bridge multiplier (section 2.6). Must be positive and finite.</param>
        /// <exception cref="ArgumentNullException"><paramref name="start"/> or <paramref name="car"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="scaleFactor"/> is not positive and finite.</exception>
        public RunConfiguration ToRunConfiguration(StartSurfaceChoice start, CarParameters car, Vec3 windDirection, double scaleFactor)
        {
            if (start == null)
            {
                throw new ArgumentNullException(nameof(start));
            }

            if (car == null)
            {
                throw new ArgumentNullException(nameof(car));
            }

            if (!(scaleFactor > 0.0) || double.IsInfinity(scaleFactor))
            {
                throw new ArgumentOutOfRangeException(nameof(scaleFactor), scaleFactor, "Scale factor must be positive and finite.");
            }

            return new RunConfiguration
            {
                StartSurfaceId = start.StartSurfaceId,
                Car = new CarParameters
                {
                    MassKg = CarMassKg,
                    WheelCount = car.WheelCount,
                    Wheelbase = car.Wheelbase,
                    Track = car.Track,
                    SpringStiffness = car.SpringStiffness,
                    DamperCoefficient = car.DamperCoefficient,
                    RestLength = car.RestLength,
                },
                WindSpeed = WindSpeed,
                WindDirection = windDirection,
                Grip = Grip,
                MaterialBudget = MaterialBudget,
                GroundHeightOverride = GroundHeightOverride,
                ScaleFactor = scaleFactor,
            };
        }

        private static void RequireNonNegativeFinite(double value, string paramName)
        {
            if (!(value >= 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Value must be zero or positive and finite.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>
    /// The player's choice of which marked surface the car starts from (spec 2.8). The other
    /// surface is the far surface. When the surfaces differ in height, this decides whether the
    /// car climbs or descends.
    /// </summary>
    public sealed class StartSurfaceChoice
    {
        /// <exception cref="ArgumentException">
        /// The two surface ids are equal, or <paramref name="startSurfaceId"/> is neither of them.
        /// </exception>
        public StartSurfaceChoice(int surfaceAId, int surfaceBId, int startSurfaceId)
        {
            if (surfaceAId == surfaceBId)
            {
                throw new ArgumentException("The two marked surfaces need different ids.", nameof(surfaceBId));
            }

            if (startSurfaceId == surfaceAId)
            {
                FarSurfaceId = surfaceBId;
            }
            else if (startSurfaceId == surfaceBId)
            {
                FarSurfaceId = surfaceAId;
            }
            else
            {
                throw new ArgumentException(
                    "Start surface " + startSurfaceId + " is neither marked surface (" + surfaceAId + " or " + surfaceBId + ").",
                    nameof(startSurfaceId));
            }

            StartSurfaceId = startSurfaceId;
        }

        public int StartSurfaceId { get; }

        public int FarSurfaceId { get; }
    }
}
