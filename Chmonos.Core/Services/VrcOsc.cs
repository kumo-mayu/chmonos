using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Chmonos.Core.Services;

/// <summary>
/// VRChat の OSC へ「このアバターに着替える」を送る（ユーザ指示 2026-09-19：改変に blueprint ID を持たせ、着替えに使う）。
///
/// VRChat は OSC を有効にすると、手元の UDP 9000 番で受ける。<c>/avatar/change</c> に blueprint ID（<c>avtr_…</c>）の文字列を送ると、
/// そのアバターに着替える（自分がアップロードしたか、お気に入りなど着られるアバターに限る。着られなければ VRChat 側で何も起きない）。
///
/// **送るだけで、着替えられたかは分からない。**OSC は送りっぱなしの UDP で、VRChat が起動していなくても送信は失敗しない。
/// 画面では「送った」とだけ言い、確かめ方（VRChat で OSC を有効にする）を添える。
/// 外部のライブラリは使わない——必要なのは1種類の短い通知だけで、形は OSC 1.0 の仕様どおり組める
/// </summary>
public static partial class VrcOsc
{
    /// <summary>VRChat が OSC を受ける既定の場所（VRChat の設定で変えられるが、既定から変える人は少ない）。</summary>
    public const string DefaultHost = "127.0.0.1";

    public const int DefaultPort = 9000;

    public const string AvatarChangeAddress = "/avatar/change";

    /// <summary>
    /// blueprint ID の形か（<c>avtr_</c> ＋ GUID）。入力の確かめに使う。形が違っても保存は止めない（書き間違いを知らせるだけ）
    /// </summary>
    public static bool LooksLikeAvatarId(string? text)
        => text is not null && AvatarIdPattern().IsMatch(text.Trim());

    [GeneratedRegex(@"^avtr_[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex AvatarIdPattern();

    /// <summary>
    /// OSC の1通（文字列を1つ持つ）を組む。アドレス・型の印（<c>,s</c>）・値を、それぞれ終わりの 0 を含めて4バイト区切りに揃える
    /// </summary>
    public static byte[] StringMessage(string address, string value)
    {
        var bytes = new List<byte>();
        AppendPadded(bytes, address);
        AppendPadded(bytes, ",s");
        AppendPadded(bytes, value);
        return [.. bytes];
    }

    private static void AppendPadded(List<byte> bytes, string text)
    {
        bytes.AddRange(Encoding.UTF8.GetBytes(text));

        // 終わりの 0 を最低1つ、そのうえで4の倍数まで 0 で埋める
        var padding = 4 - (bytes.Count % 4);
        bytes.AddRange(new byte[padding]);
    }

    /// <summary>着替えを送る。送れなかった（ネットワークの口が使えない）ときだけ理由を返す。null なら送った。</summary>
    public static async Task<string?> SendAvatarChangeAsync(
        string blueprintId, string host = DefaultHost, int port = DefaultPort, CancellationToken cancellationToken = default)
    {
        var message = StringMessage(AvatarChangeAddress, blueprintId.Trim());
        try
        {
            using var client = new UdpClient();
            await client.SendAsync(message, host, port, cancellationToken);
            return null;
        }
        catch (SocketException exception)
        {
            // 理由は改変の画面にそのまま出る。.NET の文ではなく、次に確かめることを返す
            Diagnostics.AppLog.Error("VRChat へ着替えを送る", exception);
            return "VRChatへ送れませんでした。VRChatを起動し、OSCを有効にしているか確かめてください。";
        }
    }
}
