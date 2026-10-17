using System.Collections.Generic;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The game loop (spec section 6): pick a challenge, mark two surfaces with four taps, build with the
    /// Draw / Fill / Road / Delete tools, then send the car across. The simulation runs on a worker thread;
    /// this class turns input into design edits and draws whatever state is current.
    /// Split across partial files: flow (here), tools, drawing, and HUD/audio.
    /// </summary>
    [RequireComponent(typeof(ControllerPointer), typeof(BridgeView), typeof(CarView))]
    public sealed partial class BridgeGame : MonoBehaviour
    {
        [SerializeField] BridgeSettings settings = new BridgeSettings();
        [SerializeField] Challenge[] challenges = Challenge.Defaults();
        [Tooltip("Show the real room behind the game (Quest passthrough).")]
        [SerializeField] bool passthrough = true;

        enum Phase { Brief, MarkSurfaces, Build, Testing }

        ControllerPointer _pointer;
        BridgeView _view;
        CarView _car;
        SimWorker _worker;

        Phase _phase;
        int _challengeIndex;
        readonly List<Vector3> _taps = new List<Vector3>();

        BridgeDesign _design;
        readonly UndoStack _undo = new UndoStack();
        BridgeStructure _structure;        // the design compiled, refreshed on every edit
        Vector3[] _restRoom = new Vector3[0];
        PreviewResult _preview;
        int _previewRequested = -1;
        float _editTime;
        RoadReport _roads;
        CarSim _parkedCar;
        float _roadWarningUntil;

        BridgeStructure _runStructure;
        Snapshot _snapshot;
        int _lastBroken;
        RunResult _shownResult;
        int _stars;
        float _budgetFraction;

        Challenge Challenge => challenges[_challengeIndex];

        void Awake()
        {
            _pointer = GetComponent<ControllerPointer>();
            _view = GetComponent<BridgeView>();
            _car = GetComponent<CarView>();
            if (challenges == null || challenges.Length == 0)
                challenges = Challenge.Defaults();
            CreateFeedback();
            _worker = new SimWorker();
        }

        void Start()
        {
            if (passthrough)
                Passthrough.Enable();
        }

        void OnDestroy() => _worker?.Dispose();

        void Update()
        {
            switch (_phase)
            {
                case Phase.Brief: UpdateBrief(); break;
                case Phase.MarkSurfaces: UpdateMarking(); break;
                case Phase.Build: UpdateBuild(); break;
                case Phase.Testing: UpdateTesting(); break;
            }
            Draw();
            UpdateHud();
            UpdateAudio();
        }

        // ---------- Brief: choose a challenge (spec 2.9) ----------

        void UpdateBrief()
        {
            if (_pointer.ToolStep != 0)
            {
                _challengeIndex = (_challengeIndex + _pointer.ToolStep + challenges.Length) % challenges.Length;
                Play(snapClip);
            }
            if (_pointer.PrimaryPressed || _pointer.TriggerPressed)
            {
                _taps.Clear();
                _phase = Phase.MarkSurfaces;
                Play(snapClip);
            }
        }

        // ---------- Four taps: both ends of each surface's edge (spec 2.2) ----------

        void UpdateMarking()
        {
            if (_pointer.ClearPressed)
            {
                if (_taps.Count > 0)
                    _taps.RemoveAt(_taps.Count - 1);
                else
                    _phase = Phase.Brief;
                return;
            }
            if (!_pointer.TriggerPressed)
                return;

            _taps.Add(_pointer.Tip);
            Play(snapClip);

            if ((_taps.Count == 2 || _taps.Count == 4) && Vector3.Distance(_taps[_taps.Count - 1], _taps[_taps.Count - 2]) < 0.08f)
            {
                Flash("Touch both ends of the edge, at least 8 cm apart");
                Play(errorClip);
                _taps.RemoveAt(_taps.Count - 1);
                return;
            }
            if (_taps.Count < 4)
                return;

            Vector3 gap = (_taps[2] + _taps[3] - _taps[0] - _taps[1]) * 0.5f;
            gap.y = 0f;
            if (gap.magnitude < 0.25f)
            {
                Flash("The surfaces need to be at least 25 cm apart");
                Play(errorClip);
                _taps.RemoveRange(2, 2);
                return;
            }

            float lowestTap = Mathf.Min(Mathf.Min(_taps[0].y, _taps[1].y), Mathf.Min(_taps[2].y, _taps[3].y));
            float floor = Mathf.Min(_pointer.FloorY, lowestTap - 0.3f);
            _design = BridgeDesign.Create(settings, _taps[0].ToNumerics(), _taps[1].ToNumerics(), _taps[2].ToNumerics(), _taps[3].ToNumerics(), floor);
            _undo.Clear();
            _structure = null;
            _preview = null;
            _phase = Phase.Build;
        }

        // ---------- Build ----------

        void UpdateBuild()
        {
            RefreshStructure();
            UpdateTools();

            if (_pointer.PrimaryPressed && !Drawing)
                StartTest();
        }

        /// <summary>Recompiles after edits, and runs the dead-load preview once the player pauses (spec 2.10).</summary>
        void RefreshStructure()
        {
            if (_structure == null || _structure.DesignVersion != _design.Version)
            {
                _structure = BridgeStructure.Compile(_design, Challenge);
                if (_restRoom.Length < _structure.NodeCount)
                    _restRoom = new Vector3[_structure.NodeCount * 2];
                for (int i = 0; i < _structure.NodeCount; i++)
                    _restRoom[i] = _structure.ToRoom(_structure.Rest[i]).ToUnity();
                _roads = BridgeEditing.ValidateRoads(_design);
                _parkedCar = new CarSim(_structure);
                _editTime = Time.time;
            }

            var preview = _worker.TakePreview();
            if (preview != null)
                _preview = preview;

            if (_previewRequested != _design.Version && !Drawing && Time.time - _editTime >= settings.PreviewDelay)
            {
                _worker.RequestPreview(_structure);
                _previewRequested = _design.Version;
            }
        }

        bool PreviewCurrent => _preview != null && _structure != null && _preview.Structure == _structure;

        void StartTest()
        {
            _roads = BridgeEditing.ValidateRoads(_design);
            if (!_roads.Ok)
            {
                Flash(_roads.Message);
                Play(errorClip);
                _roadWarningUntil = Time.time + 3f;
                return;
            }

            ResetToolState();
            _runStructure = BridgeStructure.Compile(_design, Challenge);
            _worker.StartRun(_runStructure);
            _snapshot = null;
            _lastBroken = 0;
            _shownResult = RunResult.Running;
            _stars = 0;
            _budgetFraction = Challenge.Budget > 0f ? _design.Cost / Challenge.Budget : 0f;
            _phase = Phase.Testing;
        }

        // ---------- Testing ----------

        void UpdateTesting()
        {
            var latest = _worker.Latest();
            _snapshot = latest.Structure == _runStructure ? latest : null;

            if (_snapshot != null)
            {
                if (_snapshot.BrokenCount > _lastBroken)
                {
                    _lastBroken = _snapshot.BrokenCount;
                    Play(breakClip);
                }
                if (_snapshot.Result != _shownResult)
                {
                    _shownResult = _snapshot.Result;
                    _stars = BridgeTestRun.StarsFor(_snapshot.Result, _snapshot.BrokenCount, _snapshot.PeakUtilization, _budgetFraction);
                    Play(_shownResult == RunResult.Passed ? passClip : failClip);
                }
            }

            // Tests run on a copy: going back restores the bridge as built (spec 2.11).
            if (_pointer.PrimaryPressed || _pointer.ClearPressed)
            {
                _worker.StopRun();
                _car.Hide();
                _snapshot = null;
                _phase = Phase.Build;
            }
        }
    }
}
