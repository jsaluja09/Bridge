using System;
using System.Collections.Generic;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Learn mode drawing: the lesson bridge on its tabletop, built step by step and colored by what each part does
    /// (blue pulls, orange pushes, purple bends, grey holds), the step's highlights and labels, the load demo, its
    /// chart, and a scale bar. Everything goes through BridgeView, so it is one mesh, rebuilt each frame.
    /// </summary>
    public sealed partial class LearnMode
    {
        [Header("Look")]
        [SerializeField] Color pullColor = new Color(0.25f, 0.55f, 1f);
        [SerializeField] Color pushColor = new Color(1f, 0.55f, 0.15f);
        [SerializeField] Color bendColor = new Color(0.7f, 0.4f, 0.95f);
        [SerializeField] Color holdColor = new Color(0.62f, 0.62f, 0.64f);
        [SerializeField] Color waterColor = new Color(0.12f, 0.3f, 0.5f);
        [SerializeField] Color roadColor = new Color(0.3f, 0.3f, 0.33f);
        [SerializeField] Color dimColor = new Color(0.35f, 0.35f, 0.38f);
        [Tooltip("Thinnest tube drawn, so cables and hangers stay visible on a scaled-down bridge.")]
        [SerializeField] float minRadius = 0.0012f;

        /// <summary>Where the lesson bridge sits: real meters to room meters.</summary>
        struct Table
        {
            public Vector3 Center;      // the water's surface under the middle of the bridge
            public Vector3 Along;       // the bridge's length runs this way (the player's right)
            public Vector3 Across;      // away from the player
            public float Scale;         // room meters per real meter
            public double MidX;         // the bridge's middle, real x

            public Vector3 Place(Vec3 p) =>
                Center + Along * (float)((p.X - MidX) * Scale) + Vector3.up * (float)(p.Y * Scale) + Across * (float)(p.Z * Scale);
        }

        Table _table;
        readonly Dictionary<string, Vec3> _anchors = new Dictionary<string, Vec3>();
        double _scaleMeters;            // the scale bar's real length, and its label: set once per lesson
        string _scaleText = "";
        static readonly LessonPart CarPart = new LessonPart { Flexible = true };     // cars ride the flexible deck

        /// <summary>Sets the table in front of the player, a little below eye height, the bridge running left to right.</summary>
        void PlaceTable(LessonBridge bridge, Camera cam)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            var (min, max) = bridge.Bounds();
            _table = new Table
            {
                Center = cam.transform.position + forward * 1.1f - Vector3.up * 0.55f,
                Along = Vector3.Cross(Vector3.up, forward).normalized,
                Across = forward,
                Scale = (float)bridge.FitScale(tableLength),
                MidX = (min.X + max.X) / 2,
            };
            _anchors.Clear();
            foreach (var part in bridge.Parts)
                _anchors[part.Id] = part.Anchor();
            double length = _player.Lesson.RealLength;
            _scaleMeters = RoundLength(length / 4);
            _scaleText = $"{_scaleMeters:N0} m   ·   whole bridge {length:N0} m";
        }

        void Draw(double now, Camera cam)
        {
            _view.Begin();
            _labels.Begin();
            if (_inLesson)
            {
                double t = _player.StepTime(now);
                DrawBridge(now, t);
                DrawDemo(t);
                DrawLabels(now, cam);
                DrawPlot(cam);
                DrawScaleBar(cam);
            }
            _labels.End();
            _view.End();
            UpdatePanel(cam);
        }

        Color RoleColor(PartRole role) => role switch
        {
            PartRole.Pull => pullColor,
            PartRole.Push => pushColor,
            PartRole.Bend => bendColor,
            PartRole.Hold => holdColor,
            _ => waterColor,
        };

        /// <summary>
        /// Every part that is built (or being built): segments grow from their first end one after another as the
        /// group builds; highlighted parts pulse, the rest dim while a step is pointing at something.
        /// </summary>
        void DrawBridge(double now, double t)
        {
            var step = _player.Step;
            var bridge = _player.Lesson.Bridge;
            bool focus = step.Highlight.Length > 0;
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)t * 5f);
            foreach (var part in bridge.Parts)
            {
                double built = _player.Built(part.Group, now);
                if (built <= 0)
                    continue;
                bool lit = !focus || part.Role == PartRole.Scenery || Array.IndexOf(step.Highlight, part.Id) >= 0;
                Color color = RoleColor(part.Role);
                if (!lit)
                    color = Color.Lerp(color, dimColor, 0.7f);
                else if (focus && part.Role != PartRole.Scenery)
                    color = Color.Lerp(color, Color.white, 0.25f * pulse);
                float radius = Mathf.Max(minRadius, (float)(part.Radius * _table.Scale));

                double reveal = built * part.Segments.Count;
                for (int i = 0; i < part.Segments.Count && i < reveal; i++)
                {
                    var s = part.Segments[i];
                    double f = Math.Min(1, reveal - i);
                    Vec3 end = f < 1 ? Vec3.Lerp(s.A, s.B, f) : s.B;
                    _view.Tube(Place(part, s.A, step.Demo, t), Place(part, end, step.Demo, t), radius, color);
                }

                Color faceColor = part.Role == PartRole.Scenery ? waterColor : part.Role == PartRole.Bend ? roadColor : color * 0.85f;
                double faces = built * part.Faces.Count;
                for (int i = 0; i < part.Faces.Count && i < faces; i++)
                {
                    var face = part.Faces[i];
                    _view.Triangle(Place(part, face.A, step.Demo, t), Place(part, face.B, step.Demo, t), Place(part, face.C, step.Demo, t), faceColor);
                }
            }
        }

        Vector3 Place(LessonPart part, Vec3 p, LoadDemo demo, double t) =>
            _table.Place(LessonMotion.Displace(_player.Lesson.Bridge, part, p, demo, t));

        /// <summary>Traffic as small colored cars riding the sagging deck, or wind as streaks blowing across.</summary>
        void DrawDemo(double t)
        {
            var bridge = _player.Lesson.Bridge;
            switch (_player.Step.Demo)
            {
                case LoadDemo.Cars:
                    for (int c = 0; c < LessonMotion.Cars; c++)
                    {
                        double x = LessonMotion.CarAlong(bridge, c, t);
                        double lane = c % 2 == 0 ? 4 : -4;
                        var p = new Vec3(x, bridge.DeckY + 2, lane);
                        Color color = Color.HSVToRGB(c / (float)LessonMotion.Cars, 0.7f, 1f);
                        _view.Dot(Place(CarPart, p, LoadDemo.Cars, t), minRadius * 3f, color);
                    }
                    break;
                case LoadDemo.Wind:
                    for (int i = 0; i < LessonMotion.Streaks; i++)
                    {
                        var (tail, head) = LessonMotion.Streak(bridge, i, t);
                        _view.Tube(_table.Place(tail), _table.Place(head), minRadius * 0.6f, new Color(0.85f, 0.92f, 1f));
                    }
                    break;
            }
        }

        /// <summary>Each label above its part (stacked so they don't overlap), with a thin line down to it, once the part is built.</summary>
        void DrawLabels(double now, Camera cam)
        {
            var step = _player.Step;
            var bridge = _player.Lesson.Bridge;
            for (int i = 0; i < step.Labels.Length; i++)
            {
                var label = step.Labels[i];
                if (_player.Built(bridge.Part(label.PartId).Group, now) < 1)
                    continue;
                Vector3 anchor = _table.Place(_anchors[label.PartId]);
                Vector3 at = anchor + Vector3.up * (0.12f + 0.05f * i);
                _view.Tube(anchor, at - Vector3.up * 0.015f, minRadius * 0.5f, Color.white);
                _labels.Show(label.Text, at, cam, Color.white);
            }
        }

        // ---------- Chart ----------

        const float PlotWidth = 0.42f, PlotHeight = 0.26f;
        LessonPlot _plotFor;            // the chart whose range below was worked out
        double _plotX0, _plotX1, _plotY0, _plotY1;
        Vector3 _plotOrigin;

        /// <summary>The step's chart on a small board beside the bridge: axes, each curve, its title and labels.</summary>
        void DrawPlot(Camera cam)
        {
            var plot = _player.Step.Plot;
            if (plot == null)
                return;
            if (plot != _plotFor)
                MeasurePlot(plot);
            _plotOrigin = _table.Center + _table.Along * (tableLength / 2 + 0.12f) + Vector3.up * 0.12f;

            Color axis = new Color(0.8f, 0.8f, 0.82f);
            _view.Tube(_plotOrigin, _plotOrigin + _table.Along * PlotWidth, minRadius * 0.6f, axis);
            _view.Tube(_plotOrigin, _plotOrigin + Vector3.up * PlotHeight, minRadius * 0.6f, axis);
            for (int s = 0; s < plot.Series.Count; s++)
            {
                var points = plot.Series[s].points;
                Color color = s == 0 ? pullColor : pushColor;
                for (int i = 1; i < points.Count; i++)
                    _view.Tube(PlotPoint(points[i - 1].x, points[i - 1].y), PlotPoint(points[i].x, points[i].y), minRadius, color);
            }
            Vector3 middle = _plotOrigin + _table.Along * (PlotWidth / 2);
            _labels.Show(plot.Title, middle + Vector3.up * (PlotHeight + 0.04f), cam, Color.white);
            _labels.Show(plot.Series[0].name, middle + Vector3.up * (PlotHeight * 0.5f), cam, pullColor);
            _labels.Show(plot.XLabel, middle - Vector3.up * 0.03f, cam, axis);
        }

        /// <summary>The range every curve of a chart covers, so its curves fill the board.</summary>
        void MeasurePlot(LessonPlot plot)
        {
            _plotX0 = _plotY0 = double.MaxValue;
            _plotX1 = _plotY1 = double.MinValue;
            foreach (var (_, points) in plot.Series)
            {
                foreach (var (x, y) in points)
                {
                    _plotX0 = Math.Min(_plotX0, x);
                    _plotX1 = Math.Max(_plotX1, x);
                    _plotY0 = Math.Min(_plotY0, y);
                    _plotY1 = Math.Max(_plotY1, y);
                }
            }
            _plotFor = plot;
        }

        Vector3 PlotPoint(double x, double y)
        {
            double u = (x - _plotX0) / Math.Max(1e-9, _plotX1 - _plotX0);
            double v = 0.1 + 0.85 * (y - _plotY0) / Math.Max(1e-9, _plotY1 - _plotY0);
            return _plotOrigin + _table.Along * (float)(PlotWidth * u) + Vector3.up * (float)(PlotHeight * v);
        }

        // ---------- Scale ----------

        /// <summary>A bar of a round real length along the front of the table, so a 2 km bridge and a 250 m one read right.</summary>
        void DrawScaleBar(Camera cam)
        {
            Vector3 start = _table.Center - _table.Along * (tableLength / 2) - _table.Across * 0.18f;
            Vector3 end = start + _table.Along * (float)(_scaleMeters * _table.Scale);
            Color color = new Color(0.9f, 0.9f, 0.9f);
            _view.Tube(start, end, minRadius, color);
            _view.Tube(start, start + Vector3.up * 0.015f, minRadius, color);
            _view.Tube(end, end + Vector3.up * 0.015f, minRadius, color);
            _labels.Show(_scaleText, (start + end) / 2 + Vector3.up * 0.035f, cam, color);
        }

        /// <summary>1, 2 or 5 times a power of ten, not above `limit`.</summary>
        static double RoundLength(double limit)
        {
            double power = Math.Pow(10, Math.Floor(Math.Log10(limit)));
            if (5 * power <= limit)
                return 5 * power;
            return 2 * power <= limit ? 2 * power : power;
        }
    }
}
