using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Floating text labels that face the player, created as needed and reused. Call Begin each frame, Show each
    /// label, then End to hide the ones not shown. Text is only reassigned when it changes, so TextMeshPro rebuilds
    /// a label's mesh only then.
    /// </summary>
    public sealed class LabelPool
    {
        readonly Transform _parent;
        readonly float _fontSize;
        readonly List<TextMeshPro> _labels = new List<TextMeshPro>();
        int _used;

        public LabelPool(Transform parent, float fontSize)
        {
            _parent = parent;
            _fontSize = fontSize;
        }

        public void Begin() => _used = 0;

        public void Show(string text, Vector3 position, Camera cam, Color color)
        {
            if (_used == _labels.Count)
                _labels.Add(Create());
            var label = _labels[_used++];
            if (!label.gameObject.activeSelf)
                label.gameObject.SetActive(true);
            if (!ReferenceEquals(label.text, text))
                label.text = text;
            label.color = color;
            label.transform.position = position;
            label.transform.rotation = Quaternion.LookRotation(position - cam.transform.position, Vector3.up);
        }

        public void End()
        {
            for (int i = _used; i < _labels.Count; i++)
            {
                if (_labels[i].gameObject.activeSelf)
                    _labels[i].gameObject.SetActive(false);
            }
        }

        TextMeshPro Create()
        {
            var go = new GameObject("Label");
            go.transform.SetParent(_parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = _fontSize;
            tmp.rectTransform.sizeDelta = new Vector2(0.6f, 0.08f);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            return tmp;
        }
    }
}
