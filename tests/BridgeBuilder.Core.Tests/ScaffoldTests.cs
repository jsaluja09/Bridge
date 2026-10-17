using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace BridgeBuilder.Core.Tests
{
    public class ScaffoldTests
    {
        private const string CoreDirectoryPath = "Assets/BridgeBuilder.Core";
        private const string CoreProjectPath = "dotnet/BridgeBuilder.Core.csproj";
        private const string TestProjectPath = "tests/BridgeBuilder.Core.Tests/BridgeBuilder.Core.Tests.csproj";
        private const string SolutionPath = "dotnet/BridgeBuilder.sln";

        private const string Fixture1 = "fixture-01-bare-cantilevers.json";
        private const string Fixture3 = "fixture-03-unbraced-rectangle.json";
        private const string Fixture5 = "fixture-05-triangulated-truss.json";
        private const string Fixture6 = "fixture-06-progressive-failure.json";
        private const string Fixture7 = "fixture-07-pier.json";

        private static readonly string[] StubRootKeys = { "name", "note", "stub", "nodes", "members", "lines" };
        private static readonly string[] StubNodeKeys = { "id", "position", "fixed", "surfaceId", "groundAnchor" };
        private static readonly string[] StubMemberKeys = { "id", "nodeA", "nodeB", "sectionId", "lineId", "hiddenBrace" };
        private static readonly string[] StubLineKeys = { "id", "memberIds" };

        private static readonly Lazy<string> RepoRootValue = new Lazy<string>(FindRepoRoot);

        private static string RepoRoot => RepoRootValue.Value;

        [Fact]
        public void CoreSourceHasNoUnityEngine()
        {
            string[] sources = Directory.GetFiles(RepoPath(CoreDirectoryPath), "*.cs", SearchOption.AllDirectories);
            Assert.NotEmpty(sources);
            foreach (string path in sources)
            {
                string text = File.ReadAllText(path);
                string relative = Path.GetRelativePath(RepoRoot, path);
                Assert.False(text.Contains("UnityEngine", StringComparison.Ordinal), relative + " mentions UnityEngine.");
                Assert.False(text.Contains("System.Text.Json", StringComparison.Ordinal), relative + " mentions System.Text.Json.");
            }

            using JsonDocument asmdef = JsonDocument.Parse(File.ReadAllText(RepoPath(CoreDirectoryPath + "/BridgeBuilder.Core.asmdef")));
            JsonElement root = asmdef.RootElement;
            Assert.Equal(JsonValueKind.True, root.GetProperty("noEngineReferences").ValueKind);
            Assert.Equal("BridgeBuilder.Core", root.GetProperty("name").GetString());
            Assert.Equal("BridgeBuilder.Core", root.GetProperty("rootNamespace").GetString());
            JsonElement references = root.GetProperty("references");
            Assert.Equal(JsonValueKind.Array, references.ValueKind);
            Assert.Equal(0, references.GetArrayLength());
        }

        [Fact]
        public void CoreProjectTargetsNetStandard21()
        {
            XDocument core = XDocument.Load(RepoPath(CoreProjectPath));
            Assert.Equal("netstandard2.1", SingleProperty(core, "TargetFramework"));
            Assert.Equal("9.0", SingleProperty(core, "LangVersion"));
            Assert.Equal("disable", SingleProperty(core, "ImplicitUsings"));
            Assert.Equal("disable", SingleProperty(core, "Nullable"));
            Assert.Equal("false", SingleProperty(core, "EnableDefaultCompileItems"));
            Assert.Empty(core.Descendants("PackageReference"));
            XElement compile = Assert.Single(core.Descendants("Compile"));
            Assert.Equal(@"..\Assets\BridgeBuilder.Core\**\*.cs", (string)compile.Attribute("Include"));

            XDocument tests = XDocument.Load(RepoPath(TestProjectPath));
            Assert.Equal("net8.0", SingleProperty(tests, "TargetFramework"));
            XElement projectReference = Assert.Single(tests.Descendants("ProjectReference"));
            string testProjectDirectory = Path.GetDirectoryName(RepoPath(TestProjectPath));
            Assert.Equal(
                Path.GetFullPath(RepoPath(CoreProjectPath)),
                Path.GetFullPath(Path.Combine(testProjectDirectory, ToLocalPath((string)projectReference.Attribute("Include")))));
            string[] packages = tests.Descendants("PackageReference").Select(e => (string)e.Attribute("Include")).ToArray();
            Assert.Contains("xunit", packages);
            Assert.Contains("xunit.runner.visualstudio", packages);
            Assert.Contains("Microsoft.NET.Test.Sdk", packages);

            string solutionDirectory = Path.GetDirectoryName(RepoPath(SolutionPath));
            string[] solutionProjects = Regex
                .Matches(File.ReadAllText(RepoPath(SolutionPath)), "^Project\\(\"\\{[^}]+\\}\"\\) = \"[^\"]*\", \"([^\"]+)\"", RegexOptions.Multiline)
                .Select(match => Path.GetFullPath(Path.Combine(solutionDirectory, ToLocalPath(match.Groups[1].Value))))
                .ToArray();
            Assert.Contains(Path.GetFullPath(RepoPath(CoreProjectPath)), solutionProjects);
            Assert.Contains(Path.GetFullPath(RepoPath(TestProjectPath)), solutionProjects);

            string workflow = File.ReadAllText(RepoPath(".github/workflows/core-ci.yml"));
            string[] workflowLines = workflow.Split('\n');
            Assert.Contains(workflowLines, line => line.Contains("dotnet build") && line.Contains(TestProjectPath));
            Assert.Contains(workflowLines, line => line.Contains("dotnet test") && line.Contains(TestProjectPath));
            Assert.DoesNotContain("unity", workflow, StringComparison.OrdinalIgnoreCase);

            string[] ignoreRules = File.ReadAllLines(RepoPath(".gitignore"))
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                .ToArray();
            int csprojRule = Array.IndexOf(ignoreRules, "*.csproj");
            int slnRule = Array.IndexOf(ignoreRules, "*.sln");
            Assert.True(csprojRule >= 0, ".gitignore must ignore *.csproj.");
            Assert.True(slnRule >= 0, ".gitignore must ignore *.sln.");
            var projectExceptions = ignoreRules
                .Select((rule, index) => new { Rule = rule, Index = index })
                .Where(e => e.Rule.StartsWith("!", StringComparison.Ordinal) && (e.Rule.Contains(".csproj") || e.Rule.Contains(".sln")))
                .ToArray();
            Assert.Equal(
                new[] { CoreProjectPath, SolutionPath, TestProjectPath }.OrderBy(p => p, StringComparer.Ordinal),
                projectExceptions.Select(e => e.Rule.TrimStart('!').TrimStart('/')).OrderBy(p => p, StringComparer.Ordinal));
            Assert.All(projectExceptions, e => Assert.True(e.Index > Math.Max(csprojRule, slnRule), e.Rule + " must come after the *.csproj and *.sln rules."));
            Assert.Contains("/dotnet/**/[Bb]in/", ignoreRules);
            Assert.Contains("/dotnet/**/[Oo]bj/", ignoreRules);
            Assert.Contains("/tests/**/[Bb]in/", ignoreRules);
            Assert.Contains("/tests/**/[Oo]bj/", ignoreRules);
        }

        [Fact]
        public void FixtureStubsLoad()
        {
            Assert.Equal(7, FixtureLoader.StarterFixtureFileNames.Count);
            foreach (string fileName in FixtureLoader.StarterFixtureFileNames)
            {
                BridgeFixture fixture = FixtureLoader.Load(fileName);
                Assert.True(fixture.Nodes.Length >= 2, fileName + " needs at least two nodes.");
                AssertIdsResolve(fileName, fixture);
                if (IsStub(fileName))
                {
                    AssertStubIsGeometryOnly(fileName);
                }
            }

            BridgeFixture cantilevers = FixtureLoader.Load(Fixture1);
            AssertBareCantilevers(cantilevers);
            if (IsStub(Fixture1))
            {
                Assert.Equal(2, cantilevers.Members.Length);
            }

            BridgeFixture rectangle = FixtureLoader.Load(Fixture3);
            Assert.True(ContainsRectangle(rectangle), Fixture3 + " must contain a four-member rectangle.");
            if (IsStub(Fixture3))
            {
                Assert.Equal(4, rectangle.Members.Length);
                Assert.Equal(4, rectangle.Nodes.Length);
            }

            AssertTriangulated(FixtureLoader.Load(Fixture5));

            if (IsStub(Fixture6))
            {
                BridgeFixture progressive = FixtureLoader.Load(Fixture6);
                Assert.True(progressive.Members.Length >= 3, Fixture6 + " needs at least three members.");
                Assert.Single(Components(Adjacency(progressive)));
            }

            AssertSinglePierToFixedGroundAnchor(FixtureLoader.Load(Fixture7));

            BridgeFixture extended = FixtureLoader.Parse(@"{
                ""name"": ""extended"",
                ""expectedTipDeflection"": 0.09,
                ""loads"": [ { ""nodeId"": 2, ""force"": [0, 0, 16000] } ],
                ""nodes"": [
                    { ""id"": 1, ""position"": [0, 0, 0], ""fixed"": true, ""label"": ""base"" },
                    { ""id"": 2, ""position"": [3, 0, 0], ""extra"": { ""nested"": [1, 2, 3] } }
                ],
                ""members"": [ { ""id"": 1, ""nodeA"": 1, ""nodeB"": 2, ""lineId"": 1, ""tag"": ""m"" } ],
                ""lines"": [ { ""id"": 1, ""memberIds"": [1], ""color"": ""red"" } ]
            }");
            Assert.Equal("extended", extended.Name);
            Assert.Equal(2, extended.Nodes.Length);
            Assert.True(extended.Nodes[0].Fixed);
            Assert.Equal(new Vec3(3, 0, 0), extended.Nodes[1].Position);
            Assert.Equal(2, Assert.Single(extended.Members).NodeB);
            Assert.Equal(new[] { 1 }, Assert.Single(extended.Lines).MemberIds);
            Assert.Empty(extended.Fills);
        }

        [Fact]
        public void FixtureLoaderExposesPathsAndRawJson()
        {
            foreach (string fileName in FixtureLoader.StarterFixtureFileNames)
            {
                string path = FixtureLoader.PathFor(fileName);
                Assert.True(Path.IsPathRooted(path));
                Assert.True(File.Exists(path));
                Assert.Equal(File.ReadAllText(path), FixtureLoader.ReadText(fileName));
                using JsonDocument document = FixtureLoader.ReadDocument(fileName);
                Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("nodes").ValueKind);
            }

            Assert.Throws<FileNotFoundException>(() => FixtureLoader.PathFor("fixture-99-missing.json"));

            BridgeFixture truss = FixtureLoader.Load(Fixture5);
            SolverInput asInput = FixtureLoader.LoadAs<SolverInput>(Fixture5);
            Assert.Equal(truss.Nodes.Length, asInput.Nodes.Length);
            Assert.Equal(truss.Members.Length, asInput.Members.Length);
            SolverInput converted = truss.ToSolverInput(20.0);
            Assert.Same(truss.Nodes, converted.Nodes);
            Assert.Equal(20.0, converted.ScaleFactor);

            string json = JsonSerializer.Serialize(new Node { Id = 7, Position = new Vec3(1.5, -2, 0.25) }, FixtureLoader.SerializerOptions);
            Node roundTrip = JsonSerializer.Deserialize<Node>(json, FixtureLoader.SerializerOptions);
            Assert.Equal(7, roundTrip.Id);
            Assert.Equal(new Vec3(1.5, -2, 0.25), roundTrip.Position);
            Assert.Null(roundTrip.SurfaceId);

            Assert.Throws<JsonException>(() => FixtureLoader.Parse(@"{ ""nodes"": [ { ""id"": 1, ""position"": [1, 2] } ] }"));
            Assert.Throws<JsonException>(() => FixtureLoader.Parse(@"{ ""nodes"": [ { ""id"": 1, ""position"": { ""x"": 1 } } ] }"));
        }

        [Fact]
        public void Vec3Arithmetic()
        {
            var v = new Vec3(3, 4, 0);
            Assert.Equal(5.0, v.Length);
            Assert.Equal(new Vec3(4, 4, 0), v + new Vec3(1, 0, 0));
            Assert.Equal(new Vec3(6, 8, 0), v * 2.0);
            Assert.Equal(new Vec3(6, 8, 0), 2.0 * v);

            Assert.Throws<ArgumentException>(() => new Vec3(double.NaN, 0, 0));
            Assert.Throws<ArgumentException>(() => new Vec3(0, double.NaN, 0));
            Assert.Throws<ArgumentException>(() => new Vec3(0, 0, double.NaN));
            Assert.Throws<ArgumentException>(() => new Vec3(double.PositiveInfinity, 0, 0) + new Vec3(double.NegativeInfinity, 0, 0));
            Assert.Throws<ArgumentException>(() => v * double.NaN);
            Assert.Throws<ArgumentException>(() => new Vec3(double.PositiveInfinity, 0, 0) * 0.0);

            Assert.True(typeof(Vec3).IsValueType);
            Assert.Equal("BridgeBuilder.Core", typeof(Vec3).Namespace);
            Assert.Equal("BridgeBuilder.Core", typeof(Mat3).Namespace);
            foreach (string component in new[] { "X", "Y", "Z" })
            {
                Assert.Equal(typeof(double), typeof(Vec3).GetProperty(component).PropertyType);
            }

            PropertyInfo[] entries = typeof(Mat3).GetProperties().Where(p => Regex.IsMatch(p.Name, "^M[0-2][0-2]$")).ToArray();
            Assert.Equal(9, entries.Length);
            Assert.All(entries, p => Assert.Equal(typeof(double), p.PropertyType));
        }

        [Fact]
        public void Vec3VectorOperations()
        {
            Assert.Equal(new Vec3(-1, 2, 3), new Vec3(1, 4, 3) - new Vec3(2, 2, 0));
            Assert.Equal(new Vec3(-1, -2, -3), -new Vec3(1, 2, 3));
            Assert.Equal(new Vec3(0.5, 1, 1.5), new Vec3(1, 2, 3) / 2.0);
            Assert.Equal(25.0, new Vec3(3, 4, 0).LengthSquared);
            Assert.Equal(5.0, Vec3.Distance(new Vec3(1, 1, 1), new Vec3(4, 5, 1)));
            Assert.Equal(32.0, Vec3.Dot(new Vec3(1, 2, 3), new Vec3(4, 5, 6)));

            Assert.Equal(Vec3.UnitZ, Vec3.Cross(Vec3.UnitX, Vec3.UnitY));
            Assert.Equal(Vec3.UnitX, Vec3.Cross(Vec3.UnitY, Vec3.UnitZ));
            Assert.Equal(Vec3.UnitY, Vec3.Cross(Vec3.UnitZ, Vec3.UnitX));
            Assert.Equal(new Vec3(-3, 6, -3), Vec3.Cross(new Vec3(1, 2, 3), new Vec3(4, 5, 6)));

            Assert.Equal(new Vec3(0.6, 0.8, 0), new Vec3(3, 4, 0).Normalized());
            Assert.Equal(1.0, new Vec3(1, 2, 3).Normalized().Length, 12);
            Assert.Throws<InvalidOperationException>(() => Vec3.Zero.Normalized());

            Assert.Equal(new Vec3(0, 0, 0), Vec3.Zero);
            Assert.Equal(Vec3.Zero, default(Vec3));
            Assert.True(new Vec3(1, 2, 3) == new Vec3(1, 2, 3));
            Assert.True(new Vec3(1, 2, 3) != new Vec3(1, 2, 4));
            Assert.Equal(new Vec3(1, 2, 3).GetHashCode(), new Vec3(1, 2, 3).GetHashCode());
            Assert.Equal("(1, 2.5, -3)", new Vec3(1, 2.5, -3).ToString());
        }

        [Fact]
        public void Mat3Operations()
        {
            var m = new Mat3(1, 2, 3, 4, 5, 6, 7, 8, 10);
            Assert.Equal(new Vec3(1, 2, 3), Mat3.Identity * new Vec3(1, 2, 3));
            Assert.Equal(m, Mat3.Identity * m);
            Assert.Equal(m, m * Mat3.Identity);
            Assert.Equal(new Vec3(14, 32, 53), m * new Vec3(1, 2, 3));
            Assert.Equal(new Mat3(-2, 1, 3, -5, 4, 6, -8, 7, 10), m * new Mat3(0, 1, 0, -1, 0, 0, 0, 0, 1));

            Assert.Equal(new Mat3(1, 4, 7, 2, 5, 8, 3, 6, 10), m.Transpose());
            Assert.Equal(m, m.Transpose().Transpose());
            Assert.Equal(-3.0, m.Determinant);
            Assert.Equal(1.0, Mat3.Identity.Determinant);

            Assert.Equal(6.0, m[1, 2]);
            Assert.Equal(new Vec3(4, 5, 6), m.Row(1));
            Assert.Equal(new Vec3(3, 6, 10), m.Column(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => m[3, 0]);
            Assert.Throws<ArgumentOutOfRangeException>(() => m[0, 3]);
            Assert.Throws<ArgumentOutOfRangeException>(() => m[-1, 2]);

            Mat3 axes = Mat3.FromRows(Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX);
            Assert.Equal(new Vec3(2, 3, 1), axes * new Vec3(1, 2, 3));
            Assert.Equal(axes.Transpose(), Mat3.FromColumns(Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX));
            Assert.Equal(Mat3.Identity, axes * axes.Transpose());

            Assert.Equal(new Mat3(2, 4, 6, 8, 10, 12, 14, 16, 20), m * 2.0);
            Assert.Equal(m * 2.0, 2.0 * m);
            Assert.Equal(m * 2.0, m + m);
            Assert.Equal(Mat3.Zero, m - m);
            Assert.Equal(Mat3.Zero, default(Mat3));
            Assert.Throws<ArgumentException>(() => new Mat3(double.NaN, 0, 0, 0, 0, 0, 0, 0, 0));
        }

        [Fact]
        public void HumanConstantsHaveIssueValues()
        {
            Assert.Equal(1.6, Constants.Human.Fixture1MinimumUtilization);
            Assert.Equal(0.5, Constants.Human.Fixture5MaximumUtilization);
            Assert.Equal(150, Constants.Human.NodeCap);
            Assert.Equal(2.0e11, Constants.Human.YoungsModulus);
            Assert.Equal(7.692307692307693e10, Constants.Human.ShearModulus);
            Assert.Equal(7850.0, Constants.Human.Density);
            Assert.Equal(1.71e-5, Constants.Human.TorsionConstant);
            Assert.Equal(1500.0, Constants.Human.CarMassKg);
            Assert.Equal(0.25, Constants.Human.ReachTolerance);

            string[] expected =
            {
                "CarMassKg", "Density", "Fixture1MinimumUtilization", "Fixture5MaximumUtilization", "NodeCap",
                "ReachTolerance", "ShearModulus", "TorsionConstant", "YoungsModulus",
            };
            Assert.Equal(expected, typeof(Constants.Human).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.True(typeof(Constants).IsAbstract && typeof(Constants).IsSealed, "Constants must be a static class.");
        }

        [Fact]
        public void HumanConstantsAreTaggedHumanOwned()
        {
            string[] lines = File.ReadAllLines(RepoPath(CoreDirectoryPath + "/Constants.Human.cs"));
            Assert.Contains(lines, line => line.Contains("public static partial class Constants"));
            int constants = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].TrimStart().StartsWith("public const ", StringComparison.Ordinal))
                {
                    continue;
                }

                constants++;
                var comment = new StringBuilder();
                for (int j = i - 1; j >= 0 && lines[j].TrimStart().StartsWith("//", StringComparison.Ordinal); j--)
                {
                    comment.Insert(0, lines[j]);
                }

                string text = comment.ToString();
                Assert.True(text.Contains("HUMAN_OWNED", StringComparison.Ordinal), "Missing HUMAN_OWNED tag above: " + lines[i].Trim());
                Assert.True(Regex.IsMatch(text, @"Spec \d"), "Missing spec section citation above: " + lines[i].Trim());
                Assert.False(text.Contains("GUESS", StringComparison.Ordinal) || text.Contains("DERIVED", StringComparison.Ordinal), "Only HUMAN_OWNED belongs here: " + lines[i].Trim());
            }

            Assert.Equal(9, constants);
        }

        [Fact]
        public void ContractTypesHaveIssueMembers()
        {
            AssertContract(typeof(Node), ("Id", typeof(int)), ("Position", typeof(Vec3)), ("Fixed", typeof(bool)), ("SurfaceId", typeof(int?)), ("GroundAnchor", typeof(bool)));
            AssertContract(typeof(Member), ("Id", typeof(int)), ("NodeA", typeof(int)), ("NodeB", typeof(int)), ("SectionId", typeof(int)), ("LineId", typeof(int)), ("HiddenBrace", typeof(bool)));
            AssertContract(typeof(Fill), ("Id", typeof(int)), ("NodeIds", typeof(int[])), ("Road", typeof(bool)), ("Valid", typeof(bool)));
            AssertContract(typeof(Line), ("Id", typeof(int)), ("MemberIds", typeof(int[])));
            AssertContract(
                typeof(RunConfiguration),
                ("StartSurfaceId", typeof(int)), ("Car", typeof(CarParameters)), ("WindSpeed", typeof(double)), ("WindDirection", typeof(Vec3)),
                ("Grip", typeof(double)), ("MaterialBudget", typeof(double)), ("GroundHeightOverride", typeof(double?)), ("ScaleFactor", typeof(double)));
            AssertContract(
                typeof(CarParameters),
                ("MassKg", typeof(double)), ("WheelCount", typeof(int)), ("Wheelbase", typeof(double)), ("Track", typeof(double)),
                ("SpringStiffness", typeof(double)), ("DamperCoefficient", typeof(double)), ("RestLength", typeof(double)));
            AssertContract(typeof(CarState), ("Position", typeof(Vec3)), ("WheelLoads", typeof(WheelLoad[])), ("SuspensionCompression", typeof(double[])), ("Speed", typeof(double)));
            AssertContract(
                typeof(SectionProperties),
                ("Id", typeof(int)), ("E", typeof(double)), ("G", typeof(double)), ("A", typeof(double)), ("Iy", typeof(double)), ("Iz", typeof(double)),
                ("J", typeof(double)), ("YieldStrength", typeof(double)), ("Density", typeof(double)));
            AssertContract(
                typeof(SolverInput),
                ("Nodes", typeof(Node[])), ("Members", typeof(Member[])), ("Fills", typeof(Fill[])), ("Sections", typeof(SectionProperties[])), ("ScaleFactor", typeof(double)));
            AssertContract(typeof(NodalLoad), ("NodeId", typeof(int)), ("Force", typeof(Vec3)), ("Moment", typeof(Vec3)));
            AssertContract(typeof(WheelLoad), ("WheelIndex", typeof(int)), ("NormalLoad", typeof(double)));
            AssertContract(typeof(DeckSample), ("WheelIndex", typeof(int)), ("Height", typeof(double)), ("Slope", typeof(double)), ("Position", typeof(Vec3)));
            AssertContract(
                typeof(MemberForce),
                ("MemberId", typeof(int)), ("Axial", typeof(double)), ("ShearLocalZAtA", typeof(double)), ("MomentLocalYAtA", typeof(double)),
                ("ShearLocalZAtB", typeof(double)), ("MomentLocalYAtB", typeof(double)), ("Utilization", typeof(double)));
            AssertContract(
                typeof(StepOutput),
                ("Displacements", typeof(Vec3[])), ("MemberForces", typeof(MemberForce[])), ("FailedMemberIds", typeof(int[])), ("CarState", typeof(CarState)));
            AssertContract(typeof(SimulationSnapshot), ("StepId", typeof(int)), ("Displacements", typeof(Vec3[])), ("Utilizations", typeof(double[])));
            AssertContract(typeof(StaticSolveResult), ("Displacements", typeof(Vec3[])), ("MemberForces", typeof(MemberForce[])));

            var contracts = new[]
            {
                typeof(Node), typeof(Member), typeof(Fill), typeof(Line), typeof(RunConfiguration), typeof(CarParameters), typeof(CarState),
                typeof(SectionProperties), typeof(SolverInput), typeof(NodalLoad), typeof(WheelLoad), typeof(DeckSample), typeof(MemberForce),
                typeof(StepOutput), typeof(SimulationSnapshot), typeof(StaticSolveResult),
            };
            foreach (Type contract in contracts)
            {
                foreach (PropertyInfo property in contract.GetProperties())
                {
                    Type scalar = Nullable.GetUnderlyingType(property.PropertyType) ?? (property.PropertyType.IsArray ? property.PropertyType.GetElementType() : property.PropertyType);
                    Assert.False(scalar == typeof(float) || scalar == typeof(decimal) || scalar == typeof(long), contract.Name + "." + property.Name + " must use double scalars and int ids.");
                }
            }

            var input = new SolverInput();
            Assert.Empty(input.Nodes);
            Assert.Empty(input.Members);
            Assert.Empty(input.Fills);
            Assert.Empty(input.Sections);
        }

        [Fact]
        public void CoreTypesUseCoreNamespaceWithoutJsonAttributes()
        {
            foreach (Type type in typeof(Vec3).Assembly.GetExportedTypes())
            {
                Assert.Equal("BridgeBuilder.Core", type.Namespace);
                MemberInfo[] members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                IEnumerable<object> attributes = type.GetCustomAttributes(false)
                    .Concat(members.SelectMany(member => member.GetCustomAttributes(false)));
                Assert.DoesNotContain(attributes, attribute => (attribute.GetType().Namespace ?? string.Empty).StartsWith("System.Text.Json", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void SolverInterfacesHaveIssueSignatures()
        {
            AssertMethod(typeof(ILinearSolver), "Add", typeof(void), typeof(int), typeof(int), typeof(double));
            AssertMethod(typeof(ILinearSolver), "Factor", typeof(void));
            AssertMethod(typeof(ILinearSolver), "Solve", typeof(void), typeof(double[]), typeof(double[]));
            AssertMethod(typeof(IStaticSolver), "Solve", typeof(StaticSolveResult), typeof(SolverInput), typeof(NodalLoad[]));
            AssertMethod(typeof(IDynamicSolver), "Reset", typeof(void), typeof(SolverInput), typeof(RunConfiguration));
            AssertMethod(typeof(IDynamicSolver), "Step", typeof(StepOutput), typeof(WheelLoad[]), typeof(double), typeof(Func<bool>));
            AssertMethod(typeof(IStrokeRecognizer), "Recognize", typeof(RecognitionResult), typeof(IReadOnlyList<Vec3>), typeof(IReadOnlyList<Vec3>), typeof(RecognizerTolerances));
            AssertMethod(typeof(ICarModel), "Step", typeof(CarState), typeof(CarState), typeof(CarParameters), typeof(DeckSample[]), typeof(Vec3), typeof(double));
            AssertMethod(typeof(ISnapshotStore), "Publish", typeof(void), typeof(SimulationSnapshot));
            MethodInfo tryRead = AssertMethod(typeof(ISnapshotStore), "TryRead", typeof(bool), typeof(SimulationSnapshot).MakeByRefType());
            Assert.True(tryRead.GetParameters()[0].IsOut);

            Assert.Equal(3, typeof(ILinearSolver).GetMethods().Length);
            Assert.Single(typeof(IStaticSolver).GetMethods());
            Assert.Equal(2, typeof(IDynamicSolver).GetMethods().Length);
            Assert.Single(typeof(IStrokeRecognizer).GetMethods());
            Assert.Single(typeof(ICarModel).GetMethods());
            Assert.Equal(2, typeof(ISnapshotStore).GetMethods().Length);

            Assert.Equal(new[] { "Line", "Arc", "Polyline", "Rejected" }, Enum.GetNames(typeof(StrokeKind)));
            string[] reasons = Enum.GetNames(typeof(RejectionReason));
            Assert.Contains("SnapStart", reasons);
            Assert.Contains("NonPlanar", reasons);
            Assert.Contains("Unrecognized", reasons);
            Assert.Contains("Degenerate", reasons);
            Assert.Equal(RejectionReason.None, default(RejectionReason));

            Assert.Equal(typeof(StrokeKind), typeof(RecognitionResult).GetProperty("Kind").PropertyType);
            Assert.Equal(typeof(RejectionReason), typeof(RecognitionResult).GetProperty("Reason").PropertyType);
            RecognitionResult rejected = RecognitionResult.Reject(RejectionReason.SnapStart);
            Assert.Equal(StrokeKind.Rejected, rejected.Kind);
            Assert.Equal(RejectionReason.SnapStart, rejected.Reason);
            Assert.False(rejected.IsAccepted);
            Assert.Empty(rejected.Vertices);
            RecognitionResult line = RecognitionResult.Accepted(StrokeKind.Line, new[] { Vec3.Zero, Vec3.UnitX });
            Assert.Equal(RejectionReason.None, line.Reason);
            Assert.True(line.IsAccepted);
            Assert.Equal(2, line.Vertices.Length);
            Assert.Throws<ArgumentException>(() => RecognitionResult.Accepted(StrokeKind.Rejected, null));
            Assert.Throws<ArgumentException>(() => RecognitionResult.Reject(RejectionReason.None));

            Assert.True(typeof(RecognizerTolerances).IsValueType);
            string[] toleranceNames =
            {
                "SnapStartToleranceMeters", "LineResidualMeters", "PlaneResidualMeters", "ArcResidualMeters",
                "CornerAngleDegrees", "ArcSweepMinDegrees", "ArcSweepMaxDegrees",
            };
            var tolerances = new RecognizerTolerances(1, 2, 3, 4, 5, 6, 7);
            for (int i = 0; i < toleranceNames.Length; i++)
            {
                PropertyInfo property = typeof(RecognizerTolerances).GetProperty(toleranceNames[i]);
                Assert.NotNull(property);
                Assert.Equal(typeof(double), property.PropertyType);
                Assert.True(property.CanWrite, toleranceNames[i] + " must be settable.");
                Assert.Equal(i + 1.0, (double)property.GetValue(tolerances));
            }

            Assert.Equal(0.02, new RecognizerTolerances { SnapStartToleranceMeters = 0.02 }.SnapStartToleranceMeters);
        }

        private static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "dotnet", "BridgeBuilder.sln")))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException("No directory containing dotnet/BridgeBuilder.sln above " + AppContext.BaseDirectory);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot, ToLocalPath(relativePath));
        }

        private static string ToLocalPath(string path)
        {
            return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string SingleProperty(XDocument project, string name)
        {
            return Assert.Single(project.Descendants(name)).Value.Trim();
        }

        private static bool IsStub(string fileName)
        {
            using JsonDocument document = FixtureLoader.ReadDocument(fileName);
            return document.RootElement.TryGetProperty("stub", out JsonElement stub) && stub.ValueKind == JsonValueKind.True;
        }

        private static void AssertStubIsGeometryOnly(string fileName)
        {
            using JsonDocument document = FixtureLoader.ReadDocument(fileName);
            JsonElement root = document.RootElement;
            AssertOnlyKeys(fileName, root, StubRootKeys);
            foreach (JsonElement node in root.GetProperty("nodes").EnumerateArray())
            {
                AssertOnlyKeys(fileName + " node", node, StubNodeKeys);
            }

            foreach (JsonElement member in root.GetProperty("members").EnumerateArray())
            {
                AssertOnlyKeys(fileName + " member", member, StubMemberKeys);
            }

            if (root.TryGetProperty("lines", out JsonElement lines))
            {
                foreach (JsonElement line in lines.EnumerateArray())
                {
                    AssertOnlyKeys(fileName + " line", line, StubLineKeys);
                }
            }
        }

        private static void AssertOnlyKeys(string context, JsonElement element, string[] allowed)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                Assert.True(
                    allowed.Contains(property.Name),
                    context + " has '" + property.Name + "'. A stub holds geometry only: no loads, sections, or expected answers.");
            }
        }

        private static void AssertIdsResolve(string fileName, BridgeFixture fixture)
        {
            var nodeIds = new HashSet<int>();
            foreach (Node node in fixture.Nodes)
            {
                Assert.True(nodeIds.Add(node.Id), fileName + " repeats node id " + node.Id + ".");
            }

            var memberIds = new HashSet<int>();
            foreach (Member member in fixture.Members)
            {
                Assert.True(memberIds.Add(member.Id), fileName + " repeats member id " + member.Id + ".");
                Assert.True(nodeIds.Contains(member.NodeA) && nodeIds.Contains(member.NodeB), fileName + " member " + member.Id + " references a missing node.");
                Assert.NotEqual(member.NodeA, member.NodeB);
                if (fixture.Sections.Length > 0)
                {
                    Assert.Contains(fixture.Sections, section => section.Id == member.SectionId);
                }
            }

            foreach (Line line in fixture.Lines)
            {
                Assert.All(line.MemberIds, id => Assert.True(memberIds.Contains(id), fileName + " line " + line.Id + " references missing member " + id + "."));
            }

            foreach (Fill fill in fixture.Fills)
            {
                Assert.All(fill.NodeIds, id => Assert.True(nodeIds.Contains(id), fileName + " fill " + fill.Id + " references missing node " + id + "."));
            }
        }

        private static Dictionary<int, List<int>> Adjacency(BridgeFixture fixture)
        {
            var adjacency = new Dictionary<int, List<int>>();
            foreach (Member member in fixture.Members)
            {
                AddNeighbor(adjacency, member.NodeA, member.NodeB);
                AddNeighbor(adjacency, member.NodeB, member.NodeA);
            }

            return adjacency;
        }

        private static void AddNeighbor(Dictionary<int, List<int>> adjacency, int from, int to)
        {
            if (!adjacency.TryGetValue(from, out List<int> neighbors))
            {
                neighbors = new List<int>();
                adjacency.Add(from, neighbors);
            }

            neighbors.Add(to);
        }

        private static List<HashSet<int>> Components(Dictionary<int, List<int>> adjacency)
        {
            var components = new List<HashSet<int>>();
            var seen = new HashSet<int>();
            foreach (int start in adjacency.Keys.OrderBy(id => id))
            {
                if (!seen.Add(start))
                {
                    continue;
                }

                var component = new HashSet<int> { start };
                var pending = new Queue<int>();
                pending.Enqueue(start);
                while (pending.Count > 0)
                {
                    foreach (int next in adjacency[pending.Dequeue()])
                    {
                        if (seen.Add(next))
                        {
                            component.Add(next);
                            pending.Enqueue(next);
                        }
                    }
                }

                components.Add(component);
            }

            return components;
        }

        private static void AssertBareCantilevers(BridgeFixture fixture)
        {
            Dictionary<int, Node> nodes = fixture.Nodes.ToDictionary(n => n.Id);
            Dictionary<int, List<int>> adjacency = Adjacency(fixture);
            List<HashSet<int>> components = Components(adjacency);
            Assert.Equal(2, components.Count);

            var surfaces = new HashSet<int>();
            var freeEnds = new List<int>();
            foreach (HashSet<int> component in components)
            {
                Assert.All(component, id => Assert.True(adjacency[id].Count <= 2, Fixture1 + " cantilevers must be simple chains."));
                Node[] ends = component.Where(id => adjacency[id].Count == 1).Select(id => nodes[id]).ToArray();
                Assert.Equal(2, ends.Length);
                Node support = Assert.Single(ends, n => n.Fixed);
                Node tip = Assert.Single(ends, n => !n.Fixed);
                Assert.True(support.SurfaceId.HasValue, Fixture1 + " cantilevers must start at a surface node.");
                Assert.Single(component, id => nodes[id].Fixed);
                surfaces.Add(support.SurfaceId.Value);
                freeEnds.Add(tip.Id);
            }

            Assert.Equal(2, surfaces.Count);
            Assert.DoesNotContain(fixture.Members, m => freeEnds.Contains(m.NodeA) && freeEnds.Contains(m.NodeB));
        }

        private static bool ContainsRectangle(BridgeFixture fixture)
        {
            Dictionary<int, Vec3> positions = fixture.Nodes.ToDictionary(n => n.Id, n => n.Position);
            Dictionary<int, List<int>> adjacency = Adjacency(fixture);
            foreach (int a in adjacency.Keys)
            {
                foreach (int b in adjacency[a])
                {
                    foreach (int d in adjacency[a].Where(d => d > b))
                    {
                        foreach (int c in adjacency[b].Where(c => c != a && c != d && adjacency[d].Contains(c)))
                        {
                            if (IsRightAngle(positions, d, a, b) && IsRightAngle(positions, a, b, c)
                                && IsRightAngle(positions, b, c, d) && IsRightAngle(positions, c, d, a))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsRightAngle(Dictionary<int, Vec3> positions, int before, int corner, int after)
        {
            Vec3 toBefore = (positions[before] - positions[corner]).Normalized();
            Vec3 toAfter = (positions[after] - positions[corner]).Normalized();
            return Math.Abs(Vec3.Dot(toBefore, toAfter)) < 1e-9;
        }

        private static void AssertTriangulated(BridgeFixture fixture)
        {
            Dictionary<int, Vec3> positions = fixture.Nodes.ToDictionary(n => n.Id, n => n.Position);
            Dictionary<int, List<int>> adjacency = Adjacency(fixture);
            Assert.True(fixture.Members.Length >= 3, Fixture5 + " needs at least three members.");
            Assert.Single(Components(adjacency));
            foreach (Member member in fixture.Members)
            {
                Vec3 a = positions[member.NodeA];
                Vec3 b = positions[member.NodeB];
                bool inTriangle = adjacency[member.NodeA].Any(k =>
                    k != member.NodeB
                    && adjacency[member.NodeB].Contains(k)
                    && Vec3.Cross(b - a, positions[k] - a).Length > 1e-9);
                Assert.True(inTriangle, Fixture5 + " member " + member.Id + " is not a side of a triangle.");
            }
        }

        private static void AssertSinglePierToFixedGroundAnchor(BridgeFixture fixture)
        {
            Dictionary<int, Vec3> positions = fixture.Nodes.ToDictionary(n => n.Id, n => n.Position);
            HashSet<int> anchors = fixture.Nodes.Where(n => n.Fixed && n.GroundAnchor).Select(n => n.Id).ToHashSet();
            Assert.NotEmpty(anchors);
            Member pier = Assert.Single(fixture.Members, m => anchors.Contains(m.NodeA) || anchors.Contains(m.NodeB));
            Vec3 axis = positions[pier.NodeB] - positions[pier.NodeA];
            Assert.True(axis.X == 0.0 && axis.Z == 0.0 && axis.Y != 0.0, Fixture7 + " pier must be vertical.");
        }

        private static void AssertContract(Type type, params (string Name, Type Type)[] properties)
        {
            Assert.Equal("BridgeBuilder.Core", type.Namespace);
            Assert.True(type.IsClass, type.Name + " should be a class.");
            Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
            foreach ((string name, Type propertyType) in properties)
            {
                PropertyInfo property = type.GetProperty(name);
                Assert.True(property != null, type.Name + "." + name + " is missing.");
                Assert.Equal(propertyType, property.PropertyType);
                Assert.True(property.GetGetMethod() != null && property.GetSetMethod() != null, type.Name + "." + name + " needs a public getter and setter.");
            }
        }

        private static MethodInfo AssertMethod(Type type, string name, Type returnType, params Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(name, parameterTypes);
            Assert.True(method != null, type.Name + "." + name + " with the issue's parameter types is missing.");
            Assert.Equal(returnType, method.ReturnType);
            return method;
        }
    }
}
