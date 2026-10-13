using System.IO;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>
/// 外から使う API の「エクスプローラで開く」（段4。ユーザ判断 2026-10-13：API は資産を使う物を出す B）。
/// 開き方は画面と同じ <see cref="Shell.TryRevealAsync"/>（フォルダは中を、zip は中を、ほかのファイルは選んだ状態で）。
///
/// 開く場所の選び方は「Unity へ送る」と同じ形：指定があればそれ（その商品の場所に限る）、無ければ場所が1つだけのときだけそれ。
/// 場所はファイル（外した物を除く）とフォルダ。記録の場所と、ドライブ文字が変わった後の今の場所のどちらで指してもよい
/// </summary>
internal static class ApiReveal
{
    /// <summary>結果の種類（API が答えに写す）。</summary>
    public const string Opened = "opened";
    public const string NoLocation = "noLocation";
    public const string Ambiguous = "ambiguous";
    public const string UnknownPath = "unknownPath";
    public const string Missing = "missing";

    /// <summary>商品の場所（記録の場所と今の場所）。</summary>
    internal static IReadOnlyList<(string Recorded, string Current)> Locations(ItemRecord item, VolumeTable volumes)
        => item.Local.LocalFiles
            .Where(file => !file.Detached)
            .SelectMany(file => file.Paths.Select(path => (path, volumes.CurrentOf(file, path))))
            .Concat(item.Local.LocalFolders.Select(folder => (folder.Path, volumes.CurrentOf(folder))))
            .ToList();

    private static bool ExistsOnDisk(string location) => File.Exists(location) || Directory.Exists(location);

    /// <param name="exists">場所が在るか（試験が差し替える）。</param>
    public static async Task<string> RevealAsync(
        ItemRecord item, string? path, VolumeTable volumes, Func<string, Task<bool>>? reveal = null, Func<string, bool>? exists = null)
    {
        var locations = Locations(item, volumes);
        string target;
        if (path is { Length: > 0 })
        {
            var wanted = path.Trim();
            var found = locations.FirstOrDefault(location =>
                string.Equals(location.Recorded, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(location.Current, wanted, StringComparison.OrdinalIgnoreCase));
            if (found == default)
            {
                return UnknownPath;
            }

            target = found.Current;
        }
        else
        {
            var distinct = locations.Select(location => location.Current).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count == 0)
            {
                return NoLocation;
            }

            if (distinct.Count > 1)
            {
                return Ambiguous;
            }

            target = distinct[0];
        }

        // 無ければ開かない。画面の開き方は、無いと親フォルダを開いて「開けた」になるので、道具が「無い」と知れなかった（点検38）。
        // 画面は押した人に近くを見せて助けるが、API は答えで伝え、どうするかは呼んだ側に任せる
        exists ??= ExistsOnDisk;
        if (!exists(target))
        {
            return Missing;
        }

        return await (reveal ?? Shell.TryRevealAsync)(target) ? Opened : Missing;
    }
}
