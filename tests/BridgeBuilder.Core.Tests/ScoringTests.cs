using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class ScoringTests
    {
        private const double Tolerance = 0.1;
        private const double BridgeScale = 1.0;
        private const double Nudge = 1e-6;

        private static readonly Vec3 EdgeStart = Vec3.Zero;
        private static readonly Vec3 EdgeEnd = new Vec3(0, 0, 2);
        private static readonly Vec3 Gap = Vec3.UnitX;

        [Fact]
        public void Pass_WhenCarCrossesFarEdge_EvenIfMembersFailed()
        {
            Assert.True(Reached(new Vec3(0.1, 0, 1)));
            Assert.True(Reached(new Vec3(3, 0, 1)));
            Assert.True(Reached(new Vec3(0.1, 0.5, 1)));

            var damaged = new RunMetrics(Reached(new Vec3(0.1, 0, 1)), new[] { 4, 7 }, 1.3, 50);
            Assert.True(damaged.Passed);
            Assert.Equal(2, damaged.MembersLost);
            Assert.Equal(1, Scoring.Stars(damaged, new StarPolicy(0, 0.8, 125)));

            var tracker = new FarSurfaceReach(EdgeStart, EdgeEnd, Gap, BridgeScale, Tolerance);
            Assert.True(tracker.Observe(new Vec3(0.1, 0, 1)));
        }

        [Fact]
        public void Fail_WhenCarHasNotCrossed()
        {
            Assert.False(Reached(new Vec3(-0.1, 0, 1)));
            Assert.False(Reached(new Vec3(0, 0, 1)));
        }

        [Fact]
        public void Fail_WhenCarIsOffTheFarSurface()
        {
            Assert.False(Reached(new Vec3(0.1, 0, 2.2)));
            Assert.False(Reached(new Vec3(0.1, -0.2, 1)));
        }

        [Fact]
        public void MembersLost_CountsFailedIds()
        {
            Assert.Equal(3, Scoring.MembersLost(new[] { 12, 3, 9 }));
            Assert.Equal(0, Scoring.MembersLost(Array.Empty<int>()));
            Assert.Equal(2, Scoring.MembersLost(new[] { 5, 5, 8, 5 }));

            Assert.Equal(0, new RunMetrics(true, Array.Empty<int>(), 0.3, 10).MembersLost);
            Assert.Equal(2, new RunMetrics(false, new[] { 5, 5, 8, 5 }, 1.2, 10).MembersLost);
            Assert.Throws<ArgumentNullException>(() => Scoring.MembersLost(null));
        }

        [Fact]
        public void Metrics_ExposePeakUtilizationAndBudgetRemaining()
        {
            var metrics = new RunMetrics(true, Array.Empty<int>(), 0.42, 17);
            Assert.Equal(0.42, metrics.PeakUtilization);
            Assert.Equal(17.0, metrics.BudgetRemaining);
        }

        [Fact]
        public void Stars_UseSuppliedPolicy()
        {
            var policy = new StarPolicy(maxLostForTwo: 0, maxUtilForThree: 0.8, minBudgetForThree: 125);
            Assert.Equal(0, policy.MaxLostForTwo);
            Assert.Equal(0.8, policy.MaxUtilForThree);
            Assert.Equal(125.0, policy.MinBudgetForThree);

            Assert.Equal(0, Stars(policy, reached: false, lost: 0, peak: 0.5, budget: 200));
            Assert.Equal(1, Stars(policy, reached: true, lost: 1, peak: 0.5, budget: 200));
            Assert.Equal(2, Stars(policy, reached: true, lost: 0, peak: 0.81, budget: 200));
            Assert.Equal(2, Stars(policy, reached: true, lost: 0, peak: 0.5, budget: 124));
            Assert.Equal(3, Stars(policy, reached: true, lost: 0, peak: 0.8, budget: 125));

            var lenient = new StarPolicy(maxLostForTwo: 1, maxUtilForThree: 0.8, minBudgetForThree: 125);
            Assert.Equal(2, Stars(lenient, reached: true, lost: 1, peak: 0.9, budget: 200));

            Assert.Null(typeof(StarPolicy).GetConstructor(Type.EmptyTypes));
            Assert.Empty(typeof(StarPolicy).GetMembers(BindingFlags.Public | BindingFlags.Static));
            Assert.Empty(typeof(Scoring).GetFields(BindingFlags.Public | BindingFlags.Static));
            Assert.Empty(typeof(Scoring).GetProperties(BindingFlags.Public | BindingFlags.Static));
            string[] constantNames = typeof(Constants).GetNestedTypes()
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
                .Select(field => field.DeclaringType.Name + "." + field.Name)
                .ToArray();
            Assert.NotEmpty(constantNames);
            Assert.DoesNotContain(constantNames, name => Regex.IsMatch(name, "Star(?!t)|ForTwo|ForThree", RegexOptions.IgnoreCase));
        }

        [Fact]
        public void Reach_DefaultTolerance_ReadsHumanConstant()
        {
            double tol = Constants.Human.ReachTolerance;

            Assert.True(ReachedByDefault(new Vec3(0.1, 0, 2 + tol)));
            Assert.False(ReachedByDefault(new Vec3(0.1, 0, 2 + tol + Nudge)));
            Assert.True(ReachedByDefault(new Vec3(0.1, -tol, 1)));
            Assert.False(ReachedByDefault(new Vec3(0.1, -tol - Nudge, 1)));

            Assert.True(new FarSurfaceReach(EdgeStart, EdgeEnd, Gap, BridgeScale).Observe(new Vec3(0.1, 0, 2 + tol)));
            Assert.False(new FarSurfaceReach(EdgeStart, EdgeEnd, Gap, BridgeScale).Observe(new Vec3(0.1, 0, 2 + tol + Nudge)));
            Assert.True(new FarSurfaceReach(EdgeStart, EdgeEnd, Gap, BridgeScale).Observe(new Vec3(0.1, -tol, 1)));
            Assert.False(new FarSurfaceReach(EdgeStart, EdgeEnd, Gap, BridgeScale).Observe(new Vec3(0.1, -tol - Nudge, 1)));
        }

        [Fact]
        public void Reach_StaysReachedOnceOnTheFarSurface()
        {
            var tracker = new FarSurfaceReach(EdgeStart, EdgeEnd, Gap, BridgeScale, Tolerance);
            Assert.False(tracker.Reached);
            Assert.Null(tracker.ReachedAtStep);

            Assert.False(tracker.Observe(new Vec3(-1, 0, 1)));
            Assert.False(tracker.Observe(new Vec3(-0.1, 0, 1)));
            Assert.Null(tracker.ReachedAtStep);

            Assert.True(tracker.Observe(new Vec3(0.1, 0, 1)));
            Assert.Equal(2, tracker.ReachedAtStep);

            Assert.True(tracker.Observe(new Vec3(0.1, -5, 1)));
            Assert.True(tracker.Observe(new Vec3(-1, 0, 1)));
            Assert.True(tracker.Reached);
            Assert.Equal(2, tracker.ReachedAtStep);

            var fast = new FarSurfaceReach(EdgeStart, EdgeEnd, Gap, BridgeScale, Tolerance);
            Assert.False(fast.Observe(new Vec3(-0.5, 0, 1)));
            Assert.True(fast.Observe(new Vec3(2.5, 0, 1)));
            Assert.Equal(1, fast.ReachedAtStep);
        }

        [Fact]
        public void Reach_ConvertsTheRoomEdgeToBridgeWorld()
        {
            var roomStart = new Vec3(1, 0.5, 0);
            var roomEnd = new Vec3(1, 0.5, 0.1);
            const double scale = 20.0;

            Assert.True(Scoring.ReachedFarSurface(new Vec3(20.1, 10, 1), roomStart, roomEnd, Gap, scale, Tolerance));
            Assert.False(Scoring.ReachedFarSurface(new Vec3(20.1, 10, 2.2), roomStart, roomEnd, Gap, scale, Tolerance));
            Assert.False(Scoring.ReachedFarSurface(new Vec3(20.1, 9.8, 1), roomStart, roomEnd, Gap, scale, Tolerance));
            Assert.False(Scoring.ReachedFarSurface(new Vec3(1.1, 0.5, 0.05), roomStart, roomEnd, Gap, scale, Tolerance));

            var slopedGap = new Vec3(5, 3, 0);
            Assert.True(Scoring.ReachedFarSurface(new Vec3(20.1, 10, 1), roomStart, roomEnd, slopedGap, scale, Tolerance));
            Assert.False(Scoring.ReachedFarSurface(new Vec3(20.1, 10, 1), roomStart, roomEnd, -Gap, scale, Tolerance));
            Assert.True(Scoring.ReachedFarSurface(new Vec3(19.9, 10, 1), roomStart, roomEnd, -Gap, scale, Tolerance));

            var risingEnd = new Vec3(0, 2, 2);
            Assert.True(Scoring.ReachedFarSurface(new Vec3(0.1, 0.95, 1), EdgeStart, risingEnd, Gap, BridgeScale, Tolerance));
            Assert.False(Scoring.ReachedFarSurface(new Vec3(0.1, 0.7, 1), EdgeStart, risingEnd, Gap, BridgeScale, Tolerance));
            Assert.True(Reached(new Vec3(0.1, 0.7, 1)));
        }

        [Fact]
        public void Reach_RejectsInvalidGeometry()
        {
            var inside = new Vec3(0.1, 0, 1);
            foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.ReachedFarSurface(inside, EdgeStart, EdgeEnd, Gap, bad, Tolerance));
                Assert.Throws<ArgumentOutOfRangeException>(() => new FarSurfaceReach(EdgeStart, EdgeEnd, Gap, bad, Tolerance));
            }

            foreach (double bad in new[] { -0.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.ReachedFarSurface(inside, EdgeStart, EdgeEnd, Gap, BridgeScale, bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => new FarSurfaceReach(EdgeStart, EdgeEnd, Gap, BridgeScale, bad));
            }

            Assert.True(Scoring.ReachedFarSurface(inside, EdgeStart, EdgeEnd, Gap, BridgeScale, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.ReachedFarSurface(inside, EdgeStart, EdgeStart, Gap, BridgeScale, Tolerance));
            Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.ReachedFarSurface(inside, EdgeStart, EdgeEnd, Vec3.UnitY, BridgeScale, Tolerance));
            Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.ReachedFarSurface(inside, EdgeStart, EdgeEnd, Vec3.Zero, BridgeScale, Tolerance));

            var infinite = new Vec3(double.PositiveInfinity, 0, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.ReachedFarSurface(infinite, EdgeStart, EdgeEnd, Gap, BridgeScale, Tolerance));
            Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.ReachedFarSurface(inside, infinite, EdgeEnd, Gap, BridgeScale, Tolerance));
            Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.ReachedFarSurface(inside, EdgeStart, infinite, Gap, BridgeScale, Tolerance));
            Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.ReachedFarSurface(inside, EdgeStart, EdgeEnd, infinite, BridgeScale, Tolerance));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FarSurfaceReach(EdgeStart, EdgeEnd, Gap, BridgeScale, Tolerance).Observe(infinite));
        }

        [Fact]
        public void StarPolicy_RejectsInvalidThresholds()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StarPolicy(-1, 0.8, 125));
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new StarPolicy(0, bad, 125));
                Assert.Throws<ArgumentOutOfRangeException>(() => new StarPolicy(0, 0.8, bad));
            }

            Assert.Equal(0, new StarPolicy(0, 0, 0).MaxLostForTwo);
        }

        [Fact]
        public void Metrics_RejectInvalidValues()
        {
            Assert.Throws<ArgumentNullException>(() => new RunMetrics(true, null, 0.5, 10));
            foreach (double bad in new[] { -0.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new RunMetrics(true, Array.Empty<int>(), bad, 10));
            }

            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new RunMetrics(true, Array.Empty<int>(), 0.5, bad));
            }

            var metrics = new RunMetrics(true, Array.Empty<int>(), 0.5, 10);
            Assert.Throws<ArgumentNullException>(() => Scoring.Stars(null, new StarPolicy(0, 0.8, 125)));
            Assert.Throws<ArgumentNullException>(() => Scoring.Stars(metrics, null));
        }

        private static bool Reached(Vec3 carPosition)
        {
            return Scoring.ReachedFarSurface(carPosition, EdgeStart, EdgeEnd, Gap, BridgeScale, Tolerance);
        }

        private static bool ReachedByDefault(Vec3 carPosition)
        {
            return Scoring.ReachedFarSurface(carPosition, EdgeStart, EdgeEnd, Gap, BridgeScale);
        }

        private static int Stars(StarPolicy policy, bool reached, int lost, double peak, double budget)
        {
            int[] failedIds = Enumerable.Range(1, lost).ToArray();
            return Scoring.Stars(new RunMetrics(reached, failedIds, peak, budget), policy);
        }
    }
}
