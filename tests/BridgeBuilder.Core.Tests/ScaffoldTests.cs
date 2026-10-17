using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridgeBuilder.Core;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>W0-01: scaffold, data contract, fixture stubs.</summary>
    public class ScaffoldTests
    {
        static string Repo
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "BridgeBuilder.Core")))
                    dir = dir.Parent;
                return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
            }
        }

        [Fact]
        public void CoreSourceHasNoUnityEngine()
        {
            string core = Path.Combine(Repo, "Assets", "BridgeBuilder.Core");
            foreach (var file in Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                Assert.DoesNotContain("UnityEngine", text);
                Assert.DoesNotContain("System.Text.Json", text);
            }
            using var asmdef = JsonDocument.Parse(File.ReadAllText(Path.Combine(core, "BridgeBuilder.Core.asmdef")));
            Assert.True(asmdef.RootElement.GetProperty("noEngineReferences").GetBoolean());
            Assert.Equal("BridgeBuilder.Core", asmdef.RootElement.GetProperty("name").GetString());
            Assert.Equal(0, asmdef.RootElement.GetProperty("references").GetArrayLength());
        }

        [Fact]
        public void CoreProjectTargetsNetStandard21()
        {
            string csproj = File.ReadAllText(Path.Combine(Repo, "dotnet", "BridgeBuilder.Core.csproj"));
            Assert.Contains("netstandard2.1", csproj);
            Assert.Contains("<LangVersion>9.0</LangVersion>", csproj);
            Assert.DoesNotContain("PackageReference", csproj);
            Assert.Contains("<ImplicitUsings>disable</ImplicitUsings>", csproj);
            Assert.Contains("<Nullable>disable</Nullable>", csproj);
            Assert.Contains("<EnableDefaultCompileItems>false</EnableDefaultCompileItems>", csproj);
            Assert.Contains(@"..\Assets\BridgeBuilder.Core\**\*.cs", csproj);

            string tests = File.ReadAllText(Path.Combine(Repo, "tests", "BridgeBuilder.Core.Tests", "BridgeBuilder.Core.Tests.csproj"));
            Assert.Contains("net8.0", tests);
            Assert.Contains("BridgeBuilder.Core.csproj", tests);
            Assert.Contains("\"xunit\"", tests);
            Assert.Contains("xunit.runner.visualstudio", tests);
            Assert.Contains("Microsoft.NET.Test.Sdk", tests);

            string sln = File.ReadAllText(Path.Combine(Repo, "dotnet", "BridgeBuilder.sln"));
            Assert.Contains("BridgeBuilder.Core.csproj", sln);
            Assert.Contains("BridgeBuilder.Core.Tests.csproj", sln);

            string ci = File.ReadAllText(Path.Combine(Repo, ".github", "workflows", "core-ci.yml"));
            Assert.Contains("dotnet build", ci);
            Assert.Contains("dotnet test", ci);
            Assert.DoesNotContain("unity", ci.ToLowerInvariant().Replace("unity-", ""));

            string ignore = File.ReadAllText(Path.Combine(Repo, ".gitignore"));
            Assert.Contains("*.csproj", ignore);
            Assert.Contains("!dotnet/BridgeBuilder.Core.csproj", ignore);
            Assert.Contains("!tests/BridgeBuilder.Core.Tests/BridgeBuilder.Core.Tests.csproj", ignore);
            Assert.Contains("!dotnet/BridgeBuilder.sln", ignore);
        }

        [Fact]
        public void FixtureStubsLoad()
        {
            foreach (var name in FixtureLoader.All)
            {
                var f = FixtureLoader.Load(name);
                Assert.True(f.Nodes.Count >= 2, name);
            }
            var f1 = FixtureLoader.Load("fixture-01-bare-cantilevers.json");
            Assert.Equal(2, f1.Members.Count);
            var free = f1.Nodes.Where(n => !n.Fixed).Select(n => n.Id).ToList();
            Assert.DoesNotContain(f1.Members, m => free.Contains(m.NodeA) && free.Contains(m.NodeB));
            Assert.Equal(4, FixtureLoader.Load("fixture-03-unbraced-rectangle.json").Members.Count);
            Assert.True(FixtureLoader.Load("fixture-05-triangulated-truss.json").Members.Count >= 9);
            Assert.True(FixtureLoader.Load("fixture-06-progressive-failure.json").Members.Count >= 2);
            var pier = FixtureLoader.Load("fixture-07-pier.json");
            Assert.Single(pier.Members);
            Assert.Contains(pier.Nodes, n => n.Fixed && n.GroundAnchor);
        }

        [Fact]
        public void Vec3Arithmetic()
        {
            var v = new Vec3(3, 4, 0);
            Assert.Equal(5, v.Length);
            Assert.Equal(new Vec3(4, 4, 0), v + new Vec3(1, 0, 0));
            Assert.Equal(new Vec3(6, 8, 0), v * 2);
            Assert.Throws<ArgumentException>(() => new Vec3(double.NaN, 0, 0));
            Assert.Throws<ArgumentException>(() => v + new Vec3(0, 0, 0) * double.NaN);
            Assert.Throws<ArgumentException>(() => v * double.NaN);
        }
    }
}
