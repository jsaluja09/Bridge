using System;
using System.Collections.Generic;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W1-07: stroke recognizer in free 3D.</summary>
    public class StrokeRecognizerTests
    {
        static readonly StrokeRecognizer Recognizer = new StrokeRecognizer();
        static RecognizerTolerances Tol => StrokeRecognizer.DefaultTolerances;
        static Vec3 V(double x, double y, double z = 0) => new Vec3(x, y, z);

        [Fact]
        public void DefaultTolerances_MatchConstants()
        {
            var t = Tol;
            Assert.Equal(0.02, t.SnapStartToleranceMeters);
            Assert.Equal(0.01, t.LineResidualMeters);
            Assert.Equal(0.015, t.PlaneResidualMeters);
            Assert.Equal(0.015, t.ArcResidualMeters);
            Assert.Equal(25, t.CornerAngleDegrees);
            Assert.Equal(10, t.ArcSweepMinDegrees);
            Assert.Equal(350, t.ArcSweepMaxDegrees);
        }

        [Fact]
        public void Reject_Degenerate()
        {
            Assert.Equal(RejectionReason.Degenerate, Recognizer.Recognize(new[] { V(0, 0) }, new[] { V(0, 0) }, Tol).RejectionReason);
            Assert.Equal(RejectionReason.Degenerate, Recognizer.Recognize(new[] { V(0, 0), V(0, 0) }, new[] { V(0, 0) }, Tol).RejectionReason);
        }

        [Fact]
        public void Reject_WhenStartIsNotASnapPoint() =>
            Assert.Equal(RejectionReason.SnapStart, Recognizer.Recognize(new[] { V(0.05, 0), V(1, 0) }, new[] { V(0, 0) }, Tol).RejectionReason);

        [Fact]
        public void Accept_StraightLine() =>
            Assert.Equal(StrokeKind.Line, Recognizer.Recognize(new[] { V(0, 0), V(0.25, 0), V(0.5, 0), V(0.75, 0), V(1, 0) }, new[] { V(0, 0) }, Tol).Kind);

        [Fact]
        public void Accept_ArcInAPlane()
        {
            var pts = new List<Vec3>();
            for (int deg = 0; deg <= 90; deg += 5)
                pts.Add(V(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180)));
            Assert.Equal(StrokeKind.Arc, Recognizer.Recognize(pts, new[] { V(1, 0) }, Tol).Kind);
            var narrow = Tol;
            narrow.ArcSweepMinDegrees = 10;
            narrow.ArcSweepMaxDegrees = 20;
            Assert.Equal(RejectionReason.Unrecognized, Recognizer.Recognize(pts, new[] { V(1, 0) }, narrow).RejectionReason);
        }

        [Fact]
        public void Reject_NonPlanarScribble() =>
            Assert.Equal(RejectionReason.NonPlanar,
                Recognizer.Recognize(new[] { V(0, 0), V(1, 0), V(0.5, 0.866), V(0.5, 0.3, 0.8) }, new[] { V(0, 0) }, Tol).RejectionReason);

        [Fact]
        public void Accept_TrianglePolyline()
        {
            double h = Math.Sqrt(3) / 2;
            var pts = new[] { V(0, 0), V(0.5, 0), V(1, 0), V(0.75, h / 2), V(0.5, h), V(0.25, h / 2), V(0, 0) };
            Assert.Equal(StrokeKind.Polyline, Recognizer.Recognize(pts, new[] { V(0, 0) }, Tol).Kind);
        }

        [Fact]
        public void Accept_ZigzagPolyline() =>
            Assert.Equal(StrokeKind.Polyline,
                Recognizer.Recognize(new[] { V(0, 0), V(0.5, 0), V(1, 0), V(1, 0.5), V(1, 1), V(1.5, 1), V(2, 1) }, new[] { V(0, 0) }, Tol).Kind);

        [Fact]
        public void Accept_WPolyline() =>
            Assert.Equal(StrokeKind.Polyline,
                Recognizer.Recognize(new[] { V(0, 1), V(0.25, 0.5), V(0.5, 0), V(0.75, 0.5), V(1, 1), V(1.25, 0.5), V(1.5, 0), V(1.75, 0.5), V(2, 1) },
                    new[] { V(0, 1) }, Tol).Kind);

        [Fact]
        public void Reject_UnrecognizedKnot()
        {
            var pts = new List<Vec3>();
            for (int i = 0; i <= 80; i++)
                pts.Add(V(i / 80.0, 0.25 * Math.Sin(2 * Math.PI * i / 80)));
            var tol = Tol;
            tol.LineResidualMeters = 0.01;
            tol.ArcResidualMeters = 0.01;
            tol.CornerAngleDegrees = 25;
            Assert.Equal(RejectionReason.Unrecognized, Recognizer.Recognize(pts, new[] { V(0, 0) }, tol).RejectionReason);
        }

        [Fact]
        public void Noise_SeededLine_StillRecognizedAsLine()
        {
            var rng = new Random(12345);
            var pts = new List<Vec3>();
            for (int i = 0; i <= 10; i++)
            {
                double x = i * 0.1 + (rng.NextDouble() * 2 - 1) * 0.001;
                double y = (rng.NextDouble() * 2 - 1) * 0.001;
                double z = (rng.NextDouble() * 2 - 1) * 0.001;
                pts.Add(V(x, y, z));
            }
            Assert.Equal(StrokeKind.Line, Recognizer.Recognize(pts, new[] { V(0, 0) }, Tol).Kind);
        }
    }
}
