using BridgeBuilder.Core;
using TMPro;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>Text panels, the tool label on the controller, and sound (including the load creak, spec 2.10).</summary>
    public sealed partial class BridgeGame
    {
        [Header("Feedback")]
        [SerializeField] AudioClip snapClip;
        [SerializeField] AudioClip errorClip;
        [SerializeField] AudioClip breakClip;
        [SerializeField] AudioClip passClip;
        [SerializeField] AudioClip failClip;
        [Tooltip("Looped while testing; pitch and volume rise as the worst member nears failure.")]
        [SerializeField] AudioClip creakClip;
        [SerializeField] float hudFontSize = 0.22f;
        [SerializeField] bool showSolverStats = true;

        TextMeshPro _hud;
        TextMeshPro _toolLabel;
        AudioSource _sfx;
        AudioSource _creak;
        string _flash;
        float _flashUntil;

        void CreateFeedback()
        {
            _hud = CreateText("HUD", new Vector2(1.6f, 0.6f));
            _toolLabel = CreateText("Tool Label", new Vector2(0.4f, 0.1f));
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _creak = gameObject.AddComponent<AudioSource>();
            _creak.playOnAwake = false;
            _creak.loop = true;
            _creak.volume = 0f;
        }

        TextMeshPro CreateText(string name, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = size;
            return text;
        }

        void Flash(string message)
        {
            _flash = message;
            _flashUntil = Time.time + 2f;
        }

        void Play(AudioClip clip)
        {
            if (clip != null)
                _sfx.PlayOneShot(clip);
        }

        void UpdateHud()
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            string text = _phase switch
            {
                Phase.Brief => BriefText(),
                Phase.MarkSurfaces => MarkText(),
                Phase.Build => BuildText(),
                _ => TestText(),
            };
            if (Time.time < _flashUntil)
                text = $"<color=#FFD24A>{_flash}</color>\n{text}";
            if (_worker.Error != null)
                text += $"\n<color=#FF5050>Simulation error: {_worker.Error}</color>";
            _hud.text = text;
            _hud.fontSize = hudFontSize;

            Vector3 anchor;
            if (_phase == Phase.Build || _phase == Phase.Testing)
            {
                Vector3 mid = (V(_design.Surfaces[0].Mid) + V(_design.Surfaces[1].Mid)) * 0.5f;
                anchor = mid + Vector3.up * 0.45f;
            }
            else
            {
                Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                anchor = cam.transform.position + forward * 0.8f - Vector3.up * 0.1f;
            }
            Face(_hud.transform, anchor, cam);

            bool showTool = _phase == Phase.Build;
            _toolLabel.gameObject.SetActive(showTool);
            if (showTool)
            {
                string label = ToolNames[(int)_tool] + (_symmetry ? "  [mirror]" : "");
                if (_tool == Tool.Fill && _fillCorners.Count > 0)
                    label += $"  {_fillCorners.Count} corners";
                _toolLabel.text = label;
                _toolLabel.fontSize = hudFontSize * 0.6f;
                Face(_toolLabel.transform, _pointer.Tip + Vector3.up * 0.05f, cam);
            }
        }

        static void Face(Transform t, Vector3 position, Camera cam)
        {
            t.position = position;
            t.rotation = Quaternion.LookRotation(position - cam.transform.position, Vector3.up);
        }

        string BriefText()
        {
            var c = Challenge;
            string wind = c.WindSpeed > 0f ? $"{c.WindSpeed:F0} m/s crosswind" : "calm";
            return $"<b>{c.Name}</b>  ({_challengeIndex + 1}/{challenges.Length})\n{c.Description}\n" +
                   $"Vehicle {c.CarMass / 1000f:F1} t  ·  Wind {wind}  ·  Grip {c.Grip:F1}  ·  Budget {c.Budget:F0}\n" +
                   "<size=70%>Stick: choose challenge   ·   A: start</size>";
        }

        string MarkText()
        {
            string[] steps =
            {
                "Touch one end of the START surface's edge facing the gap, and pull the trigger",
                "Now the other end of that edge",
                "Touch one end of the FAR surface's edge",
                "Now the other end of that edge",
            };
            return steps[Mathf.Min(_taps.Count, 3)] + "\n<size=70%>X: undo a tap</size>";
        }

        string BuildText()
        {
            float cost = _design.Cost;
            string budget = $"Budget {cost:F0} / {Challenge.Budget:F0}";
            string hint = _tool switch
            {
                Tool.Draw => "Hold the trigger on a dot and draw: a line, an arc, or straight zigzags",
                Tool.Fill => "Tap the corners of a closed, flat shape, then the first corner again",
                Tool.Road => "Touch a fill to flag it as road (the car drives on road fills only)",
                _ => "Touch a line or fill and pull the trigger to delete it",
            };
            string road = _roads == null ? "" : _roads.Ok ? "Road: ready" : $"Road: {_roads.Message}";
            string text = $"{budget}   ·   {road}\n{hint}\n" +
                          "<size=70%>Stick: tool · Stick click: mirror · A: test · B: undo · X: clear · Y: car's side</size>";
            if (PreviewCurrent)
                text += "\n<size=60%>Colors show load under the bridge's own weight</size>";
            return text + SolverStats();
        }

        string TestText()
        {
            var snap = _snapshot;
            if (snap == null)
                return "Starting...";
            if (snap.Result == RunResult.Running)
                return $"Testing...  members broken {snap.BrokenCount}  ·  worst load {Mathf.RoundToInt(snap.WorstUtilization * 100f)}%" + SolverStats();

            string stars = snap.Result == RunResult.Passed ? $"  {new string('*', _stars)}{new string('-', 3 - _stars)}" : "";
            string verdict = snap.Result == RunResult.Passed ? "<color=#5CE65C>PASS</color>" : "<color=#FF5050>FAIL</color>";
            return $"{verdict}{stars}  {snap.Message}\n" +
                   $"Members broken: {snap.BrokenCount}   ·   Peak load: {Mathf.RoundToInt(snap.PeakUtilization * 100f)}%\n" +
                   "<size=70%>A: back to building</size>" + SolverStats();
        }

        /// <summary>Live solver timing: the spec's node-cap benchmark, measured on the headset.</summary>
        string SolverStats()
        {
            if (!showSolverStats)
                return "";
            return $"\n<size=50%><color=#9A9A9A>{_worker.Equations} equations · band {_worker.Bandwidth} · " +
                   $"step {_worker.StepMs:F2} ms · start {_worker.StartMs:F0} ms · preview {_worker.PreviewMs:F0} ms</color></size>";
        }

        void UpdateAudio()
        {
            if (creakClip == null)
                return;
            float worst = _phase == Phase.Testing && _snapshot != null && _snapshot.Result == RunResult.Running ? _snapshot.WorstUtilization : 0f;
            float volume = Mathf.Clamp01((worst - 0.6f) / 0.4f);
            if (volume > 0f && !_creak.isPlaying)
            {
                _creak.clip = creakClip;
                _creak.Play();
            }
            _creak.volume = volume;
            _creak.pitch = 0.8f + 0.6f * Mathf.Clamp01(worst);
        }
    }
}
