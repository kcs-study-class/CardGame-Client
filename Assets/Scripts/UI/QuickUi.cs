using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KTC.UI
{
    /// <summary>
    /// ランタイム生成UIの共通ヘルパー (プロトタイプ用)。
    /// アートパス前の画面はこれで組み、確定したらプレハブ化していく方針。
    /// </summary>
    public static class QuickUi
    {
        // 共通パレット (ダークテーマ)
        public static readonly Color Bg = new Color(0.055f, 0.055f, 0.086f, 1f);
        public static readonly Color Panel = new Color(0.125f, 0.125f, 0.173f, 1f);
        public static readonly Color PanelDark = new Color(0.10f, 0.10f, 0.14f, 1f);
        public static readonly Color Accent = new Color(0.878f, 0.706f, 0.361f, 1f);
        public static readonly Color Text = new Color(0.925f, 0.925f, 0.949f, 1f);
        public static readonly Color TextDark = new Color(0.1f, 0.1f, 0.12f, 1f);
        public static readonly Color Warn = new Color(0.85f, 0.35f, 0.35f, 1f);
        public static readonly Color CardFace = new Color(0.941f, 0.902f, 0.824f, 1f);
        public static readonly Color CardBack = new Color(0.173f, 0.227f, 0.396f, 1f);
        public static readonly Color RedSuit = new Color(0.78f, 0.16f, 0.16f, 1f);
        public static readonly Color Disabled = new Color(0.35f, 0.35f, 0.40f, 1f);

        public static RectTransform MakeRect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image MakePanel(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            var rt = MakeRect(name, parent, pos, size);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>全面ストレッチの背景パネル。</summary>
        public static Image MakeBackground(Transform parent)
        {
            var image = MakePanel("Background", parent, Vector2.zero, Vector2.zero, Bg);
            var rt = (RectTransform)image.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return image;
        }

        public static TextMeshProUGUI MakeText(
            string name, Transform parent, Vector2 pos, Vector2 size,
            float fontSize, string content, TMP_FontAsset font,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var rt = MakeRect(name, parent, pos, size);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                tmp.font = font;
            }
            tmp.fontSize = fontSize;
            tmp.color = Text;
            tmp.text = content;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static Button MakeButton(
            string name, Transform parent, Vector2 pos, Vector2 size,
            string label, TMP_FontAsset font, UnityEngine.Events.UnityAction onClick,
            out TextMeshProUGUI labelText)
        {
            var image = MakePanel(name, parent, pos, size, Accent);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.disabledColor = Disabled;
            button.colors = colors;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }
            labelText = MakeText("Label", image.transform, Vector2.zero, size - new Vector2(16f, 10f), size.y * 0.42f, label, font);
            labelText.color = TextDark;
            return button;
        }

        /// <summary>選択トグル系ボタンの選択状態を配色で表す (選択=アクセント)。</summary>
        public static void SetSelected(Button button, bool selected)
        {
            ((Image)button.targetGraphic).color = selected ? Accent : Panel;
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = selected ? TextDark : Text;
            }
        }
    }
}
