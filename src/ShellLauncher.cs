// Windows Audio Keys - 릴리스 페이지 열기.
// 로그인 자동 시작 인스턴스는 관리자 권한으로 실행된다. 그 상태에서 주소를 그냥 열면
// 브라우저까지 관리자 권한으로 뜨므로, 이때는 바탕 화면 셸(Explorer)에 실행을 맡겨
// 일반 사용자 권한으로 연다. 방법: Raymond Chen, "How can I launch an unelevated process
// from my elevated process and vice versa?" (The Old New Thing, 2013-11-18).
// 셸에 맡기지 못하면 브라우저를 열지 않고 주소를 클립보드에 복사한다.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GameAudioKeys
{
    internal static class ShellLauncher
    {
        private const int SwcDesktop = 8;          // SWC_DESKTOP
        private const int SwfoNeedDispatch = 1;    // SWFO_NEEDDISPATCH
        private const int CsidlDesktop = 0;        // CSIDL_DESKTOP
        private const uint SvgioBackground = 0;    // SVGIO_BACKGROUND
        private const int SwShowNormal = 1;

        private static readonly Guid ClsidShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid SidTopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid IidShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid IidDispatch = new Guid("00020400-0000-0000-C000-000000000046");

        // UI(STA) 스레드에서 부른다. 브라우저 열기를 요청했으면 true, 아니면 false와 안내 문구.
        public static bool OpenReleasePage(string url, out string messageKo)
        {
            messageKo = null;
            if (!UpdateCheckLogic.IsAllowedReleaseUrl(url))
            {
                messageKo = "허용되지 않은 주소라 열지 않았습니다.";
                return false;
            }

            // 권한을 판단하지 못하면 안전한 쪽(관리자 권한으로 간주)을 택한다.
            bool elevated;
            try
            {
                elevated = StartupTaskHost.IsElevated();
            }
            catch (Exception)
            {
                elevated = true;
            }

            if (!elevated)
            {
                try
                {
                    ProcessStartInfo start = new ProcessStartInfo(url);
                    start.UseShellExecute = true;
                    using (Process.Start(start))
                    {
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    return CopyInstead(url, "브라우저를 열지 못했습니다(" + ex.Message + ").", out messageKo);
                }
            }

            if (TryShellExecuteUnelevated(url))
            {
                return true;
            }

            return CopyInstead(url, "관리자 권한으로 실행 중이라 브라우저를 직접 열지 않았습니다.", out messageKo);
        }

        private static bool CopyInstead(string url, string reason, out string messageKo)
        {
            try
            {
                Clipboard.SetText(url);
                messageKo = reason + " 릴리스 주소를 클립보드에 복사했습니다. 브라우저 주소창에 붙여 넣으세요.";
            }
            catch (Exception)
            {
                messageKo = reason + " 릴리스 주소: " + url;
            }

            return false;
        }

        // 바탕 화면 셸 뷰 → Shell.Application → IShellDispatch2.ShellExecute.
        // 셸이 없거나(대체 셸), 연결이 실패하면 false. 예외를 밖으로 던지지 않는다.
        private static bool TryShellExecuteUnelevated(string url)
        {
            object shellWindows = null;
            object desktopBrowser = null;
            object shellBrowserObject = null;
            IShellViewNative shellView = null;
            object folderView = null;
            object shellApplication = null;
            try
            {
                Type shellWindowsType = Type.GetTypeFromCLSID(ClsidShellWindows, true);
                shellWindows = Activator.CreateInstance(shellWindowsType);

                // FindWindowSW(&CSIDL_DESKTOP, &empty, SWC_DESKTOP, &hwnd, SWFO_NEEDDISPATCH)
                object[] args = new object[] { CsidlDesktop, null, SwcDesktop, 0, SwfoNeedDispatch };
                ParameterModifier byRef = new ParameterModifier(5);
                byRef[3] = true;
                desktopBrowser = shellWindowsType.InvokeMember(
                    "FindWindowSW",
                    BindingFlags.InvokeMethod,
                    null,
                    shellWindows,
                    args,
                    new ParameterModifier[] { byRef },
                    null,
                    null);

                IComServiceProvider provider = desktopBrowser as IComServiceProvider;
                if (provider == null)
                {
                    return false;
                }

                Guid service = SidTopLevelBrowser;
                Guid iid = IidShellBrowser;
                provider.QueryService(ref service, ref iid, out shellBrowserObject);
                IShellBrowserNative shellBrowser = shellBrowserObject as IShellBrowserNative;
                if (shellBrowser == null)
                {
                    return false;
                }

                shellBrowser.QueryActiveShellView(out shellView);
                if (shellView == null)
                {
                    return false;
                }

                Guid dispatch = IidDispatch;
                shellView.GetItemObject(SvgioBackground, ref dispatch, out folderView);
                if (folderView == null)
                {
                    return false;
                }

                shellApplication = folderView.GetType().InvokeMember(
                    "Application",
                    BindingFlags.GetProperty,
                    null,
                    folderView,
                    null);
                if (shellApplication == null)
                {
                    return false;
                }

                // IShellDispatch2.ShellExecute(File, vArgs, vDir, vOperation, vShow)
                shellApplication.GetType().InvokeMember(
                    "ShellExecute",
                    BindingFlags.InvokeMethod,
                    null,
                    shellApplication,
                    new object[] { url, string.Empty, string.Empty, "open", SwShowNormal });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                Release(shellApplication);
                Release(folderView);
                Release(shellView);
                Release(shellBrowserObject);
                Release(desktopBrowser);
                Release(shellWindows);
            }
        }

        private static void Release(object comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject))
            {
                return;
            }

            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch (Exception)
            {
                // 이미 해제된 RCW는 무시한다.
            }
        }

        // IServiceProvider(COM). System.IServiceProvider와 이름이 겹치지 않게 따로 둔다.
        [ComImport]
        [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IComServiceProvider
        {
            void QueryService(
                [In] ref Guid guidService,
                [In] ref Guid riid,
                [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
        }

        // IShellBrowser. 호출하는 것은 QueryActiveShellView뿐이지만 vtable 순서를 맞추려고
        // 앞선 메서드를 모두 같은 순서로 선언한다(IOleWindow 2개 포함).
        [ComImport]
        [Guid("000214E2-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowserNative
        {
            void GetWindow(out IntPtr phwnd);
            void ContextSensitiveHelp(int fEnterMode);
            void InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
            void SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
            void RemoveMenusSB(IntPtr hmenuShared);
            void SetStatusTextSB(IntPtr pszStatusText);
            void EnableModelessSB(int fEnable);
            void TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
            void BrowseObject(IntPtr pidl, uint wFlags);
            void GetViewStateStream(uint grfMode, IntPtr ppStrm);
            void GetControlWindow(uint id, out IntPtr phwnd);
            void SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr pret);
            void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellViewNative ppshv);
            void OnViewWindowActive(IntPtr pshv);
            void SetToolbarItems(IntPtr lpButtons, uint nButtons, uint uFlags);
        }

        // IShellView. 호출하는 것은 GetItemObject뿐이며 vtable 순서를 맞춰 선언한다.
        [ComImport]
        [Guid("000214E3-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellViewNative
        {
            void GetWindow(out IntPtr phwnd);
            void ContextSensitiveHelp(int fEnterMode);
            void TranslateAccelerator(IntPtr pmsg);
            void EnableModeless(int fEnable);
            void UIActivate(uint uState);
            void Refresh();
            void CreateViewWindow(IntPtr psvPrevious, IntPtr pfs, IntPtr psb, IntPtr prcView, out IntPtr phWnd);
            void DestroyViewWindow();
            void GetCurrentInfo(IntPtr pfs);
            void AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lparam);
            void SaveViewState();
            void SelectItem(IntPtr pidlItem, uint uFlags);
            void GetItemObject(uint uItem, [In] ref Guid riid, [MarshalAs(UnmanagedType.IDispatch)] out object ppv);
        }
    }
}
