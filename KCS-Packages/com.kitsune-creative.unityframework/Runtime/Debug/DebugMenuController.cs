using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnityFramework.Debugging
{
    /// <summary>
    /// 実行時デバッグメニュー。ゲーム側から項目を宣言的に登録して使う:
    /// <code>
    /// var menu = DebugMenuController.Instance;
    /// menu.AddButton("データ", "セーブ削除", () => ...);
    /// menu.AddToggle("カード", "CPU手札公開", () => flag, v => flag = v);
    /// menu.AddLabel("情報", "バージョン", () => Application.version);
    /// menu.AddInput("カード", "シード", () => seed.ToString(), text => ...);
    /// </code>
    /// 開閉は <see cref="Toggle"/> (ホットキー割り当てはゲーム側の責務)。
    ///
    /// UI はレガシー uGUI Text で構築する (OSフォントフォールバックで日本語も
    /// フォントアセット不要のため。デバッグ用途では堅牢性を優先)。
    /// System カテゴリに FPS / メモリ / 直近ログを標準搭載。
    /// </summary>
    [DisallowMultipleComponent]
    public class DebugMenuController : SingletonMonoBehaviour<DebugMenuController>
    {
        [SerializeField] private int _sortingOrder = 31000; // ModalCanvas(30000) より上、Fade(32767) より下

        private const float LABEL_REFRESH_INTERVAL = 0.25f;
        private const int LOG_CAPACITY = 100;
        private const int LOG_TAIL_LINES = 18;

        private Canvas _canvas = null;
        private GameObject _root = null;
        private Transform _categoryBar = null;
        private Transform _contentParent = null;
        private Font _font = null;

        private readonly Dictionary<string, Transform> _categories = new Dictionary<string, Transform>();
        private readonly List<(Text text, string label, Func<string> getter)> _labels = new List<(Text, string, Func<string>)>();
        private readonly Queue<string> _logBuffer = new Queue<string>();
        private string _activeCategory = null;
        private float _nextLabelRefresh = 0f;

        // FPS 計測
        private float _fpsAccum = 0f;
        private int _fpsFrames = 0;
        private float _fpsValue = 0f;

        public bool IsOpen => _root != null && _root.activeSelf;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            Application.logMessageReceived += OnLogMessage;
            EnsureSetup();
            RegisterBuiltInItems();
        }

        protected override void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessage;
            base.OnDestroy();
        }

        private void Update()
        {
            // FPS は閉じていても計測し続ける (開いた瞬間から正しい値を出すため)
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsAccum >= 0.5f)
            {
                _fpsValue = _fpsFrames / _fpsAccum;
                _fpsAccum = 0f;
                _fpsFrames = 0;
            }

            if (!IsOpen || Time.unscaledTime < _nextLabelRefresh)
            {
                return;
            }
            _nextLabelRefresh = Time.unscaledTime + LABEL_REFRESH_INTERVAL;
            foreach ((Text text, string label, Func<string> getter) entry in _labels)
            {
                if (!entry.text) continue;
                string value;
                try { value = entry.getter(); }
                catch (Exception e) { value = "<error: " + e.Message + ">"; }
                entry.text.text = string.IsNullOrEmpty(entry.label) ? value : entry.label + " : " + value;
            }
        }

        // ---- 公開 API ----

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            EnsureSetup();
            _root.SetActive(true);
            _nextLabelRefresh = 0f;
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
        }

        /// <summary>ボタン項目を追加する。</summary>
        public void AddButton(string category, string label, Action onClick)
        {
            Transform row = MakeRow(category);
            Button button = MakeButton(row, label, () =>
            {
                try { onClick?.Invoke(); }
                catch (Exception e) { SafeLogger.LogError($"[DebugMenu] '{label}' 実行時エラー: {e.Message}"); }
            });
            Stretch(button);
        }

        /// <summary>ON/OFFトグル項目を追加する。</summary>
        public void AddToggle(string category, string label, Func<bool> getter, Action<bool> setter)
        {
            Transform row = MakeRow(category);
            Button button = null;
            Text buttonLabel = null;
            button = MakeButton(row, "", () =>
            {
                bool next = !SafeGet(getter);
                setter?.Invoke(next);
                buttonLabel.text = ToggleLabel(label, next);
            });
            buttonLabel = button.GetComponentInChildren<Text>();
            buttonLabel.text = ToggleLabel(label, SafeGet(getter));
            Stretch(button);
            // コードから直接値が変更された場合も表示が追従するよう、定期更新に乗せる
            _labels.Add((buttonLabel, "", () => ToggleLabel(label, SafeGet(getter))));
        }

        /// <summary>値表示ラベルを追加する (開いている間、定期更新される)。</summary>
        public void AddLabel(string category, string label, Func<string> getter)
        {
            Transform row = MakeRow(category);
            Text text = MakeText(row, "", 26, TextAnchor.MiddleLeft);
            LayoutElement layout = text.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            _labels.Add((text, label, getter));
        }

        /// <summary>テキスト入力項目を追加する。Enter/フォーカスアウトで onSubmit が呼ばれる。</summary>
        public void AddInput(string category, string label, Func<string> getter, Action<string> onSubmit)
        {
            Transform row = MakeRow(category);
            Text caption = MakeText(row, label, 26, TextAnchor.MiddleLeft);
            LayoutElement captionLayout = caption.gameObject.AddComponent<LayoutElement>();
            captionLayout.preferredWidth = 320f;

            GameObject fieldGo = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            fieldGo.transform.SetParent(row, false);
            fieldGo.GetComponent<Image>().color = new Color(0.9f, 0.9f, 0.92f, 1f);
            LayoutElement fieldLayout = fieldGo.AddComponent<LayoutElement>();
            fieldLayout.flexibleWidth = 1f;
            fieldLayout.preferredHeight = 48f;

            InputField input = fieldGo.GetComponent<InputField>();
            Text inputText = MakeText(fieldGo.transform, "", 26, TextAnchor.MiddleLeft);
            inputText.color = Color.black;
            RectTransform textRt = (RectTransform)inputText.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10f, 4f);
            textRt.offsetMax = new Vector2(-10f, -4f);
            input.textComponent = inputText;
            input.text = SafeGetString(getter);
            input.onEndEdit.AddListener(value =>
            {
                try { onSubmit?.Invoke(value); }
                catch (Exception e) { SafeLogger.LogError($"[DebugMenu] '{label}' 入力エラー: {e.Message}"); }
            });
        }

        // ---- 内蔵項目 ----

        private void RegisterBuiltInItems()
        {
            AddLabel("System", "FPS", () => _fpsValue.ToString("F1"));
            AddLabel("System", "メモリ (確保)", () =>
                (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f)).ToString("F1") + " MB");
            AddLabel("System", "メモリ (予約)", () =>
                (UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f)).ToString("F1") + " MB");
            AddLabel("System", "", BuildLogTail);
            AddButton("System", "ログクリア", () => _logBuffer.Clear());
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            string prefix = "";
            if (type == LogType.Warning)
            {
                prefix = "[W] ";
            }
            else if (type == LogType.Error || type == LogType.Exception)
            {
                prefix = "[E] ";
            }
            _logBuffer.Enqueue(prefix + condition);
            while (_logBuffer.Count > LOG_CAPACITY)
            {
                _logBuffer.Dequeue();
            }
        }

        private string BuildLogTail()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("---- 直近ログ ----");
            int skip = Mathf.Max(0, _logBuffer.Count - LOG_TAIL_LINES);
            int index = 0;
            foreach (string line in _logBuffer)
            {
                if (index++ < skip) continue;
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        // ---- UI 構築 ----

        private void EnsureSetup()
        {
            if (_canvas != null) return;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            GameObject canvasGo = new GameObject("[DebugMenuCanvas]");
            canvasGo.transform.SetParent(transform, false);
            canvasGo.layer = 5;
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = _sortingOrder;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // ルートパネル
            GameObject rootGo = new GameObject("Root", typeof(RectTransform), typeof(Image));
            rootGo.transform.SetParent(canvasGo.transform, false);
            rootGo.layer = 5;
            RectTransform rootRt = (RectTransform)rootGo.transform;
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(1400f, 950f);
            rootGo.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.96f);
            _root = rootGo;

            Text title = MakeText(rootGo.transform, "DEBUG MENU", 30, TextAnchor.MiddleCenter);
            RectTransform titleRt = (RectTransform)title.transform;
            titleRt.anchorMin = new Vector2(0.5f, 1f);
            titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -34f);
            titleRt.sizeDelta = new Vector2(600f, 50f);
            title.color = new Color(1f, 0.8f, 0.4f, 1f);

            Button closeButton = MakeButton(rootGo.transform, "閉じる", Close);
            RectTransform closeRt = (RectTransform)closeButton.transform;
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.anchoredPosition = new Vector2(-16f, -12f);
            closeRt.sizeDelta = new Vector2(140f, 48f);

            // カテゴリバー
            GameObject barGo = new GameObject("Categories", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(rootGo.transform, false);
            RectTransform barRt = (RectTransform)barGo.transform;
            barRt.anchorMin = new Vector2(0f, 1f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(0.5f, 1f);
            barRt.anchoredPosition = new Vector2(0f, -70f);
            barRt.sizeDelta = new Vector2(-40f, 54f);
            HorizontalLayoutGroup bar = barGo.GetComponent<HorizontalLayoutGroup>();
            bar.spacing = 8f;
            bar.childControlWidth = true;
            bar.childControlHeight = true;
            bar.childForceExpandWidth = false;
            bar.childForceExpandHeight = true;
            bar.childAlignment = TextAnchor.MiddleLeft;
            _categoryBar = barGo.transform;

            // コンテンツ (スクロール)
            GameObject scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(rootGo.transform, false);
            RectTransform scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(20f, 20f);
            scrollRt.offsetMax = new Vector2(-20f, -134f);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;

            GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            RectTransform contentRt = (RectTransform)contentGo.transform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = Vector2.zero; // 新規RectTransformの既定(100,100)を消す (幅はアンカー追従)
            VerticalLayoutGroup contentLayout = contentGo.GetComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(14, 14, 10, 10);
            contentLayout.spacing = 6f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRt;
            _contentParent = contentGo.transform;

            _root.SetActive(false);
        }

        private Transform EnsureCategory(string category)
        {
            EnsureSetup();
            if (_categories.TryGetValue(category, out Transform existing))
            {
                return existing;
            }

            // タブボタン
            Button tab = MakeButton(_categoryBar, category, () => SwitchCategory(category));
            LayoutElement tabLayout = tab.gameObject.AddComponent<LayoutElement>();
            tabLayout.preferredWidth = 190f;

            // 項目コンテナ
            GameObject containerGo = new GameObject("Cat_" + category, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            containerGo.transform.SetParent(_contentParent, false);
            VerticalLayoutGroup layout = containerGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            containerGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _categories[category] = containerGo.transform;
            if (_activeCategory == null)
            {
                SwitchCategory(category);
            }
            else
            {
                containerGo.SetActive(false);
            }
            return containerGo.transform;
        }

        private void SwitchCategory(string category)
        {
            _activeCategory = category;
            foreach (KeyValuePair<string, Transform> pair in _categories)
            {
                pair.Value.gameObject.SetActive(pair.Key == category);
            }
            _nextLabelRefresh = 0f;
        }

        private Transform MakeRow(string category)
        {
            Transform parent = EnsureCategory(category);
            GameObject rowGo = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowGo.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = rowGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            rowGo.GetComponent<LayoutElement>().minHeight = 52f;
            return rowGo.transform;
        }

        private Button MakeButton(Transform parent, string label, Action onClick)
        {
            GameObject go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.layer = 5;
            go.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.32f, 1f);
            Button button = go.GetComponent<Button>();
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }
            Text text = MakeText(go.transform, label, 26, TextAnchor.MiddleCenter);
            RectTransform textRt = (RectTransform)text.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 2f);
            textRt.offsetMax = new Vector2(-8f, -2f);
            return button;
        }

        private Text MakeText(Transform parent, string content, int size, TextAnchor anchor)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = 5;
            Text text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.color = new Color(0.92f, 0.92f, 0.95f, 1f);
            text.text = content;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void Stretch(Button button)
        {
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.preferredHeight = 48f;
        }

        private static string ToggleLabel(string label, bool value)
        {
            return label + " : " + (value ? "ON" : "OFF");
        }

        private static bool SafeGet(Func<bool> getter)
        {
            try { return getter != null && getter(); }
            catch { return false; }
        }

        private static string SafeGetString(Func<string> getter)
        {
            try { return getter != null ? getter() : ""; }
            catch { return ""; }
        }
    }
}
