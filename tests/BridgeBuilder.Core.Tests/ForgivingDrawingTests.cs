using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>
    /// Forgiving drawing on top of FreeDraw: a stroke FreeDraw can't read (a squiggle) becomes a straight beam
    /// between its ends; only a stroke too short to be a beam is still refused.
    /// </summary>
    public class ForgivingDrawingTests
    {
        static Vec3 P(double x, double y, double z = 0) => new Vec3(x, y, z);

        static StrokeBuilder.Context Ctx() => new StrokeBuilder.Context { Pads = Rooms.Pads() };

        static FreeDraw.Context Draw(BridgeModel m) => StrokeBuilder.DrawContext(m, Ctx());

        [Fact]
        public void Scribble_BecomesAStraightBeamBetweenItsEnds()
        {
            var m = Rooms.Model();
            var stroke = ShapeResolverTests.Scribble(7, P(0.3, 1.2, 0));
            Assert.Empty(FreeDraw.Build(stroke, Draw(m), out _, out var reason));
            Assert.Equal(RejectionReason.Unrecognized, reason);

            var shaped = StrokeBuilder.Shape(stroke, m, Ctx());

            Assert.True(shaped.Straightened);
            var chain = Assert.Single(shaped.Chains);
            Assert.True(Vec3.Distance(stroke[0], chain[0].Position) < 0.01);
            Assert.True(Vec3.Distance(stroke[stroke.Count - 1], chain[chain.Count - 1].Position) < 0.01);
            var a = chain[0].Position;
            Assert.All(chain, v => Assert.True(Geometry.DistanceToSegment(v.Position, a, chain[chain.Count - 1].Position) < 1e-6));
            Assert.True(StrokeBuilder.Build(m, shaped, Ctx()).Ok);
        }

        [Fact]
        public void MessyStrokes_AreNeverThrownAway()
        {
            var spiral = Enumerable.Range(0, 601).Select(i =>
            {
                double t = i / 600.0, a = t * 4 * System.Math.PI, r = 0.05 + 0.2 * t;
                return P(0.5 + r * System.Math.Cos(a), 1 + r * System.Math.Sin(a), 0.1);
            }).ToList();
            var helix = Enumerable.Range(0, 401).Select(i =>
            {
                double t = i / 400.0, a = t * 3 * System.Math.PI;
                return P(0.15 * System.Math.Cos(a), 1 + 0.15 * System.Math.Sin(a), 0.4 * t);
            }).ToList();

            foreach (var stroke in new[] { spiral, helix, ShapeResolverTests.Scribble(3, P(0.2, 1.1, 0)) })
                Assert.False(StrokeBuilder.Shape(stroke, Rooms.Model(), Ctx()).IsEmpty);
        }

        [Fact]
        public void RecognisedShape_IsNotStraightened()
        {
            var m = Rooms.Model();
            var stroke = StrokeBuilderTests.Stroke(P(0, 1), P(0.5, 1.1));
            var shaped = StrokeBuilder.Shape(stroke, m, Ctx());

            Assert.False(shaped.Straightened);
            Assert.Equal(FreeDraw.Build(stroke, Draw(m), out _).Single().Select(v => v.Position),
                         shaped.Chains.Single().Select(v => v.Position));
        }

        [Fact]
        public void TooShortStrokes_AreStillRefused()
        {
            var m = Rooms.Model();
            Assert.True(StrokeBuilder.Shape(new List<Vec3>(), m, Ctx()).IsEmpty);
            Assert.True(StrokeBuilder.Shape(new[] { P(0, 1) }, m, Ctx()).IsEmpty);
            Assert.True(StrokeBuilder.Shape(StrokeBuilderTests.Stroke(P(0, 1), P(0.01, 1)), m, Ctx()).IsEmpty);
        }

        [Fact]
        public void ScribbleThatEndsWhereItStarted_IsRefused()
        {
            var loop = ShapeResolverTests.Scribble(11, P(0.3, 1.2, 0));
            loop.Add(loop[0] + P(0.005, 0));                   // closes on itself: no beam between the ends
            Assert.True(StrokeBuilder.Shape(loop, Rooms.Model(), Ctx()).IsEmpty);
        }

        [Fact]
        public void StraightenedScribble_JoinsAnExistingJoint()
        {
            var m = Rooms.Model();
            int joint = m.AddFixedNode(P(0.3, 1.2, 0));
            var stroke = ShapeResolverTests.Scribble(7, P(0.31, 1.2, 0));   // starts 1 cm from the joint

            var shaped = StrokeBuilder.Shape(stroke, m, Ctx());

            Assert.True(shaped.Straightened);
            Assert.Equal(joint, shaped.Chains[0][0].ExistingId);
            Assert.True(StrokeBuilder.Build(m, shaped, Ctx()).Ok);
        }
    }
}
