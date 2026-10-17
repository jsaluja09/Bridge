using System.Numerics;
using BridgeBuilder.Core;
using NUnit.Framework;

namespace BridgeBuilder.Tests
{
    /// <summary>
    /// The bridges every tuning change must still get right. Gap is 1 m, both surfaces level.
    /// Node indices: 0 = start back anchor, 1 = start tap, 2 = far tap, 3 = far back anchor.
    /// </summary>
    public class BridgeFixtureTests
    {
        BridgeSettings _settings;
        BridgeFrame _frame;

        [SetUp]
        public void SetUp()
        {
            _settings = new BridgeSettings();
            _frame = new BridgeFrame(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, _settings.RoadWidth);
        }

        BridgeDesign NewDesign() => new BridgeDesign(_settings, 1f, 0f);

        /// <summary>Draws a line from the node at "from" to "to", which must snap to a node or the grid.</summary>
        static void Line(BridgeDesign d, float fx, float fy, float tx, float ty)
        {
            int start = d.FindNode(new Vector2(fx, fy), 0.01f);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no node at ({fx}, {fy})");
            Assert.That(d.TryAddLine(start, new Vector2(tx, ty)), Is.EqualTo(AddLineResult.Added));
        }

        BridgeTestRun Run(BridgeDesign d)
        {
            var run = new BridgeTestRun(d, _frame, _settings);
            run.RunToEnd();
            TestContext.WriteLine($"{run.Result}: {run.Message}, broken {run.Solver.BrokenCount}, " +
                                  $"peak utilization {run.Solver.PeakUtilization:F2}, time {run.Time:F1}s");
            return run;
        }

        static BridgeDesign Truss(BridgeDesign d)
        {
            // Pratt truss, 4 panels of 0.25 m, 0.2 m deep.
            for (int i = 0; i < 4; i++)
                Line(d, i * 0.25f, 0f, (i + 1) * 0.25f, 0f);
            Line(d, 0f, 0f, 0.25f, 0.2f);
            Line(d, 0.25f, 0.2f, 0.5f, 0.2f);
            Line(d, 0.5f, 0.2f, 0.75f, 0.2f);
            Line(d, 0.75f, 0.2f, 1f, 0f);
            Line(d, 0.25f, 0.2f, 0.25f, 0f);
            Line(d, 0.5f, 0.2f, 0.5f, 0f);
            Line(d, 0.75f, 0.2f, 0.75f, 0f);
            Line(d, 0.25f, 0.2f, 0.5f, 0f);
            Line(d, 0.75f, 0.2f, 0.5f, 0f);
            return d;
        }

        [Test]
        public void BareCantilever_Fails()
        {
            var d = NewDesign();
            Line(d, 0f, 0f, 0.5f, 0f);
            Assert.That(Run(d).Result, Is.EqualTo(RunResult.Failed));
        }

        [Test]
        public void TwoBareLinesMeetingMidSpan_Fail()
        {
            var d = NewDesign();
            Line(d, 0f, 0f, 0.5f, 0f);
            Line(d, 0.5f, 0f, 1f, 0f);
            Assert.That(Run(d).Result, Is.EqualTo(RunResult.Failed));
        }

        [Test]
        public void SingleLongPlank_FailsInBending()
        {
            var d = NewDesign();
            Line(d, 0f, 0f, 1f, 0f);
            Assert.That(Run(d).Result, Is.EqualTo(RunResult.Failed));
        }

        [Test]
        public void KingPostTruss_Holds_ButNotWithoutItsTop()
        {
            var open = NewDesign();
            Line(open, 0f, 0f, 0.5f, 0f);
            Line(open, 0.5f, 0f, 1f, 0f);
            Line(open, 0.5f, 0f, 0.5f, 0.25f);
            Assert.That(Run(open).Result, Is.EqualTo(RunResult.Failed), "open frame should fold");

            var braced = NewDesign();
            Line(braced, 0f, 0f, 0.5f, 0f);
            Line(braced, 0.5f, 0f, 1f, 0f);
            Line(braced, 0.5f, 0f, 0.5f, 0.25f);
            Line(braced, 0f, 0f, 0.5f, 0.25f);
            Line(braced, 0.5f, 0.25f, 1f, 0f);
            Assert.That(Run(braced).Result, Is.EqualTo(RunResult.Passed), "triangles should hold");
        }

        [Test]
        public void PrattTruss_HoldsWithMargin()
        {
            var run = Run(Truss(NewDesign()));
            Assert.That(run.Result, Is.EqualTo(RunResult.Passed));
            Assert.That(run.Solver.BrokenCount, Is.EqualTo(0));
            Assert.That(run.Solver.PeakUtilization, Is.LessThan(0.85f));
        }

        [Test]
        public void HeavyCarBreaksTheTruss_WithoutNaN()
        {
            _settings.CarMass = 40f;
            var run = new BridgeTestRun(Truss(NewDesign()), _frame, _settings);
            for (int i = 0; i < 600; i++)
                run.Step();
            Assert.That(run.Result, Is.EqualTo(RunResult.Failed));
            Assert.That(run.Solver.BrokenCount, Is.GreaterThan(0));
            Assert.That(run.Solver.Exploded, Is.False);
        }

        [Test]
        public void LongMembers_BuckleBeforeShortOnes()
        {
            Assert.That(_settings.CompressionCapacity(1f), Is.LessThan(_settings.CompressionCapacity(0.25f)));
        }

        [Test]
        public void Undo_RemovesLineAndItsNewNode()
        {
            var d = NewDesign();
            int nodes = d.Nodes.Count;
            Line(d, 0f, 0f, 0.3f, 0.1f);
            Assert.That(d.Nodes.Count, Is.EqualTo(nodes + 1));
            Assert.That(d.Undo(), Is.True);
            Assert.That(d.Nodes.Count, Is.EqualTo(nodes));
            Assert.That(d.Members.Count, Is.EqualTo(0));
        }
    }
}
