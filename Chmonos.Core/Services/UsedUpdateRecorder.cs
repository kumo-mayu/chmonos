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
    /// 未読の「中身の更新」の知らせから、控えの無い商品に控えを作る（v1.3.0 に上げたとき・ユーザ判断 2026-11-09）。起動時の見回りで呼ぶ。
    /// 印は持たず、毎回「知らせより新しい控えが無い商品」だけを見るので、何回走っても同じになる
    /// （作った控えは知らせより新しい。「確認した」を押した控えも残るので作り直さない。既読にした知らせは見ない）。
    /// 検出し直しはしない（BOOTH に問い合わせない見回りなので）。見分けは今の対応アバターの名前で行う。
    /// 控えるファイルは今持っている物（上げる前に新しい版を取り込んでいても、それは分からない）。
    /// </summary>
    /// <returns>控えを書いた商品。</returns>
    public async Task<IReadOnlyList<string>> FillFromNotificationsAsync(CancellationToken cancellationToken)
    {
        var notes = (await _store.Notifications.LoadAsync(cancellationToken))
            .Where(note => note.Kind == NotificationKind.ItemUpdated && note.IsStrong && !note.IsRead && !note.IsResolved && note.ItemId is not null)
            .ToList();

        var written = new List<string>();
        foreach (var note in notes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _store.Items.LoadAsync(note.ItemId!, cancellationToken) is not { } item)
            {
                continue;
            }

            var noted = note.UpdatedAt ?? note.CreatedAt;
            if (item.Local.ContentUpdate is { } existing && existing.FoundAt >= noted)
            {
                continue;
            }

            if (!UsedUpdateKind.ShouldPrompt(note.Diffs ?? [], await AvatarNamesAsync(item, cancellationToken)))
            {
                continue;
            }

            var now = DateTimeOffset.Now;
            if (await _store.Items.ChangeLocalAsync(
                    item.Id,
                    local => local.ContentUpdate is { } current && current.FoundAt >= noted
                        ? local
                        : local with { ContentUpdate = UsedUpdates.Found(local, now) },
                    LocalOwners.ContentUpdate,
                    cancellationToken))
            {
                written.Add(item.Id);
            }
        }

        return written;
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
