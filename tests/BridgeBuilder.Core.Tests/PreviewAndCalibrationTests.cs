using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridgeBuilder.Core;
using Xunit;
using Xunit.Abstractions;

namespace BridgeBuilder.Core.Tests
{
    sealed class SpyStaticSolver : IStaticSolver
    {
        public int Calls;
        public StaticSolveResult Solve(SolverInput input, NodalLoad[] loads)
        {
            Calls++;
            return new StaticSolveResult
            {
                Displacements = new Vec3[input.Nodes.Count],
                MemberForces = input.Members.Select((m, i) => new MemberForce { MemberId = m.Id, Axial = 1000 * (i + 1), MomentLocalYAtA = 500 }).ToArray(),
            };
        }
    }

    /// <summary>W2-06: dead-load preview.</summary>
    public class DeadLoadPreviewTests
    {
        static SolverInput Beam(double area, double density)
        {
            var input = new SolverInput { ScaleFactor = 10 };
            input.Sections.Add(new SectionProperties { Id = 0, E = 2e11, G = 7.7e10, A = area, Iy = 1e-5, Iz = 1e-5, J = 1e-6, YieldStrength = 250e6, Density = density, OuterFiberY = 0.1, OuterFiberZ = 0.1 });
            input.Nodes.Add(new Node { Id = 0, Position = new Vec3(0, 0, 0), Fixed = true });
            input.Nodes.Add(new Node { Id = 1, Position = new Vec3(2, 0, 0) });
            input.Members.Add(new Member { Id = 0, NodeA = 0, NodeB = 1 });
            return input;
        }

        [Fact]
        public void SelfWeight_SingleHorizontalMember_NodalLoads()
        {
            var loads = DeadLoadPreview.SelfWeight(Beam(0.01, 1000), 9.80665, 10);
            foreach (var l in loads)
            {
                Assert.True(Math.Abs(l.Force.Y + 980.665) <= 1e-6);
                Assert.Equal(0, l.Force.X);
                Assert.Equal(0, l.Force.Z);
                Assert.True(l.Force.IsFinite);
            }
            var byDefault = DeadLoadPreview.Run(Beam(0.01, 1000), new SpyStaticSolver(), null, Constants.Loads.Gravity, 10).SelfWeight;
            Assert.Equal(loads.Select(l => l.Force), byDefault.Select(l => l.Force));
        }

        [Fact]
        public void Preview_UsesStaticSolverAndReturnsUtilization()
        {
            var spy = new SpyStaticSolver();
            var input = Beam(0.01, 1000);
            var r = DeadLoadPreview.Run(input, spy, () => false);
            Assert.Equal(1, spy.Calls);
            Assert.Equal(PreviewStatus.Completed, r.Status);
            var expected = MemberCapacity.Evaluate(r.Solve.MemberForces[0], input.Sections[0], 20).Utilization;
            Assert.Equal(expected, r.Utilizations[0], 12);
            Assert.All(r.Utilizations, u => Assert.True(double.IsFinite(u)));
        }

        [Fact]
        public void Preview_CancelBeforeSolve_DoesNotCallSolver()
        {
            var spy = new SpyStaticSolver();
            var r = DeadLoadPreview.Run(Beam(0.01, 1000), spy, () => true);
            Assert.Equal(PreviewStatus.Cancelled, r.Status);
            Assert.Equal(0, spy.Calls);
        }

        [Fact]
        public void Preview_NoNaN()
        {
            var r = DeadLoadPreview.Run(Beam(0.01, 1000), new BandedStaticSolver(), null);
            Assert.All(r.SelfWeight, l => Assert.True(l.Force.IsFinite));
            Assert.All(r.Utilizations, u => Assert.True(double.IsFinite(u)));
        }
    }

    /// <summary>
    /// W2-07: calibrate each material's section and strengths so fixture 1 fails in every material and fixture 5,
    /// built in wood, holds the standard car.
    /// </summary>
    public class FixtureCalibrationTests
    {
        readonly ITestOutputHelper _out;
        public FixtureCalibrationTests(ITestOutputHelper output) => _out = output;

        static double WorstUtilization(string fixture, string carNodesProperty, BridgeMaterial material)
        {
            var file = FixtureLoader.Load(fixture);
            var section = Materials.Section(material);
            section.Id = 0;
            var input = file.ToInput(section);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureLoader.Directory, fixture)));
            var carNodes = json.RootElement.GetProperty(carNodesProperty).EnumerateArray().Select(e => e.GetInt32()).ToArray();

            var loads = DeadLoadPreview.SelfWeight(input, Constants.Loads.Gravity, file.Scale).ToList();
            double share = Constants.Human.CarMassKg * Constants.Loads.Gravity / carNodes.Length;
            foreach (int id in carNodes)
                loads.Add(new NodalLoad(id, new Vec3(0, -share, 0)));

            var result = new StaticSolver().Solve(input, loads.ToArray());
            var lengths = BucklingLengths.Compute(input, file.Scale);
            double worst = 0;
            for (int k = 0; k < input.Members.Count; k++)
            {
                var f = result.MemberForces[k];
                Assert.True(double.IsFinite(f.Axial) && double.IsFinite(f.MomentLocalYAtA));
                double u = MemberCapacity.Evaluate(f, input.Sections[0], lengths[k]).Utilization;
                Assert.True(double.IsFinite(u));
                worst = Math.Max(worst, u);
            }
            foreach (var d in result.Displacements)
                Assert.True(d.IsFinite);
            return worst;
        }

        [Theory]
        [InlineData(BridgeMaterial.Wood)]
        [InlineData(BridgeMaterial.Cable)]
        [InlineData(BridgeMaterial.Concrete)]
        public void Fixture1_BareCantilevers_UtilizationAtLeastHumanMinimum(BridgeMaterial material)
        {
            double u = WorstUtilization("fixture-01-bare-cantilevers.json", "tips", material);
            _out.WriteLine($"fixture 1 ({material}) worst utilization {u:F3}");
            Assert.True(u >= Constants.Human.Fixture1MinimumUtilization);
        }

        [Fact]
        public void Fixture5_Truss_UtilizationBelowHumanMaximum()
        {
            double u = WorstUtilization("fixture-05-triangulated-truss.json", "deckNodes", BridgeMaterial.Wood);
            _out.WriteLine($"fixture 5 (wood) worst utilization {u:F3}");
            Assert.True(u < Constants.Human.Fixture5MaximumUtilization);
        }
    }
}
