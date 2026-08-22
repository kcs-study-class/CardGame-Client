#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#endif

/// <summary>
/// Windows スタンドアロンビルドをワンクリックで実行するツールバーボタン + メニュー。
/// Addressables のコンテンツビルド → プレイヤービルドの順で行い、完了後に出力フォルダを開く。
/// (StartScenePlayButton と同じ MainToolbarElement 方式)
/// </summary>
[InitializeOnLoad]
public static class WindowsBuildButton
{
    private const string BUTTON_TEXT = "Win Build";
    private const string OUTPUT_DIR = "Builds/Windows";
    private const string EXE_NAME = "CardGame.exe";

    private static readonly Texture BUILD_TEXTURE =
        EditorGUIUtility.IconContent("BuildSettings.Standalone.Small").image;

#if UNITY_6000_3_OR_NEWER
    private const string TOOLBAR_TOOLTIP = "WindowsBuild";
    private const string MAIN_TOOLBAR_ELEMENT_PATH = TOOLBAR_TOOLTIP;

    private class WindowsBuildToolButton : MainToolbarElement
    {
        private readonly Action _action;

        public WindowsBuildToolButton(Action action)
        {
            this.content = new MainToolbarContent();
            _action = action;
        }

        internal override VisualElement CreateElement()
        {
            var element = new CustomToolbarButton(BUTTON_TEXT, BUILD_TEXTURE as Texture2D, _action);
            element.AddToClassList("unity-editor-toolbar-element");
            element.tooltip = TOOLBAR_TOOLTIP;
            return element;
        }
    }

    private class CustomToolbarButton : UnityEditor.UIElements.ToolbarButton
    {
        public CustomToolbarButton(string text, Texture2D icon, Action clickEvent)
            : base(clickEvent)
        {
            AddToClassList("unity-editor-toolbar-element");
            _ = new EditorToolbarContent(this, text, new EditorToolbarIcon(icon));
        }
    }

    [MainToolbarElement(MAIN_TOOLBAR_ELEMENT_PATH, defaultDockPosition = MainToolbarDockPosition.Middle)]
    public static MainToolbarElement Create()
    {
        return new WindowsBuildToolButton(BuildRelease);
    }
#endif

    static WindowsBuildButton()
    {
    }

    [MenuItem("Tools/Build/Windows (Release)")]
    public static void BuildRelease() => Build(BuildOptions.None);

    [MenuItem("Tools/Build/Windows (Development)")]
    public static void BuildDevelopment() => Build(BuildOptions.Development | BuildOptions.AllowDebugging);

    private static void Build(BuildOptions options)
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[WindowsBuild] 再生中はビルドできません。停止してから実行してください。");
            return;
        }

        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[WindowsBuild] Build Settings に有効なシーンがありません。");
            return;
        }

        // Addressables のコンテンツを先にビルドする (これを忘れるとアセットが読めないビルドになる)
        Debug.Log("[WindowsBuild] Addressables コンテンツをビルド中...");
        AddressableAssetSettings.CleanPlayerContent();
        AddressableAssetSettings.BuildPlayerContent();

        string exePath = Path.Combine(OUTPUT_DIR, EXE_NAME);
        Debug.Log($"[WindowsBuild] プレイヤーをビルド中... → {exePath}");
        BuildReport report = BuildPipeline.BuildPlayer(
            scenes, exePath, BuildTarget.StandaloneWindows64, options);

        BuildSummary summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[WindowsBuild] 成功: {summary.outputPath} ({summary.totalSize / (1024 * 1024)}MB, {summary.totalTime.TotalSeconds:F0}秒)");
            EditorUtility.RevealInFinder(summary.outputPath);
        }
        else
        {
            Debug.LogError($"[WindowsBuild] 失敗: {summary.result} (エラー {summary.totalErrors} 件)");
        }
    }
}
#endif
