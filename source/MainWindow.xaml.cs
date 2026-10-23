using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Forms = System.Windows.Forms;

namespace SideDock;
public partial class MainWindow : Window
{
    readonly Preferences prefs;
    readonly Dictionary<string, WebView2> views = new();
    readonly Dictionary<string, Task> initializing = new();
    readonly DispatcherTimer watcher = new() { Interval = TimeSpan.FromMilliseconds(60) };
    readonly bool smoke;
    Forms.NotifyIcon? tray;
    IntPtr handle;
    string? active;
    bool quitting, modal, transitioning, edgeArmed = true;
    DateTime outsideSince = DateTime.UtcNow, edgeSince = DateTime.MaxValue, shownAt;
    CoreWebView2Environment? environment;
    Forms.Screen? monitor;
    readonly EdgeGesture edgeGesture = new();
    readonly DispatcherTimer sessionTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    HistoryStore history = null!;

    public MainWindow(bool smoke)
    {
        this.smoke = smoke;
        if (smoke) Store.Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "smoke-profile"));
        prefs = Store.Load();
        history = new HistoryStore();
        InitializeComponent();
        Width = prefs.Width;
        PinButton.Content = prefs.Pinned ? "●" : "○";
        SourceInitialized += (_, _) => {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle).AddHook(WndProc);
            Native.RegisterMouse(handle);
            if (!Native.RegisterHotKey(handle, 1, 0x4003, 0x20)) Status.Text = "快捷键被占用，请使用屏幕边缘或托盘唤出";
        };
        Loaded += async (_, _) => {
            await Dispatcher.Yield(DispatcherPriority.Background);
            Native.GetCursorPos(out var point);
            monitor = Forms.Screen.FromPoint(new(point.X, point.Y));
            Place(); shownAt = DateTime.UtcNow;
            RenderRail();
            if (!smoke) SetupTray();
            watcher.Tick += Watch; if (!smoke) watcher.Start();
            if (Environment.GetCommandLineArgs().Contains("--startup-check")) {
                Hide(); Opacity = 1;
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-result.json"), System.Text.Json.JsonSerializer.Serialize(new { hidden = !IsVisible, browserViews = views.Count, command = StartupService.Command("C:\\Program Files\\SideDock\\SideDock.exe") }));
                quitting = true; Close(); System.Windows.Application.Current.Shutdown(); return;
            }
            if (smoke) { await SmokeTest(); return; }
            if (Environment.GetCommandLineArgs().Contains("--startup")) { Hide(); Opacity = 1; edgeGesture.Reset(); }
            else await ActivatePad(prefs.Pads.FirstOrDefault(x => x.Id == prefs.ActiveId) ?? prefs.Pads.FirstOrDefault());
            sessionTimer.Tick += async (_, _) => await SaveActiveSession(); sessionTimer.Start();
        };
        Closing += (_, e) => { if (!quitting) { e.Cancel = true; _ = Conceal(); } };
        Closed += (_, _) => { watcher.Stop(); sessionTimer.Stop(); Native.UnregisterHotKey(handle, 1); tray?.Dispose(); foreach (var v in AllViews()) v.Dispose(); };
        PreviewKeyDown += Keys;
    }

    void SetupTray()
    {
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "SideDock · Ctrl+Alt+Space", Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(() => _ = Reveal(true)); };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("展开 / 收起", null, (_, _) => Dispatcher.Invoke(Toggle));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(() => { _ = Reveal(true); Settings_Click(this, new()); }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 SideDock", null, (_, _) => Dispatcher.Invoke(Quit));
        tray.ContextMenuStrip = menu;
    }
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x0312 && w.ToInt32() == 1) { Toggle(); handled = true; }
        if (msg == 0x007E) Dispatcher.BeginInvoke(() => { monitor = null; if (IsVisible) Place(); });
        if (msg == 0x00FF && IsVisible && !transitioning && !modal && Native.MouseMotion(l, out int dx, out _)) {
            Native.GetCursorPos(out var p);
            var screen = Forms.Screen.FromPoint(new(p.X, p.Y));
            bool atEdge = screen.DeviceName == monitor?.DeviceName && Native.AtEdge(p, screen.Bounds, prefs.Edge);
            if (edgeGesture.Push(dx, prefs.Edge == "right", atEdge, prefs.Pinned, (DateTime.UtcNow - shownAt).TotalMilliseconds > 600, DateTime.UtcNow)) Dispatcher.BeginInvoke(() => _ = Conceal());
        }
        return IntPtr.Zero;
    }
    void Toggle() { if (IsVisible) _ = Conceal(); else _ = Reveal(true); }
    async void Quit() { await SaveActiveSession(); Store.Save(prefs); quitting = true; Close(); System.Windows.Application.Current.Shutdown(); }

    (int x, int y, int w, int h) Bounds()
    {
        Native.GetCursorPos(out var p);
        monitor ??= Forms.Screen.FromPoint(new(p.X, p.Y));
        var area = monitor.WorkingArea;
        var scale = Native.Scale(new Native.Point { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 });
        int gap = (int)(8 * scale), width = Math.Min((int)(prefs.Width * scale), area.Width - gap * 2);
        return (prefs.Edge == "left" ? area.Left + gap : area.Right - width - gap, area.Top + gap, width, area.Height - gap * 2);
    }
    void Place() { var b = Bounds(); Native.SetWindowPos(handle, new IntPtr(-1), b.x, b.y, b.w, b.h, 0x0010); }
    async Task Reveal(bool focus)
    {
        if (transitioning || IsVisible) { if (focus) Activate(); return; }
        Native.GetCursorPos(out var p); monitor = Forms.Screen.FromPoint(new(p.X, p.Y));
        transitioning = true;
        var b = Bounds(); int offset = prefs.Edge == "left" ? -b.w : b.w;
        Show();
        for (int i = 0; i <= 10; i++) {
            double t = 1 - Math.Pow(1 - i / 10d, 3);
            Native.SetWindowPos(handle, new IntPtr(-1), b.x + (int)(offset * (1 - t)), b.y, b.w, b.h, 0x0010);
            await Task.Delay(12);
        }
        shownAt = DateTime.UtcNow; outsideSince = shownAt; edgeGesture.Reset();
        transitioning = false;
        if (active == null) await ActivatePad(prefs.Pads.FirstOrDefault(x => x.Id == prefs.ActiveId) ?? prefs.Pads.FirstOrDefault());
        if (focus) { Activate(); CurrentView()?.Focus(); }
    }
    async Task Conceal()
    {
        if (transitioning || !IsVisible || modal) return;
        transitioning = true;
        await SaveActiveSession();
        prefs.Width = Math.Clamp(ActualWidth, 380, 1000); Store.Save(prefs);
        Native.GetWindowRect(handle, out var r); int width = r.Right - r.Left, height = r.Bottom - r.Top;
        for (int i = 0; i <= 8; i++) {
            double t = Math.Pow(i / 8d, 2);
            Native.SetWindowPos(handle, IntPtr.Zero, r.Left + (int)(width * t * (prefs.Edge == "left" ? -1 : 1)), r.Top, width, height, 0x0014);
            await Task.Delay(12);
        }
        Hide(); edgeArmed = false; edgeSince = DateTime.MaxValue; transitioning = false;
    }
    void Watch(object? sender, EventArgs e)
    {
        if (transitioning || modal) return;
        Native.GetCursorPos(out var p);
        var screen = Forms.Screen.FromPoint(new(p.X, p.Y));
        bool atEdge = Native.AtEdge(p, screen.Bounds, prefs.Edge);
        if (!atEdge) { edgeArmed = true; edgeSince = DateTime.MaxValue; }
        if (!IsVisible) {
            if (!prefs.EdgeEnabled || !edgeArmed || !atEdge) return;
            if (edgeSince == DateTime.MaxValue) edgeSince = DateTime.UtcNow;
            if ((DateTime.UtcNow - edgeSince).TotalMilliseconds >= 240) _ = Reveal(false);
            return;
        }
        Native.GetWindowRect(handle, out var r);
        if (edgeGesture.ShouldHide(r.Contains(p), atEdge && screen.DeviceName == monitor?.DeviceName, prefs.Pinned, DateTime.UtcNow)) { _ = Conceal(); return; }
        if (r.Contains(p) || System.Windows.Input.Keyboard.IsKeyDown(Key.LeftCtrl) || System.Windows.Input.Keyboard.IsKeyDown(Key.RightCtrl)) outsideSince = DateTime.UtcNow;
        else if (!prefs.Pinned && (DateTime.UtcNow - shownAt).TotalMilliseconds > 1500 && (DateTime.UtcNow - outsideSince).TotalMilliseconds > 850) _ = Conceal();
    }

    void RenderRail()
    {
        PadRail.Children.Clear();
        foreach (var pad in prefs.Pads) {
            var button = new System.Windows.Controls.Button {
                Content = pad.Name.Length > 0 ? System.Globalization.StringInfo.GetNextTextElement(pad.Name) : "·",
                FontSize = 19, Height = 44, Margin = new Thickness(0, 3, 0, 3),
                Background = pad.Id == active ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(65, 78, 116)) : System.Windows.Media.Brushes.Transparent,
                ToolTip = pad.Name + "\n" + pad.Url + "\n右键编辑、排序或移除"
            };
            if (pad.IconFile != null) { try { var image = new System.Windows.Media.Imaging.BitmapImage(); image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.UriSource = new Uri(Path.Combine(Store.Root, "Icons", Path.GetFileName(pad.IconFile))); image.EndInit(); button.Content = new System.Windows.Controls.Image { Source = image, Width = 23, Height = 23 }; } catch { } }
            button.Click += async (_, _) => await ActivatePad(pad);
            var menu = new ContextMenu();
            var edit = new MenuItem { Header = "编辑名称与网址" }; edit.Click += (_, _) => EditPad(pad);
            var up = new MenuItem { Header = "向上移动" }; up.Click += (_, _) => { int n = prefs.Pads.IndexOf(pad); if (n > 0) { prefs.Pads.RemoveAt(n); prefs.Pads.Insert(n - 1, pad); Store.Save(prefs); RenderRail(); } };
            var remove = new MenuItem { Header = "移除" }; remove.Click += async (_, _) => {
                prefs.Pads.Remove(pad); DisposePad(pad);
                if (active == pad.Id) await ActivatePad(prefs.Pads.FirstOrDefault());
                Store.Save(prefs); RenderRail();
            };
            menu.Items.Add(edit); menu.Items.Add(up); menu.Items.Add(remove);
            menu.Opened += (_, _) => modal = true; menu.Closed += (_, _) => { modal = false; outsideSince = DateTime.UtcNow; };
            button.ContextMenu = menu; PadRail.Children.Add(button);
        }
    }
    void Address_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) { Navigate(); e.Handled = true; } }
    void Back_Click(object sender, RoutedEventArgs e) => GoBack();
    void Reload_Click(object sender, RoutedEventArgs e) => CurrentView()?.Reload();
    void External_Click(object sender, RoutedEventArgs e) { var url = CurrentView()?.CoreWebView2?.Source; if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" && u.Host != "sidedock.internal") Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
    void Pin_Click(object sender, RoutedEventArgs e) { prefs.Pinned = !prefs.Pinned; PinButton.Content = prefs.Pinned ? "●" : "○"; Store.Save(prefs); }
    void Hide_Click(object sender, RoutedEventArgs e) => _ = Conceal();
    void Title_Down(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left && e.OriginalSource is TextBlock) { DragMove(); } }
    void Keys(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _ = Conceal(); e.Handled = true; }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.L) { Address.Focus(); Address.SelectAll(); e.Handled = true; }
        if (e.Key == Key.T) { EditPad(null); e.Handled = true; }
        if (e.Key == Key.R) { Reload_Click(this, new()); e.Handled = true; }
        if (e.Key == Key.H) { History_Click(this, new()); e.Handled = true; }
    }
    void Add_Click(object sender, RoutedEventArgs e) => EditPad(null);
    void EditPad(Pad? pad)
    {

        var dialog = new EditorWindow(pad, prefs.SearchEngine) { Owner = this };
        modal = true;
        try {
            if (dialog.ShowDialog() == true) {
                if (pad == null) { pad = new Pad { Name = dialog.PadName, Url = dialog.PadUrl }; prefs.Pads.Add(pad); }
                else { DisposePad(pad); pad.Name = dialog.PadName; pad.Url = dialog.PadUrl; pad.LastUrl = null; pad.BackStack.Clear(); }
                Store.Save(prefs); _ = ActivatePad(pad);
            }
        } finally { modal = false; outsideSince = DateTime.UtcNow; }
    }
    void Settings_Click(object sender, RoutedEventArgs e)
    {
        modal = true;
        try {
            var dialog = new SettingsWindow(prefs) { Owner = this };
            if (dialog.ShowDialog() == true) { Store.Save(prefs); Place(); }
        } finally { modal = false; outsideSince = DateTime.UtcNow; }
    }
    const string NotesHtml = """
    <!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>随手记</title>
    <style>*{box-sizing:border-box}body{margin:0;padding:32px 26px;background:#f9f8f4;color:#283141;font:15px 'Segoe UI',sans-serif}small{color:#8490a2;letter-spacing:2px}h1{font-size:30px;font-weight:500;margin:18px 0 6px}p{color:#8a91a0;font-size:12px}textarea{resize:none;width:100%;height:calc(100vh - 210px);border:0;outline:none;background:transparent;color:#364151;font:15px/1.9 'Segoe UI',sans-serif}footer{font-size:11px;color:#98a0ac}</style>
    <small>YOUR QUIET SPACE</small><h1>随手记。</h1><p>暂存想法、链接，或今天的一件小事。</p><textarea id="note" placeholder="从这里开始…"></textarea><footer id="state">仅保存在这台电脑 · 不上传云端</footer>
    <script>const n=document.querySelector('#note');try{n.value=localStorage.getItem('notes')||''}catch{}n.addEventListener('input',()=>{try{localStorage.setItem('notes',n.value);document.querySelector('#state').textContent='已保存到本机 · '+new Date().toLocaleTimeString()}catch{document.querySelector('#state').textContent='保存失败，请先复制备份'}})</script></html>
    """;
}
