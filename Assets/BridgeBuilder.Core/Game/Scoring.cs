using System;
using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    /// <summary>Thresholds for three stars, supplied by the caller (HG-stars sets the numbers).</summary>
    public sealed class StarPolicy
    {
        public int MaxLostForThree { get; set; }
        public double MaxUtilForThree { get; set; }
        public double MinBudgetForThree { get; set; }
    }

    public sealed class RunMetrics
    {
        public int MembersLost { get; set; }
        public double PeakUtilization { get; set; }
        public double BudgetRemaining { get; set; }
    }

    /// <summary>
    /// Scoring (spec 2.9): the run passes when the car reaches the far surface, even if members broke.
    /// "Reaches" (HG-reach stand-in): the car has crossed the plane of the far marked edge in the gap direction
    /// and is within the tolerance of that edge segment.
    /// </summary>
    public static class Scoring
    {
        public static bool Reached(Vec3 car, Vec3 farEdgeA, Vec3 farEdgeB, Vec3 gapDirection, double tolerance)
        {
            Vec3 dir = gapDirection.Normalized();
            if (Vec3.Dot(car - farEdgeA, dir) < 0)
                return false;
            return Geometry.DistanceToSegment(car, farEdgeA, farEdgeB) <= tolerance;
        }

        public static bool Reached(Vec3 car, Vec3 farEdgeA, Vec3 farEdgeB, Vec3 gapDirection) =>
            Reached(car, farEdgeA, farEdgeB, gapDirection, Constants.Human.ReachTolerance);

        public static int MembersLost(IEnumerable<int> failedIds) => failedIds?.Distinct().Count() ?? 0;

        /// <summary>3 when all three thresholds hold, otherwise 0.</summary>
        public static int Stars(RunMetrics m, StarPolicy p) =>
            m.MembersLost <= p.MaxLostForThree && m.PeakUtilization <= p.MaxUtilForThree && m.BudgetRemaining >= p.MinBudgetForThree ? 3 : 0;
    }
}
