using System.IO;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 使おうとして見たファイルの在る・無いを、記録の「見つからなくなった日時」へ渡す（ユーザ判断 2026-10-04）。
/// 商品ページ・開く・一時的に展開する・Unityへ送るが、どれも同じここを通る。
/// </summary>
/// <remarks>
/// 前は商品ページだけがその場でディスクを見て「見つかりません」を出し、記録は書かなかったので、
/// 同じ商品がカードの印・検索の条件・統計に出ず、画面どうしで食い違っていた。
/// 書くのは <see cref="UiCommand.NoteFilePresence"/>（画面からの書き込みは命令1本）。記録と同じなら命令も出さない
/// （商品ページを開くたびに書き込みの門を叩かない）。
/// </remarks>
internal static class FilePresenceNotes
{
    /// <summary>ファイルを今見る。ディスクを見るので画面のスレッドの外で（落ちたネットワークドライブで待たされないように）。</summary>
    /// <param name="volumes">ドライブ文字の読み替え（<see cref="VolumeTable.Current"/>）と、控えた文字に別のディスクが来ているかの見分け。</param>
    public static Task<IReadOnlyList<FileSighting>> LookAsync(IReadOnlyList<LocalFileRecord> files, VolumeTable volumes)
        => Task.Run(() => Look(files, volumes.Current, volumes.Snapshot()));

    /// <summary>
    /// 画面のスレッドの外で呼ぶ。ドライブごとに1回だけつながっているかを見る（<see cref="FilePresenceProbe"/>）。
    /// **見るのは読み替えた後の場所**（フォルダビュー・検索と同じ答えにする。読み替えないと、ドライブ文字が変わった商品だけ
    /// 「取り外しているドライブ」と出て開けなかった。file-lifecycle.md「気になった所」2）。
    /// 返す見た結果の場所は記録のまま（書く側が、見てから書くまでに場所が変わっていないかを記録と突き合わせるため）。
    /// 控えた文字に別のディスクが来ていれば「取り外しているドライブ」と同じに見る（日時を付けない。2026-10-05・点検の2）。
    /// 比べるのは記録の文字の控えと、見る文字の今の番号（読み替えた先は控えた番号のディスクなので、別とは見ない）。
    /// </summary>
    public static IReadOnlyList<FileSighting> Look(
        IReadOnlyList<LocalFileRecord> files, Func<string, string> remap, VolumeSnapshot? volumes = null)
    {
        var probe = new FilePresenceProbe(volumes: volumes);
        return [.. files.Select(file => new FileSighting(file.Hash, file.Paths, probe.Of(file.Paths, remap)))];
    }

    /// <summary>
    /// 見た結果が記録と食い違えば書く。書いたら読み直した商品を返す（呼ぶ側が検索の写しへ知らせる）。書かなければ null。
    /// </summary>
    /// <remarks>
    /// 書けなくても使う操作は止めない（記録は印のためで、開く・送るの成否には関わらない）。失敗はログにだけ残す。
    /// </remarks>
    public static async Task<ItemRecord?> NoteAsync(
        AppServiceContainer services, ItemRecord item, IReadOnlyList<FileSighting> sightings)
    {
        if (sightings.Count == 0 || !FileMissingMarks.Differs(item.Local.LocalFiles, sightings))
        {
            return null;
        }

        try
        {
            if (await services.Commands.ExecuteAsync(new UiCommand.NoteFilePresence(item.Id, sightings))
                is not CommandResult.ItemSaved)
            {
                return null;
            }

            return await services.Store.Items.LoadAsync(item.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Core.Diagnostics.AppLog.Warn("見つからないファイルの記録", exception.Message);
            return null;
        }
    }

    /// <summary>
    /// Unity へ送った列のうち、**本当に送れなかった**物の zip を見直して記録へ（検索の複数選択・改変の画面・「Unityで選択」の道。
    /// 商品ページの1件の送り方と同じく、「無い」を記録に残す。前は商品ページと検索のカードの1件だけが書き、
    /// 同じ zip が無いのに、こちらの道で送ると印にも条件にも出なかった。file-lifecycle.md「気になった所」17）。
    /// 人が止めた分は、zip の有無と関わらないので見ない。見て在れば何も書かない（理由の文では見分けない）。
    /// </summary>
    /// <param name="sent">送った列（どの商品の包みか）。</param>
    /// <param name="changed">書いた商品を読み直した物を受ける（検索の写しへ知らせる）。</param>
    public static async Task NoteFailedSendsAsync(
        AppServiceContainer services,
        IEnumerable<(string ItemId, Core.Services.UnityPackageEntry Package)> sent,
        IReadOnlyList<Services.UnityQueueOutcome> outcomes,
        Action<ItemRecord>? changed)
    {
        var failed = outcomes
            .Where(outcome => !outcome.Opened && !Services.UnityImportQueue.IsStopped(outcome.Problem))
            .Select(outcome => outcome.Package)
            .ToHashSet();
        if (failed.Count == 0)
        {
            return;
        }

        foreach (var group in sent.Where(entry => failed.Contains(entry.Package)).GroupBy(entry => entry.ItemId, StringComparer.Ordinal))
        {
            var item = await services.Store.Items.LoadAsync(group.Key);
            if (item is null)
            {
                continue;
            }

            var packages = group.Select(entry => entry.Package).ToList();
            var files = item.Local.LocalFiles
                .Where(file => packages.Any(package =>
                    (package.ZipHash is not null && string.Equals(file.Hash, package.ZipHash, StringComparison.OrdinalIgnoreCase))
                    || file.Paths.Contains(package.ZipPath, StringComparer.OrdinalIgnoreCase)))
                .ToList();
            if (await LookAndNoteAsync(services, item, files) is { } reloaded)
            {
                changed?.Invoke(reloaded);
            }
        }
    }

    /// <summary>商品のファイルを見て、食い違えば書く（開く・送るが「無かった」ときの道）。</summary>
    public static async Task<ItemRecord?> LookAndNoteAsync(
        AppServiceContainer services, ItemRecord item, IReadOnlyList<LocalFileRecord> files)
        => files.Count == 0 ? null : await NoteAsync(services, item, await LookAsync(files, services.Volumes));
}
