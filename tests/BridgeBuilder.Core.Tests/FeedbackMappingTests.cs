using System;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W1-10: feedback mapping functions.</summary>
    public class FeedbackMappingTests
    {
        [Fact]
        public void Color_BucketsFollowThresholds()
        {
            Assert.Equal(LoadColor.Green, FeedbackMapping.Color(-0.1, 0.5, 0.8));
            Assert.Equal(LoadColor.Green, FeedbackMapping.Color(0, 0.5, 0.8));
            Assert.Equal(LoadColor.Yellow, FeedbackMapping.Color(0.5, 0.5, 0.8));
            Assert.Equal(LoadColor.Yellow, FeedbackMapping.Color(0.79, 0.5, 0.8));
            Assert.Equal(LoadColor.Red, FeedbackMapping.Color(0.8, 0.5, 0.8));
            Assert.Equal(LoadColor.Red, FeedbackMapping.Color(1.5, 0.5, 0.8));
        }

        [Fact]
        public void Color_DefaultThresholds_ReadConstants()
        {
            Assert.Equal(0.50, Constants.Feedback.YellowUtilization);
            Assert.Equal(0.80, Constants.Feedback.RedUtilization);
            Assert.Equal(0.90, Constants.Feedback.PulseUtilization);
            Assert.Equal(1, Constants.Feedback.StripeDensityMin);
            Assert.Equal(8, Constants.Feedback.StripeDensityMax);
            Assert.Equal(0.05, Constants.Feedback.MaxBowAmplitudeMeters);
            Assert.Equal(80, Constants.Feedback.CreakPitchMinHz);
            Assert.Equal(400, Constants.Feedback.CreakPitchMaxHz);
            Assert.Equal(LoadColor.Yellow, FeedbackMapping.Color(Constants.Feedback.YellowUtilization));
            Assert.Equal(LoadColor.Red, FeedbackMapping.Color(Constants.Feedback.RedUtilization));
            Assert.Equal(Constants.Feedback.StripeDensityMin, FeedbackMapping.StripeDensity(0));
            Assert.Equal(Constants.Feedback.StripeDensityMax, FeedbackMapping.StripeDensity(1));
            Assert.True(FeedbackMapping.Pulse(Constants.Feedback.PulseUtilization));
            Assert.Equal(0, FeedbackMapping.BowAmplitude(0));
            Assert.Equal(Constants.Feedback.MaxBowAmplitudeMeters, FeedbackMapping.BowAmplitude(1));
            Assert.Equal(Constants.Feedback.CreakPitchMinHz, FeedbackMapping.CreakPitch(0));
            Assert.Equal(Constants.Feedback.CreakPitchMaxHz, FeedbackMapping.CreakPitch(1));
        }

        [Fact]
        public void StripeDensity_IncreasesWithUtilization()
        {
            Assert.Equal(1, FeedbackMapping.StripeDensity(-1, 1, 8));
            Assert.Equal(4.5, FeedbackMapping.StripeDensity(0.5, 1, 8));
            Assert.Equal(8, FeedbackMapping.StripeDensity(2, 1, 8));
            Assert.True(FeedbackMapping.StripeDensity(0.75, 1, 8) > FeedbackMapping.StripeDensity(0.25, 1, 8));
        }

        [Fact]
        public void Pulse_AtOrAboveThreshold()
        {
            Assert.False(FeedbackMapping.Pulse(0.89, 0.9));
            Assert.True(FeedbackMapping.Pulse(0.9, 0.9));
            Assert.True(FeedbackMapping.Pulse(1, 0.9));
        }

        [Fact]
        public void Bow_ZeroWhenUnloaded_AndMaxAtOrAboveOne()
        {
            Assert.Equal(0, FeedbackMapping.BowAmplitude(-1, 0.05));
            Assert.Equal(0.025, FeedbackMapping.BowAmplitude(0.5, 0.05), 12);
            Assert.Equal(0.05, FeedbackMapping.BowAmplitude(2, 0.05));
        }

        [Fact]
        public void CreakPitch_IncreasesWithWorstUtilization()
        {
            Assert.Equal(80, FeedbackMapping.CreakPitch(-1, 80, 400));
            Assert.Equal(160, FeedbackMapping.CreakPitch(0.25, 80, 400));
            Assert.Equal(400, FeedbackMapping.CreakPitch(2, 80, 400));
        }

        [Fact]
        public void JointColor_IsWorstMember()
        {
            Assert.Equal(LoadColor.Green, FeedbackMapping.JointColor(new[] { 0.10 }, 0.5, 0.8));
            Assert.Equal(LoadColor.Yellow, FeedbackMapping.JointColor(new[] { 0.10, 0.55 }, 0.5, 0.8));
            Assert.Equal(LoadColor.Red, FeedbackMapping.JointColor(new[] { 0.10, 0.55, 0.90 }, 0.5, 0.8));
        }

        [Fact]
        public void FillColor_IsWorstOfBracingAndFrame()
        {
            Assert.Equal(LoadColor.Green, FeedbackMapping.FillColor(new[] { 0.10 }, new[] { 0.10 }, 0.5, 0.8));
            Assert.Equal(LoadColor.Yellow, FeedbackMapping.FillColor(new[] { 0.60 }, new[] { 0.20 }, 0.5, 0.8));
            Assert.Equal(LoadColor.Red, FeedbackMapping.FillColor(new[] { 0.10 }, new[] { 0.85 }, 0.5, 0.8));
        }

        [Fact]
        public void UtilizationNaN_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.Color(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.StripeDensity(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.Pulse(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.BowAmplitude(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.CreakPitch(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.JointColor(new[] { double.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackMapping.FillColor(new[] { double.NaN }, new[] { 0.1 }));
        }
    }
}
