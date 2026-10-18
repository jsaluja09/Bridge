using System.Collections.Generic;
using BridgeBuilder.Core;
using TMPro;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Learn mode, separate from the game: pick a famous bridge and click through how and why it was built. The
    /// bridge assembles on a tabletop in front of you, step by step, with highlights, labels, a chart and load
    /// demos. Stick + A picks a bridge; then A is next, B is back, X returns to the bridges (and X there returns to
    /// the start menu). Split across partial files: flow and text (here), drawing (LearnMode.Draw).
    /// </summary>
    [RequireComponent(typeof(ControllerPointer), typeof(BridgeView))]
    public sealed partial class LearnMode : MonoBehaviour
    {
        [SerializeField] float panelFontSize = 0.16f;
        [SerializeField] float labelFontSize = 0.12f;
        [Tooltip("Length of the tabletop each bridge is fitted to, room meters.")]
        [SerializeField] float tableLength = 1.5f;

        /// <summary>Raised when the player leaves Learn mode (X on the bridge list).</summary>
        public event System.Action ExitRequested;

        ControllerPointer _pointer;
        BridgeView _view;
        TextMeshPro _panel;
        LabelPool _labels;
        LessonPlayer _player;
        IReadOnlyList<Lesson> _lessons;
        bool _inLesson;
        int _choice;
        string _panelText = "";
        int _panelFor = int.MinValue;   // which lesson step (or picker choice) the panel text was built for

        void Awake()
        {
            _pointer = GetComponent<ControllerPointer>();
            _view = GetComponent<BridgeView>();
            _lessons = Lessons.All();
            _player = new LessonPlayer(_lessons);
            _labels = new LabelPool(transform, labelFontSize);

            var go = new GameObject("Lesson Panel");
            go.transform.SetParent(transform, false);
            _panel = go.AddComponent<TextMeshPro>();
            _panel.alignment = TextAlignmentOptions.Center;
            _panel.rectTransform.sizeDelta = new Vector2(1.5f, 0.5f);
            _panel.fontSize = panelFontSize;
        }

        void OnEnable()
        {
            _inLesson = false;
            _panelFor = int.MinValue;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            double now = Time.timeAsDouble;
            if (_inLesson)
                UpdateLesson(now, cam);
            else
                UpdatePicker(now, cam);
            Draw(now, cam);
        }

        // ---------- Picking a bridge ----------

        void UpdatePicker(double now, Camera cam)
        {
            if (_pointer.ClearPressed)
            {
                ExitRequested?.Invoke();
                return;
            }
            if (_pointer.StickStep != 0)
                _choice = (_choice + _lessons.Count + _pointer.StickStep) % _lessons.Count;
            if (_pointer.PrimaryPressed || _pointer.TriggerPressed)
            {
                _player.Choose(_choice, now);
                PlaceTable(_player.Lesson.Bridge, cam);
                _inLesson = true;
            }
        }

        string PickerText()
        {
            var text = new System.Text.StringBuilder("<b>Learn from famous bridges</b>\n\n");
            for (int i = 0; i < _lessons.Count; i++)
            {
                var l = _lessons[i];
                string line = $"{l.Title}  <size=70%>({l.Place}, {l.Year})</size>";
                text.Append(i == _choice ? $"<color=#FFD24A>> {line} <</color>\n" : $"<color=#B0B0B0>{line}</color>\n");
            }
            text.Append("\n<size=70%>Stick: choose   ·   A: start   ·   X: menu</size>");
            return text.ToString();
        }

        // ---------- A lesson ----------

        void UpdateLesson(double now, Camera cam)
        {
            if (_pointer.ClearPressed)
            {
                _inLesson = false;
                return;
            }
            if (_pointer.PrimaryPressed || _pointer.TriggerPressed)
            {
                if (!_player.Next(now))
                    _inLesson = false;          // finished: back to the bridges
            }
            else if (_pointer.SecondaryPressed)
            {
                _player.Back(now);
            }
        }

        string StepText()
        {
            var lesson = _player.Lesson;
            var step = _player.Step;
            string next = _player.AtLastStep ? "A: back to the bridges" : "A: next";
            return $"<size=70%><color=#B0B0B0>{lesson.Title} · {lesson.Place}, {lesson.Year} · " +
                   $"step {_player.StepIndex + 1} of {lesson.Steps.Count}</color></size>\n" +
                   $"<b>{step.Title}</b>\n{step.Text}\n<color=#FFD24A>{step.Formula}</color>\n" +
                   $"<size=60%>{next}   ·   B: back   ·   X: bridges</size>";
        }

        /// <summary>The panel above the table (or in front of you on the picker); its text is rebuilt only when the step changes.</summary>
        void UpdatePanel(Camera cam)
        {
            int key = _inLesson ? _player.LessonIndex * 100 + _player.StepIndex : -1 - _choice;
            if (key != _panelFor)
            {
                _panelText = _inLesson ? StepText() : PickerText();
                _panel.text = _panelText;
                _panelFor = key;
            }
            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 at = _inLesson ? _table.Center + Vector3.up * 0.55f : cam.transform.position + forward * 0.9f;
            _panel.transform.position = at;
            _panel.transform.rotation = Quaternion.LookRotation(at - cam.transform.position, Vector3.up);
        }
    }
}
