using System;
using System.IO;
using BridgeBuilder.Core;
using BridgeBuilder.Serialization;
using NUnit.Framework;

namespace BridgeBuilder.Tests
{
    /// <summary>The fixture bridges as JSON files (spec section 5), shared by both build tracks.</summary>
    public class JsonFixtureTests
    {
        static string FixtureDir
        {
            get
            {
                string root = Environment.GetEnvironmentVariable("BRIDGE_PROJECT") ?? Directory.GetCurrentDirectory();
                return Path.Combine(root, "Assets", "BridgeBuilder", "Tests", "Fixtures");
            }
        }

        static BridgeDesign Load(string name) =>
            BridgeJson.FromJson(new BridgeSettings(), File.ReadAllText(Path.Combine(FixtureDir, name + ".json")));

        [Test]
        public void BareDeckFixture_Fails() =>
            Assert.That(TestBridges.Run(Load("bare-deck")).Result, Is.EqualTo(RunResult.Failed));

        [Test]
        public void PrattTrussFixture_Holds() =>
            Assert.That(TestBridges.Run(Load("pratt-truss")).Result, Is.EqualTo(RunResult.Passed));

        [Test]
        public void JsonRoundTrip_GivesTheSameRun()
        {
            var original = TestBridges.Truss();
            var copy = BridgeJson.FromJson(new BridgeSettings(), BridgeJson.ToJson(original));
            Assert.That(copy.Nodes.Count, Is.EqualTo(original.Nodes.Count));
            Assert.That(copy.Version, Is.GreaterThan(0));
            var a = TestBridges.Run(original);
            var b = TestBridges.Run(copy);
            Assert.That(b.Result, Is.EqualTo(a.Result));
            Assert.That(b.PeakUtilization, Is.EqualTo(a.PeakUtilization).Within(1e-4f));
        }

        /// <summary>Regenerates the fixture files from the code-built bridges.</summary>
        [Test, Explicit]
        public void ExportFixtures()
        {
            Directory.CreateDirectory(FixtureDir);
            File.WriteAllText(Path.Combine(FixtureDir, "bare-deck.json"), BridgeJson.ToJson(TestBridges.BareDeck()));
            File.WriteAllText(Path.Combine(FixtureDir, "pratt-truss.json"), BridgeJson.ToJson(TestBridges.Truss()));
        }
    }
}
