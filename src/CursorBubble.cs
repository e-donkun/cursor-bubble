// CursorBubble
// マウス・キーボードの操作内容を、カーソルの横に吹き出しで表示する授業用ツール。
//
// ・管理者権限不要（ユーザーモードの低レベルフック WH_MOUSE_LL / WH_KEYBOARD_LL を使用）
// ・Windows 標準搭載の .NET Framework 4.x の csc.exe でビルドできるよう C# 5 の範囲で記述
// ・入力内容は表示に使うだけで、保存も送信もしない

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("CursorBubble")]
[assembly: AssemblyDescription("マウス・キーボード操作をカーソル横に表示する授業用ツール")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace CursorBubble
{
    // ------------------------------------------------------------------
    // Win32 API
    // ------------------------------------------------------------------
    static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A, WM_XBUTTONDOWN = 0x020B, WM_MOUSEHWHEEL = 0x020E;

        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000;

        public const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const int SW_HIDE = 0;
        public const int ULW_ALPHA = 2;
        public const int SM_CXDOUBLECLK = 36;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int CX; public int CY; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")]
        public static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT pt);

        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        public static bool IsDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
    }

    // ------------------------------------------------------------------
    // 設定（%APPDATA%\CursorBubble\settings.txt に保存）
    // ------------------------------------------------------------------
    class Settings
    {
        public int SizeIndex = 1;      // 0:小 1:中 2:大 3:特大
        public bool AllKeys = false;   // 文字キーも表示するか
        public bool Hints = true;      // ショートカットの説明を付けるか
        public bool Ripple = true;     // クリック位置の波紋

        static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CursorBubble", "settings.txt");
            }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] kv = line.Split('=');
                    if (kv.Length != 2) continue;
                    int v;
                    if (!int.TryParse(kv[1].Trim(), out v)) continue;
                    switch (kv[0].Trim())
                    {
                        case "size": s.SizeIndex = Math.Max(0, Math.Min(3, v)); break;
                        case "allkeys": s.AllKeys = v != 0; break;
                        case "hints": s.Hints = v != 0; break;
                        case "ripple": s.Ripple = v != 0; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new string[] {
                    "size=" + SizeIndex,
                    "allkeys=" + (AllKeys ? 1 : 0),
                    "hints=" + (Hints ? 1 : 0),
                    "ripple=" + (Ripple ? 1 : 0)
                });
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // クリックを透過する最前面のレイヤードウィンドウ（半透明ビットマップを表示）
    // ------------------------------------------------------------------
    class Layer : NativeWindow, IDisposable
    {
        IntPtr memDc = IntPtr.Zero, hBmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
        Size size;

        public Layer()
        {
            CreateParams cp = new CreateParams();
            cp.Style = Native.WS_POPUP;
            cp.ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                       | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            CreateHandle(cp);
        }

        public Size Size { get { return size; } }

        public void SetContent(Bitmap bmp, byte alpha)
        {
            FreeBitmap();
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            memDc = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            oldBmp = Native.SelectObject(memDc, hBmp);
            size = bmp.Size;
            SetAlpha(alpha);
        }

        public void SetAlpha(byte alpha)
        {
            if (memDc == IntPtr.Zero) return;
            Native.SIZE sz = new Native.SIZE();
            sz.CX = size.Width; sz.CY = size.Height;
            Native.POINT src = new Native.POINT();
            Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
            bf.BlendOp = 0;             // AC_SRC_OVER
            bf.SourceConstantAlpha = alpha;
            bf.AlphaFormat = 1;         // AC_SRC_ALPHA
            Native.UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, ref sz, memDc, ref src, 0, ref bf, Native.ULW_ALPHA);
        }

        public void ShowAt(int x, int y)
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public void MoveTo(int x, int y)
        {
            Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
        }

        public void Hide()
        {
            Native.ShowWindow(Handle, Native.SW_HIDE);
        }

        void FreeBitmap()
        {
            if (memDc != IntPtr.Zero)
            {
                Native.SelectObject(memDc, oldBmp);
                Native.DeleteObject(hBmp);
                Native.DeleteDC(memDc);
                memDc = IntPtr.Zero; hBmp = IntPtr.Zero; oldBmp = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            FreeBitmap();
            DestroyHandle();
        }
    }

    enum Kind { Left, Right, Middle, Scroll, Key, Info }
    enum Hold { None, Drag, Keys }

    // ------------------------------------------------------------------
    // 本体
    // ------------------------------------------------------------------
    class App : ApplicationContext
    {
        const int LingerMs = 1300;   // 吹き出しを表示し続ける時間
        const int FadeMs = 250;      // フェードアウト時間
        const int RippleMs = 450;    // 波紋のアニメーション時間
        const int DragThreshold = 6; // これ以上動いたらドラッグとみなす（ピクセル）

        static readonly float[] SizePx = { 16f, 22f, 30f, 42f };
        static readonly string[] SizeNames = { "小", "中", "大", "特大" };

        readonly Settings settings;
        readonly Layer bubble = new Layer();
        readonly Layer ripple = new Layer();
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly NotifyIcon tray = new NotifyIcon();
        ToolStripMenuItem miEnabled;

        // フックのデリゲートは GC に回収されないようフィールドで保持する
        readonly Native.HookProc mouseProc, keyProc;
        IntPtr mouseHook = IntPtr.Zero, keyHook = IntPtr.Zero;

        bool paused;
        Point cursor;

        // 吹き出しの状態
        bool bubbleVisible, bubbleFading;
        long bubbleExpire;
        Hold bubbleHold = Hold.None;
        string countLabel;   // 同じキーの連打を「×n」で数えるための直前ラベル
        int count;

        // 波紋の状態
        bool rippleActive, rippleDouble;
        long rippleStart;
        Kind rippleKind;
        float rippleScale = 1f;

        // マウスの状態
        int downButton;      // 押されているボタン（0:なし 1:左 2:右 3:中）
        Point downPt;
        bool dragging;
        int lastClickButton, clickCount;
        uint lastClickTime;
        Point lastClickPt;

        bool winAlone;       // Win キーが単独で押されているか

        static readonly Dictionary<string, string> HintTable = new Dictionary<string, string>
        {
            { "Ctrl + C", "コピー" }, { "Ctrl + X", "切り取り" }, { "Ctrl + V", "貼り付け" },
            { "Ctrl + Z", "元に戻す" }, { "Ctrl + Y", "やり直し" }, { "Ctrl + A", "すべて選択" },
            { "Ctrl + S", "上書き保存" }, { "Ctrl + F", "検索" }, { "Ctrl + H", "置換" },
            { "Ctrl + P", "印刷" }, { "Ctrl + N", "新規作成" }, { "Ctrl + O", "開く" },
            { "Ctrl + W", "閉じる" }, { "Ctrl + T", "新しいタブ" }, { "Ctrl + Tab", "タブ切り替え" },
            { "Ctrl + Shift + T", "閉じたタブを開く" }, { "Ctrl + Shift + Esc", "タスクマネージャー" },
            { "Ctrl + Shift + S", "名前を付けて保存" },
            { "Alt + Tab", "ウィンドウ切り替え" }, { "Alt + F4", "ウィンドウを閉じる" },
            { "Alt + PrintScreen", "ウィンドウを画像でコピー" }, { "PrintScreen", "画面を画像でコピー" },
            { "Win + D", "デスクトップを表示" }, { "Win + E", "エクスプローラー" }, { "Win + L", "ロック" },
            { "Win + R", "ファイル名を指定して実行" }, { "Win + I", "設定" }, { "Win + V", "クリップボード履歴" },
            { "Win + .", "絵文字" }, { "Win + Tab", "タスクビュー" }, { "Win + Shift + S", "画面の切り取り" },
            { "Win + ↑", "最大化" }, { "Win + ↓", "元に戻す／最小化" },
            { "Win + ←", "左半分に配置" }, { "Win + →", "右半分に配置" }
        };

        public App()
        {
            settings = Settings.Load();

            timer.Interval = 15;
            timer.Tick += OnTick;

            BuildTray();

            mouseProc = MouseProc;
            keyProc = KeyProc;
            IntPtr hMod = Native.GetModuleHandle(null);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, hMod, 0);
            keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, hMod, 0);
            if (mouseHook == IntPtr.Zero || keyHook == IntPtr.Zero)
            {
                MessageBox.Show("入力フックの登録に失敗しました。(エラー " + Marshal.GetLastWin32Error() + ")",
                    "CursorBubble", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Native.POINT p;
            if (Native.GetCursorPos(out p)) cursor = new Point(p.X, p.Y);
            ShowBubble("CursorBubble 起動（Ctrl + Alt + F9 で表示オン／オフ）", Kind.Info, Hold.None);
        }

        // ---------------- タスクトレイ ----------------

        void BuildTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            miEnabled = new ToolStripMenuItem("操作を表示する (Ctrl + Alt + F9)");
            miEnabled.Checked = true;
            miEnabled.Click += delegate { SetPaused(!paused); };
            menu.Items.Add(miEnabled);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miAll = new ToolStripMenuItem("文字キーも表示する（パスワード入力時は注意）");
            miAll.Checked = settings.AllKeys;
            miAll.Click += delegate { settings.AllKeys = !settings.AllKeys; miAll.Checked = settings.AllKeys; settings.Save(); };
            menu.Items.Add(miAll);

            ToolStripMenuItem miHints = new ToolStripMenuItem("ショートカットの説明を表示する");
            miHints.Checked = settings.Hints;
            miHints.Click += delegate { settings.Hints = !settings.Hints; miHints.Checked = settings.Hints; settings.Save(); };
            menu.Items.Add(miHints);

            ToolStripMenuItem miRipple = new ToolStripMenuItem("クリック位置に波紋を表示する");
            miRipple.Checked = settings.Ripple;
            miRipple.Click += delegate { settings.Ripple = !settings.Ripple; miRipple.Checked = settings.Ripple; settings.Save(); };
            menu.Items.Add(miRipple);

            ToolStripMenuItem miSize = new ToolStripMenuItem("表示の大きさ");
            ToolStripMenuItem[] sizeItems = new ToolStripMenuItem[SizeNames.Length];
            for (int i = 0; i < SizeNames.Length; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem(SizeNames[i]);
                item.Checked = settings.SizeIndex == i;
                item.Click += delegate
                {
                    settings.SizeIndex = index;
                    for (int j = 0; j < sizeItems.Length; j++) sizeItems[j].Checked = j == index;
                    settings.Save();
                    ShowBubble("表示の大きさ：" + SizeNames[index], Kind.Info, Hold.None);
                };
                sizeItems[i] = item;
                miSize.DropDownItems.Add(item);
            }
            menu.Items.Add(miSize);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem miExit = new ToolStripMenuItem("終了");
            miExit.Click += delegate { ExitThread(); };
            menu.Items.Add(miExit);

            tray.Icon = MakeIcon();
            tray.Text = "CursorBubble（操作の表示）";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
        }

        static Icon MakeIcon()
        {
            // exe に埋め込んだアイコンがあればそれを使う
            try
            {
                using (Icon exe = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    if (exe != null) return new Icon(exe, SystemInformation.SmallIconSize);
            }
            catch { }

            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (GraphicsPath path = RoundRect(new RectangleF(1, 1, 30, 30), 8))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(37, 99, 235)))
                        g.FillPath(b, path);
                    PointF[] arrow = {
                        new PointF(9, 6), new PointF(9, 25), new PointF(14, 20),
                        new PointF(18, 27), new PointF(21, 25), new PointF(17, 19), new PointF(24, 19)
                    };
                    g.FillPolygon(Brushes.White, arrow);
                }
                IntPtr h = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }

        void SetPaused(bool value)
        {
            paused = value;
            miEnabled.Checked = !paused;
            dragging = false;
            downButton = 0;
            ShowBubble(paused ? "操作の表示：オフ" : "操作の表示：オン", Kind.Info, Hold.None);
        }

        protected override void ExitThreadCore()
        {
            if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            if (keyHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; }
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            bubble.Dispose();
            ripple.Dispose();
            base.ExitThreadCore();
        }

        // ---------------- マウス ----------------

        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    Native.MSLLHOOKSTRUCT info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    OnMouse(wParam.ToInt32(), info);
                }
                catch { }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        void OnMouse(int msg, Native.MSLLHOOKSTRUCT info)
        {
            cursor = new Point(info.pt.X, info.pt.Y);

            if (msg == Native.WM_MOUSEMOVE)
            {
                if (!paused && downButton != 0 && !dragging &&
                    (Math.Abs(cursor.X - downPt.X) > DragThreshold || Math.Abs(cursor.Y - downPt.Y) > DragThreshold))
                {
                    dragging = true;
                    ShowBubble(ModsNow() + (downButton == 2 ? "右ドラッグ" : downButton == 3 ? "ホイールドラッグ" : "ドラッグ"),
                        ButtonKind(downButton), Hold.Drag);
                }
                else if (bubbleVisible)
                {
                    PlaceBubble(false);
                }
                return;
            }

            if (paused) return;

            switch (msg)
            {
                case Native.WM_LBUTTONDOWN: OnButtonDown(1, info); break;
                case Native.WM_RBUTTONDOWN: OnButtonDown(2, info); break;
                case Native.WM_MBUTTONDOWN: OnButtonDown(3, info); break;
                case Native.WM_LBUTTONUP: OnButtonUp(1); break;
                case Native.WM_RBUTTONUP: OnButtonUp(2); break;
                case Native.WM_MBUTTONUP: OnButtonUp(3); break;

                case Native.WM_XBUTTONDOWN:
                    winAlone = false;
                    ShowBubble((info.mouseData >> 16) == 1 ? "戻るボタン" : "進むボタン", Kind.Middle, Hold.None);
                    StartRipple(Kind.Middle, false);
                    break;

                case Native.WM_MOUSEWHEEL:
                    {
                        int delta = (short)(ushort)(info.mouseData >> 16);
                        string mods = ModsNow();
                        string text = mods + "スクロール " + (delta > 0 ? "↑" : "↓");
                        if (settings.Hints && mods == "Ctrl + ") text += delta > 0 ? "（拡大）" : "（縮小）";
                        ShowBubble(text, Kind.Scroll, Hold.None);
                    }
                    break;

                case Native.WM_MOUSEHWHEEL:
                    {
                        int delta = (short)(ushort)(info.mouseData >> 16);
                        ShowBubble(ModsNow() + "スクロール " + (delta > 0 ? "→" : "←"), Kind.Scroll, Hold.None);
                    }
                    break;
            }
        }

        void OnButtonDown(int button, Native.MSLLHOOKSTRUCT info)
        {
            winAlone = false;

            // ダブルクリック判定（同じボタン・短時間・ほぼ同じ位置）
            int tol = Math.Max(4, Native.GetSystemMetrics(Native.SM_CXDOUBLECLK));
            bool again = button == lastClickButton
                && unchecked(info.time - lastClickTime) <= Native.GetDoubleClickTime()
                && Math.Abs(cursor.X - lastClickPt.X) <= tol && Math.Abs(cursor.Y - lastClickPt.Y) <= tol;
            clickCount = again ? clickCount + 1 : 1;
            lastClickButton = button;
            lastClickTime = info.time;
            lastClickPt = cursor;

            downButton = button;
            downPt = cursor;
            dragging = false;

            string name;
            if (button == 3)
                name = "ホイールクリック";
            else
            {
                string prefix = button == 2 ? "右" : (clickCount == 1 ? "左" : "");
                name = prefix + (clickCount == 1 ? "クリック" : clickCount == 2 ? "ダブルクリック" : "トリプルクリック");
            }
            ShowBubble(ModsNow() + name, ButtonKind(button), Hold.None);
            StartRipple(ButtonKind(button), clickCount >= 2);
        }

        void OnButtonUp(int button)
        {
            if (button != downButton) return;
            downButton = 0;
            if (dragging)
            {
                dragging = false;
                ShowBubble("ドロップ", ButtonKind(button), Hold.None);
            }
        }

        static Kind ButtonKind(int button)
        {
            return button == 2 ? Kind.Right : button == 3 ? Kind.Middle : Kind.Left;
        }

        // ---------------- キーボード ----------------

        IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    Native.KBDLLHOOKSTRUCT info = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    int msg = wParam.ToInt32();
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN) OnKeyDown((int)info.vkCode);
                    else if (msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP) OnKeyUp((int)info.vkCode);
                }
                catch { }
            }
            return Native.CallNextHookEx(keyHook, nCode, wParam, lParam);
        }

        static bool IsCtrl(int vk) { return vk == 0x11 || vk == 0xA2 || vk == 0xA3; }
        static bool IsAlt(int vk) { return vk == 0x12 || vk == 0xA4 || vk == 0xA5; }
        static bool IsShift(int vk) { return vk == 0x10 || vk == 0xA0 || vk == 0xA1; }
        static bool IsWin(int vk) { return vk == 0x5B || vk == 0x5C; }

        static bool WinDown() { return Native.IsDown(0x5B) || Native.IsDown(0x5C); }
        static bool CmdModDown() { return Native.IsDown(0x11) || Native.IsDown(0x12) || WinDown(); }

        static string Mods(bool ctrl, bool alt, bool shift, bool win)
        {
            string s = "";
            if (win) s += "Win + ";
            if (ctrl) s += "Ctrl + ";
            if (alt) s += "Alt + ";
            if (shift) s += "Shift + ";
            return s;
        }

        static string ModsNow()
        {
            return Mods(Native.IsDown(0x11), Native.IsDown(0x12), Native.IsDown(0x10), WinDown());
        }

        void OnKeyDown(int vk)
        {
            if (vk == 0xE7) return; // VK_PACKET（ソフトウェアが送る文字入力）

            // フック内では、いま処理中のキーはまだ GetAsyncKeyState に反映されていない。
            // そのため「すでに押されている」ならキーリピートと判断できる。
            bool repeat = Native.IsDown(vk);
            bool ctrl = Native.IsDown(0x11) || IsCtrl(vk);
            bool alt = Native.IsDown(0x12) || IsAlt(vk);
            bool shift = Native.IsDown(0x10) || IsShift(vk);
            bool win = WinDown() || IsWin(vk);

            // 表示オン／オフの切り替え（Ctrl + Alt + F9）
            if (vk == 0x78 && ctrl && alt && !shift && !win)
            {
                if (!repeat) SetPaused(!paused);
                return;
            }
            if (paused) return;

            bool cmd = ctrl || alt || win;

            if (IsCtrl(vk) || IsAlt(vk) || IsShift(vk) || IsWin(vk))
            {
                if (repeat) return;
                winAlone = IsWin(vk) && !ctrl && !alt && !shift;
                if (cmd || settings.AllKeys)
                {
                    string chain = Mods(ctrl, alt, shift, win);
                    ShowBubble(chain.Substring(0, chain.Length - 3), Kind.Key, cmd ? Hold.Keys : Hold.None);
                }
                return;
            }

            winAlone = false;
            bool special;
            string name = KeyName(vk, out special);
            if (name == null) return;
            // 文字キー単独の入力は、設定で有効にしたときだけ表示する（パスワード等の保護）
            if (!cmd && !special && !settings.AllKeys) return;

            string label = Mods(ctrl, alt, shift, win) + name;
            string text = label;
            string hint;
            if (settings.Hints && HintTable.TryGetValue(label, out hint)) text += "（" + hint + "）";

            // 同じキーを続けて押したときは「×n」を付ける
            int n = (bubbleVisible && !bubbleFading && countLabel == label) ? count + 1 : 1;
            if (n > 1) text += " ×" + n;
            ShowBubble(text, Kind.Key, cmd ? Hold.Keys : Hold.None);
            countLabel = label;
            count = n;
        }

        void OnKeyUp(int vk)
        {
            if (IsWin(vk) && winAlone && !paused)
            {
                winAlone = false;
                ShowBubble(settings.Hints ? "Win（スタートメニュー）" : "Win", Kind.Key, Hold.None);
            }
        }

        static string KeyName(int vk, out bool special)
        {
            special = true;
            switch (vk)
            {
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x13: return "Pause";
                case 0x14: return "CapsLock";
                case 0x1B: return "Esc";
                case 0x20: return "Space";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "←";
                case 0x26: return "↑";
                case 0x27: return "→";
                case 0x28: return "↓";
                case 0x2C: return "PrintScreen";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x5D: return "メニューキー";
                case 0x90: return "NumLock";
                case 0x91: return "ScrollLock";
                case 0x19: case 0xF3: case 0xF4: return "半角/全角";
                case 0x1C: return "変換";
                case 0x1D: return "無変換";
                case 0x15: case 0xF2: return "カタカナ/ひらがな";
                case 0xF0: return "英数";
                case 0xAD: return "ミュート";
                case 0xAE: return "音量 −";
                case 0xAF: return "音量 ＋";
            }
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);

            special = false;
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "テンキー " + (vk - 0x60);
            switch (vk)
            {
                case 0x6A: return "テンキー *";
                case 0x6B: return "テンキー +";
                case 0x6D: return "テンキー -";
                case 0x6E: return "テンキー .";
                case 0x6F: return "テンキー /";
            }
            // 記号キーなどは、キーボード配列に応じた（Shift なしの）文字を名前にする
            uint ch = Native.MapVirtualKey((uint)vk, 2) & 0x7FFF;
            if (ch > 0x20) return char.ToUpperInvariant((char)ch).ToString();
            return null;
        }

        // ---------------- 吹き出し ----------------

        static Color KindColor(Kind kind)
        {
            switch (kind)
            {
                case Kind.Left: return Color.FromArgb(37, 99, 235);     // 青
                case Kind.Right: return Color.FromArgb(220, 80, 20);    // オレンジ
                case Kind.Middle: return Color.FromArgb(22, 140, 90);   // 緑
                case Kind.Scroll: return Color.FromArgb(124, 58, 237);  // 紫
                case Kind.Key: return Color.FromArgb(38, 42, 50);       // 濃いグレー
                default: return Color.FromArgb(90, 96, 105);
            }
        }

        // カーソルがあるモニターの表示倍率（100% = 1.0）
        float DpiScale()
        {
            try
            {
                Native.POINT p = new Native.POINT();
                p.X = cursor.X; p.Y = cursor.Y;
                uint dx, dy;
                IntPtr mon = Native.MonitorFromPoint(p, 2);
                if (Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch { }
            return 1f;
        }

        void ShowBubble(string text, Kind kind, Hold hold)
        {
            float scale = DpiScale();
            using (Bitmap bmp = RenderBubble(text, kind, SizePx[settings.SizeIndex] * scale))
                bubble.SetContent(bmp, 255);
            bubbleVisible = true;
            bubbleFading = false;
            bubbleHold = hold;
            bubbleExpire = clock.ElapsedMilliseconds + LingerMs;
            countLabel = null;
            PlaceBubble(true);
            timer.Start();
        }

        void PlaceBubble(bool show)
        {
            float px = SizePx[settings.SizeIndex] * DpiScale();
            int ox = (int)(px * 0.9f) + 6, oy = (int)(px * 1.0f) + 8;
            Size sz = bubble.Size;
            Rectangle area = Screen.FromPoint(cursor).Bounds;

            int x = cursor.X + ox, y = cursor.Y + oy;
            if (x + sz.Width > area.Right) x = cursor.X - ox - sz.Width;   // 右端では左側に出す
            if (y + sz.Height > area.Bottom) y = cursor.Y - oy - sz.Height; // 下端では上側に出す
            if (x < area.Left) x = area.Left;
            if (y < area.Top) y = area.Top;

            if (show) bubble.ShowAt(x, y); else bubble.MoveTo(x, y);
        }

        static Bitmap RenderBubble(string text, Kind kind, float px)
        {
            using (Font font = new Font("Yu Gothic UI", px, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                fmt.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;

                SizeF ts;
                using (Bitmap tmp = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(tmp))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    ts = g.MeasureString(text, font, 10000, fmt);
                }

                float border = Math.Max(2f, px * 0.09f);
                float padX = px * 0.6f, padY = px * 0.28f;
                int w = (int)Math.Ceiling(ts.Width + padX * 2 + border * 2) + 2;
                int h = (int)Math.Ceiling(ts.Height + padY * 2 + border * 2) + 2;

                Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    g.Clear(Color.Transparent);

                    RectangleF rect = new RectangleF(border / 2 + 1, border / 2 + 1, w - border - 2, h - border - 2);
                    using (GraphicsPath path = RoundRect(rect, Math.Min(rect.Height / 2, px * 0.55f)))
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(242, KindColor(kind))))
                    using (Pen pen = new Pen(Color.FromArgb(235, 255, 255, 255), border))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(pen, path); // 白い縁取りで、どんな背景の上でも見えるようにする
                    }
                    g.DrawString(text, font, Brushes.White, (w - ts.Width) / 2f, (h - ts.Height) / 2f, fmt);
                }
                return bmp;
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------------- 波紋 ----------------

        void StartRipple(Kind kind, bool isDouble)
        {
            if (!settings.Ripple) return;
            rippleKind = kind;
            rippleDouble = isDouble;
            rippleScale = SizePx[settings.SizeIndex] * DpiScale() / 22f;
            rippleStart = clock.ElapsedMilliseconds;
            rippleActive = true;
            DrawRipple(0f);
            Size sz = ripple.Size;
            ripple.ShowAt(cursor.X - sz.Width / 2, cursor.Y - sz.Height / 2);
            timer.Start();
        }

        void DrawRipple(float t)
        {
            float maxR = 34f * rippleScale;
            float stroke = Math.Max(3f, 5f * rippleScale);
            int s = (int)Math.Ceiling((maxR + stroke) * 2) + 4;
            float c = s / 2f;
            float ease = 1f - (1f - t) * (1f - t);
            float r = maxR * (0.3f + 0.7f * ease);
            Color col = KindColor(rippleKind);

            using (Bitmap bmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(60, col)))
                        g.FillEllipse(fill, c - r, c - r, r * 2, r * 2);
                    using (Pen white = new Pen(Color.FromArgb(220, 255, 255, 255), stroke + 2f))
                    using (Pen pen = new Pen(col, stroke))
                    {
                        g.DrawEllipse(white, c - r, c - r, r * 2, r * 2);
                        g.DrawEllipse(pen, c - r, c - r, r * 2, r * 2);
                        if (rippleDouble) // ダブルクリックは二重の輪
                        {
                            float r2 = r * 0.55f;
                            g.DrawEllipse(white, c - r2, c - r2, r2 * 2, r2 * 2);
                            g.DrawEllipse(pen, c - r2, c - r2, r2 * 2, r2 * 2);
                        }
                    }
                }
                ripple.SetContent(bmp, (byte)(255 * (1f - t * t)));
            }
        }

        // ---------------- アニメーション ----------------

        void OnTick(object sender, EventArgs e)
        {
            try
            {
                long now = clock.ElapsedMilliseconds;
                bool active = false;

                if (rippleActive)
                {
                    float t = (now - rippleStart) / (float)RippleMs;
                    if (t >= 1f) { ripple.Hide(); rippleActive = false; }
                    else { DrawRipple(t); active = true; }
                }

                if (bubbleVisible)
                {
                    // ドラッグ中、または Ctrl / Alt / Win を押している間は表示を続ける
                    bool held = (bubbleHold == Hold.Drag && dragging) || (bubbleHold == Hold.Keys && CmdModDown());
                    if (held && bubbleExpire < now + 400) bubbleExpire = now + 400;

                    if (now <= bubbleExpire)
                    {
                        active = true;
                    }
                    else
                    {
                        float a = 1f - (now - bubbleExpire) / (float)FadeMs;
                        if (a <= 0f)
                        {
                            bubble.Hide();
                            bubbleVisible = false;
                            countLabel = null;
                        }
                        else
                        {
                            bubbleFading = true;
                            bubble.SetAlpha((byte)(255 * a));
                            active = true;
                        }
                    }
                }

                if (!active) timer.Stop();
            }
            catch { }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            // 高 DPI・マルチモニターでも座標がずれないよう、モニターごとの DPI に対応させる
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch { try { Native.SetProcessDPIAware(); } catch { } }

            bool created;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "CursorBubble_SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("CursorBubble はすでに起動しています。\nタスクトレイ（画面右下）のアイコンを確認してください。",
                        "CursorBubble", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.Run(new App());
                GC.KeepAlive(mutex);
            }
        }
    }
}
