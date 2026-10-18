using System.Collections.Generic;
using static BridgeBuilder.Core.LessonShapes;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// The Golden Gate Bridge (San Francisco, 1937): a suspension bridge. Rounded public figures, to be fact-checked
    /// before students see them:
    ///   main span 1,280 m (4,200 ft); side spans 343 m (1,125 ft); towers 227 m (746 ft) above the water;
    ///   deck about 67 m (220 ft) above the water; two main cables 0.92 m (36 3/8 in) across, 27,572 wires each,
    ///   about 27 m (90 ft) apart; suspender ropes every 15 m (50 ft); stiffening truss 7.6 m (25 ft) deep;
    ///   anchorages about 60,000 t (short tons) each; the deck can drop about 3.3 m (10.8 ft) and swing about
    ///   8.4 m (27.7 ft) sideways. Cable sag about a ninth of the span. Paint: "International Orange".
    /// </summary>
    public static class GoldenGateLesson
    {
        const double Side = 343, Main = 1280, Length = Side + Main + Side;
        const double TowerTop = 227, DeckY = 67, CableZ = 13.7, Sag = Main / 9;
        const double AnchorY = 60, SideSag = 15, HangerSpacing = 15, TrussDepth = 7.6;

        // Groups, in build order.
        const int Setting = 0, Towers = 1, Anchorages = 2, Cables = 3, Hangers = 4, Deck = 5, Truss = 6;

        public static Lesson Build()
        {
            var bridge = Geometry();
            var lesson = new Lesson { Title = "Golden Gate Bridge", Place = "San Francisco, USA", Year = 1937, RealLength = 2737, Bridge = bridge };

            lesson.Steps.Add(new LessonStep
            {
                Title = "A gap with no middle",
                Text = "The Golden Gate strait is about 1.6 km wide, deep, with strong tides, and ships need about 67 m of " +
                       "clearance. Piers in the middle were out, so the bridge needed one enormous span.",
                Formula = "Main span = 1,280 m: the longest in the world when it opened",
                Builds = new[] { Setting },
                Labels = new[] { new LessonLabel("water", "Golden Gate strait") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Towers push",
                Text = "Two steel towers, 227 m tall, carry almost the whole bridge's weight straight down to the seabed. " +
                       "They are squeezed. A tall, thin column fails by bending sideways (buckling), so cross-beams brace the legs.",
                Formula = "Buckling load  P = π²EI / L²   ·   halve the free length L and a leg is 4× harder to buckle",
                Builds = new[] { Towers },
                Highlight = new[] { "towers" },
                Labels = new[] { new LessonLabel("towers", "Towers: 227 m, squeezed") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Anchorages hold",
                Text = "The cables end in a concrete block on each shore, about 60,000 t each. The cables try to pull the " +
                       "blocks toward the water; their weight pressing on the rock holds them still.",
                Formula = "Grip from weight  F = μ·W   ·   more weight, more grip",
                Builds = new[] { Anchorages },
                Highlight = new[] { "anchorages" },
                Labels = new[] { new LessonLabel("anchorages", "Anchorage: ~60,000 t of concrete") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Cables pull",
                Text = "Two cables, 92 cm thick, each spun from 27,572 steel wires. Steel is strongest when pulled, and a " +
                       "cable can only pull, so the cables carry the road in pure tension, draped over the towers.",
                Formula = "Cable pull  H = wL² / 8d   ·   sag d ≈ L/9, so H ≈ 1.1 × the weight of the whole span",
                Builds = new[] { Cables },
                Highlight = new[] { "cables" },
                Labels = new[] { new LessonLabel("cables", "Main cables: 27,572 wires each, pulled") },
                Plot = CablePlot(),
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Hangers carry the road",
                Text = "Vertical ropes every 15 m hang the road from the cables. The road's weight is spread evenly along it, " +
                       "and a cable carrying an even load hangs in a parabola.",
                Formula = "Cable shape  y = 4d · x(L − x) / L²",
                Builds = new[] { Hangers, Deck },
                Highlight = new[] { "hangers", "deck" },
                Labels = new[] { new LessonLabel("hangers", "Hangers: every 15 m"), new LessonLabel("deck", "Road: 67 m above the water") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "A truss keeps it flat",
                Text = "A steel truss 7.6 m deep runs under the road. It spreads a heavy truck over many hangers and stops " +
                       "the road twisting in the wind, the lesson Tacoma Narrows taught everyone in 1940.",
                Formula = "Bending stiffness grows with depth²: twice as deep ≈ 4× stiffer",
                Builds = new[] { Truss },
                Highlight = new[] { "truss" },
                Labels = new[] { new LessonLabel("truss", "Stiffening truss: 7.6 m deep") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Built to move",
                Text = "Under heavy traffic the middle of the road can drop about 3 m, and in a strong wind it can swing " +
                       "about 8 m sideways (shown 10× here). The steel stretches and springs back: moving is how it copes.",
                Formula = "Steel stretches  ΔL = F·L / (E·A)   ·   E ≈ 200 GPa",
                Highlight = new[] { "cables", "deck", "truss" },
                Demo = LoadDemo.Cars,
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "How it all works",
                Text = "The road hangs from the hangers, the hangers from the cables, the cables push down on the towers and " +
                       "pull on the anchorages, and all of it ends in rock. The orange paint was picked to stand out in fog.",
                Formula = "Road → hangers → cables → towers ↓ and anchorages ← → rock",
                Highlight = new[] { "towers", "cables", "anchorages" },
                Demo = LoadDemo.Wind,
            });
            return lesson;
        }

        static LessonBridge Geometry()
        {
            var b = new LessonBridge
            {
                Name = "Golden Gate Bridge",
                MainSpanStart = Side,
                MainSpanEnd = Side + Main,
                DeckY = DeckY,
                LiveLoadSag = 3.3,
                WindSway = 8.4,
            };
            double t1 = Side, t2 = Side + Main;

            var water = b.Add("water", "Water", PartRole.Scenery, Setting, 0);
            Quad(water, new Vec3(-80, 0, -160), new Vec3(Length + 80, 0, -160), new Vec3(Length + 80, 0, 160), new Vec3(-80, 0, 160));

            var towers = b.Add("towers", "Towers", PartRole.Push, Towers, 5);
            foreach (double x in new[] { t1, t2 })
            {
                foreach (double z in new[] { -CableZ, CableZ })
                    Line(towers, new Vec3(x, 0, z), new Vec3(x, TowerTop, z));
                foreach (double y in new[] { 45.0, 110, 150, 190, TowerTop - 4 })
                    Line(towers, new Vec3(x, y, -CableZ), new Vec3(x, y, CableZ));
            }

            var anchorages = b.Add("anchorages", "Anchorages", PartRole.Hold, Anchorages, 0);
            Box(anchorages, new Vec3(-60, 0, -32), new Vec3(5, AnchorY + 5, 32));
            Box(anchorages, new Vec3(Length - 5, 0, -32), new Vec3(Length + 60, AnchorY + 5, 32));

            var cables = b.Add("cables", "Main cables", PartRole.Pull, Cables, 2.5, flexible: true);
            var hangers = b.Add("hangers", "Hangers", PartRole.Pull, Hangers, 0.8, flexible: true);
            foreach (double z in new[] { -CableZ, CableZ })
            {
                Vec3 a0 = new Vec3(0, AnchorY, z), top1 = new Vec3(t1, TowerTop, z), top2 = new Vec3(t2, TowerTop, z), a1 = new Vec3(Length, AnchorY, z);
                Polyline(cables, Cable(a0, top1, SideSag, 12));
                Polyline(cables, Cable(top1, top2, Sag, 48));
                Polyline(cables, Cable(top2, a1, SideSag, 12));
                LessonShapes.Hangers(hangers, a0, top1, SideSag, DeckY, 0, t1, HangerSpacing);
                LessonShapes.Hangers(hangers, top1, top2, Sag, DeckY, t1, t2, HangerSpacing);
                LessonShapes.Hangers(hangers, top2, a1, SideSag, DeckY, t2, Length, HangerSpacing);
            }

            var deck = b.Add("deck", "Road deck", PartRole.Bend, Deck, 1.5, flexible: true);
            const double piece = 60;
            for (double x = 0; x < Length - 1e-6; x += piece)
            {
                double x1 = System.Math.Min(Length, x + piece);
                Quad(deck, new Vec3(x, DeckY, -CableZ), new Vec3(x1, DeckY, -CableZ), new Vec3(x1, DeckY, CableZ), new Vec3(x, DeckY, CableZ));
            }
            foreach (double z in new[] { -CableZ, CableZ })
                Line(deck, new Vec3(0, DeckY, z), new Vec3(Length, DeckY, z));

            var truss = b.Add("truss", "Stiffening truss", PartRole.Bend, Truss, 0.8, flexible: true);
            foreach (double z in new[] { -CableZ, CableZ })
                LessonShapes.Truss(truss, 0, Length, DeckY, TrussDepth, z, 30);
            return b;
        }

        /// <summary>The main cable's shape: height above the road along the main span, from the parabola.</summary>
        static LessonPlot CablePlot()
        {
            var plot = new LessonPlot { Title = "Main cable shape", XLabel = "along the span (m)", YLabel = "height (m)" };
            var points = new List<(double, double)>();
            for (int i = 0; i <= 32; i++)
            {
                double x = Main * i / 32;
                points.Add((x, TowerTop - 4 * Sag * x * (Main - x) / (Main * Main)));
            }
            plot.Series.Add(("y = 4d·x(L − x)/L²", points));
            return plot;
        }
    }
}
