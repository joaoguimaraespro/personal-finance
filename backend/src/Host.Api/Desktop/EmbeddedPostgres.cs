using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Npgsql;

namespace Host.Api.Desktop;

/// <summary>
/// The desktop app's own PostgreSQL: portable binaries shipped with the installer, a data directory in the user's
/// profile, listening on 127.0.0.1 only on a free port, with a generated password (SCRAM) kept next to the data.
/// Started before the API and stopped with it; nothing is installed system-wide.
/// </summary>
internal sealed class EmbeddedPostgres
{
    private const string Database = "finance";
    private const string User = "finance";

    private readonly string _bin;
    private readonly string _data;
    private readonly string _log;

    private EmbeddedPostgres(string binDir, string dataDir)
    {
        _bin = binDir;
        _data = Path.Combine(dataDir, "postgres");
        _log = Path.Combine(dataDir, "postgres.log");
    }

    public string ConnectionString { get; private set; } = "";

    /// <summary>Creates the cluster on first run, starts it and makes sure the app database exists.</summary>
    public static async Task<EmbeddedPostgres> StartAsync(string binDir, string dataDir, CancellationToken ct)
    {
        Directory.CreateDirectory(dataDir);
        var pg = new EmbeddedPostgres(binDir, dataDir);
        var password = await pg.EnsureClusterAsync(dataDir, ct);
        var port = FreePort();
        // A previous run that crashed may have left the server up: stop it so it restarts on the new port.
        await pg.RunAsync("pg_ctl", ["-D", pg._data, "-m", "fast", "-w", "stop"], ct, allowFailure: true);
        await pg.RunAsync("pg_ctl",
            ["-D", pg._data, "-l", pg._log, "-w", "-t", "60", "-o",
                $"-p {port} -c listen_addresses=127.0.0.1 -c unix_socket_directories=''", "start"], ct);

        var admin = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = port, Username = User, Password = password, Database = "postgres",
        };
        await using (var conn = new NpgsqlConnection(admin.ConnectionString))
        {
            await conn.OpenAsync(ct);
            await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", conn);
            exists.Parameters.AddWithValue("n", Database);
            if (await exists.ExecuteScalarAsync(ct) is null)
            {
                await using var create = new NpgsqlCommand($"CREATE DATABASE {Database}", conn);
                await create.ExecuteNonQueryAsync(ct);
            }
        }

        admin.Database = Database;
        pg.ConnectionString = admin.ConnectionString;
        return pg;
    }

    /// <summary>Stops the server (fast shutdown: open transactions roll back, data is safe).</summary>
    public void Stop() =>
        RunAsync("pg_ctl", ["-D", _data, "-m", "fast", "-w", "stop"], CancellationToken.None, allowFailure: true)
            .GetAwaiter().GetResult();

    private async Task<string> EnsureClusterAsync(string dataDir, CancellationToken ct)
    {
        var passwordFile = Path.Combine(dataDir, "postgres.secret");
        if (File.Exists(Path.Combine(_data, "PG_VERSION")))
        {
            return (await File.ReadAllTextAsync(passwordFile, ct)).Trim();
        }

        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await File.WriteAllTextAsync(passwordFile, password, ct);
        RestrictToOwner(passwordFile);
        var pwFile = Path.Combine(dataDir, "initdb.tmp");
        await File.WriteAllTextAsync(pwFile, password, ct);
        try
        {
            await RunAsync("initdb",
                ["-D", _data, "-U", User, "--pwfile", pwFile, "--auth=scram-sha-256", "-E", "UTF8", "--locale=C"],
                ct);
        }
        finally
        {
            File.Delete(pwFile);
        }

        return password;
    }

    private async Task RunAsync(string tool, string[] args, CancellationToken ct, bool allowFailure = false)
    {
        var exe = Path.Combine(_bin, OperatingSystem.IsWindows() ? tool + ".exe" : tool);
        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            start.ArgumentList.Add(a);
        }

        // The server inherits the environment: keep it predictable whatever the user's locale.
        start.Environment["LC_ALL"] = "C";
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {tool}.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException(
                $"{tool} failed ({process.ExitCode}): {await error}{await output}".Trim());
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
