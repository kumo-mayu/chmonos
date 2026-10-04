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
    public static Task<IReadOnlyList<FileSighting>> LookAsync(IReadOnlyList<LocalFileRecord> files)
        => Task.Run(() => Look(files));

    /// <summary>画面のスレッドの外で呼ぶ。ドライブごとに1回だけつながっているかを見る（<see cref="FilePresenceProbe"/>）。</summary>
    public static IReadOnlyList<FileSighting> Look(IReadOnlyList<LocalFileRecord> files)
    {
        var probe = new FilePresenceProbe();
        return [.. files.Select(file => new FileSighting(file.Hash, file.Paths, probe.Of(file.Paths)))];
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

    /// <summary>商品のファイルを見て、食い違えば書く（開く・送るが「無かった」ときの道）。</summary>
    public static async Task<ItemRecord?> LookAndNoteAsync(
        AppServiceContainer services, ItemRecord item, IReadOnlyList<LocalFileRecord> files)
        => files.Count == 0 ? null : await NoteAsync(services, item, await LookAsync(files));
}
