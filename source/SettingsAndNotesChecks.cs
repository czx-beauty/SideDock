using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace SideDock;
public partial class MainWindow
{
    async Task SettingsAndNotesChecks(List<string> checks, WebView2 notes)
    {
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
        // Exercise the real settings form at a height where the old fixed StackPanel clipped Save.
        var settings = new SettingsWindow(prefs) { Owner = this, Height = 340 };
        string? uiFailure = null;
        settings.Loaded += (_, _) => {
            try {
                settings.UpdateLayout();
                var button = (Button)settings.FindName("SaveSettingsButton");
                var root = (FrameworkElement)settings.Content;
                var point = button.TransformToAncestor(root).Transform(new Point(0, 0));
                Check(button.IsVisible && button.ActualHeight > 20 && point.Y >= 0 && point.Y + button.ActualHeight <= root.ActualHeight + 1, "Settings Save remains reachable in short window");
                ((TextBox)settings.FindName("SearchEngineInput")).Text = "bing.com";
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (settings.DialogResult != true) throw new Exception("Settings form did not save");
            } catch (Exception ex) { uiFailure = ex.ToString(); settings.Close(); }
        };
        Check(settings.ShowDialog() == true && uiFailure == null, "Settings Save button commits successfully: " + (uiFailure ?? "OK"));
        var reopened = new SettingsWindow(Store.Load());
        Check(((TextBox)reopened.FindName("SearchEngineInput")).Text == "https://bing.com", "Reopening settings retains Bing from disk");
        reopened.Close();
        Check(prefs.Pads.Where(p => p.IsSearch).Any(p => p.Name == "Bing" && p.Url == "https://bing.com" && p.LastUrl == null), "Default sidebar search entry follows engine setting");
        Check(Store.NormalizeUrl("hello world", Store.Load().SearchEngine).StartsWith("https://bing.com/search?q="), "Search after preferences reload uses Bing");
        string oldRoot = Store.Root;
        string oldEdge = prefs.Edge;
        string unwritable = Path.Combine(oldRoot, "not-a-directory"); File.WriteAllText(unwritable, "fixture");
        try {
            Store.Root = unwritable;
            bool rejected = false;
            try { Store.UpdateSettings(prefs, "duckduckgo.com", "left", false, false); } catch (IOException) { rejected = true; }
            Check(rejected && prefs.SearchEngine == "https://bing.com" && prefs.Edge == oldEdge, "Failed settings write leaves live settings unchanged");
        } finally { Store.Root = oldRoot; File.Delete(unwritable); }
        var legacy = new Preferences { Version = 2, SearchEngine = "https://bing.com", Pads = [new() { Name = "Google", Url = "https://www.google.com" }, new() { Name = "My bookmark", Url = "https://www.google.com" }] };
        Store.Save(legacy);
        var migrated = Store.Load();
        Check(migrated.Pads[0].IsSearch && migrated.Pads[0].Name == "Bing" && !migrated.Pads[1].IsSearch && migrated.Pads[1].Url == "https://www.google.com", "Old search entry migration preserves ordinary bookmarks");
        Store.UpdateSettings(prefs, "google.com", "right", true, true);
        async Task<bool> JS(string script) => await notes.ExecuteScriptAsync(script) == "true";
        async Task Reload() {
            var loaded = new TaskCompletionSource<bool>();
            void Handler(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult(e.IsSuccess);
            notes.CoreWebView2.NavigationCompleted += Handler;
            try { notes.Reload(); Check(await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15)), "Notebook browser reload completes"); }
            finally { notes.CoreWebView2.NavigationCompleted -= Handler; }
        }
        Check(await JS("JSON.parse(localStorage.getItem('sidedock.notebook.v1')).notes[0].body==='Smoke test note' && localStorage.getItem('notes')==='Smoke test note'"), "Legacy single-page note migrates and original backup remains");
        await notes.ExecuteScriptAsync("document.getElementById('new').click();document.getElementById('title').value='<script>not executable</script>';document.getElementById('title').dispatchEvent(new Event('input'));document.getElementById('note').value='长笔记\\n'.repeat(10000);document.getElementById('note').dispatchEvent(new Event('input'))");
        Check(await JS("document.querySelectorAll('#list button').length===2 && !document.querySelector('#list script') && document.getElementById('note').value.length===40000"), "Multiple notes support long content and safe literal titles");
        await Reload();
        Check(await JS("document.getElementById('note').value.length===40000 && document.getElementById('title').value==='<script>not executable</script>'"), "Active note, title and long content survive reload");
        await notes.ExecuteScriptAsync("document.querySelectorAll('#list button')[1].click()");
        Check(await JS("document.getElementById('note').value==='Smoke test note'"), "Switching notes restores each note's content");
        await notes.ExecuteScriptAsync("document.getElementById('delete').click();document.getElementById('restore').click()");
        Check(await JS("document.getElementById('note').value==='Smoke test note' && JSON.parse(localStorage.getItem('sidedock.notebook.v1')).notes.length===2"), "Deleted note can be restored without losing other notes");
        await notes.ExecuteScriptAsync("window.originalSet=Storage.prototype.setItem;Storage.prototype.setItem=function(){throw new DOMException('Full','QuotaExceededError')};document.getElementById('note').value='unsaved draft';document.getElementById('note').dispatchEvent(new Event('input'));document.getElementById('new').click()");
        Check(await JS("document.getElementById('state').dataset.error==='true' && document.getElementById('note').value==='unsaved draft' && document.querySelectorAll('#list button').length===2"), "Storage failure reports error and protects unsaved draft from switching");
        await notes.ExecuteScriptAsync("Storage.prototype.setItem=window.originalSet;document.getElementById('note').dispatchEvent(new Event('input'))");
        string backup = JsonSerializer.Deserialize<string>(await notes.ExecuteScriptAsync("localStorage.getItem('sidedock.notebook.v1')"))!;
        await notes.ExecuteScriptAsync("localStorage.setItem('sidedock.notebook.v1','broken data')"); await Reload();
        Check(await JS("document.getElementById('note').disabled && localStorage.getItem('sidedock.notebook.v1')==='broken data'"), "Malformed notebook is preserved rather than overwritten");
        await notes.ExecuteScriptAsync("localStorage.setItem('sidedock.notebook.v1'," + JsonSerializer.Serialize(backup) + ")"); await Reload();
        Check(await JS("document.getElementById('note').value==='unsaved draft'"), "Notebook recovers after restoring a valid backup");
        string exportPath = Path.Combine(Store.Root, "notebook-export.json");
        var downloaded = new TaskCompletionSource<bool>();
        void Download(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2DownloadStartingEventArgs e) {
            e.ResultFilePath = exportPath; e.Handled = true;
            e.DownloadOperation.StateChanged += (_, _) => {
                if (e.DownloadOperation.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Completed) downloaded.TrySetResult(true);
                if (e.DownloadOperation.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Interrupted) downloaded.TrySetResult(false);
            };
        }
        notes.CoreWebView2.DownloadStarting += Download;
        try {
            await notes.ExecuteScriptAsync("document.getElementById('export').click()");
            Check(await downloaded.Task.WaitAsync(TimeSpan.FromSeconds(15)) && File.Exists(exportPath), "Export creates a real notebook backup download");
        } finally { notes.CoreWebView2.DownloadStarting -= Download; }
        using var exported = JsonDocument.Parse(File.ReadAllText(exportPath));
        Check(exported.RootElement.GetProperty("notes").GetArrayLength() == 2, "Downloaded backup contains all notes");
        await notes.ExecuteScriptAsync("(()=>{const dt=new DataTransfer();dt.items.add(new File([" + JsonSerializer.Serialize(backup) + "],'backup.json',{type:'application/json'}));document.getElementById('importFile').files=dt.files;document.getElementById('importFile').dispatchEvent(new Event('change'))})()");
        await WaitFor(async () => await JS("document.querySelectorAll('#list button').length===4"), "backup file imports");
        Check(await JS("JSON.parse(localStorage.getItem('sidedock.notebook.v1')).notes.filter(n=>n.body==='unsaved draft').length===2"), "Import appends backup without overwriting existing notes");
        await notes.ExecuteScriptAsync("document.getElementById('filter').value='not executable';document.getElementById('filter').dispatchEvent(new Event('input'))");
        Check(await JS("document.querySelectorAll('#list button').length===2"), "Notebook search filters title and body");
        await notes.ExecuteScriptAsync("document.getElementById('filter').value='';document.getElementById('filter').dispatchEvent(new Event('input'))");
    }
}
