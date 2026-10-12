using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Chmonos.App.Services;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>外から使う API への知らせ（取り込みの始まりと終わり。docs/spec/local-api.md「同期」）。</summary>
public sealed class ApiNotifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-notifier-tests", Guid.NewGuid().ToString("N"));

    public ApiNotifierTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public async Task APIが動いていなければ何もしない()
    {
        var paths = new AppPaths(_root);
        var notifier = new ApiNotifier(paths);

        // api.json が無い
        await notifier.SendAsync("import");

        // api.json はあるが止まっている（プロセスが空）・つながらない
        JsonStore.Write(paths.ApiFile, new ApiEndpointRecord { Port = 9, Token = "token" });
        await notifier.SendAsync("import");
        JsonStore.Write(paths.ApiFile, new ApiEndpointRecord { Port = FreePort(), Token = "token", ProcessId = 1 });
        await notifier.SendAsync(null);
    }

    [Fact]
    public async Task 合言葉を付けて_始まりと終わりを送る()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var paths = new AppPaths(_root);
            JsonStore.Write(paths.ApiFile, new ApiEndpointRecord
            {
                Port = ((IPEndPoint)listener.LocalEndpoint).Port,
                Token = "test-token",
                ProcessId = Environment.ProcessId,
            });
            var notifier = new ApiNotifier(paths);

            var start = ReceiveAsync(listener);
            await notifier.SendAsync("import");
            var request = await start;
            Assert.StartsWith("POST /internal/busy ", request);
            Assert.Contains("Authorization: Bearer test-token", request);
            Assert.EndsWith("""{"state":"start","kind":"import"}""", request);

            var end = ReceiveAsync(listener);
            await notifier.SendAsync(null);
            Assert.EndsWith("""{"state":"end"}""", await end);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>1本だけ受けて、本文まで読んで 204 を返す。</summary>
    private static async Task<string> ReceiveAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var buffer = new byte[8192];
        var text = new StringBuilder();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            text.Append(Encoding.UTF8.GetString(buffer, 0, read));
            var request = text.ToString();
            var split = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (split >= 0)
            {
                var length = request.Split("\r\n").FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) is { } header
                    ? int.Parse(header["Content-Length:".Length..].Trim())
                    : 0;
                if (Encoding.UTF8.GetByteCount(request[(split + 4)..]) >= length)
                {
                    await stream.WriteAsync("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), timeout.Token);
                    return request;
                }
            }
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
