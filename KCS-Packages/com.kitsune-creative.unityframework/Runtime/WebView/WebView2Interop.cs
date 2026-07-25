#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Runtime.InteropServices;

namespace UnityFramework.WebViews
{
    // =====================================================================
    // Microsoft Edge WebView2 の COM interop (必要最小限)。
    //
    // - マネージド SDK (WinForms/WPF) は Unity で動かないため、ネイティブの
    //   WebView2Loader.dll + COM インターフェースを直接呼ぶ
    // - COM の vtable はスロット位置がすべてなので、呼ばないメソッドも
    //   「プレースホルダ」として正しい順序で宣言している (絶対に呼ばないこと)
    // - IID は WebView2 SDK (WebView2.idl) の安定版 v1 のもの
    // =====================================================================

    internal static class WebView2Loader
    {
        [DllImport("WebView2Loader", CharSet = CharSet.Unicode)]
        public static extern int CreateCoreWebView2EnvironmentWithOptions(
            string browserExecutableFolder,
            string userDataFolder,
            IntPtr environmentOptions,
            ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler environmentCreatedHandler);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [ComImport, Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
    {
        [PreserveSig] int Invoke(int errorCode, ICoreWebView2Environment createdEnvironment);
    }

    [ComImport, Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f431"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
    {
        [PreserveSig] int Invoke(int errorCode, ICoreWebView2Controller createdController);
    }

    [ComImport, Guid("b96d755e-0319-4e92-a296-23436f46a1fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2Environment
    {
        [PreserveSig] int CreateCoreWebView2Controller(IntPtr parentWindow, ICoreWebView2CreateCoreWebView2ControllerCompletedHandler handler); // 0
        [PreserveSig] int PH_CreateWebResourceResponse();          // 1 (未使用)
        [PreserveSig] int PH_get_BrowserVersionString();           // 2 (未使用)
        [PreserveSig] int PH_add_NewBrowserVersionAvailable();     // 3 (未使用)
        [PreserveSig] int PH_remove_NewBrowserVersionAvailable();  // 4 (未使用)
    }

    [ComImport, Guid("4d00c0d1-9434-4eb6-8078-8697a560334f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2Controller
    {
        [PreserveSig] int PH_get_IsVisible();                      // 0 (未使用)
        [PreserveSig] int put_IsVisible(int isVisible);            // 1
        [PreserveSig] int PH_get_Bounds();                         // 2 (未使用)
        [PreserveSig] int put_Bounds(Win32Rect bounds);            // 3
        [PreserveSig] int PH_get_ZoomFactor();                     // 4 (未使用)
        [PreserveSig] int PH_put_ZoomFactor();                     // 5 (未使用)
        [PreserveSig] int PH_add_ZoomFactorChanged();              // 6 (未使用)
        [PreserveSig] int PH_remove_ZoomFactorChanged();           // 7 (未使用)
        [PreserveSig] int PH_SetBoundsAndZoomFactor();             // 8 (未使用)
        [PreserveSig] int PH_MoveFocus();                          // 9 (未使用)
        [PreserveSig] int PH_add_MoveFocusRequested();             // 10 (未使用)
        [PreserveSig] int PH_remove_MoveFocusRequested();          // 11 (未使用)
        [PreserveSig] int PH_add_GotFocus();                       // 12 (未使用)
        [PreserveSig] int PH_remove_GotFocus();                    // 13 (未使用)
        [PreserveSig] int PH_add_LostFocus();                      // 14 (未使用)
        [PreserveSig] int PH_remove_LostFocus();                   // 15 (未使用)
        [PreserveSig] int PH_add_AcceleratorKeyPressed();          // 16 (未使用)
        [PreserveSig] int PH_remove_AcceleratorKeyPressed();       // 17 (未使用)
        [PreserveSig] int PH_get_ParentWindow();                   // 18 (未使用)
        [PreserveSig] int PH_put_ParentWindow();                   // 19 (未使用)
        [PreserveSig] int NotifyParentWindowPositionChanged();     // 20
        [PreserveSig] int Close();                                 // 21
        [PreserveSig] int get_CoreWebView2(out ICoreWebView2 coreWebView2); // 22
    }

    [ComImport, Guid("76eceacb-0462-4d94-ac83-423a6793775e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICoreWebView2
    {
        [PreserveSig] int PH_get_Settings();                       // 0 (未使用)
        [PreserveSig] int PH_get_Source();                         // 1 (未使用)
        [PreserveSig] int Navigate([MarshalAs(UnmanagedType.LPWStr)] string uri); // 2
        // これ以降のスロットは宣言省略。このインターフェース経由で他のメソッドを呼ばないこと。
    }
}
#endif
