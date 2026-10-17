using System;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W1-03: tension, compression, buckling, bending. Fixture 4 numbers: do not modify without human approval.</summary>
    public class MemberCapacityTests
    {
        const double PcrLong = 1973920.8802178716;
        const double PcrShort = 7895683.5208714865;

        static void Relative(double expected, double actual) => Assert.True(Math.Abs(actual - expected) <= 1e-9 * Math.Abs(expected), $"{actual} vs {expected}");

        [Fact]
        public void Buckling_LongColumn_MatchesClosedForm()
        {
            double p = MemberCapacity.EulerCriticalLoad(2.0e11, 1.0e-6, 0.5, 2.0);
            Relative(Math.PI * Math.PI * 2.0e11 * 1.0e-6 / ((0.5 * 2.0) * (0.5 * 2.0)), p);
            Relative(PcrLong, p);
            Assert.True(double.IsFinite(p));
        }

        [Fact]
        public void Buckling_ShortColumn_MatchesClosedForm()
        {
            double p = MemberCapacity.EulerCriticalLoad(2.0e11, 1.0e-6, 0.5, 1.0);
            Relative(Math.PI * Math.PI * 2.0e11 * 1.0e-6 / (0.5 * 0.5), p);
            Relative(PcrShort, p);
        }

        [Fact]
        public void Buckling_LongFailsBeforeShort()
        {
            var longer = MemberCapacity.Evaluate(-3.0e6, 0, 0, 2.0e11, 1e-6, 1e-6, 1e-3, 4.0e9, 0.01, 0.01, 0.5, 2.0);
            var shorter = MemberCapacity.Evaluate(-3.0e6, 0, 0, 2.0e11, 1e-6, 1e-6, 1e-3, 4.0e9, 0.01, 0.01, 0.5, 1.0);
            Assert.True(longer.Utilization >= 1 && longer.Failed);
            Assert.True(shorter.Utilization < 1 && !shorter.Failed);
            Assert.True(double.IsFinite(longer.Utilization) && double.IsFinite(shorter.Utilization));
        }

        [Fact]
        public void Tension_AtYield_UtilizationIs1()
        {
            var r = MemberCapacity.Evaluate(250000, 0, 0, 2e11, 1e-6, 1e-6, 1e-3, 250e6, 0.01, 0.01, 0.5, 1);
            Assert.True(Math.Abs(r.Utilization - 1) <= 1e-12);
        }

        [Fact]
        public void SignedForce_TensionPositive_CompressionNegative()
        {
            Assert.Equal(250000, MemberCapacity.Evaluate(250000, 0, 0, 2e11, 1e-6, 1e-6, 1e-3, 250e6, 0.01, 0.01, 0.5, 1).SignedForce);
            Assert.Equal(-250000, MemberCapacity.Evaluate(-250000, 0, 0, 2e11, 1e-6, 1e-6, 1e-3, 250e6, 0.01, 0.01, 0.5, 1).SignedForce);
        }

        [Fact]
        public void Bending_AtYield_UtilizationIs1()
        {
            var r = MemberCapacity.Evaluate(0, 25000, 0, 2e11, 1e-6, 1e-6, 1e-3, 250e6, 0.01, 0.01, 0.5, 1);
            Assert.True(Math.Abs(r.Utilization - 1) <= 1e-12);
            Assert.Equal(FailureMode.Bending, r.Governing);
        }

        [Fact]
        public void Utilization_IsMaxNotSum()
        {
            // Pure bending ratio 0.4 (Fy 250e6, S = 1e-4: My = 25000, so M = 10000).
            Assert.True(Math.Abs(MemberCapacity.Evaluate(0, 10000, 0, 2e11, 1e-6, 1e-6, 1e-3, 250e6, 0.01, 0.01, 0.5, 1).Utilization - 0.4) <= 1e-12);

            double pcr = MemberCapacity.EulerCriticalLoad(2.0e11, 1.0e-6, 0.5, 2);
            double my = 0.7 * 1.0e9 * 1.0e-6 / 0.01;
            var r = MemberCapacity.Evaluate(-0.2 * pcr, my, 0, 2.0e11, 1e-6, 1e-6, 1.0e-3, 1.0e9, 0.01, 0.01, 0.5, 2);
            Assert.True(Math.Abs(r.Utilization - 0.7) <= 1e-12, r.Utilization.ToString());
        }

        [Fact]
        public void Pier_CarriesLoadUntilBuckling()
        {
            var pier = FixtureLoader.Load("fixture-07-pier.json");
            Assert.Single(pier.Members);
            var atPcr = MemberCapacity.Evaluate(-PcrLong, 0, 0, 2.0e11, 1e-6, 1e-6, 1.0e-3, 1.0e10, 0.01, 0.01, 0.5, 2);
            var atHalf = MemberCapacity.Evaluate(-0.5 * PcrLong, 0, 0, 2.0e11, 1e-6, 1e-6, 1.0e-3, 1.0e10, 0.01, 0.01, 0.5, 2);
            Relative(1, atPcr.Utilization);
            Assert.True(atPcr.Failed);
            Relative(0.5, atHalf.Utilization);
            Assert.False(atHalf.Failed);
        }

        [Fact]
        public void Default_K_ReadsHumanOwnedConstant()
        {
            Assert.Equal(0.5, Constants.Capacity.EffectiveLengthFactor);
            Relative(PcrLong, MemberCapacity.EulerCriticalLoad(2.0e11, 1.0e-6, 2.0));
        }
    }
}
