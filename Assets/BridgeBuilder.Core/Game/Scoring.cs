using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Star thresholds for one challenge (spec 2.9). Section 8 has not chosen the numbers, so they
    /// are caller data and no constant supplies them. Every comparison against them is inclusive.
    /// </summary>
    public sealed class StarPolicy
    {
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxLostForTwo"/> is negative, or a threshold is NaN or infinite.
        /// </exception>
        public StarPolicy(int maxLostForTwo, double maxUtilForThree, double minBudgetForThree)
        {
            if (maxLostForTwo < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLostForTwo), maxLostForTwo, "The members-lost limit must not be negative.");
            }

            RequireFinite(maxUtilForThree, nameof(maxUtilForThree));
            RequireFinite(minBudgetForThree, nameof(minBudgetForThree));
            MaxLostForTwo = maxLostForTwo;
            MaxUtilForThree = maxUtilForThree;
            MinBudgetForThree = minBudgetForThree;
        }

        /// <summary>Most members a run may lose and still earn two stars.</summary>
        public int MaxLostForTwo { get; }

        /// <summary>Highest peak utilization that still earns three stars.</summary>
        public double MaxUtilForThree { get; }

        /// <summary>Least budget remaining that still earns three stars.</summary>
        public double MinBudgetForThree { get; }

        private static void RequireFinite(double value, string paramName)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Star thresholds must be finite.");
            }
        }
    }

    /// <summary>The scored outcome of one test run (spec 2.9).</summary>
    public sealed class RunMetrics
    {
        /// <param name="reachedFarSurface">Whether the car reached the far surface during the run, as <see cref="FarSurfaceReach"/> latches it.</param>
        /// <param name="failedMemberIds">Ids of every member that failed during the run, in any order. Repeats count once.</param>
        /// <param name="peakUtilization">Highest utilization any member reached during the run, failed members included.</param>
        /// <param name="budgetRemaining">Material budget the bridge left unspent.</param>
        /// <exception cref="ArgumentNullException"><paramref name="failedMemberIds"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="peakUtilization"/> is negative or not finite, or <paramref name="budgetRemaining"/> is not finite.
        /// </exception>
        public RunMetrics(bool reachedFarSurface, IEnumerable<int> failedMemberIds, double peakUtilization, double budgetRemaining)
        {
            int membersLost = Scoring.MembersLost(failedMemberIds);
            if (!(peakUtilization >= 0.0) || double.IsInfinity(peakUtilization))
            {
                throw new ArgumentOutOfRangeException(nameof(peakUtilization), peakUtilization, "Peak utilization must be zero or positive and finite.");
            }

            if (!double.IsFinite(budgetRemaining))
            {
                throw new ArgumentOutOfRangeException(nameof(budgetRemaining), budgetRemaining, "Budget remaining must be finite.");
            }

            Passed = reachedFarSurface;
            MembersLost = membersLost;
            PeakUtilization = peakUtilization;
            BudgetRemaining = budgetRemaining;
        }

        /// <summary>True when the car reached the far surface, even if some members broke.</summary>
        public bool Passed { get; }

        /// <summary>Number of distinct members that failed during the run.</summary>
        public int MembersLost { get; }

        public double PeakUtilization { get; }

        public double BudgetRemaining { get; }
    }

    /// <summary>
    /// Pass or fail, members lost, and the star rating for one test run (spec 2.9). The reach test
    /// works in bridge-world meters with Y up (HG-reach).
    /// </summary>
    public static class Scoring
    {
        /// <summary>Same as the full overload with the tolerance <see cref="Constants.Human.ReachTolerance"/>.</summary>
        public static bool ReachedFarSurface(Vec3 carPosition, Vec3 farEdgeStart, Vec3 farEdgeEnd, Vec3 gapDirection, double scaleFactor)
        {
            return ReachedFarSurface(carPosition, farEdgeStart, farEdgeEnd, gapDirection, scaleFactor, Constants.Human.ReachTolerance);
        }

        /// <summary>
        /// Whether the car reference point <c>p</c> is on the far surface at this step (HG-reach). The far
        /// edge runs from <c>E0</c> to <c>E1</c> in bridge-world units, with length <c>L</c> and unit
        /// direction <c>e</c>, and <c>g</c> is the horizontal unit gap direction. With
        /// <c>t = dot(p - E0, e)</c> and <c>C = E0 + clamp(t, 0, L) * e</c>, the car is on the far surface
        /// when it has crossed the edge plane, <c>dot(p - E0, g) &gt; 0</c>; is within the edge's ends,
        /// <c>max(-t, t - L) &lt;= tolerance</c>; and has not fallen, <c>p.Y &gt;= C.Y - tolerance</c>.
        /// Members lost play no part, so a run with broken members can still pass. Evaluate it every
        /// step; <see cref="FarSurfaceReach"/> keeps the run reached once it holds.
        /// </summary>
        /// <param name="carPosition"><see cref="CarState.Position"/>, bridge-world m.</param>
        /// <param name="farEdgeStart">One end of the far surface's marked edge, room units.</param>
        /// <param name="farEdgeEnd">The other end of the far surface's marked edge, room units.</param>
        /// <param name="gapDirection">Direction from the start surface toward the far surface, in any units. Only its horizontal direction is used.</param>
        /// <param name="scaleFactor">Room-to-bridge multiplier applied to the edge (section 2.6). Must be positive and finite.</param>
        /// <param name="tolerance">Reach tolerance, bridge-world m. Must be zero or positive and finite.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The car position or an edge end is not finite, the scale factor is not positive and finite, the
        /// edge has zero length, the gap direction has no horizontal part, or the tolerance is negative or not finite.
        /// </exception>
        public static bool ReachedFarSurface(Vec3 carPosition, Vec3 farEdgeStart, Vec3 farEdgeEnd, Vec3 gapDirection, double scaleFactor, double tolerance)
        {
            return FarEdgeFrame.Create(farEdgeStart, farEdgeEnd, gapDirection, scaleFactor, tolerance).Holds(carPosition);
        }

        /// <summary>Number of distinct ids in <paramref name="failedMemberIds"/>: 0 for an empty list, and a repeated id counts once.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="failedMemberIds"/> is null.</exception>
        public static int MembersLost(IEnumerable<int> failedMemberIds)
        {
            if (failedMemberIds == null)
            {
                throw new ArgumentNullException(nameof(failedMemberIds));
            }

            return new HashSet<int>(failedMemberIds).Count;
        }

        /// <summary>
        /// Star rating under <paramref name="policy"/> (HG-stars). 0 when the run did not pass; 1 when it
        /// passed; 2 when it also lost at most <see cref="StarPolicy.MaxLostForTwo"/> members; 3 when it
        /// also kept peak utilization at or below <see cref="StarPolicy.MaxUtilForThree"/> and budget
        /// remaining at or above <see cref="StarPolicy.MinBudgetForThree"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="metrics"/> or <paramref name="policy"/> is null.</exception>
        public static int Stars(RunMetrics metrics, StarPolicy policy)
        {
            if (metrics == null)
            {
                throw new ArgumentNullException(nameof(metrics));
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            if (!metrics.Passed)
            {
                return 0;
            }

            if (metrics.MembersLost > policy.MaxLostForTwo)
            {
                return 1;
            }

            bool keptMargin = metrics.PeakUtilization <= policy.MaxUtilForThree;
            bool savedMaterial = metrics.BudgetRemaining >= policy.MinBudgetForThree;
            return keptMargin && savedMaterial ? 3 : 2;
        }
    }

    /// <summary>
    /// The reach test over one run (HG-reach). Observe the car reference point every step. The run
    /// passes at the first step where <see cref="Scoring.ReachedFarSurface(Vec3, Vec3, Vec3, Vec3, double, double)"/>
    /// holds, and from then on it stays reached, even if a later step would fail the test.
    /// </summary>
    public sealed class FarSurfaceReach
    {
        private readonly FarEdgeFrame frame;
        private int stepsObserved;

        /// <summary>Same as the full overload with the tolerance <see cref="Constants.Human.ReachTolerance"/>.</summary>
        public FarSurfaceReach(Vec3 farEdgeStart, Vec3 farEdgeEnd, Vec3 gapDirection, double scaleFactor)
            : this(farEdgeStart, farEdgeEnd, gapDirection, scaleFactor, Constants.Human.ReachTolerance)
        {
        }

        /// <summary>The arguments mean what they do in <see cref="Scoring.ReachedFarSurface(Vec3, Vec3, Vec3, Vec3, double, double)"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// An edge end is not finite, the scale factor is not positive and finite, the edge has zero
        /// length, the gap direction has no horizontal part, or the tolerance is negative or not finite.
        /// </exception>
        public FarSurfaceReach(Vec3 farEdgeStart, Vec3 farEdgeEnd, Vec3 gapDirection, double scaleFactor, double tolerance)
        {
            frame = FarEdgeFrame.Create(farEdgeStart, farEdgeEnd, gapDirection, scaleFactor, tolerance);
        }

        public bool Reached => ReachedAtStep.HasValue;

        /// <summary>Zero-based index of the first observed step at which the test held, or null while the car has not reached the far surface.</summary>
        public int? ReachedAtStep { get; private set; }

        /// <summary>Tests one step's <see cref="CarState.Position"/>, bridge-world m, and returns <see cref="Reached"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="carPosition"/> is not finite.</exception>
        public bool Observe(Vec3 carPosition)
        {
            bool holds = frame.Holds(carPosition);
            if (holds && !ReachedAtStep.HasValue)
            {
                ReachedAtStep = stepsObserved;
            }

            stepsObserved++;
            return Reached;
        }
    }

    /// <summary>The far edge in bridge-world units with the horizontal gap direction and the reach tolerance.</summary>
    internal readonly struct FarEdgeFrame
    {
        private readonly Vec3 start;
        private readonly Vec3 direction;
        private readonly double length;
        private readonly Vec3 gap;
        private readonly double tolerance;

        private FarEdgeFrame(Vec3 start, Vec3 direction, double length, Vec3 gap, double tolerance)
        {
            this.start = start;
            this.direction = direction;
            this.length = length;
            this.gap = gap;
            this.tolerance = tolerance;
        }

        public static FarEdgeFrame Create(Vec3 farEdgeStart, Vec3 farEdgeEnd, Vec3 gapDirection, double scaleFactor, double tolerance)
        {
            if (!(scaleFactor > 0.0) || double.IsInfinity(scaleFactor))
            {
                throw new ArgumentOutOfRangeException(nameof(scaleFactor), scaleFactor, "Scale factor must be positive and finite.");
            }

            RequireFinite(farEdgeStart, nameof(farEdgeStart));
            RequireFinite(farEdgeEnd, nameof(farEdgeEnd));
            if (!(tolerance >= 0.0) || double.IsInfinity(tolerance))
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Reach tolerance must be zero or positive and finite.");
            }

            Vec3 start = RoomBridgeAdapter.ToBridge(farEdgeStart, scaleFactor);
            Vec3 edge = RoomBridgeAdapter.ToBridge(farEdgeEnd, scaleFactor) - start;
            double length = edge.Length;
            if (!(length > 0.0) || !double.IsFinite(length))
            {
                throw new ArgumentOutOfRangeException(nameof(farEdgeEnd), length, "The far edge must have a positive, finite length.");
            }

            var horizontalGap = new Vec3(gapDirection.X, 0.0, gapDirection.Z);
            double gapLength = horizontalGap.Length;
            if (!(gapLength > 0.0) || !double.IsFinite(gapLength))
            {
                throw new ArgumentOutOfRangeException(nameof(gapDirection), gapDirection, "The gap direction needs a positive, finite horizontal part.");
            }

            return new FarEdgeFrame(start, edge / length, length, horizontalGap / gapLength, tolerance);
        }

        public bool Holds(Vec3 carPosition)
        {
            RequireFinite(carPosition, nameof(carPosition));
            Vec3 offset = carPosition - start;
            if (!(Vec3.Dot(offset, gap) > 0.0))
            {
                return false;
            }

            double t = Vec3.Dot(offset, direction);
            if (Math.Max(-t, t - length) > tolerance)
            {
                return false;
            }

            double closestY = start.Y + direction.Y * Math.Min(length, Math.Max(0.0, t));
            return carPosition.Y >= closestY - tolerance;
        }

        private static void RequireFinite(Vec3 v, string paramName)
        {
            if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z))
            {
                throw new ArgumentOutOfRangeException(paramName, v, "Must be finite.");
            }
        }
    }
}
