using System;
using System.Numerics;
using BridgeBuilder.Core;
using NUnit.Framework;
using static BridgeBuilder.Tests.TestBridges;

namespace BridgeBuilder.Tests
{
    /// <summary>The spec's starting fixtures (section 5). Hand calculations use bridge-world units.</summary>
    public class BridgeFixtureTests
    {
        static BridgeSettings NoGravity() => new BridgeSettings { Gravity = 0f };

        static int NodeAt(BridgeStructure st, Vector3 room)
        {
            Vector3 w = st.ToWorld(room);
            for (int i = 0; i < st.NodeCount; i++)
            {
                if (Vector3.Distance(st.Rest[i], w) < 0.05f)
                    return i;
            }
            Assert.Fail($"no structure node at {room}");
            return -1;
        }

        static double EI(BridgeStructure st) => st.Settings.YoungsModulus * st.Inertia;

        [Test]
        public void F1_BareSpan_Fails()
        {
            var run = Run(BareDeck());
            Assert.That(run.Result, Is.EqualTo(RunResult.Failed));
            Assert.That(run.BrokenCount, Is.GreaterThan(0));
        }

        [Test]
        public void F2_FixedBeam_MatchesHandCalculation()
        {
            // One line from surface to surface: fixed at both ends, 20 m, 10 elements. Point load P at midspan.
            var d = Room(NoGravity());
            var undo = new UndoStack();
            Line(d, undo, P(0f, Top, -Side), P(1f, Top, -Side));
            var st = BridgeStructure.Compile(d, new Challenge());
            var solver = new BridgeSolver(st);
            solver.Rebuild(true);

            const float load = 10000f;
            int mid = NodeAt(st, P(0.5f, Top, -Side));
            var loads = new Vector3[st.NodeCount];
            loads[mid] = new Vector3(0f, -load, 0f);
            Assert.That(solver.SolveStatic(loads), Is.True);

            double L = 20.0;
            double expected = load * L * L * L / (192 * EI(st));                 // fixed-fixed, central point load
            double actual = -solver.U[mid * 6 + 1];
            Assert.That(actual, Is.EqualTo(expected).Within(1).Percent);

            solver.UpdateUtilization();
            solver.ElementForces(0, out _, out double endMoment);
            Assert.That(endMoment, Is.EqualTo(load * L / 8).Within(1).Percent); // fixed-end moment PL/8
        }

        [Test]
        public void F2b_Cantilever_MatchesHandCalculation()
        {
            var d = Room(NoGravity());
            var undo = new UndoStack();
            Line(d, undo, P(0f, Top, -Side), P(0.5f, Top, -Side));
            var st = BridgeStructure.Compile(d, new Challenge());
            var solver = new BridgeSolver(st);
            solver.Rebuild(true);

            const float load = 5000f;
            int tip = NodeAt(st, P(0.5f, Top, -Side));
            var loads = new Vector3[st.NodeCount];
            loads[tip] = new Vector3(0f, -load, 0f);
            solver.SolveStatic(loads);

            double L = 10.0;
            Assert.That(-solver.U[tip * 6 + 1], Is.EqualTo(load * L * L * L / (3 * EI(st))).Within(1).Percent);
        }

        static double Drift(bool braced)
        {
            // A portal frame on the ground: two 6 m columns and a 6 m beam, rigid joints.
            var d = Room(NoGravity());
            var undo = new UndoStack();
            Line(d, undo, P(0.1f, 0f, -Side), P(0.1f, 0.3f, -Side));
            Line(d, undo, P(0.4f, 0f, -Side), P(0.4f, 0.3f, -Side));
            Line(d, undo, P(0.1f, 0.3f, -Side), P(0.4f, 0.3f, -Side));
            if (braced)
                Line(d, undo, P(0.1f, 0f, -Side), P(0.4f, 0.3f, -Side));

            var st = BridgeStructure.Compile(d, new Challenge());
            var solver = new BridgeSolver(st);
            solver.Rebuild(true);
            int corner = NodeAt(st, P(0.1f, 0.3f, -Side));
            var loads = new Vector3[st.NodeCount];
            loads[corner] = new Vector3(20000f, 0f, 0f);
            Assert.That(solver.SolveStatic(loads), Is.True);
            return solver.U[corner * 6];
        }

        [Test]
        public void F3_UnbracedRectangle_IsAFrameNotAMechanism_AndBracingStiffensIt()
        {
            double open = Drift(false), braced = Drift(true);
            TestContext.WriteLine($"drift open {open * 1000:F1} mm, braced {braced * 1000:F2} mm");
            Assert.That(double.IsFinite(open) && open > 0 && open < 1.0, "open frame should sway a finite amount");
            Assert.That(open / braced, Is.GreaterThan(5.0), "a diagonal should make it far stiffer");
        }

        static (BridgeStructure st, BridgeSolver solver, int top) Column(float height)
        {
            var d = Room(NoGravity());
            var undo = new UndoStack();
            Line(d, undo, P(0.4f, 0f, -Side), P(0.4f, height, -Side));
            var st = BridgeStructure.Compile(d, new Challenge());
            var solver = new BridgeSolver(st);
            solver.Rebuild(true);
            return (st, solver, NodeAt(st, P(0.4f, height, -Side)));
        }

        static float BaseUtilization((BridgeStructure st, BridgeSolver solver, int top) c, float load)
        {
            var loads = new Vector3[c.st.NodeCount];
            loads[c.top] = new Vector3(0f, -load, 0f);
            c.solver.SolveStatic(loads);
            c.solver.UpdateUtilization();
            float worst = 0f;
            for (int k = 0; k < c.st.ElementCount; k++)
                worst = Math.Max(worst, c.solver.Utilization[k]);
            return worst;
        }

        [Test]
        public void F4_LongColumn_BucklesFirst_AtTheEulerLoad()
        {
            var tall = Column(0.6f);   // 12 m
            var stub = Column(0.2f);   // 4 m
            double euler = Math.PI * Math.PI * EI(tall.st) / (12.0 * 12.0);
            TestContext.WriteLine($"Euler load of the 12 m member: {euler / 1000:F0} kN; yield {tall.st.YieldForce / 1000:F0} kN");
            Assert.That(tall.st.BucklingLength[0], Is.EqualTo(12f).Within(0.01f), "buckling uses the whole member, not the 10 cm element");

            float load = (float)(euler * 0.98);
            Assert.That(BaseUtilization(tall, load), Is.EqualTo(0.98f).Within(0.01f));
            Assert.That(BaseUtilization(stub, load), Is.LessThan(0.98f));
            Assert.That(BaseUtilization(tall, (float)(euler * 1.02)), Is.GreaterThan(1f));
        }

        [Test]
        public void F5_TriangulatedTruss_HoldsTheCar_WithMargin()
        {
            var run = Run(Truss());
            Assert.That(run.Result, Is.EqualTo(RunResult.Passed), run.Message);
            Assert.That(run.BrokenCount, Is.EqualTo(0));
            Assert.That(run.PeakUtilization, Is.LessThan(0.8f));
        }

        [Test]
        public void F6_ProgressiveFailure_RedistributesWithoutNaN()
        {
            var run = Run(Truss(), new Challenge { CarMass = 400000f }, 15f);
            Assert.That(run.Result, Is.EqualTo(RunResult.Failed));
            Assert.That(run.BrokenCount, Is.GreaterThan(0));
            Assert.That(run.Frame.Singular, Is.False);
            foreach (var p in run.Positions)
                Assert.That(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z));
        }

        [Test]
        public void F7_PierToGround_CarriesLoadUntilItBuckles()
        {
            var pier = Column(0.7f);   // 14 m pier on a ground anchor
            double euler = Math.PI * Math.PI * EI(pier.st) / (14.0 * 14.0);
            Assert.That(BaseUtilization(pier, (float)(euler * 0.5)), Is.LessThan(1f));
            Assert.That(BaseUtilization(pier, (float)(euler * 1.05)), Is.GreaterThan(1f));
        }

        [Test]
        public void Crosswind_PushesHarderThanCalmAir()
        {
            var calm = Run(Truss());
            var windy = Run(Truss(), new Challenge { WindSpeed = 25f });
            Assert.That(windy.PeakUtilization, Is.GreaterThan(calm.PeakUtilization));
        }

        [Test]
        public void DeadLoadPreview_FlagsUnsupportedPieces()
        {
            var d = Room();
            var undo = new UndoStack();
            Line(d, undo, P(0f, Top, -Side), P(0.3f, Top, -Side));
            var st = BridgeStructure.Compile(d, new Challenge());
            var preview = PreviewResult.Compute(st);
            Assert.That(preview.Detached, Has.All.False);
            Assert.That(preview.Utilization[0], Is.GreaterThan(0f));
        }
    }
}
