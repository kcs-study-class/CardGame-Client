#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityFramework.WebViews
{
    /// <summary>
    /// WebView2 による Windows 実装。Unity のウィンドウ (エディタではエディタウィンドウ) の
    /// 子として WebView2 の HWND を重ねる。生成は非同期コールバックで完了する。
    /// </summary>
    internal sealed class WindowsWebView
    {
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out Win32Rect rect);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int maxCount);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int w, int h, uint flags);

        private const uint GW_CHILD = 5;
        private const uint GW_HWNDNEXT = 2;
        private const uint SWP_NOSIZE = 0x1;
        private const uint SWP_NOMOVE = 0x2;
        private const uint SWP_NOACTIVATE = 0x10;

        private ICoreWebView2Environment _environment = null;
        private ICoreWebView2Controller _controller = null;
        private ICoreWebView2 _webView = null;
        private IntPtr _hostWindow = default;
        private RectOffset _margins = new RectOffset();
        private string _pendingUrl = null;
        private Win32Rect _lastClient = default;

        /// <summary>コントローラ生成中 (コールバック待ち)。</summary>
        public bool IsCreating { get; private set; }

        public bool IsOpen => _controller != null;

        /// <summary>直近の失敗理由 (成功時は null)。</summary>
        public string LastError { get; private set; }

        /// <summary>
        /// WebView を開く (非同期開始)。完了は <see cref="IsOpen"/> のポーリングで判定する。
        /// </summary>
        public bool BeginOpen(string url, RectOffset margins)
        {
            LastError = null;
            _pendingUrl = url;
            _margins = margins != null ? margins : new RectOffset();

            if (IsOpen)
            {
                UpdateBounds(force: true);
                Navigate(url);
                return true;
            }
            if (IsCreating)
            {
                return true; // 生成中。完了時に _pendingUrl が開かれる
            }

            _hostWindow = GetActiveWindow();
            if (_hostWindow == IntPtr.Zero)
            {
                LastError = "ウィンドウハンドルを取得できませんでした。";
                return false;
            }

            IsCreating = true;
            if (_environment != null)
            {
                // 2回目以降は環境を再利用してコントローラだけ作る
                return CreateController();
            }

            string userDataFolder = Path.Combine(Application.persistentDataPath, "WebView2");
            int hr = WebView2Loader.CreateCoreWebView2EnvironmentWithOptions(
                null, userDataFolder, IntPtr.Zero, new EnvironmentHandler(this));
            if (hr != 0)
            {
                LastError = $"WebView2 環境の作成に失敗しました (HRESULT=0x{hr:X8})。WebView2 Runtime の有無を確認してください。";
                IsCreating = false;
                return false;
            }
            return true;
        }

        public void Navigate(string url)
        {
            if (_webView != null)
            {
                _webView.Navigate(url);
            }
            else
            {
                _pendingUrl = url;
            }
        }

        /// <summary>ホストウィンドウのリサイズに追従する (毎フレーム呼んでよい)。</summary>
        public void UpdateBounds(bool force = false)
        {
            if (_controller == null || _hostWindow == IntPtr.Zero)
            {
                return;
            }
            if (!GetClientRect(_hostWindow, out Win32Rect client))
            {
                return;
            }
            if (!force && client.Right == _lastClient.Right && client.Bottom == _lastClient.Bottom)
            {
                return;
            }
            _lastClient = client;
            Win32Rect bounds = new Win32Rect
            {
                Left = _margins.left,
                Top = _margins.top,
                Right = Math.Max(_margins.left, client.Right - _margins.right),
                Bottom = Math.Max(_margins.top, client.Bottom - _margins.bottom),
            };
            _controller.put_Bounds(bounds);
            _controller.NotifyParentWindowPositionChanged();
            BringToTop();
        }

        /// <summary>
        /// WebView2 の子ウィンドウを兄弟の最前面へ上げる。
        /// エディタではドックペインの子ウィンドウ描画に上書きされて見えなくなるため必須。
        /// </summary>
        private void BringToTop()
        {
            IntPtr webViewHwnd = FindWebViewChildWindow();
            if (webViewHwnd != IntPtr.Zero)
            {
                SetWindowPos(webViewHwnd, IntPtr.Zero /* HWND_TOP */, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        private IntPtr FindWebViewChildWindow()
        {
            IntPtr child = GetWindow(_hostWindow, GW_CHILD);
            System.Text.StringBuilder className = new System.Text.StringBuilder(256);
            int guard = 0;
            while (child != IntPtr.Zero && guard++ < 128)
            {
                className.Length = 0;
                GetClassName(child, className, 256);
                if (className.ToString().StartsWith("Chrome_WidgetWin", StringComparison.Ordinal))
                {
                    return child;
                }
                child = GetWindow(child, GW_HWNDNEXT);
            }
            return IntPtr.Zero;
        }

        public void Close()
        {
            _pendingUrl = null;
            if (_controller != null)
            {
                _controller.Close();
                Marshal.FinalReleaseComObject(_controller);
                _controller = null;
            }
            _webView = null;
            // _environment は再利用のため保持する
        }

        // ---- コールバック ----

        private void OnEnvironmentCreated(int errorCode, ICoreWebView2Environment environment)
        {
            if (errorCode != 0 || environment == null)
            {
                LastError = $"WebView2 環境の初期化に失敗しました (HRESULT=0x{errorCode:X8})。";
                IsCreating = false;
                return;
            }
            _environment = environment;
            CreateController();
        }

        private bool CreateController()
        {
            int hr = _environment.CreateCoreWebView2Controller(_hostWindow, new ControllerHandler(this));
            if (hr != 0)
            {
                LastError = $"WebView2 コントローラの作成に失敗しました (HRESULT=0x{hr:X8})。";
                IsCreating = false;
                return false;
            }
            return true;
        }

        private void OnControllerCreated(int errorCode, ICoreWebView2Controller controller)
        {
            IsCreating = false;
            if (errorCode != 0 || controller == null)
            {
                LastError = $"WebView2 コントローラの初期化に失敗しました (HRESULT=0x{errorCode:X8})。";
                return;
            }
            _controller = controller;

            int hr = controller.get_CoreWebView2(out _webView);
            if (hr != 0 || _webView == null)
            {
                LastError = $"CoreWebView2 の取得に失敗しました (HRESULT=0x{hr:X8})。";
                Close();
                return;
            }

            UpdateBounds(force: true);
            _controller.put_IsVisible(1);
            if (!string.IsNullOrEmpty(_pendingUrl))
            {
                _webView.Navigate(_pendingUrl);
                _pendingUrl = null;
            }
        }

        private sealed class EnvironmentHandler : ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
        {
            private readonly WindowsWebView _owner;
            public EnvironmentHandler(WindowsWebView owner) { _owner = owner; }

            public int Invoke(int errorCode, ICoreWebView2Environment createdEnvironment)
            {
                _owner.OnEnvironmentCreated(errorCode, createdEnvironment);
                return 0;
            }
        }

        private sealed class ControllerHandler : ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
        {
            private readonly WindowsWebView _owner;
            public ControllerHandler(WindowsWebView owner) { _owner = owner; }

            public int Invoke(int errorCode, ICoreWebView2Controller createdController)
            {
                _owner.OnControllerCreated(errorCode, createdController);
                return 0;
            }
        }
    }
}
#endif
