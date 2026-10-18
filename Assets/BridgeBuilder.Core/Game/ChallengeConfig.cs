using System;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// A challenge, authored and shown before building (spec 2.9): car weight, wind speed, grip and budget,
    /// plus an optional ground height override (spec 2.5). Free play comes later and is not here.
    /// </summary>
    public sealed class ChallengeConfig
    {
        public string Name { get; set; } = "";
        public string Brief { get; set; } = "";
        public double CarWeightKg { get; set; } = Constants.Human.CarMassKg;
        public double WindSpeed { get; set; }
        public double Grip { get; set; } = Constants.Car.DefaultGrip;
        public double Budget { get; set; } = Constants.Budget.MaterialBudget;
        public double? GroundHeightOverride { get; set; }

        /// <summary>The run configuration for this challenge, starting from the chosen surface.</summary>
        public RunConfiguration ToRunConfiguration(StartSurfaceChoice start, Vec3 windDirection, double scale) => new RunConfiguration
        {
            StartSurfaceId = start.Start,
            Car = Constants.Car.Standard(CarWeightKg),
            WindSpeed = WindSpeed,
            WindDirection = windDirection,
            Grip = Grip,
            MaterialBudget = Budget,
            GroundHeightOverride = GroundHeightOverride,
            ScaleFactor = scale,
        };

        /// <summary>The one level: the standard car, calm air, and the level budget. TestPhases adds the weather and traffic.</summary>
        public static ChallengeConfig Level() => new ChallengeConfig
        {
            Name = "Bridge the gap",
            Brief = "Build a bridge that survives calm, wind, snow, traffic and a rush-hour jam.",
            Budget = Constants.Phases.LevelBudget,
        };

        /// <summary>The game's starting challenges. Budgets are room meters of line at the default cost.</summary>
        public static ChallengeConfig[] Defaults() => new[]
        {
            new ChallengeConfig { Name = "First crossing", Brief = "Get a car across. Hint: triangles are strong.", Budget = 14 },
            new ChallengeConfig { Name = "Heavy truck", Brief = "A 12 tonne truck. Spread its weight.", CarWeightKg = 12000, Budget = 18 },
            new ChallengeConfig { Name = "Crosswind", Brief = "A 15 m/s wind pushes from the side.", WindSpeed = 15, Budget = 16 },
            new ChallengeConfig { Name = "Icy and windy", Brief = "Slippery tires and a 10 m/s wind.", WindSpeed = 10, Grip = 0.3, Budget = 16 },
        };
    }

    /// <summary>Which surface the car starts from (spec 2.8); the other is the far surface.</summary>
    public sealed class StartSurfaceChoice
    {
        public int SurfaceA { get; }
        public int SurfaceB { get; }
        public int Start { get; private set; }
        public int Far => Start == SurfaceA ? SurfaceB : SurfaceA;

        public StartSurfaceChoice(int surfaceA, int surfaceB)
        {
            SurfaceA = surfaceA;
            SurfaceB = surfaceB;
            Start = surfaceA;
        }

        public void Choose(int surfaceId)
        {
            if (surfaceId != SurfaceA && surfaceId != SurfaceB)
                throw new ArgumentException($"Surface {surfaceId} is neither surface");
            Start = surfaceId;
        }

        public void Swap() => Start = Far;
    }
}
