using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace SideDock;
public partial class MainWindow
{
    async Task SmokeTest()
    {
        var checks = new List<string>();
        string? failure = null;
        try {
            void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
            Check(Store.NormalizeUrl("example.com") == "https://example.com/", "URL normalization");
            Check(Store.NormalizeUrl("你好 世界").StartsWith("https://www.google.com/search?q="), "Search normalization");
            bool rejected = false; try { Store.NormalizeUrl("javascript:alert(1)"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Reject script URLs");
            var rect = new System.Drawing.Rectangle(-1920, 0, 1920, 1080);
            Check(Native.AtEdge(new() { X = -1920, Y = 500 }, rect, "left"), "Negative-coordinate monitor edge");
            Check(!Native.AtEdge(new() { X = -1920, Y = 5 }, rect, "left"), "Avoid corner activation");
            Store.Save(prefs); Check(Store.Load().Pads.Count == prefs.Pads.Count, "Atomic preferences round trip");
            var pad = prefs.Pads.FirstOrDefault(x => x.Url == "sidedock:notes") ?? new Pad { Name = "便笺", Url = "sidedock:notes" };
            if (!prefs.Pads.Contains(pad)) prefs.Pads.Add(pad);
            var navigation = new TaskCompletionSource<bool>();
            await ActivatePad(pad);
            var view = views[pad.Id];
            view.CoreWebView2.NavigationCompleted += (_, e) => { if (e.IsSuccess) navigation.TrySetResult(true); };
            if (await view.ExecuteScriptAsync("document.title") == "\"随手记\"") navigation.TrySetResult(true);
            Check(await navigation.Task.WaitAsync(TimeSpan.FromSeconds(25)), "Real WebView2 navigation");
            Check(await view.ExecuteScriptAsync("document.title") == "\"随手记\"", "JavaScript execution in browser");
            var saved = await view.ExecuteScriptAsync("localStorage.getItem('smokePersist')");
            checks.Add("Previous-run localStorage: " + saved);
            await view.ExecuteScriptAsync("localStorage.setItem('smokePersist','retained'); localStorage.setItem('notes','Smoke test note'); document.querySelector('#note').value='Smoke test note'");
            var reloaded = new TaskCompletionSource<bool>();
            view.CoreWebView2.NavigationCompleted += (_, e) => { if (e.IsSuccess) reloaded.TrySetResult(true); }; view.Reload();
            Check(await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(15)), "Browser reload");
            Check(await view.ExecuteScriptAsync("document.querySelector('#note').value") == "\"Smoke test note\"", "Notes persist after reload");
            var second = new Pad { Name = "第二便笺", Url = "sidedock:notes" }; prefs.Pads.Add(second);
            await ActivatePad(second); await ActivatePad(pad);
            Check(views.Count == 2 && view.Visibility == System.Windows.Visibility.Visible, "Live tab switching");
            modal = false; await Conceal(); Check(!IsVisible, "Hide animation completes");
            await Reveal(false); Check(IsVisible, "Reveal animation completes");
            Native.GetWindowRect(handle, out var actual);
            var expected = Bounds(); Check(Math.Abs(actual.Left - expected.x) <= 2, "Native monitor positioning");
            prefs.Pads.Remove(second); RenderRail();
            DisposePad(second);
            await view.ExecuteScriptAsync("document.querySelector('#note').value='想到的事，先放在这里。\\n\\n☐ 整理今天的灵感\\n☐ 留一点时间专注' ").WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(150);
            using var image = new MemoryStream();
            await view.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image).WaitAsync(TimeSpan.FromSeconds(10));
            File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "smoke-browser.png"), image.ToArray());
            image.Position = 0;
            var browserImage = new System.Windows.Media.Imaging.BitmapImage();
            browserImage.BeginInit(); browserImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; browserImage.StreamSource = image; browserImage.EndInit();
            var shell = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); shell.Render(this);
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen()) {
                dc.DrawImage(shell, new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));
                var origin = view.TransformToAncestor(this).Transform(new System.Windows.Point(0, 0));
                dc.DrawImage(browserImage, new System.Windows.Rect(origin.X, origin.Y, view.ActualWidth, view.ActualHeight));
            }
            var preview = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); preview.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
            using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "smoke-window.png"))) encoder.Save(output);
            checks.Add("Browser and window screenshots captured");
            await RegressionChecks(checks).WaitAsync(TimeSpan.FromSeconds(65));
            if (Environment.GetCommandLineArgs().Contains("--online-smoke")) {
                await OpenUrl("https://example.com");
                await WaitFor(async () => await CurrentView()!.ExecuteScriptAsync("document.title") == "\"Example Domain\"", "external HTTPS page");
                Check(true, "External HTTPS navigation");
                var externalTitle = await CurrentView()!.ExecuteScriptAsync("document.title");
                checks.Add("External title: " + externalTitle);
                Check(JsonSerializer.Deserialize<string>(externalTitle)?.Trim() == "Example Domain", "External webpage renders expected content");
            }
            Store.Save(prefs);
        } catch (Exception ex) { failure = ex.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-result.json"), JsonSerializer.Serialize(new { passed = failure == null, checks, failure }, new JsonSerializerOptions { WriteIndented = true }));
        quitting = true; Close(); System.Windows.Application.Current.Shutdown(failure == null ? 0 : 1);
    }
}
