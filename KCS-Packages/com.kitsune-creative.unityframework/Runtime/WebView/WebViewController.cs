using System.Threading;
using UnityEngine;

namespace UnityFramework.WebViews
{
    /// <summary>
    /// 共通 WebView。ゲーム内にウェブページ (お知らせ・規約・ヘルプ等) を重ねて表示する。
    ///
    /// 対応状況:
    /// - Windows (Editor / Standalone): WebView2 でウィンドウ内にオーバーレイ表示
    ///   (要: WebView2 Runtime — Win10/11 標準搭載, Plugins の WebView2Loader.dll)
    /// - その他のプラットフォーム: 未対応。外部ブラウザ (Application.OpenURL) にフォールバック
    ///   (モバイル実装は必要になった時点でこの窓口の裏に追加する)
    ///
    /// 使い方:
    /// <code>
    /// await WebViewController.Instance.OpenAsync("https://example.com", new RectOffset(100, 100, 80, 80));
    /// WebViewController.Instance.Close();
    /// </code>
    /// 注意: ネイティブウィンドウを Unity 描画の上に重ねる方式のため、uGUI より常に前面になる。
    /// </summary>
    [DisallowMultipleComponent]
    public class WebViewController : SingletonMonoBehaviour<WebViewController>
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private WindowsWebView _impl;
#endif

        /// <summary>この環境でゲーム内表示が可能か (false の場合 OpenAsync は外部ブラウザに逃がす)。</summary>
        public bool IsSupported
        {
            get
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return true;
#else
                return false;
#endif
            }
        }

        public bool IsOpen
        {
            get
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return _impl != null && _impl.IsOpen;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// WebView を開く。margins はウィンドウ端からの余白 (px)。
        /// ゲーム内表示できない場合は外部ブラウザで開いて false を返す。
        /// </summary>
        public async Awaitable<bool> OpenAsync(string url, RectOffset margins = null, CancellationToken cancellationToken = default)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (_impl == null)
            {
                _impl = new WindowsWebView();
            }
            if (!_impl.BeginOpen(url, margins))
            {
                return FallbackToExternalBrowser(url, _impl.LastError);
            }

            // コントローラ生成 (非同期コールバック) の完了を待つ
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!_impl.IsOpen && _impl.IsCreating && Time.realtimeSinceStartup < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(cancellationToken);
            }
            if (!_impl.IsOpen)
            {
                return FallbackToExternalBrowser(url, _impl.LastError ?? "タイムアウト");
            }
            return true;
#else
            await Awaitables.Completed;
            return FallbackToExternalBrowser(url, "このプラットフォームは未対応");
#endif
        }

        /// <summary>開いている WebView のページを切り替える。</summary>
        public void Navigate(string url)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            _impl?.Navigate(url);
#endif
        }

        public void Close()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            _impl?.Close();
#endif
        }

        private static bool FallbackToExternalBrowser(string url, string reason)
        {
            SafeLogger.LogWarning($"[WebView] ゲーム内表示できないため外部ブラウザで開きます ({reason})");
            Application.OpenURL(url);
            return false;
        }

        private void LateUpdate()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // ウィンドウリサイズへの追従
            _impl?.UpdateBounds();
#endif
        }

        protected override void OnDestroy()
        {
            Close();
            base.OnDestroy();
        }
    }
}
