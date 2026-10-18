using System.Collections.Generic;
using System.Linq;

namespace BridgeBuilder.Core
{
    /// <summary>A deck as solver indices: triangles over SolverInput.Nodes and, per triangle edge, the member index (-1 = none).</summary>
    public sealed class DeckGeometry
    {
        public int[] Triangles;
        public int[] TriangleEdges;
        public double Area;
        public int SectionId;           // the deck's material
    }

    /// <summary>A bridge ready to simulate or draw: the solver input plus the decks and road check.</summary>
    public sealed class CompiledBridge
    {
        public SolverInput Input;
        public List<DeckGeometry> Decks = new List<DeckGeometry>();
        public RoadReport Road;
        public IReadOnlyList<Pad> Pads;
        public int ModelVersion;
        public Dictionary<int, int> NodeIndex = new Dictionary<int, int>();

        public double DeckArea => Decks.Sum(d => d.Area);

        /// <summary>What the automatic decks cost: each deck's room area at its material's area price.</summary>
        public double DeckCost(BudgetPolicy policy) => Decks.Sum(d => d.Area * policy.AreaCost(d.SectionId));
    }

    /// <summary>
    /// Turns the player's model and its automatic decks (spec 11.3) into SolverInput: one section per material,
    /// every member, the decks' hidden braces (spec 2.4) in the deck's material, and the decks as road fills.
    /// Room units in; the solver applies the scale.
    /// </summary>
    public static class BridgeCompiler
    {
        public const int DeckBraceIdBase = 1_000_000;
        public const int DeckFillIdBase = 2_000_000;

        public static CompiledBridge Compile(BridgeModel model, IReadOnlyList<Pad> pads, double scale)
        {
            var decks = AutoDeck.Find(model);
            var input = new SolverInput { ScaleFactor = scale };
            input.Sections.AddRange(Materials.Sections());

            var joined = new HashSet<(int, int)>();
            foreach (var m in model.Members)
            {
                input.Members.Add(m);
                joined.Add(Key(m.NodeA, m.NodeB));
            }
            int braceId = DeckBraceIdBase;
            foreach (var deck in decks)
            {
                foreach (var (a, b) in deck.Braces)
                {
                    if (!joined.Add(Key(a, b)) || (model.GetNode(a).Fixed && model.GetNode(b).Fixed))
                        continue;
                    input.Members.Add(new Member { Id = braceId++, NodeA = a, NodeB = b, SectionId = deck.SectionId, HiddenBrace = true });
                }
            }

            var used = new HashSet<int>();
            foreach (var m in input.Members)
            {
                used.Add(m.NodeA);
                used.Add(m.NodeB);
            }
            foreach (var deck in decks)
                used.UnionWith(deck.Loop);
            var compiled = new CompiledBridge { Input = input, Pads = pads, ModelVersion = model.Version };
            foreach (var n in model.Nodes)
            {
                if (!used.Contains(n.Id))
                    continue;
                compiled.NodeIndex[n.Id] = input.Nodes.Count;
                input.Nodes.Add(n);
            }

            var memberIndex = new Dictionary<(int, int), int>();
            for (int k = 0; k < input.Members.Count; k++)
                memberIndex[Key(input.Members[k].NodeA, input.Members[k].NodeB)] = k;

            for (int i = 0; i < decks.Count; i++)
            {
                var deck = decks[i];
                input.Fills.Add(new Fill { Id = DeckFillIdBase + i, Loop = new List<int>(deck.Loop), Road = true, Valid = true, SectionId = deck.SectionId });
                var g = new DeckGeometry
                {
                    Triangles = deck.Triangles.Select(id => compiled.NodeIndex[id]).ToArray(),
                    TriangleEdges = new int[deck.Triangles.Count],
                    Area = deck.Area,
                    SectionId = deck.SectionId,
                };
                for (int t = 0; t < deck.Triangles.Count; t += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int a = deck.Triangles[t + e], b = deck.Triangles[t + (e + 1) % 3];
                        g.TriangleEdges[t + e] = memberIndex.TryGetValue(Key(a, b), out int k) ? k : -1;
                    }
                }
                compiled.Decks.Add(g);
            }

            int startPad = pads.Count > 0 ? pads[0].Id : 0, farPad = pads.Count > 1 ? pads[1].Id : 1;
            compiled.Road = RoadValidation.Validate(input.Fills, id => model.GetNode(id), startPad, farPad);
            return compiled;
        }

        static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
    }
}
