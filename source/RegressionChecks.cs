using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace SideDock;
public partial class MainWindow
{
    static async Task WaitFor(Func<Task<bool>> predicate, string message)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < until) { try { if (await predicate()) return; } catch { } await Task.Delay(100); }
        throw new Exception("Timed out: " + message);
    }
    async Task RegressionChecks(List<string> checks)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks.Add(label); File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "smoke-progress.log"), label + "\n"); }
        Check(Store.NormalizeUrl("你好 世界").StartsWith("https://www.google.com/search?q="), "Google default search");
        Check(Store.SearchUrl("bing.com", "hello").StartsWith("https://bing.com/search?q="), "Search engine by URL");
        Check(Store.SearchUrl("https://custom.test/find?term={query}", "a b").Contains("term=a%20b"), "Custom search template");
        Check(Store.SearchUrl("https://www.baidu.com", "hello").Contains("/s?wd="), "Baidu URL parameter");
        var gesture = new EdgeGesture(); DateTime now = DateTime.UtcNow;
        Check(!gesture.ShouldHide(false, true, false, now), "Fresh edge reveal does not immediately hide");
        gesture.ShouldHide(true, false, false, now);
        Check(!gesture.ShouldHide(false, true, false, now), "Edge retreat debounce");
        Check(gesture.ShouldHide(false, true, false, now.AddMilliseconds(200)), "Return to dock edge hides");
        Check(!gesture.ShouldHide(false, true, true, now.AddSeconds(1)), "Pinned panel stays open");
        Check(!gesture.Push(40, true, true, false, false, now), "Outward movement during reveal is ignored");
        Check(!gesture.Push(15, true, true, false, true, now), "Small physical edge push is debounced");
        Check(gesture.Push(20, true, true, false, true, now.AddMilliseconds(40)), "Second outward push at clamped right edge hides");
        gesture.Reset(); Check(gesture.Push(-35, false, true, false, true, now), "Physical push mirrors on left edge");
        Check(Store.SiteKey("https://chat.deepseek.com/a") == Store.SiteKey("https://www.deepseek.com"), "DeepSeek site grouping");
        Check(Store.SiteKey("https://youtu.be/test") == Store.SiteKey("https://www.youtube.com/watch?v=test"), "YouTube site grouping");
        Check(StartupService.Command("C:\\My App\\SideDock.exe") == "\"C:\\My App\\SideDock.exe\" --startup", "Startup command quoting");
        using var fixture = new BrowserFixture();
        int count = prefs.Pads.Count;
        await OpenUrl(BrowserFixture.First + "/conversation");
        string firstId = active!; var first = views[firstId];
        await WaitFor(async () => await first.ExecuteScriptAsync("!!document.querySelector('#draft')") == "true", "first site ready");
        Check(prefs.Pads.Any(p => p.Id == firstId && Store.SiteKey(p.Url) == "127.0.0.1"), "Opening website automatically pins it");
        await first.ExecuteScriptAsync("document.querySelector('#draft').value='question kept';document.querySelector('#answer').textContent='answer kept';window.fixtureIdentity='live-instance';history.pushState({},'', '/chat/42');window.scrollTo(0,640)");
        await SaveActiveSession();
        await first.ExecuteScriptAsync("window.open('" + BrowserFixture.Second + "/watch','_blank')");
        await WaitFor(() => Task.FromResult(active != firstId && active != null), "cross-site popup routed");
        string secondId = active!;
        await WaitFor(async () => await CurrentView()!.ExecuteScriptAsync("!!document.querySelector('#draft')") == "true", "second site ready");
        Check(System.Windows.Application.Current.Windows.Count == 1, "Target blank opens inside single application window");
        var firstPad = prefs.Pads.First(p => p.Id == firstId);
        await ActivatePad(firstPad);
        Check(await first.ExecuteScriptAsync("window.fixtureIdentity") == "\"live-instance\"", "Switching sites preserves exact live browser instance");
        Check(await first.ExecuteScriptAsync("document.querySelector('#draft').value") == "\"question kept\"", "Unsent input preserved while switching");
        Check(await first.ExecuteScriptAsync("document.querySelector('#answer').textContent") == "\"answer kept\"", "Answer preserved while switching");
        await SaveActiveSession();
        Check(firstPad.LastUrl?.EndsWith("/chat/42") == true, "SPA route saved");
        Check(firstPad.ScrollY >= 600, "Scroll position saved");
        var restored = Store.Load().Pads.First(p => p.Id == firstId);
        Check(restored.LastUrl == firstPad.LastUrl && restored.ScrollY >= 600, "Session restored from disk");
        Check(new HistoryStore().Items.Any(v => v.Url.EndsWith("/chat/42")), "SPA history persists on disk");
        DisposePad(firstPad); await ActivatePad(firstPad);
        var recreated = views[firstId];
        await WaitFor(async () => await recreated.ExecuteScriptAsync("location.pathname") == "\"/chat/42\"", "restored URL loaded");
        await WaitFor(async () => double.TryParse(await recreated.ExecuteScriptAsync("window.scrollY"), out double y) && y >= 600, "restored scroll");
        Check(!ReferenceEquals(first, recreated), "Restart simulation creates a fresh browser");
        checks.Add("Last URL and scroll restored in fresh browser instance");
        await recreated.ExecuteScriptAsync("window.open('/login','auth')");
        await WaitFor(() => Task.FromResult(inlinePages.GetValueOrDefault(firstId)?.Count == 1), "inline auth opened");
        await WaitFor(async () => await CurrentView()!.ExecuteScriptAsync("document.title") == "\"Inline login\"", "inline auth ready");
        Check(System.Windows.Application.Current.Windows.Count == 1, "Login remains in same application window");
        Check(await CurrentView()!.ExecuteScriptAsync("!!window.opener") == "true", "Inline login preserves window.opener");
        await Task.WhenAny(CurrentView()!.ExecuteScriptAsync("opener.postMessage('signed-in','*');window.close()"), Task.Delay(1500));
        await WaitFor(() => Task.FromResult(inlinePages[firstId].Count == 0), "inline auth closes");
        await WaitFor(async () => await recreated.ExecuteScriptAsync("window.received") == "\"signed-in\"", "auth callback reaches opener");
        checks.Add("Inline auth callback returns to original page");
        var secondPad = prefs.Pads.First(p => p.Id == secondId);
        int pinnedCount = prefs.Pads.Count; await OpenUrl(BrowserFixture.Second + "/watch/next");
        Check(prefs.Pads.Count == pinnedCount, "Repeated visits reuse existing sidebar entry");
        foreach (var testPad in new[] { firstPad, secondPad }) { DisposePad(testPad); prefs.Pads.Remove(testPad); }
        await ActivatePad(prefs.Pads.First(p => p.Url == "sidedock:notes"));
        Check(await CurrentView()!.ExecuteScriptAsync("document.visibilityState") == "\"visible\"", "Returning to original tab restores visible document");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "smoke-progress.log"), "Returned to notes\n");
    }
}
