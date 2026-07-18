#if UNITY_EDITOR
using System.Linq;
using JetBrains.Annotations;
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
    [CanBeNull] private const string DEFAULT_BOOT_SCENE_PATH = "Assets/Scenes/Boot.unity";

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
        private bool _isPlaying = false;

        /// <summary>
        ///   <para>Specify the content and function of a main toolbar button.</para>
        /// </summary>
        /// <param name="action">The action to perform when the user selects the button.</param>
        /// <param name="isPlaying"></param>
        public MainDevelopmentBootToolButton(Action action, bool isPlaying)
        {
            this.content = new MainToolbarContent();
            this._action = action;
            this._isPlaying = isPlaying;
        }

        internal override VisualElement CreateElement()
        {
            string text = _isPlaying ? StopText : BootStartText;
            Texture2D texture = (_isPlaying ? StopTexture : PlayTexture) as Texture2D;
            CustomEditorToolbarButton element = new CustomEditorToolbarButton(text, texture, this._action);

            element.AddToClassList("unity-editor-toolbar-element");
            element.tooltip = DevelopmentBootTooltip;
            element.style.backgroundColor = _isPlaying ? Color.red : Color.green;
            element.style.color = _isPlaying ? Color.white : Color.black;
            element.IconElement.tintColor = _isPlaying ? Color.white : Color.black;
            return (VisualElement)element;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private class CustomEditorToolbarButton : ToolbarButton
    {
        private EditorToolbarContent m_Content;

        public Image IconElement => m_Content.iconElement;

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
            this.m_Content = new EditorToolbarContent((VisualElement)this, text, new EditorToolbarIcon(icon));
        }
    }
#endif


#if UNITY_6000_3_OR_NEWER
    private const string DevelopmentBootTooltip = "DevelopmentBoot";
    private const string MainToolbarElementPath = DevelopmentBootTooltip;
    private static bool _isPlaying = false;
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
        return new MainDevelopmentBootToolButton(
            action: OnClickButton,
            _isPlaying
        );
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
        string scenePath = DEFAULT_BOOT_SCENE_PATH;

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
            MainToolbar.Refresh(MainToolbarElementPath);
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
            MainToolbar.Refresh(MainToolbarElementPath);
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