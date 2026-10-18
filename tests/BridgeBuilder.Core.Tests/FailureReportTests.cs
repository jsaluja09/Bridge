using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class FailureReportTests
    {
        static SolverInput TwoNodes(Vec3 free, double scale)
        {
            var input = new SolverInput { ScaleFactor = scale };
            input.Nodes.Add(new Node { Id = 0, Position = Vec3.Zero, Fixed = true });
            input.Nodes.Add(new Node { Id = 1, Position = free });
            input.Members.Add(new Member { Id = 7, NodeA = 0, NodeB = 1, SectionId = 0, LineId = 0 });
            input.Sections.Add(Constants.Wood.Section());
            return input;
        }

        /// <summary>Solves statically and reports every member at or past capacity, as one step would.</summary>
        static FailureReport SolveAndReport(SolverInput input, NodalLoad[] loads, long step)
        {
            var result = new StaticSolver().Solve(input, loads);
            var lengths = BucklingLengths.Compute(input, input.ScaleFactor);
            var output = new StepOutput { MemberForces = result.MemberForces };
            for (int k = 0; k < input.Members.Count; k++)
            {
                if (MemberCapacity.Evaluate(result.MemberForces[k], input.Sections[0], lengths[k]).Failed)
                    output.FailedMemberIds.Add(input.Members[k].Id);
            }
            return FailureReport.Build(output, input, step, input.ScaleFactor);
        }

        static void AssertFinite(MemberFailure f)
        {
            foreach (double v in new[] { f.Utilization, f.TensionUtilization, f.CompressionUtilization, f.BucklingUtilization,
                                         f.BendingUtilization, f.Slenderness, f.FailurePoint })
                Assert.True(double.IsFinite(v));
        }

        [Fact]
        public void LongSlenderColumn_FailsAsBuckling()
        {
            // 20 m bridge-world column (1 room m at 20x), squeezed from the top.
            var input = TwoNodes(new Vec3(0, 1, 0), 20);
            var report = SolveAndReport(input, new[] { new NodalLoad(1, new Vec3(0, -5e5, 0)) }, 12);

            var f = Assert.Single(report.Failures);
            Assert.Equal(7, f.MemberId);
            Assert.Equal(12, f.Step);
            Assert.Equal(FailureMode.Buckling, f.Mode);
            Assert.True(f.BucklingUtilization > f.CompressionUtilization);
            Assert.Equal(f.BucklingUtilization, f.Utilization, 9);
            Assert.Equal(0, f.TensionUtilization);
            Assert.Equal(0.5, f.FailurePoint);
            double r = Math.Sqrt(Constants.Wood.SecondMoment / Constants.Wood.Area);
            Assert.Equal(20 / r, f.Slenderness, 6);
            AssertFinite(f);
        }

        [Fact]
        public void ShortTie_FailsAsTension()
        {
            // 1 m bridge-world tie pulled at twice its breaking force.
            var input = TwoNodes(new Vec3(0.05, 0, 0), 20);
            double strength = Constants.Wood.TensionStrength * Constants.Wood.Area;
            var report = SolveAndReport(input, new[] { new NodalLoad(1, new Vec3(2 * strength, 0, 0)) }, 3);

            var f = Assert.Single(report.Failures);
            Assert.Equal(FailureMode.Tension, f.Mode);
            Assert.Equal(2.0, f.TensionUtilization, 6);
            Assert.Equal(0, f.CompressionUtilization);
            Assert.Equal(0, f.BucklingUtilization);
            Assert.True(f.Slenderness < 20);
            AssertFinite(f);
        }

        [Fact]
        public void Fixture1_BareCantilever_FailsAsBendingAtTheSupport()
        {
            var file = FixtureLoader.Load("fixture-01-bare-cantilevers.json");
            var input = file.ToInput(Constants.Wood.Section());
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureLoader.Directory, "fixture-01-bare-cantilevers.json")));
            var tips = json.RootElement.GetProperty("tips").EnumerateArray().Select(e => e.GetInt32()).ToArray();

            var frame = new FrameDynamics(input);
            frame.Rebuild(true);
            var loads = new Vec3[input.Nodes.Count];
            foreach (var w in DeadLoadPreview.SelfWeight(input, Constants.Loads.Gravity, file.Scale))
                loads[input.Nodes.FindIndex(n => n.Id == w.NodeId)] += w.Force;
            foreach (int id in tips)
                loads[input.Nodes.FindIndex(n => n.Id == id)] += new Vec3(0, -Constants.Human.CarMassKg * Constants.Loads.Gravity / tips.Length, 0);
            frame.SolveStatic(loads);

            var tracker = new FailureTracker(input, file.Scale);
            Assert.Null(tracker.Observe(frame, 0));
            Assert.NotEmpty(frame.RemoveFailures(Constants.Dynamics.MaxFailuresPerStep));
            var report = tracker.Observe(frame, 1);

            Assert.NotNull(report);
            Assert.Equal(1, report.Step);
            Assert.NotEmpty(report.Failures);
            foreach (var f in report.Failures)
            {
                Assert.Equal(FailureMode.Bending, f.Mode);
                Assert.True(f.Utilization >= Constants.Human.Fixture1MinimumUtilization);
                var member = input.Members.Single(m => m.Id == f.MemberId);
                bool supportAtA = input.Nodes.Single(n => n.Id == member.NodeA).Fixed;
                Assert.Equal(supportAtA ? 0.0 : 1.0, f.FailurePoint);
                AssertFinite(f);
            }
            Assert.Null(tracker.Observe(frame, 2));   // each failure is reported once
        }

        [Fact]
        public void Report_IsWorstFirst()
        {
            var input = TwoNodes(new Vec3(0.05, 0, 0), 20);
            input.Nodes.Add(new Node { Id = 2, Position = new Vec3(0, 0.05, 0) });
            input.Members.Add(new Member { Id = 3, NodeA = 0, NodeB = 2, SectionId = 0, LineId = 1 });
            double strength = Constants.Wood.TensionStrength * Constants.Wood.Area;
            var report = SolveAndReport(input, new[]
            {
                new NodalLoad(1, new Vec3(1.5 * strength, 0, 0)),
                new NodalLoad(2, new Vec3(0, 3 * strength, 0)),
            }, 0);

            Assert.Equal(new[] { 3, 7 }, report.Failures.Select(f => f.MemberId));
        }

        [Fact]
        public void TensionAndCompression_AreEachAgainstTheirOwnStrength()
        {
            var concrete = Constants.Concrete.Section();
            double pull = Constants.Concrete.TensionStrength * Constants.Concrete.Area;
            double push = Constants.Concrete.CompressionStrength * Constants.Concrete.Area;
            Assert.Equal(1.0, FailureReport.Evaluate(new MemberForce { Axial = pull }, concrete, 1).TensionUtilization, 9);
            Assert.Equal(1.0, FailureReport.Evaluate(new MemberForce { Axial = -push }, concrete, 1).CompressionUtilization, 9);
        }

        [Fact]
        public void UnknownMember_Throws()
        {
            var input = TwoNodes(new Vec3(0.05, 0, 0), 20);
            var output = new StepOutput { MemberForces = new[] { new MemberForce { MemberId = 7 } } };
            output.FailedMemberIds.Add(99);
            Assert.Throws<ArgumentException>(() => FailureReport.Build(output, input, 0, 20));
        }

        [Fact]
        public void NaNForce_Throws()
        {
            var force = new MemberForce { Axial = double.NaN };
            Assert.Throws<ArgumentOutOfRangeException>(() => FailureReport.Evaluate(force, Constants.Wood.Section(), 1));
        }
    }
}
