using System.Collections.Generic;
using BridgeBuilder.Core;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace BridgeBuilder.Game
{
    /// <summary>The build tools (spec 2.1, 2.4, 2.11): Draw, Fill, Road and Delete, plus undo, symmetry and clear.</summary>
    public sealed partial class BridgeGame
    {
        enum Tool { Draw, Fill, Road, Delete }

        static readonly string[] ToolNames = { "Draw", "Fill", "Road", "Delete" };

        Tool _tool;
        bool _symmetry;
        int _drawStart = -1;
        readonly List<Vector3> _stroke = new List<Vector3>();
        readonly List<Vector3> _rejected = new List<Vector3>();
        float _rejectedUntil;
        readonly List<int> _fillCorners = new List<int>();
        int _hoverNode = -1;
        int _hoverLine = -1;
        int _hoverFill = -1;
        List<int> _blocked = new List<int>();
        float _blockedUntil;
        float _clearArmedUntil;

        bool Drawing => _drawStart >= 0;
        float HoverRadius => settings.NodeSnapRadius * 1.25f;

        void ResetToolState()
        {
            _drawStart = -1;
            _stroke.Clear();
            _fillCorners.Clear();
            _hoverNode = _hoverLine = _hoverFill = -1;
        }

        void UpdateTools()
        {
            if (_pointer.ToolStep != 0 && !Drawing)
            {
                _tool = (Tool)(((int)_tool + _pointer.ToolStep + ToolNames.Length) % ToolNames.Length);
                ResetToolState();
                Play(snapClip);
            }
            if (_pointer.SymmetryPressed)
            {
                _symmetry = !_symmetry;
                Flash(_symmetry ? "Symmetry on: lines are mirrored across the road" : "Symmetry off");
            }

            switch (_tool)
            {
                case Tool.Draw: UpdateDraw(); break;
                case Tool.Fill: UpdateFill(); break;
                case Tool.Road: UpdateRoad(); break;
                case Tool.Delete: UpdateDelete(); break;
            }

            if (_pointer.SecondaryPressed && !Drawing)
            {
                if (_tool == Tool.Fill && _fillCorners.Count > 0)
                    _fillCorners.RemoveAt(_fillCorners.Count - 1);
                else if (_undo.Undo(_design))
                    Play(snapClip);
            }

            if (_pointer.SwapPressed)
            {
                _undo.Do(_design, new SetStartSurfaceEdit(1 - _design.StartSurface));
                Flash("The car now starts from the other side");
            }

            if (_pointer.ClearPressed)
            {
                if (_design.Lines.Count == 0)
                {
                    _phase = Phase.Brief;
                }
                else if (Time.time < _clearArmedUntil)
                {
                    _undo.Do(_design, new ClearEdit());
                    _clearArmedUntil = 0f;
                    ResetToolState();
                }
                else
                {
                    _clearArmedUntil = Time.time + 2f;
                    Flash("Press X again to clear the bridge (B undoes it)");
                }
            }
        }

        // ---------- Draw ----------

        void UpdateDraw()
        {
            NVector3 tip = _pointer.Tip.ToNumerics();
            _hoverNode = Drawing ? -1 : _design.FindNode(tip, HoverRadius);

            if (_pointer.TriggerPressed)
            {
                if (_hoverNode < 0)
                {
                    Flash("Start each line on a dot");
                    Play(errorClip);
                    return;
                }
                _drawStart = _hoverNode;
                _stroke.Clear();
            }
            else if (Drawing && _pointer.TriggerHeld)
            {
                if (_stroke.Count == 0 || Vector3.Distance(_stroke[_stroke.Count - 1], _pointer.Tip) > 0.005f)
                    _stroke.Add(_pointer.Tip);
            }
            else if (Drawing)
            {
                FinishStroke();
            }
        }

        void FinishStroke()
        {
            int start = _drawStart;
            _drawStart = -1;
            var raw = _stroke.ConvertAll(p => p.ToNumerics());
            if (raw.Count == 0)
                return;

            if (!StrokeRecognizer.TryRecognize(_design, start, raw, out var stroke, out string error))
            {
                Reject(error);
                return;
            }
            if (!Affordable(stroke, 0f))
            {
                Reject("Over budget");
                return;
            }

            var edits = new List<IEdit>();
            var line = BridgeEditing.CreateLine(_design, stroke);
            line.Apply(_design);
            edits.Add(line);

            if (_symmetry && BridgeEditing.TryMirror(_design, stroke, out var mirror) && Affordable(mirror, 0f))
            {
                var mirrored = BridgeEditing.CreateLine(_design, mirror);
                mirrored.Apply(_design);
                edits.Add(mirrored);
            }
            _undo.Push(edits.Count == 1 ? edits[0] : new CompositeEdit(edits));
            _stroke.Clear();
            Play(snapClip);
        }

        /// <summary>The budget (spec 2.9), which also enforces the hidden node cap.</summary>
        bool Affordable(RecognizedStroke stroke, float extraCost)
        {
            BridgeEditing.StrokeCost(_design, stroke, out float cost, out int nodes);
            return _design.Cost + cost + extraCost <= Challenge.Budget && _design.FreeNodeCount + nodes <= settings.NodeCap;
        }

        void Reject(string message)
        {
            Flash(message);
            Play(errorClip);
            _rejected.Clear();
            _rejected.AddRange(_stroke);
            _rejectedUntil = Time.time + 1f;
            _stroke.Clear();
        }

        // ---------- Fill: tap the corners, then the first corner again ----------

        void UpdateFill()
        {
            _hoverNode = _design.FindNode(_pointer.Tip.ToNumerics(), HoverRadius);
            if (!_pointer.TriggerPressed)
                return;

            if (_hoverNode < 0)
            {
                Flash("Tap the corners of a closed, flat shape");
                Play(errorClip);
            }
            else if (_fillCorners.Count >= 3 && _hoverNode == _fillCorners[0])
            {
                CompleteFill();
            }
            else if (_fillCorners.Count > 0 && _hoverNode == _fillCorners[_fillCorners.Count - 1])
            {
                _fillCorners.RemoveAt(_fillCorners.Count - 1);
            }
            else if (!_fillCorners.Contains(_hoverNode))
            {
                _fillCorners.Add(_hoverNode);
                Play(snapClip);
            }
        }

        void CompleteFill()
        {
            if (!BridgeEditing.TryBuildFill(_design, _fillCorners, out var fill, out string error))
            {
                Flash(error);
                Play(errorClip);
                return;
            }
            if (_design.Cost + fill.Area * settings.CostPerSquareMeter > Challenge.Budget)
            {
                Flash("Over budget");
                Play(errorClip);
                return;
            }
            _undo.Do(_design, new AddFillEdit(fill));
            _fillCorners.Clear();
            Flash("Filled. Use the Road tool to make it drivable.");
            Play(snapClip);
        }

        // ---------- Road: flag fills the car may drive on ----------

        void UpdateRoad()
        {
            _hoverFill = BridgeEditing.PickFill(_design, _pointer.Tip.ToNumerics(), 0.05f);
            if (!_pointer.TriggerPressed)
                return;
            if (_hoverFill < 0)
            {
                Flash("Touch a fill to make it road");
                Play(errorClip);
                return;
            }
            var fill = _design.GetFill(_hoverFill);
            _undo.Do(_design, new SetRoadEdit(fill, !fill.IsRoad));
            if (fill.IsRoad && fill.SlopeDeg > settings.MaxRoadSlopeDeg)
                Flash($"Too steep to drive ({fill.SlopeDeg:F0} degrees, max {settings.MaxRoadSlopeDeg:F0})");
            Play(snapClip);
        }

        // ---------- Delete: point at a line or fill ----------

        void UpdateDelete()
        {
            NVector3 tip = _pointer.Tip.ToNumerics();
            _hoverLine = BridgeEditing.PickLine(_design, tip, 0.03f);
            _hoverFill = _hoverLine < 0 ? BridgeEditing.PickFill(_design, tip, 0.04f) : -1;
            if (!_pointer.TriggerPressed)
                return;

            if (_hoverLine >= 0)
            {
                var edit = BridgeEditing.RemoveLine(_design, _hoverLine);
                if (edit == null)
                {
                    _blocked = BridgeEditing.LineDependents(_design, _hoverLine);
                    _blockedUntil = Time.time + 2f;
                    Flash("Other lines attach to this one. Delete those first.");
                    Play(errorClip);
                    return;
                }
                _undo.Do(_design, edit);
                if (edit.RemovedFills > 0)
                    Flash("That also removed a fill that used this line");
                Play(snapClip);
            }
            else if (_hoverFill >= 0)
            {
                _undo.Do(_design, new RemoveFillEdit(_design.GetFill(_hoverFill)));
                Play(snapClip);
            }
        }
    }
}
