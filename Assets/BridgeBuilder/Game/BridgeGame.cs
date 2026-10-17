using BridgeBuilder.Core;
using TMPro;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The game loop: mark two surfaces, draw one side of the bridge on the vertical plane between them
    /// (it is mirrored to a full 3D bridge), then press A to send the car across.
    /// </summary>
    [RequireComponent(typeof(ControllerPointer), typeof(BridgeView), typeof(CarView))]
    public sealed class BridgeGame : MonoBehaviour
    {
        [SerializeField] BridgeSettings settings = new BridgeSettings();
        [Tooltip("Show the real room behind the game (Quest passthrough).")]
        [SerializeField] bool passthrough = true;

        [Header("Feedback")]
        [SerializeField] AudioClip snapClip;
        [SerializeField] AudioClip errorClip;
        [SerializeField] AudioClip breakClip;
        [SerializeField] AudioClip passClip;
        [SerializeField] AudioClip failClip;
        [SerializeField] float statusFontSize = 0.25f;
        [Tooltip("Seconds the collapse keeps animating after the result.")]
        [SerializeField] float afterResultTime = 6f;

        [Header("Look")]
        [SerializeField] float memberRadius = 0.006f;
        [SerializeField] float lateralRadius = 0.002f;
        [SerializeField] float nodeRadius = 0.009f;
        [SerializeField] Color drawnColor = new Color(0.85f, 0.9f, 1f);
        [SerializeField] Color anchorColor = new Color(1f, 0.55f, 0.1f);
        [SerializeField] Color lateralColor = new Color(0.55f, 0.55f, 0.6f);

        enum Phase { MarkStart, MarkFar, Build, Testing }

        ControllerPointer _pointer;
        BridgeView _view;
        CarView _car;
        AudioSource _audio;
        TextMeshPro _status;

        Phase _phase;
        Vector3 _startTap;
        BridgeFrame _frame;
        BridgeDesign _design;
        int _drawStart = -1;

        BridgeTestRun _run;
        float _accumulator;
        float _resultTime;
        RunResult _shownResult;

        string _flash;
        float _flashUntil;

        void Awake()
        {
            _pointer = GetComponent<ControllerPointer>();
            _view = GetComponent<BridgeView>();
            _car = GetComponent<CarView>();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;

            var text = new GameObject("Status");
            text.transform.SetParent(transform, false);
            _status = text.AddComponent<TextMeshPro>();
            _status.alignment = TextAlignmentOptions.Center;
            _status.rectTransform.sizeDelta = new Vector2(2f, 0.5f);
        }

        void Start()
        {
            if (passthrough)
                Passthrough.Enable();
        }

        void Update()
        {
            switch (_phase)
            {
                case Phase.MarkStart: UpdateMarkStart(); break;
                case Phase.MarkFar: UpdateMarkFar(); break;
                case Phase.Build: UpdateBuild(); break;
                case Phase.Testing: UpdateTesting(); break;
            }
            Draw();
            UpdateStatus();
        }

        // ---------- Phases ----------

        void UpdateMarkStart()
        {
            if (_pointer.TriggerPressed)
            {
                _startTap = _pointer.Tip;
                _phase = Phase.MarkFar;
                Play(snapClip);
            }
        }

        void UpdateMarkFar()
        {
            if (_pointer.ResetPressed)
            {
                _phase = Phase.MarkStart;
                return;
            }
            if (!_pointer.TriggerPressed)
                return;

            Vector3 far = _pointer.Tip;
            Vector3 gap = Vector3.ProjectOnPlane(far - _startTap, Vector3.up);
            if (gap.magnitude < 0.2f)
            {
                Flash("Too close. Pick a surface at least 20 cm away.");
                Play(errorClip);
                return;
            }

            Vector3 viewer = Camera.main != null ? Camera.main.transform.position : _startTap - gap.normalized;
            _frame = BridgeFrame.FromTaps(_startTap.ToNumerics(), far.ToNumerics(), Vector3.up.ToNumerics(),
                                          viewer.ToNumerics(), settings.RoadWidth);
            NVector2 farPlane = _frame.ToPlane(far.ToNumerics());
            _design = new BridgeDesign(settings, farPlane.X, farPlane.Y);
            _phase = Phase.Build;
            Play(snapClip);
        }

        void UpdateBuild()
        {
            NVector2 p = _frame.ToPlane(_pointer.Tip.ToNumerics());

            if (_pointer.TriggerPressed)
            {
                _drawStart = _design.FindNode(p, settings.NodeSnapRadius * 1.5f);
                if (_drawStart < 0)
                {
                    Flash("Start each line on a dot");
                    Play(errorClip);
                }
            }
            else if (_pointer.TriggerReleased && _drawStart >= 0)
            {
                var result = _design.TryAddLine(_drawStart, p);
                _drawStart = -1;
                switch (result)
                {
                    case AddLineResult.Added: Play(snapClip); break;
                    case AddLineResult.TooMany: Flash("Out of material"); Play(errorClip); break;
                    case AddLineResult.Duplicate: Flash("That line already exists"); Play(errorClip); break;
                }
            }

            if (_pointer.SecondaryPressed && _design.Undo())
                Play(snapClip);

            if (_pointer.ResetPressed)
            {
                if (_design.LineCount > 0)
                    _design.Clear();
                else
                    _phase = Phase.MarkStart;
                _drawStart = -1;
            }

            if (_pointer.PrimaryPressed)
                StartTest();
        }

        void StartTest()
        {
            _drawStart = -1;
            _run = new BridgeTestRun(_design, _frame, settings);
            _accumulator = 0f;
            _resultTime = 0f;
            _shownResult = RunResult.Running;
            _phase = Phase.Testing;
        }

        void UpdateTesting()
        {
            if (_pointer.PrimaryPressed || _pointer.ResetPressed)
            {
                _run = null;
                _car.Hide();
                _phase = Phase.Build;
                return;
            }

            bool animating = _run.Result == RunResult.Running || _resultTime < afterResultTime;
            if (animating)
            {
                _accumulator += Mathf.Min(Time.deltaTime, 0.1f);
                bool broke = false;
                while (_accumulator >= settings.FixedDt)
                {
                    _run.Step();
                    broke |= _run.Solver.BrokenThisStep.Count > 0;
                    _accumulator -= settings.FixedDt;
                }
                if (broke)
                    Play(breakClip);
                if (_run.Result != RunResult.Running)
                    _resultTime += Time.deltaTime;
            }

            if (_run.Result != _shownResult)
            {
                _shownResult = _run.Result;
                Play(_run.Result == RunResult.Passed ? passClip : failClip);
            }

            if (_run.Car.State == CarState.Falling)
                _car.Fall();
            else if (_run.Car.State != CarState.Arrived)
                _car.Show(_run.Car.Position.ToUnity(), _run.Car.Forward.ToUnity());
        }

        // ---------- Drawing ----------

        void Draw()
        {
            _view.Begin();
            switch (_phase)
            {
                case Phase.MarkStart:
                    _view.Dot(_pointer.Tip, nodeRadius, anchorColor);
                    break;
                case Phase.MarkFar:
                    _view.Dot(_startTap, nodeRadius * 1.3f, anchorColor);
                    _view.Dot(_pointer.Tip, nodeRadius, anchorColor);
                    break;
                case Phase.Build:
                    DrawDesign();
                    DrawCursor();
                    break;
                case Phase.Testing:
                    DrawRun();
                    break;
            }
            _view.End();
        }

        Vector3 World(int designNode, int side) => _frame.ToWorld(_design.Nodes[designNode].Position, side).ToUnity();

        void DrawSurfaces()
        {
            for (int side = 0; side < 2; side++)
            {
                _view.Segment(World(0, side), World(1, side), memberRadius * 0.6f, anchorColor);
                _view.Segment(World(2, side), World(3, side), memberRadius * 0.6f, anchorColor);
            }
        }

        void DrawDesign()
        {
            DrawSurfaces();
            for (int i = 0; i < _design.Members.Count; i++)
            {
                var m = _design.Members[i];
                for (int side = 0; side < 2; side++)
                    _view.Segment(World(m.A, side), World(m.B, side), memberRadius, side == 0 ? drawnColor : drawnColor * 0.7f);
                if (_design.IsRoad(i))
                    _view.RoadQuad(World(m.A, 0), World(m.B, 0), World(m.A, 1), World(m.B, 1));
            }
            for (int i = 0; i < _design.Nodes.Count; i++)
                _view.Dot(World(i, 0), nodeRadius, _design.Nodes[i].Anchor ? anchorColor : drawnColor);
        }

        void DrawCursor()
        {
            NVector2 p = _frame.ToPlane(_pointer.Tip.ToNumerics());
            Vector3 onPlane = _frame.ToWorld(p).ToUnity();
            _view.Segment(_pointer.Tip, onPlane, lateralRadius * 0.5f, lateralColor);

            if (_drawStart >= 0)
            {
                Vector3 end = _frame.ToWorld(_design.SnapEnd(p, out _)).ToUnity();
                _view.Segment(World(_drawStart, 0), end, memberRadius, new Color(0.3f, 0.9f, 1f));
                _view.Dot(end, nodeRadius * 1.2f, new Color(0.3f, 0.9f, 1f));
            }
            else
            {
                int hover = _design.FindNode(p, settings.NodeSnapRadius * 1.5f);
                if (hover >= 0)
                    _view.Dot(World(hover, 0), nodeRadius * 1.8f, new Color(0.3f, 0.9f, 1f));
                else
                    _view.Dot(onPlane, nodeRadius * 0.7f, lateralColor);
            }
        }

        void DrawRun()
        {
            DrawSurfaces();
            var solver = _run.Solver;
            var pos = solver.Positions;
            for (int m = 0; m < solver.MemberCount; m++)
            {
                if (!solver.Active[m])
                    continue;
                Vector3 a = pos[solver.MemberA[m]].ToUnity();
                Vector3 b = pos[solver.MemberB[m]].ToUnity();
                if (solver.Kind[m] == MemberKind.Lateral)
                    _view.Segment(a, b, lateralRadius, lateralColor);
                else
                    _view.Segment(a, b, memberRadius, BridgeView.UtilizationColor(solver.Utilization[m]));
            }

            var st = _run.Structure;
            foreach (int m in st.RoadMembers)
            {
                int twin = st.TwinMember(m);
                if (!solver.Active[m] || !solver.Active[twin])
                    continue;
                _view.RoadQuad(pos[solver.MemberA[m]].ToUnity(), pos[solver.MemberB[m]].ToUnity(),
                               pos[solver.MemberA[twin]].ToUnity(), pos[solver.MemberB[twin]].ToUnity());
            }

            for (int i = 0; i < st.DesignNodeCount; i++)
            {
                if (solver.IsFixed(i))
                    _view.Dot(pos[i].ToUnity(), nodeRadius, anchorColor);
            }
        }

        // ---------- Status text and sound ----------

        void UpdateStatus()
        {
            string text;
            switch (_phase)
            {
                case Phase.MarkStart:
                    text = "Touch the edge of the START surface\nand pull the trigger";
                    break;
                case Phase.MarkFar:
                    text = "Now touch the edge of the FAR surface";
                    break;
                case Phase.Build:
                    text = $"Draw from a dot  ({_design.Members.Count}/{settings.MaxDrawnMembers} lines)\n" +
                           "A: test   B: undo   X: clear";
                    break;
                default:
                    text = _run.Result == RunResult.Running
                        ? "Testing..."
                        : $"{(_run.Result == RunResult.Passed ? "PASS" : "FAIL")}: {_run.Message}\n" +
                          $"Members broken: {_run.Solver.BrokenCount}   Peak load: {Mathf.RoundToInt(_run.Solver.PeakUtilization * 100f)}%\n" +
                          "A: back to building";
                    break;
            }
            if (Time.time < _flashUntil)
                text = _flash + "\n" + text;
            _status.text = text;
            _status.fontSize = statusFontSize;

            var cam = Camera.main;
            if (cam == null)
                return;
            Vector3 anchor = _phase == Phase.MarkStart
                ? cam.transform.position + Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized * 0.7f
                : _phase == Phase.MarkFar
                    ? _startTap + Vector3.up * 0.25f
                    : (_frame.Origin.ToUnity() + _frame.ToWorld(new NVector2(_design.FarU, _design.FarV)).ToUnity()) * 0.5f + Vector3.up * 0.4f;
            _status.transform.position = anchor;
            _status.transform.rotation = Quaternion.LookRotation(anchor - cam.transform.position, Vector3.up);
        }

        void Flash(string message)
        {
            _flash = message;
            _flashUntil = Time.time + 1.5f;
        }

        void Play(AudioClip clip)
        {
            if (clip != null)
                _audio.PlayOneShot(clip);
        }
    }
}
