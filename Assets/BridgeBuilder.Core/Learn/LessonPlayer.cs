using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Steps through the lessons (A next, B back). Each step builds its groups one after another, each growing
    /// into place over BuildSeconds; groups from earlier steps stand complete, later ones aren't there yet. Times
    /// are in seconds on any clock the caller likes.
    /// </summary>
    public sealed class LessonPlayer
    {
        public const double BuildSeconds = 1.2;
        public const double BuildStagger = 0.5;      // between groups built in the same step

        readonly IReadOnlyList<Lesson> _lessons;
        readonly Dictionary<int, (int step, int order)> _builtAt = new Dictionary<int, (int, int)>();

        public int LessonIndex { get; private set; }
        public int StepIndex { get; private set; }
        public double StepStartedAt { get; private set; }

        public LessonPlayer(IReadOnlyList<Lesson> lessons)
        {
            if (lessons == null || lessons.Count == 0)
                throw new ArgumentException("No lessons", nameof(lessons));
            _lessons = lessons;
            Choose(0, 0);
        }

        public IReadOnlyList<Lesson> Lessons => _lessons;
        public Lesson Lesson => _lessons[LessonIndex];
        public LessonStep Step => Lesson.Steps[StepIndex];
        public bool AtLastStep => StepIndex == Lesson.Steps.Count - 1;

        /// <summary>Starts a lesson from its first step.</summary>
        public void Choose(int lesson, double now)
        {
            LessonIndex = Math.Max(0, Math.Min(_lessons.Count - 1, lesson));
            _builtAt.Clear();
            var steps = Lesson.Steps;
            for (int s = 0; s < steps.Count; s++)
            {
                for (int i = 0; i < steps[s].Builds.Length; i++)
                    _builtAt[steps[s].Builds[i]] = (s, i);
            }
            StepIndex = 0;
            StepStartedAt = now;
        }

        /// <summary>The next step; false (and no change) at the last one.</summary>
        public bool Next(double now)
        {
            if (AtLastStep)
                return false;
            StepIndex++;
            StepStartedAt = now;
            return true;
        }

        /// <summary>The previous step; false (and no change) at the first one.</summary>
        public bool Back(double now)
        {
            if (StepIndex == 0)
                return false;
            StepIndex--;
            StepStartedAt = now;
            return true;
        }

        /// <summary>Seconds since the current step began.</summary>
        public double StepTime(double now) => Math.Max(0, now - StepStartedAt);

        /// <summary>
        /// How much of a group is built at `now`, 0 to 1: whole if an earlier step built it, growing if this step
        /// builds it, absent if a later step does. A group no step builds is always whole (the setting).
        /// </summary>
        public double Built(int group, double now)
        {
            if (!_builtAt.TryGetValue(group, out var at))
                return 1;
            if (at.step < StepIndex)
                return 1;
            if (at.step > StepIndex)
                return 0;
            double t = (StepTime(now) - at.order * BuildStagger) / BuildSeconds;
            return Math.Max(0, Math.Min(1, t));
        }
    }

    /// <summary>
    /// The load demos as positions over time (real meters): every point of a flexible part is moved by the demo,
    /// exaggerated so a few meters on a kilometer-long bridge show on a tabletop model. Pure functions of time.
    /// </summary>
    public static class LessonMotion
    {
        /// <summary>How much the demos magnify real movements so they can be seen.</summary>
        public const double Exaggeration = 10;
        /// <summary>A car crosses the main span in this many seconds in the demo.</summary>
        public const double CrossingSeconds = 6;
        public const int Cars = 6;
        public const double SwayPeriodSeconds = 4;

        public const int Streaks = 24;
        const double StreakSpeed = 60;          // real m/s across the bridge (it is a demo, not a forecast)
        const double StreakLength = 40;         // real m
        const double StreakReach = 160;         // real m either side of the bridge's centerline

        /// <summary>
        /// Wind streak i at `time` (real meters): blowing across the bridge (+z) at a fixed spot along it and a
        /// fixed height near the deck, wrapping when it leaves the model. From time alone, like the game's snow.
        /// </summary>
        public static (Vec3 tail, Vec3 head) Streak(LessonBridge b, int i, double time)
        {
            double along = b.MainSpanStart + b.MainSpan * Fraction(i, 1);
            double y = b.DeckY * (0.4 + 1.2 * Fraction(i, 2));
            double width = 2 * StreakReach;
            double head = -StreakReach + (time * StreakSpeed + Fraction(i, 3) * width) % width;
            double tail = Math.Max(-StreakReach, head - StreakLength);
            return (new Vec3(along, y, tail), new Vec3(along, y, head));
        }

        /// <summary>A fixed pseudo-random number in [0, 1) for streak i.</summary>
        static double Fraction(int i, int salt)
        {
            unchecked
            {
                uint h = (uint)i * 2654435761u ^ (uint)salt * 97531u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }

        /// <summary>Where a car is along the bridge (x, real meters) at `time`: cars spaced evenly, looping over the main span.</summary>
        public static double CarAlong(LessonBridge b, int car, double time)
        {
            double u = (time / CrossingSeconds + (double)car / Cars) % 1.0;
            return b.MainSpanStart + u * b.MainSpan;
        }

        /// <summary>Where a point of a part is drawn during a demo at `time`.</summary>
        public static Vec3 Displace(LessonBridge b, LessonPart part, Vec3 p, LoadDemo demo, double time)
        {
            if (!part.Flexible || p.X <= b.MainSpanStart || p.X >= b.MainSpanEnd)
                return p;
            double u = (p.X - b.MainSpanStart) / b.MainSpan;      // 0..1 across the main span
            double shape = Math.Sin(Math.PI * u);                  // zero at the towers, most in the middle
            switch (demo)
            {
                case LoadDemo.Cars:
                    // Each car pulls the span down most when it is in the middle. Evenly spread cars average 2/pi of
                    // their peak, so pi/(2 Cars) scales a full span of traffic to the bridge's real live-load sag.
                    double sag = 0;
                    for (int c = 0; c < Cars; c++)
                    {
                        double uc = (CarAlong(b, c, time) - b.MainSpanStart) / b.MainSpan;
                        sag += Math.Sin(Math.PI * uc);
                    }
                    return p - Vec3.UnitY * (b.LiveLoadSag * Exaggeration * shape * sag * Math.PI / (2 * Cars));
                case LoadDemo.Wind:
                    double sway = Math.Sin(2 * Math.PI * time / SwayPeriodSeconds);
                    return p + Vec3.UnitZ * (b.WindSway * Exaggeration * shape * sway);
                default:
                    return p;
            }
        }
    }
}
