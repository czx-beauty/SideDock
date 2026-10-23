using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SideDock;
public class EditorWindow : Window
{
    readonly TextBox name = new(), url = new();
    readonly TextBlock error = new() { Foreground = System.Windows.Media.Brushes.Salmon, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    public string PadName => name.Text.Trim();
    public string PadUrl { get; private set; } = "";
    public EditorWindow(Pad? pad, string engine = "https://www.google.com")
    {
        Title = pad == null ? "添加网站" : "编辑网站";
        Width = 380; Height = 340; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 28, 41));
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = Title, FontSize = 23, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(new TextBlock { Text = "名称", Margin = new Thickness(0, 0, 0, 5) }); name.Text = pad?.Name ?? ""; panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = "网址（便笺使用 sidedock:notes）", Margin = new Thickness(0, 14, 0, 5) }); url.Text = pad?.Url ?? ""; panel.Children.Add(url);
        panel.Children.Add(error);
        var save = new System.Windows.Controls.Button { Content = "保存", Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 89, 151)), Margin = new Thickness(0, 12, 0, 0), IsDefault = true };
        save.Click += (_, _) => {
            try { if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("请填写名称"); PadUrl = url.Text.Trim() == "sidedock:notes" ? "sidedock:notes" : Store.NormalizeUrl(url.Text, engine); DialogResult = true; }
            catch (ArgumentException ex) { error.Text = ex.Message; }
        }; panel.Children.Add(save);
        Loaded += (_, _) => name.Focus();
    }
}
public class SettingsWindow : Window
{
    public SettingsWindow(Preferences prefs)
    {
        NameScope.SetNameScope(this, new NameScope());
        Title = "SideDock 设置 · 0.3"; Width = 470; Height = Math.Min(680, SystemParameters.WorkArea.Height); MinWidth = 400; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 28, 41));
        var layout = new DockPanel { Margin = new Thickness(22) }; Content = layout;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = "保持随手可及。", FontSize = 25, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock { Text = "唤出位置", Margin = new Thickness(0, 0, 0, 6) });
        var edge = new ComboBox { ItemsSource = new[] { "屏幕右侧", "屏幕左侧" }, SelectedIndex = prefs.Edge == "left" ? 1 : 0, Padding = new Thickness(8) }; panel.Children.Add(edge);
        var trigger = new System.Windows.Controls.CheckBox { Content = "鼠标停留屏幕边缘时展开", IsChecked = prefs.EdgeEnabled }; panel.Children.Add(trigger);
        var autoPin = new System.Windows.Controls.CheckBox { Content = "打开新网站时自动固定到侧边栏", IsChecked = prefs.AutoPinSites }; panel.Children.Add(autoPin);
        var startup = new System.Windows.Controls.CheckBox { Content = "登录 Windows 时自动启动（后台待命）", IsChecked = StartupService.Enabled }; panel.Children.Add(startup);
        panel.Children.Add(new TextBlock { Text = "搜索引擎网址", Margin = new Thickness(0, 20, 0, 6) });
        var engine = new TextBox { Text = prefs.SearchEngine, Name = "SearchEngineInput" }; RegisterName(engine.Name, engine); panel.Children.Add(engine);
        panel.Children.Add(new TextBlock { Text = "输入 google.com、bing.com、duckduckgo.com 即可。\n自定义参数可填 https://你的站点/search?q={query}", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "触边唤出 → 移进面板 → 再移回边缘收起。\n鼠标离开面板也会自动收起，点击 ○ 可固定展开。\n\nCtrl+Alt+Space  展开 / 收起\nCtrl+L  地址    Ctrl+T  添加    Ctrl+H  历史\nEsc  收起    右键网站图标  编辑 / 排序", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(155, 168, 194)), FontSize = 12, LineHeight = 22, Margin = new Thickness(0, 16, 0, 0) });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Salmon, Margin = new Thickness(0, 8, 0, 0) }; footer.Children.Add(error);
        var save = new System.Windows.Controls.Button { Name = "SaveSettingsButton", Content = "保存设置", Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 89, 151)), Margin = new Thickness(0, 12, 0, 0), IsDefault = true };
        RegisterName(save.Name, save);
        save.Click += (_, _) => {
            try {
                string value = engine.Text.Trim(); if (!value.Contains("://")) value = "https://" + value;
                Store.SearchUrl(value, "test");
                if ((startup.IsChecked == true) != StartupService.Enabled) StartupService.Set(startup.IsChecked == true);
                Store.UpdateSettings(prefs, value, edge.SelectedIndex == 1 ? "left" : "right", trigger.IsChecked == true, autoPin.IsChecked == true);
                DialogResult = true;
            } catch (Exception ex) { error.Text = ex.Message; }
        }; footer.Children.Add(save);
    }
}
public class HistoryWindow : Window
{
    public string? SelectedUrl { get; private set; }
    public HistoryWindow(List<Visit> visits)
    {
        Title = "浏览历史 · 本机保存"; Width = 680; Height = 580; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 28, 41));
        var panel = new DockPanel { Margin = new Thickness(20) }; Content = panel;
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 12), ToolTip = "搜索标题或网址" }; DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        var hint = new TextBlock { Text = "双击打开 · 最近 10,000 条 · 数据仅保存在本机", Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(hint, Dock.Bottom); panel.Children.Add(hint);
        var list = new ListBox { Background = Background, Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) }; panel.Children.Add(list);
        void Refresh() {
            list.Items.Clear(); foreach (var v in visits.AsEnumerable().Reverse().Where(v => (v.Title + v.Url).Contains(search.Text, StringComparison.OrdinalIgnoreCase)).Take(500))
                list.Items.Add(new ListBoxItem { Content = $"{v.At.ToLocalTime():MM-dd HH:mm}   {v.Title}\n{v.Url}", Tag = v.Url, Padding = new Thickness(6, 9, 6, 9) });
        }
        search.TextChanged += (_, _) => Refresh();
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: string url }) { SelectedUrl = url; DialogResult = true; } }; Refresh();
    }
}
