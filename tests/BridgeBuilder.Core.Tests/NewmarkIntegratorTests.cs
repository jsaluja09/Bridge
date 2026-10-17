using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>A linear solver that counts Factor calls.</summary>
    sealed class CountingSolver : ILinearSolver
    {
        public static int Factors;
        readonly BandedMatrix _inner;
        public CountingSolver(int n, int band) => _inner = new BandedMatrix(n, band);
        public void Add(int row, int column, double value) => _inner.Add(row, column, value);
        public void Factor() { Factors++; _inner.Factor(); }
        public void Solve(double[] rhs, double[] solution) => _inner.Solve(rhs, solution);
    }

    /// <summary>W2-01: Newmark integrator and progressive member failure.</summary>
    public class NewmarkIntegratorTests
    {
        static NewmarkSystem Sdof(double c)
        {
            var sys = new NewmarkSystem(1, 0, new[] { 1.0 }, new List<(int, int, double)> { (0, 0, 100.0) }, c, 0, null);
            sys.U[0] = 0.1;
            sys.V[0] = 0;
            sys.A[0] = -10;
            return sys;
        }

        [Fact]
        public void Newmark_FirstStep_MatchesWorkedExample()
        {
            var sys = Sdof(0);
            sys.Step(new[] { 0.0 }, 0.01);
            Assert.True(Math.Abs(sys.U[0] - 0.09950124688279302) <= 1e-9);
            Assert.True(Math.Abs(sys.V[0] - -0.09975062344139696) <= 1e-9);
            Assert.True(Math.Abs(sys.A[0] - -9.950124688279391) <= 1e-9);
            Assert.True(double.IsFinite(sys.U[0]) && double.IsFinite(sys.V[0]) && double.IsFinite(sys.A[0]));
        }

        [Fact]
        public void Newmark_LargeTimestep_StaysBounded()
        {
            var sys = Sdof(0);
            for (int i = 0; i < 20; i++)
            {
                sys.Step(new[] { 0.0 }, 1.0);
                Assert.True(Math.Abs(sys.U[0]) <= 0.2);
                Assert.True(double.IsFinite(sys.U[0]) && double.IsFinite(sys.V[0]) && double.IsFinite(sys.A[0]));
            }
        }

        [Fact]
        public void Newmark_DampingReducesAmplitude()
        {
            // zeta 0.05: c = 2 zeta sqrt(k m) = 1.0, applied as mass-proportional damping alpha = c / m.
            var sys = Sdof(1.0);
            sys.A[0] = -100 * 0.1;
            var u = new List<double> { sys.U[0] };
            for (int i = 0; i < 500; i++)
            {
                sys.Step(new[] { 0.0 }, 0.01);
                Assert.True(double.IsFinite(sys.U[0]));
                u.Add(sys.U[0]);
            }
            double early = u.Take(101).Max(Math.Abs), late = u.Skip(400).Take(101).Max(Math.Abs);
            Assert.True(late < early);
        }

        [Fact]
        public void Progressive_RemovingMember_RedistributesAndStaysFinite()
        {
            var file = FixtureLoader.Load("fixture-06-progressive-failure.json");
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureLoader.Directory, "fixture-06-progressive-failure.json")));
            var load = json.RootElement.GetProperty("load");
            var force = load.GetProperty("force").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            double dt = json.RootElement.GetProperty("dt").GetDouble();
            int failsFirst = json.RootElement.GetProperty("failsFirst").GetInt32();

            var strong = new SectionProperties { Id = 0, E = 2e11, G = 7.7e10, A = 1e-2, Iy = 1e-4, Iz = 1e-4, J = 1e-6, YieldStrength = 250e6, Density = 0, OuterFiberY = 0.1, OuterFiberZ = 0.1 };
            var weak = new SectionProperties { Id = 1, E = 2e11, G = 7.7e10, A = 1e-4, Iy = 1e-9, Iz = 1e-9, J = 1e-9, YieldStrength = 250e6, Density = 0, OuterFiberY = 0.01, OuterFiberZ = 0.01 };
            var input = file.ToInput(strong, weak);
            foreach (var n in input.Nodes)
                Assert.True(n.Position.IsFinite);

            CountingSolver.Factors = 0;
            var frame = new FrameDynamics(input, (n, b) => new CountingSolver(n, b));
            frame.Rebuild(true);
            var loads = new Vec3[3];
            loads[2] = new Vec3(force[0], force[1], force[2]);

            double before = double.NaN;
            int failedAt = -1;
            for (int step = 0; step < 200 && failedAt < 0; step++)
            {
                frame.Step(loads, dt);
                AssertFinite(frame);
                before = frame.Forces[1].Axial;
                var failed = frame.RemoveFailures(3);
                if (failed.Count > 0)
                {
                    Assert.Equal(new[] { failsFirst }, failed);
                    failedAt = step;
                }
            }
            Assert.True(failedAt >= 0, "the weak member should fail");

            int factors = CountingSolver.Factors;
            frame.Step(loads, dt);
            Assert.Equal(factors + 1, CountingSolver.Factors);
            Assert.NotEqual(before, frame.Forces[1].Axial);
            AssertFinite(frame);

            for (int i = 0; i < 20; i++)
            {
                frame.Step(loads, dt);
                Assert.Empty(frame.RemoveFailures(3));
                AssertFinite(frame);
            }
            Assert.Equal(factors + 1, CountingSolver.Factors);
        }

        static void AssertFinite(FrameDynamics frame)
        {
            foreach (double u in frame.U)
                Assert.True(double.IsFinite(u));
            foreach (var f in frame.Forces)
                Assert.True(double.IsFinite(f.Axial) && double.IsFinite(f.MomentLocalYAtA));
        }

        [Fact]
        public void Constants_AreAverageAcceleration()
        {
            Assert.Equal(0.25, Constants.Dynamics.NewmarkBeta);
            Assert.Equal(0.5, Constants.Dynamics.NewmarkGamma);
            Assert.Equal(0.02, Constants.Dynamics.DampingRatio);
            Assert.Equal(60, Constants.Dynamics.SolverRateHz);
        }
    }
}
