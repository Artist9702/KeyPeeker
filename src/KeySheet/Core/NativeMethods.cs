using System.Runtime.InteropServices;

namespace KeySheet.Core;

internal static partial class NativeMethods
{
    // ---------- 窗口 / 进程 ----------
    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    internal static partial int GetWindowText(IntPtr hWnd, IntPtr lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    internal static partial int GetWindowTextLength(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

    // ---------- Win32 菜单 ----------
    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetMenu(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetSubMenu(IntPtr hMenu, int nPos);

    [LibraryImport("user32.dll")]
    internal static partial int GetMenuItemCount(IntPtr hMenu);

    [LibraryImport("user32.dll", EntryPoint = "GetMenuStringW")]
    internal static partial int GetMenuString(IntPtr hMenu, uint uIDItem, IntPtr lpString, int nMaxCount, uint uFlag);

    internal const uint MF_BYPOSITION = 0x400;

    // ---------- 光标 / 屏幕 ----------
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    // ---------- 低级键盘钩子 ----------
    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    internal static partial IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll", EntryPoint = "UnhookWindowsHookEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x100;
    internal const int WM_KEYUP = 0x101;
    internal const int WM_SYSKEYDOWN = 0x104;
    internal const int WM_SYSKEYUP = 0x105;

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr GetModuleHandle(string? lpModuleName);

    // ---------- 异步按键状态 ----------
    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    // ---------- 控制台（--diag 模式用） ----------
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachConsole(uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FreeConsole();

    internal const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    // ---------- 窗口扩展样式（弹窗不抢焦点） ----------
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    internal static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x80;
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    internal const int WS_EX_TOPMOST = 0x8;

    // ---------- 菜单项信息（获取命令 ID，用于把加速键表对应回菜单项） ----------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MENUITEMINFO
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    internal const uint MIIM_ID = 0x00000002;

    [LibraryImport("user32.dll", EntryPoint = "GetMenuItemInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMenuItemInfo(IntPtr hMenu, uint uItem, [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFO lpmii);

    // ---------- 加速键表资源（读取目标 exe 内的真实快捷键） ----------
    [StructLayout(LayoutKind.Sequential)]
    internal struct ACCEL
    {
        public ushort fVirt;
        public ushort key;
        public ushort cmd;
    }

    internal const ushort FVIRTKEY = 0x01;
    internal const ushort FCONTROL = 0x08;
    internal const ushort FSHIFT = 0x04;
    internal const ushort FALT = 0x10;

    internal delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    internal const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    internal const uint LOAD_LIBRARY_AS_IMAGE = 0x00000020;
    internal static readonly IntPtr RT_ACCELERATOR = (IntPtr)9;

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FreeLibrary(IntPtr hLibModule);

    [LibraryImport("kernel32.dll", EntryPoint = "EnumResourceNamesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "LoadAcceleratorsW")]
    internal static partial IntPtr LoadAccelerators(IntPtr hInstance, IntPtr lpTableName);

    [LibraryImport("user32.dll", EntryPoint = "CopyAcceleratorTableW")]
    internal static partial int CopyAcceleratorTable(IntPtr hAccel, IntPtr lpAccel, int cEntries);
}
