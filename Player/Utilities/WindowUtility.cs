using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace ContentDistributionPlayer.Utilities
{
    public static class WindowUtility
    {
        public static bool Find(Func<IntPtr, bool> fn)
        {
            return EnumWindows((hwnd, lp) => !fn(hwnd), 0) == 0;
        }
        public static string GetClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(1024);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        public static uint GetProcessId(IntPtr hwnd)     // {0:X8}
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return pid;
        }
        public static string GetText(IntPtr hwnd)
        {
            var sb = new StringBuilder(1024);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetBrowserPath()
        {
            using (RegistryKey userChoiceKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"))
            {
                if (userChoiceKey != null)
                {
                    object progIdValue = userChoiceKey.GetValue("Progid");
                    if (progIdValue != null)
                    {
                        string progId = progIdValue.ToString();


                        const string exeSuffix = ".exe";
                        string path = progId + @"\shell\open\command";
                        FileInfo browserPath;
                        using (RegistryKey pathKey = Registry.ClassesRoot.OpenSubKey(path))
                        {
                            if (pathKey == null)
                            {
                                return string.Empty;
                            }

                            // Trim parameters.
                            try
                            {
                                path = pathKey.GetValue(null).ToString().ToLower().Replace("\"", "");
                                if (!path.EndsWith(exeSuffix))
                                {
                                    path = path.Substring(0, path.LastIndexOf(exeSuffix, StringComparison.Ordinal) + exeSuffix.Length);
                                    browserPath = new FileInfo(path);
                                    return browserPath.FullName;
                                }
                            }
                            catch
                            {
                                // Assume the registry value is set incorrectly, or some funky browser is used which currently is unknown.
                            }
                        }
                    }
                }
            }
            return string.Empty;
        }

        delegate bool CallBackPtr(IntPtr hwnd, int lParam);

        [DllImport("user32.dll")]
        static extern int EnumWindows(CallBackPtr callPtr, int lPar);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("User32", CharSet = CharSet.Auto, ExactSpelling = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndParent);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int W, int H, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("USER32.DLL")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("USER32.DLL")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("User32.dll")]
        public static extern bool SetForegroundWindow(IntPtr handle);

        // Necessary dll import
        [DllImport("urlmon.dll")]
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Error)] public static extern int CoInternetSetFeatureEnabled(int FeatureEntry, [MarshalAs(UnmanagedType.U4)] int dwFlags, bool fEnable);

    }
}
