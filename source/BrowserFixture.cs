using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SideDock;
internal sealed class BrowserFixture : IDisposable
{
    readonly TcpListener server = new(IPAddress.Loopback, 18643);
    readonly CancellationTokenSource stop = new();
    public const string First = "http://127.0.0.1:18643";
    public const string Second = "http://localhost:18643";
    public BrowserFixture() { server.Start(); _ = Run(); }
    async Task Run() {
        while (!stop.IsCancellationRequested) {
            try { var client = await server.AcceptTcpClientAsync(stop.Token); _ = Respond(client); }
            catch (OperationCanceledException) { break; } catch (SocketException) { break; }
        }
    }
    static async Task Respond(TcpClient client)
    {
        using (client) {
            try {
                using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                string first = await reader.ReadLineAsync() ?? ""; string path = first.Split(' ').ElementAtOrDefault(1) ?? "/";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                string html = path.StartsWith("/login")
                    ? "<html><title>Inline login</title><body>Sign in<button onclick=\"opener.postMessage('signed-in','*');window.close()\">Finish</button></body></html>"
                    : "<html><head><meta charset='utf-8'><title>Site fixture</title></head><body style='height:5000px'><h1>Persistent workspace</h1><textarea id='draft'></textarea><div id='answer'></div><a id='other' target='_blank' href='" + Second + "/watch'>Other site</a><script>window.received='';addEventListener('message',e=>window.received=e.data);</script></body></html>";
                byte[] body = Encoding.UTF8.GetBytes(html);
                byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header); await stream.WriteAsync(body);
            } catch { }
        }
    }
    public void Dispose() { stop.Cancel(); server.Stop(); stop.Dispose(); }
}
