using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>What moves during a step, to show the load the step is about.</summary>
    public enum LoadDemo
    {
        None,
        Cars,           // traffic rolls across; the main span sags under it (exaggerated)
        Wind,           // streaks blow across; the main span sways sideways (exaggerated)
        Temperature,    // the deck or arch grows and shrinks with a day of heat and cold (exaggerated)
        Bascules,       // Tower Bridge's road lifts and lowers
        TowerSway,      // a tower rocks, then settles once its damper works
        Flutter,        // the deck twists further and further each swing
    }

    /// <summary>A label pointing at a part.</summary>
    public readonly struct LessonLabel
    {
        public readonly string PartId;
        public readonly string Text;

        public LessonLabel(string partId, string text)
        {
            PartId = partId;
            Text = text;
        }
    }

    /// <summary>A small chart for a step: one or more curves through points.</summary>
    public sealed class LessonPlot
    {
        public string Title = "";
        public string XLabel = "";
        public string YLabel = "";
        public readonly List<(string name, List<(double x, double y)> points)> Series = new List<(string, List<(double, double)>)>();
    }

    /// <summary>
    /// One click of a lesson: what to read (title, plain words, one formula with real numbers), which groups of
    /// parts get built during it, which parts glow, the labels, and the load demo or chart that goes with it.
    /// </summary>
    public sealed class LessonStep
    {
        public string Title = "";
        public string Text = "";
        public string Formula = "";
        public int[] Builds = new int[0];
        public string[] Highlight = new string[0];
        public LessonLabel[] Labels = new LessonLabel[0];
        public LoadDemo Demo;
        public LessonPlot Plot;
    }

    /// <summary>A famous bridge and the steps that explain it.</summary>
    public sealed class Lesson
    {
        public string Title = "";
        public string Place = "";
        public int Year;
        public double RealLength;            // meters, for the scale bar
        public LessonBridge Bridge;
        public readonly List<LessonStep> Steps = new List<LessonStep>();
    }
}
