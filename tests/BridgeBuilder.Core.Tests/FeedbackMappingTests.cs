using System;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class FeedbackMappingTests
    {
        private const double Yellow = 0.50;
        private const double Red = 0.80;
        private const int Precision = 12;

        [Fact]
        public void Color_BucketsFollowThresholds()
        {
            Assert.Equal(UtilizationColor.Green, FeedbackMapping.Color(-0.1, Yellow, Red));
            Assert.Equal(UtilizationColor.Green, FeedbackMapping.Color(0.0, Yellow, Red));
            Assert.Equal(UtilizationColor.Yellow, FeedbackMapping.Color(0.50, Yellow, Red));
            Assert.Equal(UtilizationColor.Yellow, FeedbackMapping.Color(0.79, Yellow, Red));
            Assert.Equal(UtilizationColor.Red, FeedbackMapping.Color(0.80, Yellow, Red));
            Assert.Equal(UtilizationColor.Red, FeedbackMapping.Color(1.5, Yellow, Red));
        }

        [Fact]
        public void Color_DefaultThresholds_ReadConstants()
        {
            Assert.Equal(0.50, Constants.Feedback.YellowUtilization);
            Assert.Equal(0.80, Constants.Feedback.RedUtilization);
            Assert.Equal(0.90, Constants.Feedback.PulseUtilization);
            Assert.Equal(1.0, Constants.Feedback.StripeDensityMin);
            Assert.Equal(8.0, Constants.Feedback.StripeDensityMax);
            Assert.Equal(0.05, Constants.Feedback.MaxBowAmplitudeMeters);
            Assert.Equal(80.0, Constants.Feedback.CreakPitchMinHz);
            Assert.Equal(400.0, Constants.Feedback.CreakPitchMaxHz);

            Assert.Equal(UtilizationColor.Yellow, FeedbackMapping.Color(Constants.Feedback.YellowUtilization));
            Assert.Equal(UtilizationColor.Red, FeedbackMapping.Color(Constants.Feedback.RedUtilization));
            Assert.Equal(Constants.Feedback.StripeDensityMin, FeedbackMapping.StripeDensity(0.0));
            Assert.Equal(Constants.Feedback.StripeDensityMax, FeedbackMapping.StripeDensity(1.0));
            Assert.True(FeedbackMapping.Pulse(Constants.Feedback.PulseUtilization));
            Assert.Equal(0.0, FeedbackMapping.BowAmplitude(0.0));
            Assert.Equal(Constants.Feedback.MaxBowAmplitudeMeters, FeedbackMapping.BowAmplitude(1.0));
            Assert.Equal(Constants.Feedback.CreakPitchMinHz, FeedbackMapping.CreakPitch(0.0));
            Assert.Equal(Constants.Feedback.CreakPitchMaxHz, FeedbackMapping.CreakPitch(1.0));
        }

        [Fact]
        public void StripeDensity_IncreasesWithUtilization()
        {
            Assert.Equal(1.0, FeedbackMapping.StripeDensity(0.0, 1.0, 8.0), Precision);
            Assert.Equal(1.0, FeedbackMapping.StripeDensity(-0.5, 1.0, 8.0), Precision);
            Assert.Equal(4.5, FeedbackMapping.StripeDensity(0.5, 1.0, 8.0), Precision);
            Assert.Equal(8.0, FeedbackMapping.StripeDensity(1.0, 1.0, 8.0), Precision);
            Assert.Equal(8.0, FeedbackMapping.StripeDensity(2.0, 1.0, 8.0), Precision);
            Assert.True(FeedbackMapping.StripeDensity(0.75, 1.0, 8.0) > FeedbackMapping.StripeDensity(0.25, 1.0, 8.0));
        }

        [Fact]
        public void Pulse_AtOrAboveThreshold()
        {
            Assert.False(FeedbackMapping.Pulse(0.89, 0.90));
            Assert.True(FeedbackMapping.Pulse(0.90, 0.90));
            Assert.True(FeedbackMapping.Pulse(1.0, 0.90));
        }

        [Fact]
        public void Bow_ZeroWhenUnloaded_AndMaxAtOrAboveOne()
        {
            Assert.Equal(0.0, FeedbackMapping.BowAmplitude(0.0, 0.05), Precision);
            Assert.Equal(0.0, FeedbackMapping.BowAmplitude(-0.3, 0.05), Precision);
            Assert.Equal(0.025, FeedbackMapping.BowAmplitude(0.5, 0.05), Precision);
            Assert.Equal(0.05, FeedbackMapping.BowAmplitude(1.0, 0.05), Precision);
            Assert.Equal(0.05, FeedbackMapping.BowAmplitude(1.7, 0.05), Precision);
        }

        [Fact]
        public void CreakPitch_IncreasesWithWorstUtilization()
        {
            Assert.Equal(80.0, FeedbackMapping.CreakPitch(0.0, 80.0, 400.0), Precision);
            Assert.Equal(80.0, FeedbackMapping.CreakPitch(-1.0, 80.0, 400.0), Precision);
            Assert.Equal(160.0, FeedbackMapping.CreakPitch(0.25, 80.0, 400.0), Precision);
            Assert.Equal(400.0, FeedbackMapping.CreakPitch(1.0, 80.0, 400.0), Precision);
            Assert.Equal(400.0, FeedbackMapping.CreakPitch(3.0, 80.0, 400.0), Precision);
        }

        [Fact]
        public void JointColor_IsWorstMember()
        {
            Assert.Equal(UtilizationColor.Green, FeedbackMapping.JointColor(new[] { 0.10 }, Yellow, Red));
            Assert.Equal(UtilizationColor.Yellow, FeedbackMapping.JointColor(new[] { 0.10, 0.55 }, Yellow, Red));
            Assert.Equal(UtilizationColor.Red, FeedbackMapping.JointColor(new[] { 0.10, 0.55, 0.90 }, Yellow, Red));
            Assert.Equal(UtilizationColor.Red, FeedbackMapping.JointColor(new[] { 0.90, 0.55, 0.10 }, Yellow, Red));
        }

        [Fact]
        public void FillColor_IsWorstOfBracingAndFrame()
        {
            Assert.Equal(UtilizationColor.Green, FeedbackMapping.FillColor(new[] { 0.10 }, new[] { 0.10 }, Yellow, Red));
            Assert.Equal(UtilizationColor.Yellow, FeedbackMapping.FillColor(new[] { 0.60 }, new[] { 0.20 }, Yellow, Red));
            Assert.Equal(UtilizationColor.Red, FeedbackMapping.FillColor(new[] { 0.10 }, new[] { 0.85 }, Yellow, Red));
        }

        [Fact]
        public void UtilizationNaN_Throws()
        {
            const double nan = double.NaN;
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.Color(nan));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.Color(nan, Yellow, Red));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.StripeDensity(nan));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.StripeDensity(nan, 1.0, 8.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.Pulse(nan));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.Pulse(nan, 0.90));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.BowAmplitude(nan));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.BowAmplitude(nan, 0.05));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.CreakPitch(nan));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.CreakPitch(nan, 80.0, 400.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.JointColor(new[] { nan }));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.JointColor(new[] { 0.10, nan }, Yellow, Red));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.JointColor(new[] { 0.90, nan }, Yellow, Red));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.FillColor(new[] { nan }, new[] { 0.10 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.FillColor(new[] { 0.10 }, new[] { nan }, Yellow, Red));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.FillColor(new[] { 0.90 }, new[] { 0.10, nan }, Yellow, Red));
        }

        [Fact]
        public void Worse_RanksRedOverYellowOverGreen()
        {
            Assert.Equal(UtilizationColor.Yellow, FeedbackMapping.Worse(UtilizationColor.Green, UtilizationColor.Yellow));
            Assert.Equal(UtilizationColor.Yellow, FeedbackMapping.Worse(UtilizationColor.Yellow, UtilizationColor.Green));
            Assert.Equal(UtilizationColor.Red, FeedbackMapping.Worse(UtilizationColor.Yellow, UtilizationColor.Red));
            Assert.Equal(UtilizationColor.Red, FeedbackMapping.Worse(UtilizationColor.Red, UtilizationColor.Green));
            Assert.Equal(UtilizationColor.Green, FeedbackMapping.Worse(UtilizationColor.Green, UtilizationColor.Green));
        }

        [Fact]
        public void InfiniteUtilization_ClampsToRangeEnds()
        {
            const double high = double.PositiveInfinity;
            const double low = double.NegativeInfinity;
            Assert.Equal(UtilizationColor.Red, FeedbackMapping.Color(high, Yellow, Red));
            Assert.Equal(UtilizationColor.Green, FeedbackMapping.Color(low, Yellow, Red));
            Assert.True(FeedbackMapping.Pulse(high, 0.90));
            Assert.False(FeedbackMapping.Pulse(low, 0.90));
            AssertFiniteEqual(8.0, FeedbackMapping.StripeDensity(high, 1.0, 8.0));
            AssertFiniteEqual(1.0, FeedbackMapping.StripeDensity(low, 1.0, 8.0));
            AssertFiniteEqual(0.05, FeedbackMapping.BowAmplitude(high, 0.05));
            AssertFiniteEqual(0.0, FeedbackMapping.BowAmplitude(low, 0.05));
            AssertFiniteEqual(400.0, FeedbackMapping.CreakPitch(high, 80.0, 400.0));
            AssertFiniteEqual(80.0, FeedbackMapping.CreakPitch(low, 80.0, 400.0));
            Assert.Equal(UtilizationColor.Red, FeedbackMapping.JointColor(new[] { 0.10, high }, Yellow, Red));
            Assert.Equal(UtilizationColor.Green, FeedbackMapping.FillColor(new[] { low }, new[] { 0.10 }, Yellow, Red));
        }

        [Fact]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.Color(0.5, double.NaN, Red));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.Color(0.5, Yellow, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.Color(0.5, Red, Yellow));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.Pulse(0.5, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.StripeDensity(0.5, 8.0, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.StripeDensity(0.5, 1.0, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.BowAmplitude(0.5, -0.05));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.BowAmplitude(0.5, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.CreakPitch(0.5, 400.0, 80.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.CreakPitch(0.5, double.NegativeInfinity, 400.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.JointColor(new[] { 0.10 }, Red, Yellow));

            Assert.Throws<ArgumentNullException>(() => FeedbackMapping.JointColor(null));
            Assert.Throws<ArgumentNullException>(() => FeedbackMapping.FillColor(null, new[] { 0.10 }));
            Assert.Throws<ArgumentNullException>(() => FeedbackMapping.FillColor(new[] { 0.10 }, null));
            Assert.Throws<ArgumentException>(() => FeedbackMapping.JointColor(Array.Empty<double>()));
            Assert.Throws<ArgumentException>(() => FeedbackMapping.FillColor(Array.Empty<double>(), Array.Empty<double>()));
            Assert.Equal(UtilizationColor.Yellow, FeedbackMapping.FillColor(Array.Empty<double>(), new[] { 0.60 }, Yellow, Red));
        }

        private static void AssertFiniteEqual(double expected, double actual)
        {
            Assert.False(double.IsNaN(actual) || double.IsInfinity(actual), "Expected a finite value but got " + actual + ".");
            Assert.Equal(expected, actual, Precision);
        }
    }
}
