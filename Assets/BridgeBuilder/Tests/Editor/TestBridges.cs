using System.Collections.Generic;
using System.Numerics;
using BridgeBuilder.Core;
using NUnit.Framework;

namespace BridgeBuilder.Tests
{
    /// <summary>
    /// Builds test bridges the way a player would: lines drawn from existing nodes through the recognizer.
    /// The room: two tables 0.75 m high, 1 m apart, each marked edge 0.2 m wide, floor at 0.
    /// At 20x that is a 20 m span, 4 m road, 15 m above the ground.
    /// </summary>
    static class TestBridges
    {
        public const float Top = 0.75f;
        public const float Side = 0.1f;

        public static BridgeDesign Room(BridgeSettings s = null)
        {
            s ??= new BridgeSettings();
            s.GroundGridSpacing = 0.1f;   // ground dots every 0.1 m, so tests can name them
            return BridgeDesign.Create(s,
                new Vector3(0f, Top, -Side), new Vector3(0f, Top, Side),
                new Vector3(1f, Top, -Side), new Vector3(1f, Top, Side), 0f);
        }

        /// <summary>Draws a straight stroke from the node at 'from' to 'to'. Returns the stroke it added.</summary>
        public static RecognizedStroke Line(BridgeDesign d, UndoStack undo, Vector3 from, Vector3 to)
        {
            int start = d.FindNode(from, 0.01f);
            Assert.That(start, Is.GreaterThan(0), $"no node at {from}");
            var raw = new List<Vector3>();
            for (int i = 1; i <= 20; i++)
                raw.Add(Vector3.Lerp(from, to, i / 20f));
            Assert.That(StrokeRecognizer.TryRecognize(d, start, raw, out var stroke, out string error), Is.True, error);
            undo.Do(d, BridgeEditing.CreateLine(d, stroke));
            return stroke;
        }

        public static Vector3 P(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>The flat deck between the two bottom chords, flagged as road.</summary>
        public static void Deck(BridgeDesign d, UndoStack undo)
        {
            var corners = new List<int>
            {
                d.FindNode(P(0f, Top, -Side), 0.01f),
                d.FindNode(P(1f, Top, -Side), 0.01f),
                d.FindNode(P(1f, Top, Side), 0.01f),
                d.FindNode(P(0f, Top, Side), 0.01f),
            };
            Assert.That(BridgeEditing.TryBuildFill(d, corners, out var fill, out string error), Is.True, error);
            fill.IsRoad = true;
            undo.Do(d, new AddFillEdit(fill));
        }

        /// <summary>Two straight beams from surface to surface with a road deck: the bare span.</summary>
        public static BridgeDesign BareDeck(BridgeSettings s = null)
        {
            var d = Room(s);
            var undo = new UndoStack();
            foreach (float z in new[] { -Side, Side })
                Line(d, undo, P(0f, Top, z), P(1f, Top, z));
            Deck(d, undo);
            return d;
        }

        /// <summary>A Pratt-style truss on each side, 0.2 m deep, panels of 0.2 m, plus the road deck.</summary>
        public static BridgeDesign Truss(BridgeSettings s = null)
        {
            var d = Room(s);
            var undo = new UndoStack();
            const float h = Top + 0.2f;
            foreach (float z in new[] { -Side, Side })
            {
                Line(d, undo, P(0f, Top, z), P(1f, Top, z));
                Line(d, undo, P(0f, Top, z), P(0.2f, h, z));
                Line(d, undo, P(0.2f, h, z), P(0.8f, h, z));
                Line(d, undo, P(0.8f, h, z), P(1f, Top, z));
                foreach (float x in new[] { 0.2f, 0.4f, 0.6f, 0.8f })
                    Line(d, undo, P(x, Top, z), P(x, h, z));
                Line(d, undo, P(0.2f, h, z), P(0.4f, Top, z));
                Line(d, undo, P(0.4f, h, z), P(0.5f, Top, z));
                Line(d, undo, P(0.6f, h, z), P(0.5f, Top, z));
                Line(d, undo, P(0.8f, h, z), P(0.6f, Top, z));
            }
            Deck(d, undo);
            return d;
        }

        public static BridgeTestRun Run(BridgeDesign d, Challenge c = null, float seconds = 30f)
        {
            var run = new BridgeTestRun(BridgeStructure.Compile(d, c ?? new Challenge()));
            run.RunToEnd(seconds);
            TestContext.WriteLine($"{run.Result}: {run.Message}; broken {run.BrokenCount}; peak {run.PeakUtilization:F2}; " +
                                  $"time {run.Time:F1}s; equations {run.Frame.Equations}, band {run.Frame.Bandwidth}");
            return run;
        }
    }
}
