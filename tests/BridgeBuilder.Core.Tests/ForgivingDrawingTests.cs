using System.Collections.Generic;
using System.Linq;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>Forgiving drawing: a stroke that fits no shape becomes a straight beam between its ends.</summary>
    public class ForgivingDrawingTests
    {
        static Vec3 P(double x, double y, double z = 0) => new Vec3(x, y, z);

        [Fact]
        public void Scribble_BecomesAStraightLineBetweenItsEnds()
        {
            var stroke = ShapeResolverTests.Scribble(7, P(0.3, 1, 0));
            Assert.False(ShapeResolver.Resolve(stroke).Valid);

            var s = ShapeResolver.ResolveOrStraighten(stroke);

            Assert.Equal(ResolvedKind.Line, s.Kind);
            Assert.True(s.Straightened);
            Assert.Equal(stroke[0], s.Points[0]);
            Assert.Equal(stroke[stroke.Count - 1], s.Points[1]);
            Assert.True(double.IsFinite(s.Residual));
        }

        [Fact]
        public void EveryInvalidReasonExceptTooShort_IsStraightened()
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
            var bowTie = new List<Vec3>();
            var corners = new[] { P(0, 0), P(0.4, 0.3), P(0.4, 0), P(0, 0.3) };
            for (int c = 0; c + 1 < corners.Length; c++)
                for (int i = 0; i < 80; i++)
                    bowTie.Add(Vec3.Lerp(corners[c], corners[c + 1], i / 80.0));
            bowTie.Add(corners[3]);

            foreach (var stroke in new[] { spiral, helix, bowTie })
            {
                Assert.False(ShapeResolver.Resolve(stroke).Valid);
                var s = ShapeResolver.ResolveOrStraighten(stroke);
                Assert.True(s.Valid);
                Assert.True(s.Straightened);
            }
        }

        [Fact]
        public void RecognisedShape_IsUnchanged()
        {
            var line = Enumerable.Range(0, 101).Select(i => Vec3.Lerp(P(0, 1), P(0.5, 1.1), i / 100.0)).ToList();
            var resolved = ShapeResolver.Resolve(line);
            var forgiving = ShapeResolver.ResolveOrStraighten(line);

            Assert.False(forgiving.Straightened);
            Assert.Equal(resolved.Kind, forgiving.Kind);
            Assert.Equal(resolved.Points, forgiving.Points);
        }

        [Fact]
        public void TooShortStrokes_StayInvalid()
        {
            Assert.Equal(InvalidReason.TooShort, ShapeResolver.ResolveOrStraighten(null).Reason);
            Assert.Equal(InvalidReason.TooShort, ShapeResolver.ResolveOrStraighten(new List<Vec3>()).Reason);
            Assert.Equal(InvalidReason.TooShort, ShapeResolver.ResolveOrStraighten(new[] { P(0, 1), P(0.01, 1) }).Reason);
        }

        [Fact]
        public void ScribbleThatEndsWhereItStarted_IsTooShort()
        {
            var loop = ShapeResolverTests.Scribble(11, P(0.3, 1, 0));
            loop.Add(loop[0] + P(0.005, 0));                   // closes back on itself: no beam to build
            var s = ShapeResolver.ResolveOrStraighten(loop);

            Assert.Equal(ResolvedKind.Invalid, s.Kind);
            Assert.Equal(InvalidReason.TooShort, s.Reason);
        }

        [Fact]
        public void StraightenedScribble_BuildsAndSnapsToAnExistingJoint()
        {
            var pads = Rooms.Pads();
            var model = Rooms.Model();
            int joint = model.AddFixedNode(P(0.3, 1, 0));
            var stroke = ShapeResolverTests.Scribble(7, P(0.31, 1, 0));   // starts 1 cm from the joint

            var shape = ShapeResolver.ResolveOrStraighten(stroke);
            var ctx = new FreeDraw.Context
            {
                Nodes = model.Nodes,
                Joined = (a, b) => model.Members.Any(m => (m.NodeA == a && m.NodeB == b) || (m.NodeA == b && m.NodeB == a)),
                Pads = pads,
            };
            var chains = FreeDraw.Build(shape.Path(), ctx, out _);

            var chain = Assert.Single(chains);
            Assert.True(chain[0].IsExisting);
            Assert.Equal(joint, chain[0].ExistingId);
            var vertices = chain.Select(v => v.IsExisting ? LineVertex.Existing(v.ExistingId) : LineVertex.New(v.Position)).ToList();
            Assert.True(model.TryAddFreeLine(vertices).Ok);
        }
    }
}
