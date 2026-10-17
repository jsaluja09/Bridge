using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class StrokeRecognizerTests
    {
        private const double ExactPrecision = 1e-9;

        private static readonly Vec3[] OriginSnap = { Vec3.Zero };

        [Fact]
        public void DefaultTolerances_MatchConstants()
        {
            RecognizerTolerances defaults = StrokeRecognizer.DefaultTolerances;
            Assert.Equal(Constants.Recognition.SnapStartToleranceMeters, defaults.SnapStartToleranceMeters);
            Assert.Equal(Constants.Recognition.LineResidualMeters, defaults.LineResidualMeters);
            Assert.Equal(Constants.Recognition.PlaneResidualMeters, defaults.PlaneResidualMeters);
            Assert.Equal(Constants.Recognition.ArcResidualMeters, defaults.ArcResidualMeters);
            Assert.Equal(Constants.Recognition.CornerAngleDegrees, defaults.CornerAngleDegrees);
            Assert.Equal(Constants.Recognition.ArcSweepMinDegrees, defaults.ArcSweepMinDegrees);
            Assert.Equal(Constants.Recognition.ArcSweepMaxDegrees, defaults.ArcSweepMaxDegrees);

            Assert.Equal(0.02, Constants.Recognition.SnapStartToleranceMeters);
            Assert.Equal(0.01, Constants.Recognition.LineResidualMeters);
            Assert.Equal(0.015, Constants.Recognition.PlaneResidualMeters);
            Assert.Equal(0.015, Constants.Recognition.ArcResidualMeters);
            Assert.Equal(25.0, Constants.Recognition.CornerAngleDegrees);
            Assert.Equal(10.0, Constants.Recognition.ArcSweepMinDegrees);
            Assert.Equal(350.0, Constants.Recognition.ArcSweepMaxDegrees);

            string[] expectedNames =
            {
                "ArcResidualMeters", "ArcSweepMaxDegrees", "ArcSweepMinDegrees", "CornerAngleDegrees",
                "LineResidualMeters", "PlaneResidualMeters", "SnapStartToleranceMeters",
            };
            Assert.Equal(
                expectedNames,
                typeof(Constants.Recognition).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal));

            Vec3[] almostStraight = { Vec3.Zero, new Vec3(0.5, 0.005, 0), new Vec3(1, 0, 0) };
            var recognizer = new StrokeRecognizer();
            RecognitionResult viaWrapper = recognizer.Recognize(almostStraight, OriginSnap);
            RecognitionResult explicitDefaults = recognizer.Recognize(almostStraight, OriginSnap, StrokeRecognizer.DefaultTolerances);
            Assert.Equal(StrokeKind.Line, viaWrapper.Kind);
            Assert.Equal(explicitDefaults.Kind, viaWrapper.Kind);
            Assert.Equal(explicitDefaults.Vertices, viaWrapper.Vertices);
        }

        [Fact]
        public void Reject_Degenerate()
        {
            Vec3 point = new Vec3(0.3, 0.2, 0.1);
            Vec3[] snaps = { point };
            AssertRejected(Recognize(new[] { point }, snaps, IssueTolerances()), RejectionReason.Degenerate);
            AssertRejected(Recognize(new[] { point, point }, snaps, IssueTolerances()), RejectionReason.Degenerate);
            AssertRejected(Recognize(Array.Empty<Vec3>(), snaps, IssueTolerances()), RejectionReason.Degenerate);
        }

        [Fact]
        public void Reject_WhenStartIsNotASnapPoint()
        {
            Vec3[] stroke = { new Vec3(0.05, 0, 0), new Vec3(0.5, 0, 0), new Vec3(1, 0, 0) };
            AssertRejected(Recognize(stroke, OriginSnap, IssueTolerances()), RejectionReason.SnapStart);
            AssertRejected(Recognize(stroke, Array.Empty<Vec3>(), IssueTolerances()), RejectionReason.SnapStart);
        }

        [Fact]
        public void Accept_StartExactlyAtSnapTolerance()
        {
            Vec3[] snaps = { new Vec3(5, 5, 5), new Vec3(0.1, 0.2, 0.3) };
            Vec3[] stroke = { new Vec3(0.113, 0.21, 0.3), new Vec3(0.613, 0.21, 0.3) };
            double distance = Vec3.Distance(stroke[0], snaps[1]);

            RecognitionResult atTolerance = Recognize(stroke, snaps, WithSnapTolerance(distance));
            AssertAccepted(atTolerance, StrokeKind.Line, stroke, ExactPrecision);

            RecognitionResult justInside = Recognize(stroke, snaps, WithSnapTolerance(Math.BitDecrement(distance)));
            AssertRejected(justInside, RejectionReason.SnapStart);
        }

        [Fact]
        public void Accept_StraightLine()
        {
            Vec3[] stroke = { new Vec3(0, 0, 0), new Vec3(0.25, 0, 0), new Vec3(0.5, 0, 0), new Vec3(0.75, 0, 0), new Vec3(1, 0, 0) };
            RecognitionResult result = Recognize(stroke, OriginSnap, IssueTolerances());
            AssertAccepted(result, StrokeKind.Line, new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0) }, ExactPrecision);
        }

        [Fact]
        public void Accept_ArcInAPlane()
        {
            Vec3[] stroke = QuarterCircle();
            Vec3[] snaps = { new Vec3(1, 0, 0) };
            double half = Math.Sqrt(0.5);

            RecognitionResult result = Recognize(stroke, snaps, IssueTolerances());
            AssertAccepted(result, StrokeKind.Arc, new[] { new Vec3(1, 0, 0), new Vec3(half, half, 0), new Vec3(0, 1, 0) }, ExactPrecision);

            var narrowSweep = new RecognizerTolerances(0.02, 0.01, 0.015, 0.015, 25.0, 10.0, 20.0);
            AssertRejected(Recognize(stroke, snaps, narrowSweep), RejectionReason.Unrecognized);
        }

        [Fact]
        public void Reject_NonPlanarScribble()
        {
            Vec3[] stroke = { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0.5, 0.866, 0), new Vec3(0.5, 0.3, 0.8) };
            var tolerances = new RecognizerTolerances(0.02, 0.01, 0.015, 0.015, 25.0, 10.0, 350.0);
            AssertRejected(Recognize(stroke, OriginSnap, tolerances), RejectionReason.NonPlanar);
        }

        [Fact]
        public void Accept_TrianglePolyline()
        {
            Vec3 a = new Vec3(0, 0, 0);
            Vec3 b = new Vec3(1, 0, 0);
            Vec3 c = new Vec3(0.5, Math.Sqrt(3.0) / 2.0, 0);
            Vec3[] stroke = { a, Midpoint(a, b), b, Midpoint(b, c), c, Midpoint(c, a), a };
            RecognitionResult result = Recognize(stroke, new[] { a }, IssueTolerances());
            AssertAccepted(result, StrokeKind.Polyline, new[] { a, b, c, a }, ExactPrecision);
        }

        [Fact]
        public void Accept_ZigzagPolyline()
        {
            RecognitionResult result = Recognize(Zigzag(), OriginSnap, IssueTolerances());
            AssertAccepted(result, StrokeKind.Polyline, ZigzagCorners(), ExactPrecision);
        }

        [Fact]
        public void Accept_WPolyline()
        {
            Vec3[] stroke =
            {
                new Vec3(0, 1, 0), new Vec3(0.25, 0.5, 0), new Vec3(0.5, 0, 0), new Vec3(0.75, 0.5, 0), new Vec3(1, 1, 0),
                new Vec3(1.25, 0.5, 0), new Vec3(1.5, 0, 0), new Vec3(1.75, 0.5, 0), new Vec3(2, 1, 0),
            };
            Vec3[] expected = { new Vec3(0, 1, 0), new Vec3(0.5, 0, 0), new Vec3(1, 1, 0), new Vec3(1.5, 0, 0), new Vec3(2, 1, 0) };
            RecognitionResult result = Recognize(stroke, new[] { new Vec3(0, 1, 0) }, IssueTolerances());
            AssertAccepted(result, StrokeKind.Polyline, expected, ExactPrecision);
        }

        [Fact]
        public void Reject_UnrecognizedKnot()
        {
            var stroke = new Vec3[81];
            for (int i = 0; i <= 80; i++)
            {
                double t = i / 80.0;
                stroke[i] = new Vec3(t, 0.25 * Math.Sin(2.0 * Math.PI * t), 0);
            }

            var tolerances = new RecognizerTolerances(0.02, 0.01, 0.015, 0.01, 25.0, 10.0, 350.0);
            AssertRejected(Recognize(stroke, OriginSnap, tolerances), RejectionReason.Unrecognized);
        }

        [Fact]
        public void Noise_SeededLine_StillRecognizedAsLine()
        {
            var rng = new Random(12345);
            var stroke = new Vec3[11];
            for (int i = 0; i <= 10; i++)
            {
                double x = i * 0.1 + Noise(rng, 0.001);
                double y = Noise(rng, 0.001);
                double z = Noise(rng, 0.001);
                stroke[i] = new Vec3(x, y, z);
            }

            var tolerances = new RecognizerTolerances(0.02, 0.01, 0.015, 0.015, 25.0, 10.0, 350.0);
            RecognitionResult result = Recognize(stroke, OriginSnap, tolerances);
            AssertAccepted(result, StrokeKind.Line, new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0) }, 0.005);
        }

        [Fact]
        public void Noise_DenseZigzag_KinksMergeIntoThreeSegments()
        {
            Vec3[] corners = { new Vec3(0, 0, 0), new Vec3(0.5, 0, 0), new Vec3(0.5, 0.5, 0), new Vec3(1, 0.5, 0) };
            var rng = new Random(12345);
            var stroke = new List<Vec3>();
            for (int leg = 0; leg < 3; leg++)
            {
                for (int step = leg == 0 ? 0 : 1; step <= 50; step++)
                {
                    Vec3 clean = corners[leg] + (corners[leg + 1] - corners[leg]) * (step / 50.0);
                    double x = clean.X + Noise(rng, 0.002);
                    double y = clean.Y + Noise(rng, 0.002);
                    double z = clean.Z + Noise(rng, 0.002);
                    stroke.Add(new Vec3(x, y, z));
                }
            }

            int kinks = 0;
            for (int i = 1; i + 1 < stroke.Count; i++)
            {
                Vec3 incoming = stroke[i] - stroke[i - 1];
                Vec3 outgoing = stroke[i + 1] - stroke[i];
                double degrees = Math.Atan2(Vec3.Cross(incoming, outgoing).Length, Vec3.Dot(incoming, outgoing)) * 180.0 / Math.PI;
                if (degrees > 25.0)
                {
                    kinks++;
                }
            }

            Assert.True(kinks > 2, "The noise should add kinks beyond the two real corners, but found " + kinks + ".");
            RecognitionResult result = Recognize(stroke.ToArray(), OriginSnap, IssueTolerances());
            AssertAccepted(result, StrokeKind.Polyline, corners, 0.01);
        }

        [Fact]
        public void RepeatedSamples_AddNoCorners()
        {
            Vec3[] repeatedZigzag = Zigzag().SelectMany(p => new[] { p, p, p }).ToArray();
            RecognitionResult zigzag = Recognize(repeatedZigzag, OriginSnap, IssueTolerances());
            AssertAccepted(zigzag, StrokeKind.Polyline, ZigzagCorners(), ExactPrecision);

            Vec3[] repeatedArc = QuarterCircle().SelectMany(p => new[] { p, p }).ToArray();
            RecognitionResult arc = Recognize(repeatedArc, new[] { new Vec3(1, 0, 0) }, IssueTolerances());
            Assert.Equal(StrokeKind.Arc, arc.Kind);
        }

        [Fact]
        public void Accept_ShapesInATiltedPlane()
        {
            Mat3 rotation = RotationX(40.0) * RotationY(-25.0) * RotationZ(70.0);
            var offset = new Vec3(0.3, 1.2, -0.7);
            Func<Vec3, Vec3> place = p => rotation * p + offset;

            Vec3[] arcStroke = QuarterCircle().Select(place).ToArray();
            double half = Math.Sqrt(0.5);
            RecognitionResult arc = Recognize(arcStroke, new[] { arcStroke[0] }, IssueTolerances());
            AssertAccepted(arc, StrokeKind.Arc, new[] { new Vec3(1, 0, 0), new Vec3(half, half, 0), new Vec3(0, 1, 0) }.Select(place).ToArray(), ExactPrecision);

            Vec3[] zigzagStroke = Zigzag().Select(place).ToArray();
            RecognitionResult zigzag = Recognize(zigzagStroke, new[] { zigzagStroke[0] }, IssueTolerances());
            AssertAccepted(zigzag, StrokeKind.Polyline, ZigzagCorners().Select(place).ToArray(), ExactPrecision);

            Vec3[] lineStroke = { place(Vec3.Zero), place(new Vec3(0.4, 0, 0)), place(new Vec3(0.9, 0, 0)) };
            RecognitionResult line = Recognize(lineStroke, new[] { lineStroke[0] }, IssueTolerances());
            AssertAccepted(line, StrokeKind.Line, new[] { lineStroke[0], lineStroke[2] }, ExactPrecision);
        }

        [Fact]
        public void Reject_NonFiniteSample_AsDegenerate()
        {
            Vec3[] infiniteMiddle = { Vec3.Zero, new Vec3(double.PositiveInfinity, 0, 0), new Vec3(1, 0, 0) };
            AssertRejected(Recognize(infiniteMiddle, OriginSnap, IssueTolerances()), RejectionReason.Degenerate);

            Vec3[] infiniteEnd = { Vec3.Zero, new Vec3(0.5, 0, 0), new Vec3(1, double.NegativeInfinity, 0) };
            AssertRejected(Recognize(infiniteEnd, OriginSnap, IssueTolerances()), RejectionReason.Degenerate);

            Vec3[] farSnaps = { new Vec3(double.PositiveInfinity, double.NegativeInfinity, 0) };
            AssertRejected(Recognize(Zigzag(), farSnaps, IssueTolerances()), RejectionReason.SnapStart);
        }

        [Fact]
        public void Recognize_NullArguments_Throw()
        {
            var recognizer = new StrokeRecognizer();
            Assert.Throws<ArgumentNullException>(() => recognizer.Recognize(null, OriginSnap, IssueTolerances()));
            Assert.Throws<ArgumentNullException>(() => recognizer.Recognize(Zigzag(), null, IssueTolerances()));
            Assert.Throws<ArgumentNullException>(() => recognizer.Recognize(null, OriginSnap));
        }

        private static RecognizerTolerances IssueTolerances()
        {
            return new RecognizerTolerances(0.02, 0.01, 0.015, 0.015, 25.0, 10.0, 350.0);
        }

        private static RecognizerTolerances WithSnapTolerance(double snapStartToleranceMeters)
        {
            return new RecognizerTolerances(snapStartToleranceMeters, 0.01, 0.015, 0.015, 25.0, 10.0, 350.0);
        }

        private static RecognitionResult Recognize(Vec3[] stroke, Vec3[] snapPoints, RecognizerTolerances tolerances)
        {
            return new StrokeRecognizer().Recognize(stroke, snapPoints, tolerances);
        }

        private static Vec3[] QuarterCircle()
        {
            var stroke = new Vec3[19];
            for (int i = 0; i < stroke.Length; i++)
            {
                double angle = i * 5.0 * Math.PI / 180.0;
                stroke[i] = new Vec3(Math.Cos(angle), Math.Sin(angle), 0);
            }

            return stroke;
        }

        private static Vec3[] Zigzag()
        {
            return new[]
            {
                new Vec3(0, 0, 0), new Vec3(0.5, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 0.5, 0),
                new Vec3(1, 1, 0), new Vec3(1.5, 1, 0), new Vec3(2, 1, 0),
            };
        }

        private static Vec3[] ZigzagCorners()
        {
            return new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(2, 1, 0) };
        }

        private static Vec3 Midpoint(Vec3 a, Vec3 b)
        {
            return (a + b) * 0.5;
        }

        private static double Noise(Random rng, double amplitude)
        {
            return (rng.NextDouble() * 2.0 - 1.0) * amplitude;
        }

        private static Mat3 RotationX(double degrees)
        {
            double r = degrees * Math.PI / 180.0;
            return new Mat3(1, 0, 0, 0, Math.Cos(r), -Math.Sin(r), 0, Math.Sin(r), Math.Cos(r));
        }

        private static Mat3 RotationY(double degrees)
        {
            double r = degrees * Math.PI / 180.0;
            return new Mat3(Math.Cos(r), 0, Math.Sin(r), 0, 1, 0, -Math.Sin(r), 0, Math.Cos(r));
        }

        private static Mat3 RotationZ(double degrees)
        {
            double r = degrees * Math.PI / 180.0;
            return new Mat3(Math.Cos(r), -Math.Sin(r), 0, Math.Sin(r), Math.Cos(r), 0, 0, 0, 1);
        }

        private static void AssertAccepted(RecognitionResult result, StrokeKind kind, Vec3[] expectedVertices, double precision)
        {
            Assert.Equal(kind, result.Kind);
            Assert.Equal(RejectionReason.None, result.Reason);
            Assert.True(result.IsAccepted);
            Assert.Equal(expectedVertices.Length, result.Vertices.Length);
            for (int i = 0; i < expectedVertices.Length; i++)
            {
                Vec3 actual = result.Vertices[i];
                Assert.True(double.IsFinite(actual.X) && double.IsFinite(actual.Y) && double.IsFinite(actual.Z), "Vertex " + i + " is not finite: " + actual);
                Assert.True(
                    Vec3.Distance(expectedVertices[i], actual) <= precision,
                    "Vertex " + i + " expected " + expectedVertices[i] + " but was " + actual + ".");
            }
        }

        private static void AssertRejected(RecognitionResult result, RejectionReason reason)
        {
            Assert.Equal(StrokeKind.Rejected, result.Kind);
            Assert.Equal(reason, result.Reason);
            Assert.False(result.IsAccepted);
            Assert.Empty(result.Vertices);
        }
    }
}
