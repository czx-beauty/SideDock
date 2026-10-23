using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SideDock;
public partial class MainWindow
{
    Task<CoreWebView2Environment>? environmentTask;
    readonly Dictionary<string, List<WebView2>> inlinePages = new();
    readonly HashSet<WebView2> ready = new();
    readonly HashSet<string> goingBack = new();
    bool savingSession;
    int activation;
    WebView2? CurrentView() => active == null ? null : inlinePages.TryGetValue(active, out var pages) && pages.Count > 0 ? pages[^1] : views.GetValueOrDefault(active);
    IEnumerable<WebView2> AllViews() => views.Values.Concat(inlinePages.Values.SelectMany(x => x));
    async Task SaveActiveSession()
    {
        if (savingSession || active == null || CurrentView() is not { } view || !ready.Contains(view)) return;
        var pad = prefs.Pads.FirstOrDefault(x => x.Id == active); if (pad == null) return;
        savingSession = true;
        try {
            string state = await view.ExecuteScriptAsync("JSON.stringify({scroll:window.scrollY,video:document.querySelector('video')?.currentTime||0})").WaitAsync(TimeSpan.FromSeconds(2));
            string? json = JsonSerializer.Deserialize<string>(state);
            if (json != null) {
                using var data = JsonDocument.Parse(json);
                pad.ScrollY = data.RootElement.GetProperty("scroll").GetDouble();
                pad.VideoTime = data.RootElement.GetProperty("video").GetDouble();
            }
            Store.Save(prefs);
        } catch { /* A navigation can replace the document while capturing it. */ }
        finally { savingSession = false; }
    }
    async Task ActivatePad(Pad? pad, bool navigateInitially = true, bool snapshot = true)
    {
        int request = ++activation;
        if (snapshot) await SaveActiveSession(); if (request != activation) return;
        active = pad?.Id; prefs.ActiveId = active;
        foreach (var v in AllViews()) v.Visibility = Visibility.Hidden;
        RenderRail(); Store.Save(prefs);
        if (pad == null) { PageTitle.Text = "添加你的第一个网站"; Address.Text = ""; return; }
        PageTitle.Text = pad.Name; Address.Text = pad.ResumeUrl;
        if (!views.TryGetValue(pad.Id, out var view)) {
            view = new WebView2(); views.Add(pad.Id, view); ContentHost.Children.Add(view);
            initializing[pad.Id] = InitializeView(view, pad, navigateInitially);
        }
        (CurrentView() ?? view).Visibility = Visibility.Visible;
        ContentHost.UpdateLayout();
        try { await initializing[pad.Id]; if (active == pad.Id) { CurrentView()?.Focus(); UpdateNavigation(CurrentView() ?? view); } }
        catch (Exception ex) { Status.Text = "浏览器启动失败：" + ex.Message + "。请检查 WebView2 Runtime。"; }
    }
    void DisposePad(Pad pad)
    {
        if (inlinePages.Remove(pad.Id, out var pages)) foreach (var page in pages) { ready.Remove(page); ContentHost.Children.Remove(page); page.Dispose(); }
        if (views.Remove(pad.Id, out var view)) { ready.Remove(view); ContentHost.Children.Remove(view); view.Dispose(); }
        initializing.Remove(pad.Id);
    }
    Pad GetOrCreatePad(string url)
    {
        string key = Store.SiteKey(url);
        var existing = prefs.Pads.FirstOrDefault(p => Store.SiteKey(p.Url) == key || Store.SiteKey(p.ResumeUrl) == key);
        if (existing != null) return existing;
        string name = key == "deepseek.com" ? "DeepSeek" : key == "youtube.com" ? "YouTube" : new Uri(url).Host;
        var pad = new Pad { Name = name, Url = url }; prefs.Pads.Add(pad); Store.Save(prefs); RenderRail(); return pad;
    }
    bool ShouldRoute(Pad pad, string url) => prefs.AutoPinSites && Store.IsWebUrl(url) && new Uri(url).Host != "sidedock.internal" && !Store.IsAuthUrl(url) && !Store.IsAuthUrl(pad.ResumeUrl) && Store.SiteKey(pad.Url) != Store.SiteKey(url);
    async Task OpenUrl(string url, bool navigateExisting = true, bool snapshot = true)
    {
        var pad = prefs.AutoPinSites ? GetOrCreatePad(url) : prefs.Pads.FirstOrDefault(p => p.Id == active) ?? GetOrCreatePad(url);
        bool existed = views.ContainsKey(pad.Id);
        if (!existed) pad.LastUrl = url;
        await ActivatePad(pad, snapshot: snapshot);
        if (existed && navigateExisting && CurrentView()?.CoreWebView2 is { } core && core.Source != url) core.Navigate(url);
    }
    async Task InitializeView(WebView2 view, Pad pad, bool navigateInitially = true, bool inline = false)
    {
        environmentTask ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(Store.Root, "Browser"));
        environment = await environmentTask;
        await view.EnsureCoreWebView2Async(environment);
        var core = view.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreDevToolsEnabled = smoke;
        core.Settings.IsPasswordAutosaveEnabled = true;
        core.Settings.IsGeneralAutofillEnabled = true;
        double restoreScroll = pad.ScrollY, restoreVideo = pad.VideoTime;
        string resume = pad.ResumeUrl;
        bool restoring = navigateInitially && pad.LastUrl != null;
        bool authFlow = false;
        core.DocumentTitleChanged += (_, _) => {
            if (active == pad.Id && CurrentView() == view) PageTitle.Text = core.DocumentTitle;
        };
        core.FaviconChanged += async (_, _) => {
            try {
                using var icon = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                if (icon == null || !icon.CanRead) return;
                string folder = Path.Combine(Store.Root, "Icons"); Directory.CreateDirectory(folder);
                string file = pad.Id + ".png";
                using (var output = File.Create(Path.Combine(folder, file))) await icon.CopyToAsync(output);
                pad.IconFile = file; Store.Save(prefs); RenderRail();
            } catch { }
        };
        core.HistoryChanged += (_, _) => { if (active == pad.Id && CurrentView() == view) UpdateNavigation(view); };
        core.SourceChanged += (_, _) => {
            string url = core.Source;
            bool isBack = goingBack.Remove(pad.Id);
            if (Store.IsWebUrl(url) && !Store.IsAuthUrl(url) && new Uri(url).Host != "sidedock.internal") {
                if (pad.LastUrl != url) {
                    if (pad.LastUrl != null && !isBack) { pad.BackStack.Add(pad.LastUrl); if (pad.BackStack.Count > 200) pad.BackStack.RemoveAt(0); }
                    pad.LastUrl = url;
                    if (!restoring) { pad.ScrollY = 0; pad.VideoTime = 0; }
                }
                history.Add(url, core.DocumentTitle, pad.Id); Store.Save(prefs);
            }
            if (active == pad.Id && CurrentView() == view) UpdateNavigation(view);
        };
        core.NavigationStarting += (_, args) => {
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var u) && u.Scheme is not ("https" or "http" or "about" or "data" or "blob")) { args.Cancel = true; Status.Text = "此链接需要外部应用，可在主浏览器打开。"; return; }
            if (Store.IsAuthUrl(args.Uri)) authFlow = true;
            if (!inline && (args.IsUserInitiated || args.IsRedirected) && !authFlow && ShouldRoute(pad, args.Uri)) {
                args.Cancel = true; Dispatcher.BeginInvoke(async () => await OpenUrl(args.Uri)); return;
            }
            ready.Remove(view);
            if (Store.SiteKey(args.Uri) == Store.SiteKey(pad.Url)) authFlow = false;
            if (active == pad.Id) Status.Text = "正在加载…";
        };
        core.NavigationCompleted += async (_, args) => {
            if (args.IsSuccess) {
                ready.Add(view);
                if (Store.IsWebUrl(core.Source) && !Store.IsAuthUrl(core.Source)) history.Add(core.Source, core.DocumentTitle, pad.Id);
                if (restoring && core.Source == resume && (restoreScroll > 0 || restoreVideo > 0)) {
                    string values = JsonSerializer.Serialize(new { scroll = restoreScroll, video = restoreVideo });
                    await core.ExecuteScriptAsync("(()=>{const s=" + values + ";let n=0;const stop=()=>clearInterval(t);const t=setInterval(()=>{window.scrollTo(0,s.scroll);const v=document.querySelector('video');if(v&&v.readyState>0&&s.video>0){v.currentTime=s.video;s.video=0}if(++n>15)stop()},200);for(const e of ['wheel','touchstart','keydown','pointerdown'])addEventListener(e,stop,{once:true})})()");
                }
                restoring = false; Store.Save(prefs);
            }
            if (active != pad.Id || CurrentView() != view) return;
            UpdateNavigation(view);
            Status.Text = args.IsSuccess ? "已保存 · Ctrl+H 历史 · 移回边缘收起" : "加载失败：" + args.WebErrorStatus + " · 点击 ↻ 重试";
        };
        core.NewWindowRequested += async (_, args) => {
            args.Handled = true;
            var deferral = args.GetDeferral();
            try {
                string url = args.Uri;
                if (!Store.IsWebUrl(url) && url != "about:blank") return;
                if (!args.IsUserInitiated && !Store.IsAuthUrl(url) && url != "about:blank" && !smoke) { Status.Text = "已阻止未经点击的弹窗"; return; }
                if (Store.IsAuthUrl(url) || url == "about:blank") {
                    var child = new WebView2(); ContentHost.Children.Add(child);
                    if (!inlinePages.TryGetValue(pad.Id, out var pages)) inlinePages[pad.Id] = pages = new();
                    pages.Add(child); view.Visibility = Visibility.Hidden;
                    await InitializeView(child, pad, false, true);
                    args.NewWindow = child.CoreWebView2;
                    child.CoreWebView2.WindowCloseRequested += (_, _) => CloseInline(pad, child);
                    if (active != pad.Id) child.Visibility = Visibility.Hidden;
                    Status.Text = "登录页在当前面板内打开 · ‹ 返回原页面";
                } else if (ShouldRoute(pad, url)) await OpenUrl(url, snapshot: false);
                else core.Navigate(url);
            } catch (Exception ex) { Status.Text = "打开链接失败：" + ex.Message; }
            finally { deferral.Complete(); }
        };
        core.PermissionRequested += (_, args) => {
            modal = true;
            try { args.State = System.Windows.MessageBox.Show(this, $"{args.Uri}\n请求权限：{args.PermissionKind}\n是否允许？", "网站权限", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny; }
            finally { modal = false; outsideSince = DateTime.UtcNow; }
        };
        core.ProcessFailed += (_, _) => { if (active == pad.Id) Status.Text = "网页进程已停止，可点击刷新重试"; };
        if (pad.Url == "sidedock:notes") {
            string local = Path.Combine(Store.Root, "Local"); Directory.CreateDirectory(local);
            File.WriteAllText(Path.Combine(local, "notes.html"), NotesHtml);
            core.SetVirtualHostNameToFolderMapping("sidedock.internal", local, CoreWebView2HostResourceAccessKind.DenyCors);
            if (navigateInitially) core.Navigate("https://sidedock.internal/notes.html");
        } else if (navigateInitially) core.Navigate(resume);
    }
    void CloseInline(Pad pad, WebView2 child)
    {
        if (inlinePages.TryGetValue(pad.Id, out var pages)) pages.Remove(child);
        ready.Remove(child); child.Visibility = Visibility.Hidden;
        Dispatcher.BeginInvoke(async () => { await Task.Delay(150); ContentHost.Children.Remove(child); child.Dispose(); });
        if (active == pad.Id && CurrentView() is { } current) { current.Visibility = Visibility.Visible; UpdateNavigation(current); }
    }
    void UpdateNavigation(WebView2 v)
    {
        var pad = prefs.Pads.FirstOrDefault(p => p.Id == active);
        BackButton.IsEnabled = v.CoreWebView2?.CanGoBack == true || (pad?.BackStack.Count ?? 0) > 0 || active != null && inlinePages.GetValueOrDefault(active)?.Count > 0;
        if (v.CoreWebView2 != null) Address.Text = v.CoreWebView2.Source.StartsWith("https://sidedock.internal") ? "本地便笺 · 自动保存" : v.CoreWebView2.Source;
    }
    async void Navigate()
    {
        try { await OpenUrl(Store.NormalizeUrl(Address.Text, prefs.SearchEngine)); }
        catch (ArgumentException ex) { Status.Text = ex.Message; }
    }
    void GoBack()
    {
        var pad = prefs.Pads.FirstOrDefault(p => p.Id == active); var v = CurrentView(); if (pad == null || v?.CoreWebView2 == null) return;
        if (inlinePages.GetValueOrDefault(pad.Id)?.Count > 0 && !v.CoreWebView2.CanGoBack) { CloseInline(pad, v); return; }
        if (v.CoreWebView2.CanGoBack) { if (pad.BackStack.Count > 0) pad.BackStack.RemoveAt(pad.BackStack.Count - 1); goingBack.Add(pad.Id); v.CoreWebView2.GoBack(); }
        else if (pad.BackStack.Count > 0) { string url = pad.BackStack[^1]; pad.BackStack.RemoveAt(pad.BackStack.Count - 1); goingBack.Add(pad.Id); v.CoreWebView2.Navigate(url); }
    }
    async void History_Click(object sender, RoutedEventArgs e)
    {
        modal = true; var dialog = new HistoryWindow(history.Items) { Owner = this };
        try { if (dialog.ShowDialog() == true && dialog.SelectedUrl != null) await OpenUrl(dialog.SelectedUrl); }
        finally { modal = false; outsideSince = DateTime.UtcNow; }
    }
}
