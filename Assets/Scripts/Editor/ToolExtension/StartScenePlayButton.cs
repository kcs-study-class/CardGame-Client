#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
using System;

#else
using Toolbar = UnityEditor.Toolbar;
#endif

[InitializeOnLoad]
public static class StartScenePlayButton
{
    private const string DefaultBootScenePath = "Assets/Scenes/Boot.unity";

    private const string BootStartText = "Boot Start";
    private const string StopText = "Stop";


    private static readonly Texture
        PlayTexture = EditorGUIUtility.IconContent("Animation.Play").image;

    private static readonly Texture
        StopTexture = EditorGUIUtility.IconContent("PreMatQuad").image;


#if UNITY_6000_3_OR_NEWER
    private class MainDevelopmentBootToolButton : MainToolbarElement
    {
        private readonly Action _action;

        /// <summary>
        ///   <para>Specify the content and function of a main toolbar button.</para>
        /// </summary>
        /// <param name="action">The action to perform when the user selects the button.</param>
        public MainDevelopmentBootToolButton(Action action)
        {
            this.content = new MainToolbarContent();
            this._action = action;
        }

        internal override VisualElement CreateElement()
        {
            var element = new CustomEditorToolbarButton(BootStartText, PlayTexture as Texture2D, this._action);
            element.AddToClassList("unity-editor-toolbar-element");
            element.tooltip = DevelopmentBootTooltip;

            // 生成済み要素を保持し、再生状態の反映は ApplyVisualState で直接行う
            // (MainToolbar.Refresh による再生成は 6000.3 で反映されないため頼らない)
            _liveButton = element;
            ApplyVisualState(element, EditorApplication.isPlayingOrWillChangePlaymode);

            // 自己修復: ドメインリロードとイベントの順序に依存しないよう、
            // 要素自身が定期的に実状態と表示を同期する (変化時のみ書き換え)
            element.schedule.Execute(() =>
                ApplyVisualState(element, EditorApplication.isPlayingOrWillChangePlaymode))
                .Every(250);
            return (VisualElement)element;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private class CustomEditorToolbarButton : ToolbarButton
    {
        private EditorToolbarContent _content;

        public Image IconElement => _content.iconElement;

        /// <summary>ラベルの TextElement (状態反映で文言を書き換えるために保持)。</summary>
        public TextElement LabelElement { get; private set; }

        /// <summary>
        ///   <para>Constructor.</para>
        /// </summary>
        /// <param name="clickEvent">Action triggered when the button is clicked.</param>
        /// <param name="text">The text associated with the element.</param>
        /// <param name="icon">The icon associated with the element.</param>
        public CustomEditorToolbarButton(string text, Action clickEvent)
            : this(text, (Texture2D)null, clickEvent)
        {
        }

        /// <summary>
        ///   <para>Constructor.</para>
        /// </summary>
        /// <param name="clickEvent">Action triggered when the button is clicked.</param>
        /// <param name="text">The text associated with the element.</param>
        /// <param name="icon">The icon associated with the element.</param>
        public CustomEditorToolbarButton(string text, Texture2D icon, Action clickEvent)
            : base(clickEvent)
        {
            this.AddToClassList("unity-editor-toolbar-element");
            this._content = new EditorToolbarContent((VisualElement)this, text, new EditorToolbarIcon(icon));

            // EditorToolbarContent が生成したラベル要素を拾っておく (自分自身は除外)
            LabelElement = this.Q<Label>();
            if (LabelElement == null)
            {
                this.Query<TextElement>().ForEach(t =>
                {
                    if (t != this && LabelElement == null)
                    {
                        LabelElement = t;
                    }
                });
            }
        }
    }
#endif


#if UNITY_6000_3_OR_NEWER
    private const string DevelopmentBootTooltip = "DevelopmentBoot";
    private const string MainToolbarElementPath = DevelopmentBootTooltip;
    private static bool _isPlaying = false;
    private static CustomEditorToolbarButton _liveButton;
    private static bool? _lastAppliedState;

    /// <summary>再生状態をボタンの見た目に反映する (生成済み要素を直接書き換える)。</summary>
    private static void ApplyVisualState(CustomEditorToolbarButton element, bool isPlaying)
    {
        if (element == null || _lastAppliedState == isPlaying)
        {
            return;
        }
        _lastAppliedState = isPlaying;
        element.style.backgroundColor = isPlaying ? Color.red : Color.green;
        element.style.color = isPlaying ? Color.white : Color.black;
        if (element.IconElement != null)
        {
            element.IconElement.image = (isPlaying ? StopTexture : PlayTexture) as Texture2D;
            element.IconElement.tintColor = isPlaying ? Color.white : Color.black;
        }
        // 文言の実体は EditorToolbarContent が生やした子の TextElement。
        // ルート (ToolbarButton 自身も TextElement) に文字を入れると二重描画になるため空にする
        string label = isPlaying ? StopText : BootStartText;
        var labelColor = isPlaying ? Color.white : Color.black;
        element.Query<TextElement>().ForEach(t =>
        {
            if (t == element)
            {
                t.text = string.Empty;
                return;
            }
            t.text = label;
            t.style.color = labelColor;
        });
    }
#else
    private static Label _bootLabel = default;
    private static ToolbarButton _playBootSceneButton = default;
    private static Image _iconImage = default;
#endif

    private static string _previousScenePath = string.Empty;

    static StartScenePlayButton()
    {
        EditorApplication.update += OnUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

#if UNITY_6000_3_OR_NEWER
    [MainToolbarElement(MainToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
    public static MainToolbarElement Create()
    {
        return new MainDevelopmentBootToolButton(action: OnClickButton);
    }
#endif

    /// <summary>
    /// 
    /// </summary>
    [MenuItem("Tools/Run/DevelopmentBoot")]
    public static void RunBoot()
    {
        //Run("Assets/BJ/Scenes/DevelopmentBoot.unity");

        // Build Settings に登録されているシーン一覧から "Boot" を含むものを探す.
        EditorBuildSettingsScene bootScene = EditorBuildSettings.scenes
            .FirstOrDefault(s => s.enabled && s.path.Contains("Boot"));

        // Defaultのパスを仮で入れておく.
        string scenePath = DefaultBootScenePath;

        if (bootScene != null)
        {
            scenePath = bootScene.path;
        }

        Run(scenePath);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="scenePath"></param>
    private static void Run(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
        {
            return;
        }

        // 現在のシーンを保存.
        _previousScenePath = SceneManager.GetActiveScene().path;
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene(scenePath);
            EditorApplication.isPlaying = true;
        }
    }

    /// <summary>
    /// Editorの描画更新に合わせて描画を更新する処理.
    /// </summary>
    private static void OnUpdate()
    {
#if UNITY_6000_3_OR_NEWER
#else
        Toolbar toolbar = Toolbar.get; // ツールバーの取得.
        if (toolbar.windowBackend?.visualTree is not VisualElement visualTree)
        {
            return; // ツールバーのVisualTreeを取得
        }

        if (visualTree.Q("ToolbarZonePlayMode") is not { } zone)
        {
            return; // ツールバーのゾーンを取得
        }
#endif

        EditorApplication.update -= OnUpdate; // 描画は一回のみでよい

#if UNITY_6000_3_OR_NEWER
#else
        _playBootSceneButton = new ToolbarButton();
        _iconImage = new Image()
        {
            image = PlayTexture
        };
        _playBootSceneButton.Add(_iconImage);
        _bootLabel = new Label(BootStartText);
        _playBootSceneButton.Add(_bootLabel);
        _playBootSceneButton.style.flexDirection = FlexDirection.Row;
        _playBootSceneButton.clicked += OnClickButton;
        zone.Add(_playBootSceneButton);
#endif
        OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
    }

    /// <summary>
    /// 
    /// </summary>
    private static void OnClickButton()
    {
        if (!EditorApplication.isPlaying)
        {
            RunBoot();
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }

    /// <summary>
    /// FIXME : 6000.3.8 で挙動変更...
    /// </summary>
    /// <param name="state"></param>
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // 再生終了後に元のシーンを開く.
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (!string.IsNullOrEmpty(_previousScenePath))
            {
                EditorSceneManager.OpenScene(_previousScenePath);
                _previousScenePath = string.Empty;
            }
#if UNITY_6000_3_OR_NEWER
            _isPlaying = false;
            if (_liveButton != null)
            {
                ApplyVisualState(_liveButton, false);
            }
            else
            {
                MainToolbar.Refresh(MainToolbarElementPath); // 要素未生成時のみ (CreateElement 側で反映される)
            }
#else
            if (_playBootSceneButton != null
                && _playBootSceneButton.style != null)
            {
                _playBootSceneButton.style.backgroundColor = Color.green;
            }

            if (_bootLabel != null)
            {
                _bootLabel.style.color = Color.black;
                _bootLabel.text = BootStartText;
            }

            if (_iconImage != null)
            {
                _iconImage.image = PlayTexture;
                _iconImage.tintColor = Color.black;
            }
#endif
        }

        else if (state == PlayModeStateChange.EnteredPlayMode
                 || state == PlayModeStateChange.ExitingEditMode)
        {
#if UNITY_6000_3_OR_NEWER
            _isPlaying = true;
            if (_liveButton != null)
            {
                ApplyVisualState(_liveButton, true);
            }
            else
            {
                MainToolbar.Refresh(MainToolbarElementPath); // 要素未生成時のみ (CreateElement 側で反映される)
            }
#else
            if (_bootLabel == null)
            {
                return;
            }
            if (_playBootSceneButton != null
                && _playBootSceneButton.style != null)
            {
                _playBootSceneButton.style.backgroundColor = Color.red;
            }

            if (_bootLabel != null)
            {
                _bootLabel.style.color = Color.white;
                _bootLabel.text = StopText;
            }


            if (_iconImage != null)
            {
                _iconImage.image = StopTexture;
                _iconImage.tintColor = Color.white;
            }
#endif
        }
    }
}
#endif