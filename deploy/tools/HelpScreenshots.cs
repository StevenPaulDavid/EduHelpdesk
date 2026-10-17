#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Takes the help centre's screenshots (wwwroot/help/images) from a running copy of EduHelpdesk, through headless
// Microsoft Edge. Run it against a scratch copy with made-up data, never the school's real one:
//   dotnet run --file deploy/tools/HelpScreenshots.cs -- <base url> <email> <password> <shots file> <output folder>
// e.g. ... -- http://localhost:5199 admin@eduhelpdesk.local "..." wwwroot/help/shots.txt wwwroot/help/images
//
// The shots file has one picture per line:  name | page | element (optional) | script (optional)
//   tickets-list | /Jobs | main .panel
//   round-tip    | /Settings/Rounds | .template-fields | document.querySelector('.help-tip-mark').click()
// "element" is a CSS selector to crop to (blank = the whole window); "script" runs on the page first. Lines starting #
// are comments. The welcome note and the notifications nudge are hidden so they don't appear in every picture.
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length < 5) { Console.Error.WriteLine("Usage: <base url> <email> <password> <shots file> <output folder> [width] [height]"); return 1; }
var (baseUrl, email, password, shotsFile, outDir) = (args[0].TrimEnd('/'), args[1], args[2], args[3], args[4]);
var width = args.Length > 5 ? int.Parse(args[5]) : 1280;
var height = args.Length > 6 ? int.Parse(args[6]) : 820;
Directory.CreateDirectory(outDir);

var edge = new[] { @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe" }.FirstOrDefault(File.Exists)
    ?? throw new InvalidOperationException("Microsoft Edge wasn't found.");
var profile = Path.Combine(Path.GetTempPath(), "eduhelpdesk-shots-" + Guid.NewGuid().ToString("N"));
const int port = 9333;
using var browser = Process.Start(new ProcessStartInfo(edge,
    $"--headless=new --disable-gpu --no-first-run --no-default-browser-check --hide-scrollbars --remote-debugging-port={port} --user-data-dir=\"{profile}\" --window-size={width},{height} about:blank")
    { UseShellExecute = false })!;
try
{
    using var http = new HttpClient();
    string? socketUrl = null;
    for (var i = 0; i < 50 && socketUrl is null; i++)
    {
        try
        {
            var targets = JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json"))!.AsArray();
            socketUrl = targets.FirstOrDefault(t => (string?)t!["type"] == "page")?["webSocketDebuggerUrl"]?.GetValue<string>();
        }
        catch (HttpRequestException) { }
        if (socketUrl is null) await Task.Delay(200);
    }
    if (socketUrl is null) throw new InvalidOperationException("Edge didn't open its debugging port.");

    using var cdp = new Cdp(new Uri(socketUrl));
    await cdp.Connect();
    await cdp.Send("Page.enable");
    await cdp.Send("Emulation.setDeviceMetricsOverride", new { width, height, deviceScaleFactor = 1, mobile = false });
    // Light, whatever this computer prefers, so every picture matches.
    await cdp.Send("Emulation.setEmulatedMedia", new { features = new[] { new { name = "prefers-color-scheme", value = "light" } } });

    await cdp.Navigate($"{baseUrl}/Login");
    await cdp.Eval($$"""
        (() => { const f = document.querySelector('form[method=post]'); f.querySelector('[name=Email]').value = {{JsonSerializer.Serialize(email)}};
        f.querySelector('[name=Password]').value = {{JsonSerializer.Serialize(password)}}; f.submit(); })()
        """);
    await cdp.WaitForLoad();
    var where = await cdp.Eval("location.pathname");
    if (where is "/Login" or "/LoginCode") throw new InvalidOperationException("Couldn't sign in (or the account needs a two-step code).");

    var count = 0;
    foreach (var raw in File.ReadAllLines(shotsFile))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var parts = line.Split('|').Select(x => x.Trim()).ToArray();
        var (name, page, selector, script) = (parts[0], parts.ElementAtOrDefault(1) ?? "/", parts.ElementAtOrDefault(2) ?? "", parts.ElementAtOrDefault(3) ?? "");
        await cdp.Navigate(baseUrl + page);
        await cdp.Eval("""
            (() => { const s = document.createElement('style'); s.textContent = '.help-welcome,.notify-nudge,.notify-warning{display:none!important}'; document.head.append(s); })()
            """);
        if (script.Length > 0) await cdp.Eval(script);
        await Task.Delay(400);
        object? clip = null;
        if (selector.Length > 0)
        {
            var rect = await cdp.Eval($$"""
                (() => { const e = document.querySelector({{JsonSerializer.Serialize(selector)}}); if (!e) return ''; const r = e.getBoundingClientRect();
                return JSON.stringify({ x: Math.max(0, r.left + scrollX - 8), y: Math.max(0, r.top + scrollY - 8), width: r.width + 16, height: r.height + 16 }); })()
                """);
            if (string.IsNullOrEmpty(rect)) { Console.Error.WriteLine($"{name}: {selector} isn't on {page}"); continue; }
            var r = JsonNode.Parse(rect)!;
            clip = new { x = (double)r["x"]!, y = (double)r["y"]!, width = Math.Min((double)r["width"]!, width), height = Math.Min((double)r["height"]!, 4000), scale = 1 };
        }
        var shot = clip is null
            ? await cdp.Send("Page.captureScreenshot", new { format = "png" })
            : await cdp.Send("Page.captureScreenshot", new { format = "png", clip, captureBeyondViewport = true });
        var file = Path.Combine(outDir, name + ".png");
        await File.WriteAllBytesAsync(file, Convert.FromBase64String((string)shot["data"]!));
        Console.WriteLine($"{name}.png  {page}");
        count++;
    }
    Console.WriteLine($"{count} screenshots saved to {outDir}");
    return 0;
}
finally
{
    try { browser.Kill(true); browser.WaitForExit(5000); } catch { }
    try { Directory.Delete(profile, true); } catch { }
}

// Just enough of the Chrome DevTools Protocol: send a command and wait for its answer, wait for a page load.
sealed class Cdp(Uri url) : IDisposable
{
    private readonly ClientWebSocket _socket = new();
    private int _id;
    private readonly Dictionary<int, TaskCompletionSource<JsonNode>> _waiting = [];
    private TaskCompletionSource _load = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task Connect()
    {
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await _socket.ConnectAsync(url, CancellationToken.None);
        _ = Task.Run(Receive);
    }

    public async Task<JsonNode> Send(string method, object? parameters = null)
    {
        var id = Interlocked.Increment(ref _id);
        var done = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waiting) _waiting[id] = done;
        var json = JsonSerializer.Serialize(new { id, method, @params = parameters ?? new { } });
        await _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
        var reply = await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (reply["error"] is { } error) throw new InvalidOperationException($"{method}: {error}");
        return reply["result"]!;
    }

    public async Task Navigate(string address)
    {
        _load = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Send("Page.navigate", new { url = address });
        await WaitForLoad();
    }

    public async Task WaitForLoad()
    {
        await _load.Task.WaitAsync(TimeSpan.FromSeconds(30));
        _load = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Task.Delay(300);
    }

    public async Task<string?> Eval(string expression)
    {
        var result = await Send("Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = true });
        return result["result"]?["value"]?.ToString();
    }

    private async Task Receive()
    {
        var buffer = new byte[1 << 20];
        var message = new MemoryStream();
        while (_socket.State == WebSocketState.Open)
        {
            var part = await _socket.ReceiveAsync(buffer, CancellationToken.None);
            message.Write(buffer, 0, part.Count);
            if (!part.EndOfMessage) continue;
            var node = JsonNode.Parse(message.ToArray())!;
            message.SetLength(0);
            if (node["id"] is { } idNode)
            {
                TaskCompletionSource<JsonNode>? waiter;
                lock (_waiting) _waiting.Remove((int)idNode, out waiter);
                waiter?.TrySetResult(node);
            }
            else if ((string?)node["method"] == "Page.loadEventFired") _load.TrySetResult();
        }
    }

    public void Dispose() => _socket.Dispose();
}
