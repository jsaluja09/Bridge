using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    public enum PhaseKind
    {
        Calm,
        Wind,
        Snow,
        Traffic,
        Jam,
    }

    /// <summary>One stage of the bridge test. Every phase runs on a fresh copy of the bridge as built.</summary>
    public sealed class TestPhase
    {
        public PhaseKind Kind { get; }
        public string Name { get; }
        /// <summary>What this phase does to the bridge, shown before it runs.</summary>
        public string Intro { get; }
        /// <summary>Stars the player has once this phase holds (phases run in order).</summary>
        public int StarsWhenPassed { get; }

        public TestPhase(PhaseKind kind, string name, string intro, int starsWhenPassed)
        {
            Kind = kind;
            Name = name;
            Intro = intro;
            StarsWhenPassed = starsWhenPassed;
        }

        /// <summary>The run for this phase: the level's car and budget, plus this phase's weather and traffic.</summary>
        public RunConfiguration Configure(ChallengeConfig level, StartSurfaceChoice start, Vec3 windDirection, double scale)
        {
            var config = level.ToRunConfiguration(start, windDirection, scale);
            switch (Kind)
            {
                case PhaseKind.Wind:
                    config.WindSpeed = Constants.Weather.WindSpeed;
                    break;
                case PhaseKind.Snow:
                    config.SnowLoadPa = Constants.Weather.SnowLoadPa;
                    config.Grip = Constants.Weather.SnowGrip;
                    break;
                case PhaseKind.Traffic:
                    config.Traffic = new TrafficSpec { CarsPerLane = Constants.Phases.TrafficCarsPerLane };
                    break;
                case PhaseKind.Jam:
                    config.Traffic = new TrafficSpec { Jam = true };
                    break;
            }
            return config;
        }
    }

    /// <summary>The one level's test, in order: each phase adds a new force on top of a car crossing.</summary>
    public static class TestPhases
    {
        public static IReadOnlyList<TestPhase> All { get; } = new[]
        {
            new TestPhase(PhaseKind.Calm, "Calm day", "One car crosses on a calm day.", 1),
            new TestPhase(PhaseKind.Wind, "Crosswind", "A strong wind blows across the bridge while a car crosses.", 1),
            new TestPhase(PhaseKind.Snow, "Snow", "Snow covers the road: extra weight everywhere, and slippery tires.", 2),
            new TestPhase(PhaseKind.Traffic, "Two-way traffic", "Cars cross in both directions at once.", 2),
            new TestPhase(PhaseKind.Jam, "Rush-hour jam", "A traffic jam fills the whole bridge and has to hold.", 3),
        };

        public const int MaxStars = 3;
    }

    /// <summary>How one phase went.</summary>
    public sealed class PhaseOutcome
    {
        public TestPhase Phase;
        public bool Passed;
        public string Message = "";
        public string Tip = "";
    }

    /// <summary>
    /// The phases of one test, in order. Record each phase's outcome; the test stops at the first failure.
    /// Stars come from the last phase that held.
    /// </summary>
    public sealed class PhaseSequence
    {
        readonly List<PhaseOutcome> _outcomes = new List<PhaseOutcome>();
        readonly IReadOnlyList<TestPhase> _phases;

        public PhaseSequence() : this(TestPhases.All) { }

        public PhaseSequence(IReadOnlyList<TestPhase> phases) => _phases = phases;

        public IReadOnlyList<PhaseOutcome> Outcomes => _outcomes;
        public IReadOnlyList<TestPhase> Phases => _phases;
        public int Index => _outcomes.Count;
        public bool Finished => Index >= _phases.Count || (Index > 0 && !_outcomes[Index - 1].Passed);

        /// <summary>The phase to run next, or null when the test is over.</summary>
        public TestPhase Current => Finished ? null : _phases[Index];

        public int Stars
        {
            get
            {
                int stars = 0;
                foreach (var o in _outcomes)
                {
                    if (o.Passed)
                        stars = Math.Max(stars, o.Phase.StarsWhenPassed);
                }
                return stars;
            }
        }

        public void Record(PhaseOutcome outcome)
        {
            if (Finished)
                throw new InvalidOperationException("The test is already over");
            if (outcome == null || outcome.Phase != Current)
                throw new ArgumentException("Record the current phase's outcome", nameof(outcome));
            _outcomes.Add(outcome);
        }
    }
}
