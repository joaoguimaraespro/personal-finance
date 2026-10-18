extern alias mcp;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Photino.NET;

namespace Host.Desktop;

/// <summary>
/// Personal Finance on a computer without a server (ADR-0010): starts the API in desktop mode in this process (its
/// own PostgreSQL, the web app on 127.0.0.1) and shows it in a native window. Closing the window stops everything.
/// <c>--smoke</c> starts, checks the API answers and stops (used by CI on each platform); <c>--server</c> runs without
/// a window until Ctrl+C.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var layout = AppLayout.Resolve();
        Directory.CreateDirectory(layout.DataDir);
        // Opened from the Start menu or Finder the working folder is anywhere: the API's files are next to this one.
        Directory.SetCurrentDirectory(layout.BaseDir);
        var console = args.Contains("--smoke") || args.Contains("--server");
        if (!console)
        {
            // No console behind a window: the log goes to a file in the data folder (replaced on each start).
            var log = new StreamWriter(Path.Combine(layout.DataDir, "app.log"), append: false) { AutoFlush = true };
            Console.SetOut(log);
            Console.SetError(log);
        }
        using var instance = SingleInstance.TryAcquire(layout.DataDir);
        if (instance is null)
        {
            // Already open: show another window on the running app instead of starting a second database.
            return SingleInstance.RunningUrl(layout.DataDir) is { } running ? ShowWindow(layout, running) : 1;
        }

        // AI clients (Claude Code, Claude Desktop, …) need an address that stays the same between runs.
        var mcpUrl = McpServer.FreeUrl();
        var server = ApiServer.Start(layout, mcpUrl);
        SingleInstance.Publish(layout.DataDir, server.Url);
        if (mcpUrl is not null)
        {
            McpServer.Start(mcpUrl, server.Url);
        }
        try
        {
            if (args.Contains("--smoke"))
            {
                return server.WaitReadyAsync(TimeSpan.FromMinutes(3)).GetAwaiter().GetResult() &&
                       server.AnswersAsync().GetAwaiter().GetResult() &&
                       (mcpUrl is null || McpServer.AnswersAsync(mcpUrl).GetAwaiter().GetResult())
                    ? 0
                    : 1;
            }

            if (args.Contains("--server"))
            {
                Console.WriteLine(server.Url);
                server.Completion.GetAwaiter().GetResult();
                return 0;
            }

            return ShowWindow(layout, server.Url);
        }
        finally
        {
            server.StopAsync().GetAwaiter().GetResult();
            SingleInstance.Clear(layout.DataDir);
        }
    }

    private static int ShowWindow(AppLayout layout, string url)
    {
        var window = new PhotinoWindow()
            .SetTitle("Personal Finance")
            .SetUseOsDefaultSize(false)
            .SetSize(1360, 880)
            .SetMinSize(400, 600)
            .Center()
            .SetContextMenuEnabled(false)
            .SetDevToolsEnabled(false)
            .SetTemporaryFilesPath(Path.Combine(layout.DataDir, "webview"));
        if (File.Exists(layout.Icon))
        {
            window.SetIconFile(layout.Icon);
        }

        // A small page waits for the API (the first run creates the database) and then opens the app.
        window.LoadRawString(LoadingPage.Html(url));
        window.WaitForClose();
        return 0;
    }
}

/// <summary>Where the app's parts are (next to the executable) and where its data lives (the user's profile).</summary>
internal sealed record AppLayout(string BaseDir, string WebRoot, string PostgresBin, string DataDir, string Icon)
{
    public static AppLayout Resolve()
    {
        var baseDir = AppContext.BaseDirectory;
        // macOS app bundle: Contents/MacOS holds the executable, Contents/Resources the rest.
        var resources = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources"));
        var root = Directory.Exists(Path.Combine(resources, "web")) ? resources : baseDir;
        var data = Environment.GetEnvironmentVariable("PERSONAL_FINANCE_DATA") ?? DefaultDataDir();
        return new AppLayout(baseDir, Path.Combine(root, "web"), Path.Combine(root, "pgsql", "bin"), data,
            Path.Combine(root, OperatingSystem.IsWindows() ? "personal-finance.ico" : "personal-finance.png"));
    }

    private static string DefaultDataDir()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalFinanceApp", "data");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support", "PersonalFinance")
            : Path.Combine(home, ".local", "share", "personal-finance-desktop");
    }
}

/// <summary>The API, run in this process through its entry point, in desktop mode.</summary>
internal sealed class ApiServer
{
    private readonly string _token;

    private ApiServer(string url, string token, Task<int> completion)
    {
        Url = url;
        _token = token;
        Completion = completion;
    }

    public string Url { get; }
    public Task<int> Completion { get; }

    public static ApiServer Start(AppLayout layout, string? mcpUrl = null)
    {
        var url = $"http://127.0.0.1:{FreePort()}";
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var settings = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["Desktop__Enabled"] = "true",
            ["Desktop__DataDir"] = layout.DataDir,
            ["Desktop__PostgresBin"] = layout.PostgresBin,
            ["Desktop__WebRoot"] = layout.WebRoot,
            ["Desktop__ShutdownToken"] = token,
            ["ASPNETCORE_URLS"] = url,
            ["ASPNETCORE_CONTENTROOT"] = layout.BaseDir,
            ["Desktop__McpUrl"] = mcpUrl is null ? "" : mcpUrl + "/mcp",
        };
        foreach (var (key, value) in settings)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        var entry = typeof(global::Program).Assembly.EntryPoint
                    ?? throw new InvalidOperationException("The API has no entry point.");
        var completion = Task.Run(async () =>
        {
            var result = entry.Invoke(null, [Array.Empty<string>()]);
            return result is Task<int> task ? await task : 0;
        });
        return new ApiServer(url, token, completion);
    }

    public async Task<bool> WaitReadyAsync(TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until && !Completion.IsCompleted)
        {
            try
            {
                if ((await http.GetAsync($"{Url}/health/ready")).IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // Slow first start.
            }

            await Task.Delay(500);
        }

        if (Completion.IsFaulted)
        {
            await Console.Error.WriteLineAsync(Completion.Exception?.ToString());
        }

        return false;
    }

    /// <summary>The API and the web app answer: /api/auth/me reports the desktop mode and / serves the app.</summary>
    public async Task<bool> AnswersAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var me = await http.GetStringAsync($"{Url}/api/auth/me");
        var page = await http.GetAsync(Url);
        var ok = me.Contains("\"desktop\":true", StringComparison.Ordinal) && page.IsSuccessStatusCode;
        await Console.Out.WriteLineAsync($"{(ok ? "OK" : "FAILED")}: {me}");
        return ok;
    }

    public async Task StopAsync()
    {
        if (Completion.IsCompleted)
        {
            return;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Url}/desktop/shutdown");
            request.Headers.Add("X-Desktop-Token", _token);
            await http.SendAsync(request);
            await Completion.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            // The API did not answer: the process exits anyway; PostgreSQL is stopped on the next start.
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>One running app per user: a lock file, and the address of the running app for a second launch.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;

    private SingleInstance(FileStream lockFile) => _lock = lockFile;

    public static SingleInstance? TryAcquire(string dataDir)
    {
        try
        {
            return new SingleInstance(new FileStream(Path.Combine(dataDir, "app.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void Publish(string dataDir, string url) => File.WriteAllText(Path.Combine(dataDir, "app.url"), url);

    public static string? RunningUrl(string dataDir)
    {
        var file = Path.Combine(dataDir, "app.url");
        return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
    }

    public static void Clear(string dataDir) => File.Delete(Path.Combine(dataDir, "app.url"));

    public void Dispose() => _lock.Dispose();
}

/// <summary>Shown while the API starts: polls it and opens the app as soon as it answers.</summary>
internal static class LoadingPage
{
    public static string Html(string url) => $$$"""
        <!doctype html>
        <html lang="pt"><head><meta charset="utf-8"><title>Personal Finance</title>
        <style>
          body{margin:0;height:100vh;display:grid;place-items:center;font:15px system-ui,sans-serif;
               background:#fafafa;color:#3f3f46}
          @media (prefers-color-scheme: dark){body{background:#09090b;color:#d4d4d8}}
          .dot{width:10px;height:10px;border-radius:50%;background:#047857;display:inline-block;margin-right:8px;
               animation:p 1s ease-in-out infinite alternate}
          @keyframes p{from{opacity:.3}to{opacity:1}}
        </style></head>
        <body><div><span class="dot"></span><span id="m">A iniciar…</span></div>
        <script>
          const url = "{{{url}}}";
          const started = Date.now();
          async function poll() {
            try {
              await fetch(url + "/health/live", { mode: "no-cors", cache: "no-store" });
              location.replace(url);
              return;
            } catch (e) { }
            if (Date.now() - started > 180000) {
              document.getElementById("m").textContent =
                "Não foi possível iniciar. Fecha e volta a abrir a aplicação.";
              return;
            }
            setTimeout(poll, 500);
          }
          poll();
        </script></body></html>
        """;
}

/// <summary>
/// The MCP server (the same one a server install runs) in this process, so AI clients on this computer can use the
/// app while it is open. It calls the API's AI gateway with each client's token, exactly as on a server.
/// </summary>
internal static class McpServer
{
    /// <summary>The usual port; the next few are tried when it is taken. <c>PERSONAL_FINANCE_MCP_PORT</c> overrides it.</summary>
    public const int DefaultPort = 47821;

    public static string? FreeUrl()
    {
        var preferred = int.TryParse(Environment.GetEnvironmentVariable("PERSONAL_FINANCE_MCP_PORT"), out var p) && p > 0
            ? p
            : DefaultPort;
        for (var port = preferred; port < preferred + 10; port++)
        {
            try
            {
                using var probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                return $"http://127.0.0.1:{port}";
            }
            catch (SocketException)
            {
                // Taken: try the next one.
            }
        }

        return null;
    }

    /// <summary>Smoke test: the MCP server is up (its health check answers) within half a minute.</summary>
    public static async Task<bool> AnswersAsync(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                if ((await http.GetAsync($"{url}/healthz")).IsSuccessStatusCode)
                {
                    Console.WriteLine($"MCP OK: {url}/mcp");
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(1000);
        }

        Console.Error.WriteLine("MCP server did not answer.");
        return false;
    }

    public static void Start(string url, string apiUrl)
    {
        var entry = typeof(mcp::Host.Mcp.GatewayClient).Assembly.EntryPoint
                    ?? throw new InvalidOperationException("The MCP server has no entry point.");
        // Command-line settings win over the API's environment variables (ASPNETCORE_URLS) in the same process.
        string[] settings = ["--urls", url, "--Gateway:BaseUrl", apiUrl];
        _ = Task.Run(async () =>
        {
            try
            {
                if (entry.Invoke(null, [settings]) is Task task)
                {
                    await task;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"MCP server stopped: {ex.GetBaseException().Message}");
            }
        });
    }
}
