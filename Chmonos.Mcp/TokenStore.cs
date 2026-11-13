using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Chmonos.Mcp;

/// <summary>控えの1行。保存先と使い手の名前ごとに1つ。</summary>
internal sealed record TokenEntry(string Store, string Client, string Token, DateTimeOffset SavedAt);

internal sealed record TokenFile(int Version, List<TokenEntry> Tokens);

/// <summary>
/// 許しで受け取ったトークンの控え（既定 <c>%LOCALAPPDATA%\Chmonos.Shared\mcp-tokens.json</c>。docs/spec/mcp.md）。
///
/// **保存先ごと・使い手の名前ごと**に持つ：Chmonos の許しは保存先の <c>api-clients.json</c> にあり、保存先が違えばトークンも違う。
/// 使い手（Claude Code・Claude Desktop…）は Chmonos の設定の「つないでいるソフト」に別々に並び、別々に取り消せる。
/// 置き場を保存先の外にしたのは、通訳が保存先に書かないため（書き出し・引越しに混ざらない。<c>api.json</c> と同じく自分の Windows ユーザーのフォルダ）。
///
/// 通訳は使い手のセッションごとに1本ずつ動き、同じファイルを書き合うので、読んで当てて書く間を名前付きの錠で1本にする。
/// 平文で置くのは <c>api.json</c> と同じ（同じ Windows ユーザーで動く道具は保存先も読める。守れる範囲は api-clients.md「なぜ」）
/// </summary>
internal sealed class TokenStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Path => path;

    public string? Get(string store, string client)
        => Read().Tokens.FirstOrDefault(entry => Same(entry, store, client))?.Token;

    public void Save(string store, string client, string token, DateTimeOffset now)
        => Change(tokens =>
        {
            tokens.RemoveAll(entry => Same(entry, store, client));
            tokens.Add(new TokenEntry(InstanceFinder.Normalize(store), client, token, now));
        });

    /// <summary>使えなかったトークンを捨てる。同じ行に別の値（ほかの通訳が後から受け取った物）があれば残す。</summary>
    public void Forget(string store, string client, string token)
        => Change(tokens => tokens.RemoveAll(entry => Same(entry, store, client) && entry.Token == token));

    private static bool Same(TokenEntry entry, string store, string client)
        => string.Equals(InstanceFinder.Normalize(entry.Store), InstanceFinder.Normalize(store), StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.Client, client, StringComparison.Ordinal);

    private TokenFile Read()
    {
        try
        {
            if (!File.Exists(path))
            {
                return new TokenFile(1, []);
            }

            var file = JsonSerializer.Deserialize<TokenFile>(File.ReadAllBytes(path), Options);
            // 手で書き換えた控えの、ヘッダに載らない字（ASCII の外・空白）を含むトークンは無かった物にする（送る所で投げて、つながらない扱いになる）
            return new TokenFile(1, (file?.Tokens ?? [])
                .Where(entry => entry is { Store: not null, Client: not null, Token: { Length: > 0 } token } && token.All(c => c is > ' ' and < (char)0x7F))
                .ToList());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 壊れた控えは空として扱う（求め直せば作り直る）。控えを読めないことで通訳を止めない
            return new TokenFile(1, []);
        }
    }

    private void Change(Action<List<TokenEntry>> change)
    {
        using var mutex = new Mutex(false, MutexName(path));
        var owned = false;
        try
        {
            try
            {
                // 書き合うのは数行の書き換えなので、5秒待って取れなければ錠なしで進める（控えは無くても求め直せる）
                owned = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            var file = Read();
            change(file.Tokens);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(file, Options));
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            if (owned)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    /// <summary>錠の名前は控えの場所から作る（試験の一時フォルダの控えどうしが、本物の控えの錠を待たないように）。</summary>
    private static string MutexName(string path)
        => @"Local\Chmonos.Mcp.Tokens." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..16];
}
