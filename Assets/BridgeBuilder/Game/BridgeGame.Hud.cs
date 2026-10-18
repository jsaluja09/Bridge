using System.Linq;
using BridgeBuilder.Core;
using TMPro;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>The text panel, written for students, and sound (including the load creak, spec 2.10).</summary>
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
        [Tooltip("Show solver timing: the headset benchmark for the node cap (spec 2.12).")]
        [SerializeField] bool showSolverStats = true;

        TextMeshPro _hud;
        AudioSource _sfx;
        AudioSource _creak;

        void CreateFeedback()
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(transform, false);
            _hud = go.AddComponent<TextMeshPro>();
            _hud.alignment = TextAlignmentOptions.Center;
            _hud.rectTransform.sizeDelta = new Vector2(1.6f, 0.6f);
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _creak = gameObject.AddComponent<AudioSource>();
            _creak.playOnAwake = false;
            _creak.loop = true;
            _creak.volume = 0f;
        }

        void Flash(string message) => HudMessages.Show(message, 2.5f);

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
                Phase.Place => _taps.Count == 0
                    ? "Touch where the road ends on THIS side of the canyon\nand pull the trigger"
                    : "Now touch where the road starts again ACROSS the canyon\n<size=70%>The canyon follows your hand   ·   X: undo</size>",
                Phase.Build => BuildText(),
                Phase.Results => ResultsText(),
                _ => TestText(),
            };
            string message = HudMessages.Current;
            if (message != null)
                text = $"<color=#FFD24A>{message}</color>\n{text}";
            if (_worker.Error != null)
                text += $"\n<color=#FF5050>Simulation error: {_worker.Error}</color>";
            _hud.text = text;
            _hud.fontSize = hudFontSize;

            _hud.transform.position = HudAnchor(cam);
            _hud.transform.rotation = Quaternion.LookRotation(_hud.transform.position - cam.transform.position, Vector3.up);
        }

        /// <summary>Above the middle of the bridge once it exists, otherwise in front of the player.</summary>
        Vector3 HudAnchor(Camera cam) => _phase == Phase.Brief || _phase == Phase.Place
            ? cam.transform.position + Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized * 0.8f - Vector3.up * 0.1f
            : (V(_pads[0].FrontCenter) + V(_pads[1].FrontCenter)) * 0.5f + Vector3.up * 0.45f;

        static readonly string PhaseNames = string.Join(", ", TestPhases.All.Select(p => p.Name));

        string BriefText() =>
            $"<b>{_level.Name}</b>\n{_level.Brief}\n" +
            $"Material {_level.Budget:F0} m   ·   Tests: {PhaseNames}\n" +
            "<size=70%>A: start   ·   X: menu</size>";

        string BuildText()
        {
            string material = $"Material {_model.Spent:F1} / {_level.Budget:F0} m";
            string road = _bridge.Road.Ok ? "<color=#5CE65C>Road ready</color>" : "Road: draw two flat lines side by side, pad to pad";
            string best = _bestStars > 0 ? $"   ·   <color=#FFD24A>Best: {_bestStars} of {TestPhases.MaxStars} stars</color>" : "";
            string text = $"{material}   ·   {road}{best}\n" +
                          "<size=70%>Hold trigger: draw   ·   Grip on a line: delete   ·   A: test   ·   B: undo   ·   X: clear   ·   Y: mirror " + (_mirror ? "<b>on</b>" : "off") + "</size>";
            if (PreviewCurrent)
                text += "\n<size=60%>Colors show how hard the bridge works to hold itself up</size>";
            return text + SolverStats();
        }

        string TestText()
        {
            string title = $"<b>Test {_runningIndex + 1} of {TestPhases.All.Count}: {_runningPhase.Name}</b>";
            var snap = _snapshot;
            if (snap == null)
                return $"{title}\nStarting...";
            // Failure messages arrive through HudMessages while testing (W4-07).
            if (snap.Result == RunResult.Running)
                return $"{title}\nworst load {Mathf.RoundToInt((float)snap.WorstUtilization * 100)}%" + SolverStats();

            var outcome = _sequence.Outcomes[_sequence.Outcomes.Count - 1];
            string verdict = outcome.Passed ? "<color=#5CE65C>PASSED</color>" : "<color=#FF5050>FAILED</color>";
            string next = outcome.Passed && !_sequence.Finished ? $"\n<size=70%>Next: {_sequence.Current.Name}</size>" : "";
            return $"{title}   {verdict}\n{snap.Message}\n<color=#FFD24A>{outcome.Tip}</color>{next}" + SolverStats();
        }

        string ResultsText()
        {
            string note = string.IsNullOrEmpty(ResultNote) ? "" : $"\n{ResultNote}";
            return $"{_resultsText}{note}\n<size=70%>A: back to building</size>";
        }

        /// <summary>
        /// Built once when the results appear (the stars are drawn above it): each phase's verdict, why the last
        /// one failed, and the advice.
        /// </summary>
        string BuildResultsText()
        {
            var lines = new System.Text.StringBuilder();
            foreach (var phase in TestPhases.All)
            {
                var outcome = _sequence.Outcomes.FirstOrDefault(o => o.Phase == phase);
                string mark = outcome == null ? "<color=#808080>not reached</color>"
                            : outcome.Passed ? "<color=#5CE65C>held</color>" : "<color=#FF5050>failed</color>";
                lines.Append($"{phase.Name}: {mark}   ");
            }
            var last = _sequence.Outcomes[_sequence.Outcomes.Count - 1];
            string summary = !last.Passed && _fx.Summary != null ? $"\n{_fx.Summary.Text}" : "";
            string point = _snapshot != null && _snapshot.WeakMember >= 0 ? "\n<size=70%>The flashing beam is the weakest spot.</size>" : "";
            return $"<b>{Stars} of {TestPhases.MaxStars} stars</b>\n<size=70%>{lines}</size>{summary}\n" +
                   $"<color=#FFD24A>{last.Tip}</color>{point}";
        }

        string SolverStats()
        {
            if (!showSolverStats)
                return "";
            return $"\n<size=45%><color=#9A9A9A>{_worker.Members} members · step {_worker.StepMs:F2} ms · start {_worker.StartMs:F0} ms · preview {_worker.PreviewMs:F0} ms</color></size>";
        }

        void UpdateAudio()
        {
            if (creakClip == null)
                return;
            double worst = _phase == Phase.Testing && _snapshot != null && _snapshot.Result == RunResult.Running ? _snapshot.WorstUtilization : 0;
            float volume = Mathf.Clamp01((float)(worst - 0.6) / 0.4f);
            if (volume > 0f && !_creak.isPlaying)
            {
                _creak.clip = creakClip;
                _creak.Play();
            }
            _creak.volume = volume;
            // Spec 2.10: pitch rises as the worst member nears failure (80 to 400 Hz mapped onto playback rate).
            _creak.pitch = (float)(FeedbackMapping.CreakPitch(worst) / Constants.Feedback.CreakPitchMinHz) * 0.5f;
        }
    }
}
