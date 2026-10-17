using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class MemberCapacityTests
    {
        private const string Fixture4 = "fixture-04-buckling-columns.json";
        private const string Fixture7 = "fixture-07-pier.json";

        // Issue W1-03 fixture 4 numbers. Do not modify without human approval.
        private const double FixtureE = 2.0e11;
        private const double FixtureI = 1.0e-6;
        private const double FixtureA = 1.0e-3;
        private const double FixtureK = 0.5;
        private const double ShortLength = 1.0;
        private const double LongLength = 2.0;
        private const double PcrShortLiteral = 7895683.5208714865;
        private const double PcrLongLiteral = 1973920.8802178716;

        private const double RelativeTolerance = 1e-9;
        private const double UtilizationTolerance = 1e-12;
        private const double OuterFiber = 0.01;

        [Fact]
        public void Buckling_LongColumn_MatchesClosedForm()
        {
            AssertFixture4ColumnMatchesClosedForm("long", LongLength, PcrLongLiteral);
        }

        [Fact]
        public void Buckling_ShortColumn_MatchesClosedForm()
        {
            AssertFixture4ColumnMatchesClosedForm("short", ShortLength, PcrShortLiteral);
        }

        [Fact]
        public void Buckling_LongFailsBeforeShort()
        {
            const double axial = -3.0e6;
            BridgeFixture fixture = FixtureLoader.Load(Fixture4);
            using JsonDocument document = FixtureLoader.ReadDocument(Fixture4);
            JsonElement root = document.RootElement;
            double k = root.GetProperty("effectiveLengthFactor").GetDouble();
            Assert.Equal(FixtureK, k);

            int longId = ColumnElement(root, "long").GetProperty("memberId").GetInt32();
            int shortId = ColumnElement(root, "short").GetProperty("memberId").GetInt32();
            SectionProperties longSection = SectionOf(fixture, longId);
            SectionProperties shortSection = SectionOf(fixture, shortId);
            AssertFixture4Section(longSection);
            AssertFixture4Section(shortSection);
            Assert.Equal(4.0e9, longSection.YieldStrength);
            Assert.Equal(4.0e9, shortSection.YieldStrength);

            MemberCapacityResult longColumn = MemberCapacity.Evaluate(axial, 0.0, 0.0, longSection, MemberLength(fixture, longId), k);
            MemberCapacityResult shortColumn = MemberCapacity.Evaluate(axial, 0.0, 0.0, shortSection, MemberLength(fixture, shortId), k);

            AssertFinite(longColumn);
            AssertFinite(shortColumn);
            Assert.True(longColumn.Utilization >= 1.0, "Long column utilization " + longColumn.Utilization + " should be at least 1.");
            Assert.True(longColumn.Failed);
            Assert.Equal(longColumn.BucklingUtilization, longColumn.Utilization);
            Assert.True(shortColumn.Utilization < 1.0, "Short column utilization " + shortColumn.Utilization + " should be below 1.");
            Assert.False(shortColumn.Failed);
        }

        [Fact]
        public void Tension_AtYield_UtilizationIs1()
        {
            SectionProperties section = Section(yieldStrength: 250e6, area: 1e-3);

            MemberCapacityResult result = MemberCapacity.Evaluate(250000.0, 0.0, 0.0, section, LongLength);

            AssertFinite(result);
            AssertWithin(1.0, result.Utilization, UtilizationTolerance);
            Assert.Equal(result.TensionUtilization, result.Utilization);
            Assert.Equal(0.0, result.CompressionUtilization);
            Assert.Equal(0.0, result.BucklingUtilization);
            Assert.Equal(0.0, result.BendingUtilization);
        }

        [Fact]
        public void SignedForce_TensionPositive_CompressionNegative()
        {
            SectionProperties section = Section(yieldStrength: 250e6, area: 1e-3);

            MemberCapacityResult tension = MemberCapacity.Evaluate(250000.0, 0.0, 0.0, section, LongLength);
            MemberCapacityResult compression = MemberCapacity.Evaluate(-250000.0, 0.0, 0.0, section, LongLength);
            MemberCapacityResult tensionFromForce = MemberCapacity.Evaluate(new MemberForce { Axial = 250000.0 }, section, LongLength);
            MemberCapacityResult compressionFromForce = MemberCapacity.Evaluate(new MemberForce { Axial = -250000.0 }, section, LongLength);

            foreach (MemberCapacityResult result in new[] { tension, compression, tensionFromForce, compressionFromForce })
            {
                AssertFinite(result);
            }

            Assert.Equal(250000.0, tension.SignedForce);
            Assert.True(tension.SignedForce > 0.0);
            Assert.Equal(-250000.0, compression.SignedForce);
            Assert.True(compression.SignedForce < 0.0);
            Assert.Equal(250000.0, tensionFromForce.SignedForce);
            Assert.Equal(-250000.0, compressionFromForce.SignedForce);
            Assert.True(tension.TensionUtilization > 0.0 && tension.CompressionUtilization == 0.0);
            Assert.True(compression.CompressionUtilization > 0.0 && compression.TensionUtilization == 0.0);
        }

        [Fact]
        public void Bending_AtYield_UtilizationIs1()
        {
            SectionProperties section = Section(yieldStrength: 250e6, iy: 1e-6, cy: 0.01);

            MemberCapacityResult result = MemberCapacity.Evaluate(0.0, 25000.0, 0.0, section, LongLength);

            AssertFinite(result);
            AssertWithin(1.0, result.Utilization, UtilizationTolerance);
            Assert.Equal(result.BendingUtilization, result.Utilization);
            Assert.Equal(0.0, result.SignedForce);
        }

        [Fact]
        public void Utilization_IsMaxNotSum()
        {
            const double yieldStrength = 1.0e9;
            SectionProperties section = Section(yieldStrength: yieldStrength, area: FixtureA, iy: FixtureI, iz: FixtureI, e: FixtureE);
            double elasticMomentY = yieldStrength * section.Iy / section.Cy;

            MemberCapacityResult pureBending = MemberCapacity.Evaluate(0.0, 0.4 * elasticMomentY, 0.0, section, LongLength, FixtureK);
            AssertFinite(pureBending);
            AssertWithin(0.4, pureBending.BendingUtilization, UtilizationTolerance);
            AssertWithin(0.4, pureBending.Utilization, UtilizationTolerance);

            double pcr = MemberCapacity.EulerCriticalLoad(FixtureE, FixtureI, FixtureK, LongLength);
            MemberCapacityResult combined = MemberCapacity.Evaluate(-0.2 * pcr, 0.7 * elasticMomentY, 0.0, section, LongLength, FixtureK);
            AssertFinite(combined);
            AssertWithin(0.2, combined.BucklingUtilization, UtilizationTolerance);
            AssertWithin(0.7, combined.BendingUtilization, UtilizationTolerance);
            Assert.True(combined.CompressionUtilization < 0.7, "Compression should not govern this case.");
            AssertWithin(0.7, combined.Utilization, UtilizationTolerance);
            Assert.True(Math.Abs(combined.Utilization - 0.9) > 0.1, "Utilization " + combined.Utilization + " looks like the sum 0.9.");
        }

        [Fact]
        public void Pier_CarriesLoadUntilBuckling()
        {
            BridgeFixture fixture = FixtureLoader.Load(Fixture7);
            using JsonDocument document = FixtureLoader.ReadDocument(Fixture7);
            JsonElement root = document.RootElement;
            int pierId = root.GetProperty("pierMemberId").GetInt32();
            double k = root.GetProperty("effectiveLengthFactor").GetDouble();
            double expectedCriticalLoad = root.GetProperty("expectedCriticalLoad").GetDouble();
            Assert.Equal(FixtureK, k);
            Assert.Equal(PcrLongLiteral, expectedCriticalLoad);

            Member pier = fixture.Members.Single(m => m.Id == pierId);
            Node bottom = fixture.Nodes.Single(n => n.Id == pier.NodeA);
            Node top = fixture.Nodes.Single(n => n.Id == pier.NodeB);
            Assert.True(bottom.Fixed && bottom.GroundAnchor, "The pier must rise from a fixed ground anchor.");
            Assert.True(top.Position.X == bottom.Position.X && top.Position.Z == bottom.Position.Z, "The pier must be vertical.");
            double length = MemberLength(fixture, pierId);
            Assert.Equal(LongLength, length);

            SectionProperties section = SectionOf(fixture, pierId);
            AssertFixture4Section(section);
            Assert.Equal(1.0e10, section.YieldStrength);

            double pcr = MemberCapacity.EulerCriticalLoad(section.E, Math.Min(section.Iy, section.Iz), k, length);
            AssertFinite(pcr, "Pcr");
            AssertRelative(expectedCriticalLoad, pcr);

            MemberCapacityResult atCritical = MemberCapacity.Evaluate(-pcr, 0.0, 0.0, section, length, k);
            AssertFinite(atCritical);
            Assert.Equal(pcr, atCritical.CriticalLoad);
            AssertRelative(1.0, atCritical.Utilization);
            Assert.True(atCritical.Failed);
            Assert.Equal(atCritical.BucklingUtilization, atCritical.Utilization);

            MemberCapacityResult atHalf = MemberCapacity.Evaluate(-0.5 * pcr, 0.0, 0.0, section, length, k);
            AssertFinite(atHalf);
            AssertRelative(0.5, atHalf.Utilization);
            Assert.False(atHalf.Failed);
            Assert.Equal(atHalf.BucklingUtilization, atHalf.Utilization);
        }

        [Fact]
        public void EulerCriticalLoad_WithoutK_UsesEffectiveLengthFactor()
        {
            Assert.Equal(0.5, Constants.Capacity.EffectiveLengthFactor);
            double withDefault = MemberCapacity.EulerCriticalLoad(FixtureE, FixtureI, LongLength);
            AssertFinite(withDefault, "Pcr");
            Assert.Equal(MemberCapacity.EulerCriticalLoad(FixtureE, FixtureI, Constants.Capacity.EffectiveLengthFactor, LongLength), withDefault);

            SectionProperties section = Section(yieldStrength: 1.0e10);
            Assert.Equal(withDefault, MemberCapacity.Evaluate(-1.0e5, 0.0, 0.0, section, LongLength).CriticalLoad);
            Assert.Equal(withDefault, MemberCapacity.Evaluate(new MemberForce { Axial = -1.0e5 }, section, LongLength).CriticalLoad);
        }

        [Fact]
        public void Evaluate_BucklingUsesWeakAxis()
        {
            double expected = MemberCapacity.EulerCriticalLoad(FixtureE, 1.0e-6, FixtureK, LongLength);

            MemberCapacityResult weakZ = MemberCapacity.Evaluate(-1.0e5, 0.0, 0.0, Section(yieldStrength: 1.0e10, iy: 4.0e-6, iz: 1.0e-6), LongLength, FixtureK);
            MemberCapacityResult weakY = MemberCapacity.Evaluate(-1.0e5, 0.0, 0.0, Section(yieldStrength: 1.0e10, iy: 1.0e-6, iz: 4.0e-6), LongLength, FixtureK);

            AssertFinite(weakZ);
            AssertFinite(weakY);
            Assert.Equal(expected, weakZ.CriticalLoad);
            Assert.Equal(expected, weakY.CriticalLoad);
        }

        [Fact]
        public void Evaluate_MemberForce_UsesWorseEndPerPlane()
        {
            const double yieldStrength = 250e6;
            SectionProperties section = Section(yieldStrength: yieldStrength, iy: 4.0e-6, cy: 0.02, iz: 1.0e-6, cz: 0.01);
            double elasticMomentY = yieldStrength * section.Iy / section.Cy;
            double elasticMomentZ = yieldStrength * section.Iz / section.Cz;

            var yGoverns = new MemberForce
            {
                MomentLocalYAtA = 10000.0,
                MomentLocalYAtB = -30000.0,
                MomentLocalZAtA = -5000.0,
                MomentLocalZAtB = 2500.0,
                ShearLocalZAtA = 1.0e6,
                ShearLocalYAtB = -1.0e6,
            };
            MemberCapacityResult fromY = MemberCapacity.Evaluate(yGoverns, section, LongLength, FixtureK);
            AssertFinite(fromY);
            AssertWithin(30000.0 / elasticMomentY, fromY.BendingUtilization, UtilizationTolerance);
            Assert.Equal(MemberCapacity.Evaluate(0.0, 30000.0, 5000.0, section, LongLength, FixtureK).Utilization, fromY.Utilization);

            var zGoverns = new MemberForce { MomentLocalZAtA = -20000.0, MomentLocalZAtB = 5000.0 };
            MemberCapacityResult fromZ = MemberCapacity.Evaluate(zGoverns, section, LongLength, FixtureK);
            AssertFinite(fromZ);
            AssertWithin(20000.0 / elasticMomentZ, fromZ.BendingUtilization, UtilizationTolerance);
            Assert.Equal(MemberCapacity.Evaluate(0.0, 0.0, 20000.0, section, LongLength, FixtureK).Utilization, fromZ.Utilization);
            Assert.Equal(-20000.0, zGoverns.MomentLocalZAtA);
            Assert.Equal(0.0, zGoverns.Utilization);
        }

        [Fact]
        public void Evaluate_RejectsInvalidInput()
        {
            SectionProperties section = Section(yieldStrength: 250e6);

            Assert.Throws<ArgumentNullException>(() => MemberCapacity.Evaluate(0.0, 0.0, 0.0, null, LongLength));
            Assert.Throws<ArgumentNullException>(() => MemberCapacity.Evaluate((MemberForce)null, section, LongLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.Evaluate(double.NaN, 0.0, 0.0, section, LongLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.Evaluate(0.0, double.PositiveInfinity, 0.0, section, LongLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.Evaluate(0.0, 0.0, double.NaN, section, LongLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.Evaluate(new MemberForce { MomentLocalZAtB = double.NaN }, section, LongLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.Evaluate(0.0, 0.0, 0.0, section, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.Evaluate(0.0, 0.0, 0.0, section, LongLength, -0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.Evaluate(0.0, 0.0, 0.0, Section(yieldStrength: 250e6, area: 0.0), LongLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.Evaluate(0.0, 0.0, 0.0, Section(yieldStrength: 250e6, cz: 0.0), LongLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.Evaluate(0.0, 0.0, 0.0, Section(yieldStrength: double.NaN), LongLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.EulerCriticalLoad(0.0, FixtureI, FixtureK, LongLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => MemberCapacity.EulerCriticalLoad(FixtureE, FixtureI, FixtureK, double.PositiveInfinity));
        }

        private static void AssertFixture4ColumnMatchesClosedForm(string role, double length, double issueLiteral)
        {
            BridgeFixture fixture = FixtureLoader.Load(Fixture4);
            using JsonDocument document = FixtureLoader.ReadDocument(Fixture4);
            JsonElement root = document.RootElement;
            JsonElement column = ColumnElement(root, role);
            int memberId = column.GetProperty("memberId").GetInt32();
            double expectedCriticalLoad = column.GetProperty("expectedCriticalLoad").GetDouble();
            Assert.Equal(issueLiteral, expectedCriticalLoad);
            Assert.Equal(length, column.GetProperty("length").GetDouble());
            Assert.Equal(length, MemberLength(fixture, memberId));
            Assert.Equal(FixtureK, root.GetProperty("effectiveLengthFactor").GetDouble());
            AssertFixture4Section(SectionOf(fixture, memberId));

            double pcr = MemberCapacity.EulerCriticalLoad(FixtureE, FixtureI, FixtureK, length);
            double closedForm = Math.PI * Math.PI * FixtureE * FixtureI / ((FixtureK * length) * (FixtureK * length));

            AssertFinite(pcr, "Pcr");
            AssertRelative(closedForm, pcr);
            AssertRelative(expectedCriticalLoad, pcr);
        }

        private static void AssertFixture4Section(SectionProperties section)
        {
            Assert.Equal(FixtureE, section.E);
            Assert.Equal(FixtureI, section.Iy);
            Assert.Equal(FixtureI, section.Iz);
            Assert.Equal(FixtureA, section.A);
        }

        private static JsonElement ColumnElement(JsonElement root, string role)
        {
            return root.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("role").GetString() == role);
        }

        private static SectionProperties SectionOf(BridgeFixture fixture, int memberId)
        {
            Member member = fixture.Members.Single(m => m.Id == memberId);
            return fixture.Sections.Single(s => s.Id == member.SectionId);
        }

        private static double MemberLength(BridgeFixture fixture, int memberId)
        {
            Member member = fixture.Members.Single(m => m.Id == memberId);
            Vec3 a = fixture.Nodes.Single(n => n.Id == member.NodeA).Position;
            Vec3 b = fixture.Nodes.Single(n => n.Id == member.NodeB).Position;
            return Vec3.Distance(a, b);
        }

        private static SectionProperties Section(
            double yieldStrength,
            double area = FixtureA,
            double iy = FixtureI,
            double iz = FixtureI,
            double cy = OuterFiber,
            double cz = OuterFiber,
            double e = FixtureE)
        {
            return new SectionProperties { E = e, A = area, Iy = iy, Iz = iz, Cy = cy, Cz = cz, YieldStrength = yieldStrength };
        }

        private static void AssertFinite(MemberCapacityResult result)
        {
            AssertFinite(result.SignedForce, "SignedForce");
            AssertFinite(result.TensionUtilization, "TensionUtilization");
            AssertFinite(result.CompressionUtilization, "CompressionUtilization");
            AssertFinite(result.BucklingUtilization, "BucklingUtilization");
            AssertFinite(result.BendingUtilization, "BendingUtilization");
            AssertFinite(result.CriticalLoad, "CriticalLoad");
            AssertFinite(result.Utilization, "Utilization");
        }

        private static void AssertFinite(double value, string name)
        {
            Assert.True(double.IsFinite(value), name + " is " + value + ".");
        }

        private static void AssertRelative(double expected, double actual)
        {
            Assert.True(
                Math.Abs(actual - expected) <= RelativeTolerance * Math.Abs(expected),
                "Expected " + expected.ToString("R") + " within relative " + RelativeTolerance + " but got " + actual.ToString("R") + ".");
        }

        private static void AssertWithin(double expected, double actual, double tolerance)
        {
            Assert.True(
                Math.Abs(actual - expected) <= tolerance,
                "Expected " + expected.ToString("R") + " within " + tolerance + " but got " + actual.ToString("R") + ".");
        }
    }
}
