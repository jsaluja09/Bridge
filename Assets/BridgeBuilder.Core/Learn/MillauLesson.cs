using System.Collections.Generic;
using static BridgeBuilder.Core.LessonShapes;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// The Millau Viaduct (France, 2004): a multi-span cable-stayed bridge over the Tarn valley. Rounded public
    /// figures, to be fact-checked before students see them:
    ///   length 2,460 m in eight spans, two end spans of 204 m and six of 342 m; seven concrete piers, the tallest
    ///   (P2) about 245 m; masts 87 m above the deck, 11 pairs of stays each in one plane down the middle; highest
    ///   point about 343 m above the river; steel box deck 32 m wide, 4.2 m deep, about 36,000 t, pushed out over
    ///   the piers from both sides; each pier splits into two thinner columns over its top 90 m; 3 m wind screens.
    ///   Engineer Michel Virlogeux, architect Norman Foster.
    ///   Pier heights used (m): 94, 245, 221, 144, 136, 112, 78. The deck is drawn level at 270 m above the river.
    /// </summary>
    public static class MillauLesson
    {
        const double EndSpan = 204, Span = 342, Length = 2 * EndSpan + 6 * Span;
        const double DeckY = 270, DeckDepth = 4.2, HalfWidth = 16, MastHeight = 87, SplitHeight = 90;
        static readonly double[] PierHeights = { 94, 245, 221, 144, 136, 112, 78 };

        const int Valley = 0, Piers = 1, Deck = 2, Masts = 3, Stays = 4, Screens = 5;

        static double PierX(int i) => EndSpan + i * Span;

        public static Lesson Build()
        {
            var bridge = Geometry();
            var lesson = new Lesson { Title = "Millau Viaduct", Place = "Tarn valley, France", Year = 2004, RealLength = Length, Bridge = bridge };

            lesson.Steps.Add(new LessonStep
            {
                Title = "Over a valley, not a river",
                Text = "The A75 motorway had to cross the Tarn valley, 2.5 km wide and 270 m deep. Winding down and back up " +
                       "meant traffic jams every summer, so the engineers carried the road straight across, high in the air.",
                Formula = "Highest point ≈ 343 m above the river: taller than the Eiffel Tower",
                Builds = new[] { Valley },
                Labels = new[] { new LessonLabel("valley", "Tarn valley: 270 m deep") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Concrete piers push",
                Text = "Seven concrete piers carry the road, the tallest about 245 m. Concrete is strong when squeezed, and " +
                       "each pier is widest at the bottom, where it carries its own weight as well as the road's.",
                Formula = "Stress  σ = F / A   ·   a wider base spreads the same force over more area",
                Builds = new[] { Piers },
                Highlight = new[] { "piers" },
                Labels = new[] { new LessonLabel("piers", "Piers: up to 245 m, squeezed") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Pushed out, not lifted",
                Text = "The 36,000 t steel deck was assembled on both hilltops and pushed out over the piers, about 60 cm " +
                       "at a time, by hydraulic jacks on every pier, with temporary towers halfway between them.",
                Formula = "Sliding needs  F = μ·N   ·   low-friction pads keep μ small, so modest jacks push a huge deck",
                Builds = new[] { Deck },
                Highlight = new[] { "deck" },
                Labels = new[] { new LessonLabel("deck", "Steel deck: 32 m wide, 4.2 m deep") },
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Masts and stays pull",
                Text = "On each pier an 87 m mast holds 11 pairs of stays in one line down the middle of the road. Each stay " +
                       "pulls up a slice of deck; the flatter a stay lies, the harder it must pull to hold the same weight.",
                Formula = "Stay pull  T = W / sin θ   ·   at 30° a stay pulls twice the weight it holds",
                Builds = new[] { Masts, Stays },
                Highlight = new[] { "masts", "stays" },
                Labels = new[] { new LessonLabel("masts", "Masts: 87 m above the road"), new LessonLabel("stays", "Stays: 11 pairs per mast") },
                Plot = StayPlot(),
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Split piers let it breathe",
                Text = "Steel grows in the heat and shrinks in the cold: the 2.5 km deck changes length by about a meter. The " +
                       "top 90 m of each pier splits into two thinner columns that bend to let it move (shown 100× here).",
                Formula = "ΔL = α·L·ΔT ≈ 12×10⁻⁶ × 2,460 m × 35 °C ≈ 1 m",
                Highlight = new[] { "piers", "deck" },
                Demo = LoadDemo.Temperature,
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "Shaped for the wind",
                Text = "High winds blow through the valley. 3 m screens along the edges shield the traffic, and the deck is " +
                       "shaped like an upside-down aircraft wing, so wind presses it down rather than lifting or twisting it.",
                Formula = "Wind pressure  q = ½ρv²   ·   at 150 km/h about 1.1 kPa on every square meter",
                Builds = new[] { Screens },
                Highlight = new[] { "screens", "deck" },
                Labels = new[] { new LessonLabel("screens", "Wind screens: 3 m high") },
                Demo = LoadDemo.Wind,
            });
            lesson.Steps.Add(new LessonStep
            {
                Title = "How it all works",
                Text = "The road hangs from the stays, the stays from the masts, and the masts sit on the piers, which carry " +
                       "everything down into the rock. Engineer Michel Virlogeux and architect Norman Foster, opened in 2004.",
                Formula = "Road → stays → masts → piers ↓ rock",
                Highlight = new[] { "stays", "masts", "piers" },
            });
            return lesson;
        }

        static LessonBridge Geometry()
        {
            var b = new LessonBridge
            {
                Name = "Millau Viaduct",
                MainSpanStart = 0,
                MainSpanEnd = Length,
                DeckY = DeckY,
                LiveLoadSag = 0,
                WindSway = 0,               // a stiff box deck: the wind demo shows the wind, not a sway
                ThermalCenter = Length / 2,
                ThermalFlexHeight = SplitHeight,
            };

            // The valley floor under each pier, from the pier heights; the hills meet the road at both ends.
            var valley = b.Add("valley", "Tarn valley", PartRole.Scenery, Valley, 0);
            var profile = new List<Vec3> { new Vec3(-60, DeckY - 8, 0) };
            for (int i = 0; i < PierHeights.Length; i++)
                profile.Add(new Vec3(PierX(i), DeckY - PierHeights[i], 0));
            profile.Add(new Vec3(Length + 60, DeckY - 8, 0));
            for (int i = 1; i < profile.Count; i++)
            {
                Vec3 a = profile[i - 1], c = profile[i];
                Quad(valley, new Vec3(a.X, a.Y, -120), new Vec3(c.X, c.Y, -120), new Vec3(c.X, c.Y, 120), new Vec3(a.X, a.Y, 120));
            }

            // Each pier: one column up from the valley, splitting into two for its top 90 m.
            var piers = b.Add("piers", "Piers", PartRole.Push, Piers, 9, flexible: true);
            for (int i = 0; i < PierHeights.Length; i++)
            {
                double x = PierX(i), foot = DeckY - PierHeights[i], split = DeckY - SplitHeight;
                if (split > foot)
                    Line(piers, new Vec3(x, foot, 0), new Vec3(x, split, 0));
                double from = System.Math.Max(foot, split);
                Line(piers, new Vec3(x, from, 0), new Vec3(x - 6, DeckY - DeckDepth, 0));
                Line(piers, new Vec3(x, from, 0), new Vec3(x + 6, DeckY - DeckDepth, 0));
            }

            var deck = b.Add("deck", "Steel deck", PartRole.Bend, Deck, 2, flexible: true);
            const double piece = 82;
            for (double x = 0; x < Length - 1e-6; x += piece)
            {
                double x1 = System.Math.Min(Length, x + piece);
                Quad(deck, new Vec3(x, DeckY, -HalfWidth), new Vec3(x1, DeckY, -HalfWidth), new Vec3(x1, DeckY, HalfWidth), new Vec3(x, DeckY, HalfWidth));
            }
            foreach (double z in new[] { -HalfWidth, HalfWidth })
            {
                Line(deck, new Vec3(0, DeckY, z), new Vec3(Length, DeckY, z));
                Line(deck, new Vec3(0, DeckY - DeckDepth, z), new Vec3(Length, DeckY - DeckDepth, z));
            }

            // Masts on the piers, and a fan of 11 stays each way in one plane down the middle (z = 0).
            var masts = b.Add("masts", "Masts", PartRole.Push, Masts, 4, flexible: true);
            var stays = b.Add("stays", "Stays", PartRole.Pull, Stays, 0.8, flexible: true);
            for (int i = 0; i < PierHeights.Length; i++)
            {
                double x = PierX(i);
                Line(masts, new Vec3(x, DeckY, 0), new Vec3(x, DeckY + MastHeight, 0));
                for (int k = 0; k < 11; k++)
                {
                    double top = DeckY + 30 + 55 * k / 10.0;          // anchored up the top two thirds of the mast
                    double reach = 25 + 140 * k / 10.0;               // the higher the anchor, the farther out it holds
                    Line(stays, new Vec3(x, top, 0), new Vec3(x - reach, DeckY, 0));
                    Line(stays, new Vec3(x, top, 0), new Vec3(x + reach, DeckY, 0));
                }
            }

            var screens = b.Add("screens", "Wind screens", PartRole.Hold, Screens, 0.4, flexible: true);
            foreach (double z in new[] { -HalfWidth, HalfWidth })
            {
                for (double x = 0; x < Length - 1e-6; x += 82)
                    Quad(screens, new Vec3(x, DeckY, z), new Vec3(System.Math.Min(Length, x + 82), DeckY, z),
                         new Vec3(System.Math.Min(Length, x + 82), DeckY + 3, z), new Vec3(x, DeckY + 3, z));
            }
            return b;
        }

        /// <summary>How hard a stay pulls for each unit of weight it holds, against how steeply it lies.</summary>
        static LessonPlot StayPlot()
        {
            var plot = new LessonPlot { Title = "Stay pull per unit of weight held", XLabel = "stay angle (degrees)", YLabel = "T / W" };
            var points = new List<(double, double)>();
            for (int angle = 20; angle <= 90; angle += 5)
                points.Add((angle, 1 / System.Math.Sin(angle * System.Math.PI / 180)));
            plot.Series.Add(("T / W = 1 / sin θ", points));
            return plot;
        }
    }
}
