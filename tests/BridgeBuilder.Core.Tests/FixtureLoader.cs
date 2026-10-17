using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridgeBuilder.Core;

namespace BridgeBuilder.Core.Tests
{
    /// <summary>
    /// Loads the JSON fixture bridges (spec 5). Positions are room units; unknown properties are ignored so later
    /// issues can add fields without editing this file.
    /// </summary>
    public static class FixtureLoader
    {
        public sealed class FixtureNode
        {
            public int Id { get; set; }
            public double[] Position { get; set; }
            public bool Fixed { get; set; }
            public int? SurfaceId { get; set; }
            public bool GroundAnchor { get; set; }
        }

        public sealed class FixtureMember
        {
            public int Id { get; set; }
            public int NodeA { get; set; }
            public int NodeB { get; set; }
            public int SectionId { get; set; }
            public int LineId { get; set; } = -1;
            public bool HiddenBrace { get; set; }
        }

        public sealed class FixtureFile
        {
            public string Name { get; set; }
            public string Note { get; set; }
            public double Scale { get; set; } = 1;
            public List<FixtureNode> Nodes { get; set; } = new List<FixtureNode>();
            public List<FixtureMember> Members { get; set; } = new List<FixtureMember>();
            public JsonElement Extra { get; set; }

            public SolverInput ToInput(params SectionProperties[] sections)
            {
                var input = new SolverInput { ScaleFactor = Scale };
                input.Nodes.AddRange(Nodes.Select(n => new Node
                {
                    Id = n.Id,
                    Position = new Vec3(n.Position[0], n.Position[1], n.Position[2]),
                    Fixed = n.Fixed,
                    SurfaceId = n.SurfaceId,
                    GroundAnchor = n.GroundAnchor,
                }));
                input.Members.AddRange(Members.Select(m => new Member
                {
                    Id = m.Id, NodeA = m.NodeA, NodeB = m.NodeB, SectionId = m.SectionId, LineId = m.LineId, HiddenBrace = m.HiddenBrace,
                }));
                input.Sections.AddRange(sections);
                return input;
            }
        }

        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static string Directory => Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        public static FixtureFile Load(string name) =>
            JsonSerializer.Deserialize<FixtureFile>(File.ReadAllText(Path.Combine(Directory, name)), Options);

        public static readonly string[] All =
        {
            "fixture-01-bare-cantilevers.json",
            "fixture-02-fixed-end-beams.json",
            "fixture-03-unbraced-rectangle.json",
            "fixture-04-buckling-columns.json",
            "fixture-05-triangulated-truss.json",
            "fixture-06-progressive-failure.json",
            "fixture-07-pier.json",
        };
    }
}
