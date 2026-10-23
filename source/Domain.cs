using System.IO;
using System.Text.Json;

namespace SideDock;
public class Pad
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新网站";
    public string Url { get; set; } = "https://www.google.com";
    public string? LastUrl { get; set; }
    public double ScrollY { get; set; }
    public double VideoTime { get; set; }
    public string? IconFile { get; set; }
    public List<string> BackStack { get; set; } = new();
    public string ResumeUrl => LastUrl ?? Url;
}
public class Preferences
{
    public int Version { get; set; }
    public string Edge { get; set; } = "right";
    public bool Pinned { get; set; }
    public bool EdgeEnabled { get; set; } = true;
    public bool AutoPinSites { get; set; } = true;
    public string SearchEngine { get; set; } = "https://www.google.com";
    public double Width { get; set; } = 510;
    public string? ActiveId { get; set; }
    public List<Pad> Pads { get; set; } = [new() { Name = "Google", Url = "https://www.google.com" }, new() { Name = "便笺", Url = "sidedock:notes" }];
}
public record Visit(string Url, string Title, string PadId, DateTime At);
public static class Store
{
    public static string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SideDock");
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static bool IsWebUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";
    public static Preferences Load()
    {
        Preferences? p = null;
        foreach (string suffix in new[] { "", ".bak" }) {
            try { p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(Root, "settings.json" + suffix))); if (p != null) break; } catch { }
        }
        p ??= new();
        p.Width = double.IsFinite(p.Width) ? Math.Clamp(p.Width, 380, 1000) : 510;
        p.Edge = p.Edge == "left" ? "left" : "right";
        p.SearchEngine ??= "https://www.google.com";
        if (!IsWebUrl(p.SearchEngine.Replace("{query}", "test"))) p.SearchEngine = "https://www.google.com";
        p.Pads = (p.Pads ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name) && (x.Url == "sidedock:notes" || IsWebUrl(x.Url))).DistinctBy(x => x.Id).ToList();
        foreach (var pad in p.Pads) {
            if (pad.LastUrl != null && !IsWebUrl(pad.LastUrl)) pad.LastUrl = null;
            pad.BackStack = (pad.BackStack ?? []).Where(IsWebUrl).TakeLast(200).ToList();
        }
        if (p.Version < 2) {
            foreach (var pad in p.Pads.Where(x => x.Url.TrimEnd('/') == "https://www.bing.com" && x.Name == "搜索")) { pad.Name = "Google"; pad.Url = p.SearchEngine; pad.LastUrl = null; }
            p.Version = 2;
        }
        return p;
    }
    public static void Save(Preferences p) => AtomicWrite("settings.json", p, true);
    public static void AtomicWrite<T>(string name, T data, bool backup = false)
    {
        Directory.CreateDirectory(Root);
        string path = Path.Combine(Root, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data, JsonOptions));
        if (backup && File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(path + ".tmp", path, true);
    }
    public static string NormalizeUrl(string input, string engine = "https://www.google.com")
    {
        input = input.Trim();
        if (string.IsNullOrEmpty(input)) throw new ArgumentException("请输入网址或搜索内容");
        if (IsWebUrl(input)) return new Uri(input).AbsoluteUri;
        if (input.Contains("://") || input.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || input.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || input.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("仅支持 HTTP / HTTPS 网站");
        if (!input.Any(char.IsWhiteSpace) && (input.Contains('.') || input.StartsWith("localhost"))) {
            if (Uri.TryCreate("https://" + input, UriKind.Absolute, out var uri)) return uri.AbsoluteUri;
        }
        return SearchUrl(engine, input);
    }
    public static string SearchUrl(string engine, string query)
    {
        engine = engine.Trim(); if (!engine.Contains("://")) engine = "https://" + engine;
        if (!IsWebUrl(engine.Replace("{query}", "test"))) throw new ArgumentException("搜索引擎必须是 HTTP / HTTPS 网址");
        if (engine.Contains("{query}")) return engine.Replace("{query}", Uri.EscapeDataString(query));
        var b = new UriBuilder(engine); string host = b.Host.ToLowerInvariant();
        string key = host.Contains("baidu.com") ? "wd" : host.Contains("yahoo.com") ? "p" : "q";
        if (b.Path == "/" && (host.Contains("google.") || host.Contains("bing.com") || host.Contains("brave.com"))) b.Path = "/search";
        if (b.Path == "/" && host.Contains("baidu.com")) b.Path = "/s";
        var parts = b.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Where(x => !x.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)).ToList();
        parts.Add(key + "=" + Uri.EscapeDataString(query)); b.Query = string.Join("&", parts); return b.Uri.AbsoluteUri;
    }
    public static string SiteKey(string url)
    {
        if (!IsWebUrl(url)) return url;
        var host = new Uri(url).IdnHost.ToLowerInvariant();
        if (host == "youtu.be" || host == "youtube.com" || host.EndsWith(".youtube.com")) return "youtube.com";
        if (host == "deepseek.com" || host.EndsWith(".deepseek.com")) return "deepseek.com";
        return host.StartsWith("www.") ? host[4..] : host;
    }
    public static bool IsAuthUrl(string url)
    {
        if (!IsWebUrl(url)) return false;
        var u = new Uri(url); string h = u.Host.ToLowerInvariant(), p = u.AbsolutePath.ToLowerInvariant();
        return h.StartsWith("accounts.") || h.StartsWith("login.") || h.StartsWith("auth.") || h.EndsWith(".auth0.com") || p.Contains("/oauth") || p.Contains("/signin") || p.Contains("/login") || p.Contains("/sso") || p.Contains("/authorize");
    }
}
public sealed class HistoryStore
{
    public List<Visit> Items { get; private set; } = new();
    public HistoryStore() { try { Items = JsonSerializer.Deserialize<List<Visit>>(File.ReadAllText(Path.Combine(Store.Root, "history.json"))) ?? new(); } catch { } }
    public void Add(string url, string title, string id) {
        if (!Store.IsWebUrl(url) || new Uri(url).Host == "sidedock.internal") return;
        if (Items.LastOrDefault() is { } last && last.Url == url && last.PadId == id && (DateTime.UtcNow - last.At).TotalSeconds < 10) Items[^1] = last with { Title = title };
        else Items.Add(new(url, title, id, DateTime.UtcNow));
        if (Items.Count > 10000) Items.RemoveRange(0, Items.Count - 10000);
        Store.AtomicWrite("history.json", Items);
    }
}
