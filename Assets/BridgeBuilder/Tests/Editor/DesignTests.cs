using System;
using System.Collections.Generic;
using System.Numerics;
using BridgeBuilder.Core;
using NUnit.Framework;
using static BridgeBuilder.Tests.TestBridges;

namespace BridgeBuilder.Tests
{
    /// <summary>Recognizer, snapping, fills, roads, editing and undo.</summary>
    public class DesignTests
    {
        static List<Vector3> Noisy(Func<float, Vector3> curve, int count = 60, float noise = 0.003f, int seed = 1)
        {
            var rng = new Random(seed);
            var pts = new List<Vector3>();
            for (int i = 1; i <= count; i++)
            {
                Vector3 p = curve(i / (float)count);
                pts.Add(p + new Vector3((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * (2 * noise));
            }
            return pts;
        }

        static RecognizedStroke Recognize(BridgeDesign d, Vector3 start, List<Vector3> raw, out string error)
        {
            int node = d.FindNode(start, 0.01f);
            StrokeRecognizer.TryRecognize(d, node, raw, out var stroke, out error);
            return stroke;
        }

        [Test]
        public void ShakyStraightStroke_BecomesALine_WithEqualTenCentimeterSegments()
        {
            var d = Room();
            Vector3 a = P(0f, Top, -Side), b = P(0.52f, Top + 0.1f, -Side);
            var stroke = Recognize(d, a, Noisy(t => Vector3.Lerp(a, b, t)), out string error);
            Assert.That(stroke, Is.Not.Null, error);
            Assert.That(stroke.Shape, Is.EqualTo(StrokeShape.Line));
            Assert.That(stroke.Points.Count, Is.EqualTo(6), "0.53 m rounds to 5 segments");
            float first = Vector3.Distance(stroke.Points[0], stroke.Points[1]);
            float last = Vector3.Distance(stroke.Points[4], stroke.Points[5]);
            Assert.That(first, Is.EqualTo(last).Within(1e-4f));
        }

        [Test]
        public void CurvedStroke_BecomesAnArc()
        {
            var d = Room();
            Vector3 a = P(0f, Top, -Side);
            // A half circle of radius 0.25 m rising over the gap.
            var raw = Noisy(t => new Vector3(0.25f - 0.25f * MathF.Cos(MathF.PI * t), Top + 0.25f * MathF.Sin(MathF.PI * t), -Side));
            var stroke = Recognize(d, a, raw, out string error);
            Assert.That(stroke, Is.Not.Null, error);
            Assert.That(stroke.Shape, Is.EqualTo(StrokeShape.Arc));
            foreach (var p in stroke.Points)
                Assert.That(Vector3.Distance(p, P(0.25f, Top, -Side)), Is.EqualTo(0.25f).Within(0.02f));
        }

        [Test]
        public void ZigzagStroke_BecomesAPolyline()
        {
            var d = Room();
            Vector3 a = P(0f, Top, -Side);
            var corners = new[] { a, P(0.2f, Top + 0.2f, -Side), P(0.4f, Top, -Side), P(0.6f, Top + 0.2f, -Side) };
            var raw = new List<Vector3>();
            for (int c = 0; c + 1 < corners.Length; c++)
                raw.AddRange(Noisy(t => Vector3.Lerp(corners[c], corners[c + 1], t), 20, 0.002f, c));
            var stroke = Recognize(d, a, raw, out string error);
            Assert.That(stroke, Is.Not.Null, error);
            Assert.That(stroke.Shape, Is.EqualTo(StrokeShape.Polyline));
        }

        [Test]
        public void Scribble_IsRejected()
        {
            var d = Room();
            Vector3 a = P(0f, Top, -Side);
            var raw = Noisy(t => a + new Vector3(0.15f * MathF.Sin(t * 40f), 0.1f * MathF.Cos(t * 31f), 0.1f * MathF.Sin(t * 23f)), 120, 0.01f);
            Assert.That(Recognize(d, a, raw, out _), Is.Null);
        }

        [Test]
        public void LineEnd_SnapsToANearbyNode()
        {
            var d = Room();
            Vector3 a = P(0f, Top, -Side);
            Vector3 nearFar = P(1.02f, Top + 0.01f, -Side);
            var stroke = Recognize(d, a, Noisy(t => Vector3.Lerp(a, nearFar, t), 60, 0.001f), out string error);
            Assert.That(stroke, Is.Not.Null, error);
            Assert.That(stroke.ExistingNodes[stroke.ExistingNodes.Count - 1], Is.EqualTo(d.FindNode(P(1f, Top, -Side), 0.01f)));
        }

        [Test]
        public void Fill_IsTriangulated_WithHiddenBracing()
        {
            var d = BareDeck();
            var fill = d.Fills[0];
            Assert.That(fill.Triangles.Count / 3, Is.EqualTo(fill.Boundary.Count - 2));
            Assert.That(fill.Bracing.Count, Is.GreaterThan(0));
            Assert.That(fill.SlopeDeg, Is.LessThan(1f));
            Assert.That(BridgeEditing.ValidateRoads(d).Ok, Is.True, BridgeEditing.ValidateRoads(d).Message);
        }

        [Test]
        public void NonPlanarOutline_IsRejected()
        {
            var d = Room();
            var undo = new UndoStack();
            Line(d, undo, P(0f, Top, -Side), P(0.5f, Top, -Side));
            Line(d, undo, P(0.5f, Top, -Side), P(0.5f, Top + 0.3f, Side));
            Line(d, undo, P(0.5f, Top + 0.3f, Side), P(0f, Top, Side));
            var corners = new List<int>
            {
                d.FindNode(P(0f, Top, -Side), 0.01f),
                d.FindNode(P(0.5f, Top, -Side), 0.01f),
                d.FindNode(P(0.5f, Top + 0.3f, Side), 0.01f),
                d.FindNode(P(0f, Top, Side), 0.01f),
            };
            // Four corners, one raised 0.3 m: not flat.
            Assert.That(BridgeEditing.TryBuildFill(d, corners, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("flat"));
        }

        [Test]
        public void RoadThatStopsShort_FailsValidation()
        {
            var d = Room();
            var undo = new UndoStack();
            Line(d, undo, P(0f, Top, -Side), P(0.5f, Top, -Side));
            Line(d, undo, P(0f, Top, Side), P(0.5f, Top, Side));
            Line(d, undo, P(0.5f, Top, -Side), P(0.5f, Top, Side));
            var corners = new List<int>
            {
                d.FindNode(P(0f, Top, -Side), 0.01f), d.FindNode(P(0.5f, Top, -Side), 0.01f),
                d.FindNode(P(0.5f, Top, Side), 0.01f), d.FindNode(P(0f, Top, Side), 0.01f),
            };
            Assert.That(BridgeEditing.TryBuildFill(d, corners, out var fill, out string error), Is.True, error);
            fill.IsRoad = true;
            undo.Do(d, new AddFillEdit(fill));
            var report = BridgeEditing.ValidateRoads(d);
            Assert.That(report.Ok, Is.False);
            Assert.That(report.ReachesStart, Is.True);
            Assert.That(report.ReachesFar, Is.False);
        }

        [Test]
        public void DeletingALineOthersAttachTo_IsBlocked_AndUndoRestoresEverything()
        {
            var d = Room();
            var undo = new UndoStack();
            Line(d, undo, P(0f, Top, -Side), P(0.5f, Top, -Side));
            Line(d, undo, P(0.3f, Top, -Side), P(0.3f, Top + 0.2f, -Side));
            int first = d.Lines[0].Id, second = d.Lines[1].Id;

            Assert.That(BridgeEditing.LineDependents(d, first), Is.EquivalentTo(new[] { second }));
            Assert.That(BridgeEditing.RemoveLine(d, first), Is.Null);

            int nodes = d.Nodes.Count;
            undo.Do(d, BridgeEditing.RemoveLine(d, second));
            Assert.That(d.Lines.Count, Is.EqualTo(1));
            Assert.That(undo.Undo(d), Is.True);
            Assert.That(d.Lines.Count, Is.EqualTo(2));
            Assert.That(d.Nodes.Count, Is.EqualTo(nodes));
        }

        [Test]
        public void Symmetry_MirrorsAcrossTheCenterline()
        {
            var d = Room();
            var undo = new UndoStack();
            var stroke = Line(d, undo, P(0f, Top, -Side), P(0.3f, Top + 0.2f, -Side));
            Assert.That(BridgeEditing.TryMirror(d, stroke, out var mirror), Is.True);
            Assert.That(mirror.Points[0].Z, Is.EqualTo(Side).Within(1e-4f));
            Assert.That(mirror.Points[mirror.Points.Count - 1].Z, Is.EqualTo(Side).Within(1e-4f));
        }

        [Test]
        public void Budget_CountsLinesAndFills()
        {
            var d = BareDeck();
            float lines = 2f * d.Settings.CostPerMeter;
            float fill = 0.2f * d.Settings.CostPerSquareMeter;
            Assert.That(d.Cost, Is.EqualTo(lines + fill).Within(1f));
        }

        [Test]
        public void Triangulation_HandlesCollinearPoints()
        {
            var poly = new List<Vector2>();
            for (int i = 0; i <= 10; i++) poly.Add(new Vector2(i * 0.1f, 0f));
            for (int i = 10; i >= 0; i--) poly.Add(new Vector2(i * 0.1f, 0.2f));
            var tris = Geometry.Triangulate(poly);
            Assert.That(tris.Count / 3, Is.EqualTo(poly.Count - 2));
            float area = 0f;
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector2 a = poly[tris[t]], b = poly[tris[t + 1]], c = poly[tris[t + 2]];
                area += Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) * 0.5f;
            }
            Assert.That(area, Is.EqualTo(0.2f).Within(1e-4f));
        }
    }
}
