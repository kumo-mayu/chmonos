using System.Net;
using System.Net.Sockets;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Api.Tests;

/// <summary>アプリと同じプロセスで API を始める・止める（ApiServer）。名前・ID は作り物。</summary>
public sealed class ApiServerTests : IDisposable
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        try
        {
            if (File.Exists(_paths.ApiFile))
            {
                File.SetAttributes(_paths.ApiFile, FileAttributes.Normal);
            }

            Directory.Delete(_paths.Root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// 待ち受けを始めた後に api.json を書けなかったら、待ち受けを止めてポートを放す（点検36：前は誰も持たない待ち受けが残り、
    /// オフにしても止まらず、古い合言葉で答え続けた）
    /// </summary>
    [Fact]
    public async Task 記録を書けなければ_待ち受けを止めてポートを放す()
    {
        var store = new DataStore(_paths);
        await store.Items.SaveAsync(new ItemRecord { Id = "9600301", Booth = new BoothBlock { Name = "作り物の靴" } });
        var port = FreePort();
        JsonStore.Write(_paths.ApiSettingsFile, new ApiSettings { Enabled = true, Port = port });

        // 読めるが置き換えられない api.json（書き込みは置き換えで行う）
        JsonStore.Write(_paths.ApiFile, new ApiEndpointRecord { Port = port, Token = "old-token" });
        File.SetAttributes(_paths.ApiFile, FileAttributes.ReadOnly);

        await Assert.ThrowsAnyAsync<Exception>(() => ApiServer.StartAsync(store));

        var again = new TcpListener(IPAddress.Loopback, port);
        again.Start();
        again.Stop();
    }
}
