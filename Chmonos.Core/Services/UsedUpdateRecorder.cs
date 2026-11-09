using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 取り直しで「中身の更新」を見つけたら、商品に控え（<see cref="ContentUpdate"/>）を書く（<c>docs/spec/used-updates.md</c>）。
/// ⑦と人が押した取り直しの両方が、<see cref="ItemService.RefreshAsync"/> の終わりでここを呼ぶ。
///
/// 通知（<see cref="BoothChangeNotes"/>）とは別に書く：商品ごとの「更新を知らせる」を切っていても、使っていれば促す（ユーザ判断 2026-11-09）。
/// </summary>
public sealed class UsedUpdateRecorder
{
    private readonly DataStore _store;
    private readonly Func<CancellationToken, Task> _detect;

    /// <param name="detect">
    /// 対応アバターの検出（全商品・落ち着くまで）。終わるまで待つ物を渡す（任せてすぐ返る依頼では、見分ける時にまだ古い）。
    /// </param>
    public UsedUpdateRecorder(DataStore store, Func<CancellationToken, Task> detect)
    {
        _store = store;
        _detect = detect;
    }

    /// <summary>
    /// 取り直した商品の前と後を見て、促す更新なら控えを書く。更新履歴の見出しが動いていなければ何もしない。
    /// </summary>
    public async Task NoteAsync(ItemRecord before, BoothBlock after, CancellationToken cancellationToken)
    {
        var diffs = BoothChanges.Describe(before.Booth, after);
        if (!BoothChanges.HasStrongChange(diffs))
        {
            return;
        }

        // 説明文・バリエーション・タグが変わったなら、見分ける前に対応アバターを検出し直す（ユーザ判断 2026-11-06）。
        // 検出は取り込み・ボタン・90日ごとにしか走らないので、足されたアバターが商品の対応アバターに入っていない。
        // 入っていれば「・〇〇」のように名前だけを並べた行も追加と見分けられ、検索の対応アバターもその分新しくなる。
        // 更新履歴の見出しが動いた時点で説明文は変わっているので、ここに来れば必ず検出し直す
        await _detect(cancellationToken);

        if (await _store.Items.LoadAsync(before.Id, cancellationToken) is not { } item)
        {
            return;
        }

        var names = await AvatarNamesAsync(item, cancellationToken);
        if (!UsedUpdateKind.ShouldPrompt(diffs, names))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        await _store.Items.ChangeLocalAsync(
            item.Id,
            local => local with { ContentUpdate = UsedUpdates.Found(local, now) },
            LocalOwners.ContentUpdate,
            cancellationToken);
    }

    /// <summary>
    /// 商品の今の対応アバターと共通素体の名前（表示名・BOOTH の名前・別名）。更新履歴の行に名前だけが並ぶ形を見分けるのに使う。
    /// </summary>
    internal async Task<IReadOnlyList<string>> AvatarNamesAsync(ItemRecord item, CancellationToken cancellationToken)
    {
        var registry = await _store.Avatars.LoadAsync(cancellationToken);
        // 登録簿は手で直せる JSON なので、同じ ID・名前が2つあっても止まらないよう先の物を使う
        var entries = registry.Entries.DistinctBy(entry => entry.ItemId).ToDictionary(entry => entry.ItemId, StringComparer.Ordinal);
        var bases = registry.BaseGroups.DistinctBy(group => group.Name).ToDictionary(group => group.Name, StringComparer.Ordinal);

        var names = new List<string>();
        foreach (var link in item.Local.Avatars)
        {
            AddIf(names, link.Name);
            if (entries.TryGetValue(link.AvatarItemId, out var entry))
            {
                AddIf(names, entry.DisplayName);
                AddIf(names, entry.BoothName);
                names.AddRange(entry.Aliases.Select(alias => alias.Text));
            }
        }

        foreach (var link in item.Local.AvatarBases)
        {
            AddIf(names, link.BaseName);
            if (bases.TryGetValue(link.BaseName, out var group))
            {
                names.AddRange(group.Aliases.Select(alias => alias.Text));
            }
        }

        return names;
    }

    private static void AddIf(List<string> names, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name);
        }
    }
}
