using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 検索画面：未読の「商品の更新」の知らせ（ユーザ判断 2026-10-02「検索にも出しましょう」「検索モジュールにも更新ありをつけます」
/// 「商品カード、リストの右クリックメニューにも既読にする機能を入れましょう」）。
///
/// 数え方はショップの画面の「更新あり」と同じ（要確認に未読で、片付けていない <see cref="NotificationKind.ItemUpdated"/> がある商品）。
/// カードの札・条件「更新あり」・右クリックの「既読にする」の3つが、この表（商品ID → 知らせのID）を見る。
/// 知らせのファイルは読むだけなので画面から直に読み（書き込みは <see cref="UiCommand.MarkNotificationsRead"/> を通す）、
/// ファイルの更新時刻と大きさが前と同じなら読み直さない（ナビの数え直しのたびに呼ばれるので、取り込み中は1秒に1回ほど来る）。
/// </summary>
public sealed partial class SearchViewModel
{
    private IReadOnlyDictionary<string, UnreadUpdate> _unreadUpdates = new Dictionary<string, UnreadUpdate>();
    private (DateTime WrittenUtc, long Length) _unreadStamp;
    private bool _readingUnread;

    /// <summary>
    /// 検索の外（フォルダビューの右の欄）で作った、札を出すカード。知らせが変わったら札を合わせ直す。
    /// 画面を離れたカードを残し続けないよう、弱い参照で持つ
    /// </summary>
    private readonly List<WeakReference<ItemCardViewModel>> _outsideUpdateCards = [];

    /// <summary>未読の更新がある商品か（条件「更新あり」とカードの札）。</summary>
    internal bool HasUnreadUpdate(string itemId) => _unreadUpdates.ContainsKey(itemId);

    /// <summary>
    /// 商品ごとの未読の更新の知らせのID（既読にするとき）と、知らせが含む変化の種類（条件「更新通知あり」の種類。ユーザ判断 2026-10-06）。
    /// 種類は知らせのファイルを読んだときに1回だけ見分ける（絞り込みのたびに差を読み直さない）
    /// </summary>
    internal sealed record UnreadUpdate(IReadOnlyList<string> Ids, Core.Services.BoothChangeKind Kinds);

    /// <summary>未読の更新の知らせがあれば、その知らせが含む変化の種類（無ければ None）。</summary>
    internal Core.Services.BoothChangeKind UnreadUpdateKinds(string itemId)
        => _unreadUpdates.TryGetValue(itemId, out var unread) ? unread.Kinds : Core.Services.BoothChangeKind.None;

    /// <summary>知らせのファイルから、商品ごとの未読の更新の知らせを引く表。</summary>
    internal static Dictionary<string, UnreadUpdate> UnreadUpdatesOf(IEnumerable<NotificationRecord> records)
        => records
            .Where(record => !record.IsRead && !record.IsResolved && record.Kind == NotificationKind.ItemUpdated && record.ItemId is not null)
            .GroupBy(record => record.ItemId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new UnreadUpdate(
                    group.Select(record => record.Id).ToList(),
                    group.Aggregate(Core.Services.BoothChangeKind.None, (kinds, record) => kinds | Core.Services.BoothChanges.KindsOf(record))),
                StringComparer.Ordinal);

    private (DateTime WrittenUtc, long Length) NotificationsStamp()
    {
        var file = new System.IO.FileInfo(_services.Paths.NotificationsFile);
        return file.Exists ? (file.LastWriteTimeUtc, file.Length) : (DateTime.MinValue, -1);
    }

    /// <summary>
    /// 知らせのファイルが変わっていたら読み直す（主画面がナビの数を数え直すたびに呼ぶ）。
    /// 読むのは裏で、当てるのは画面のスレッド。読んでいる途中に来た分は、読み終えてから確かめ直す
    /// </summary>
    public void NoteNotificationsMaybeChanged()
    {
        if (_readingUnread || NotificationsStamp() == _unreadStamp)
        {
            return;
        }

        _readingUnread = true;
        Task.Run(() =>
            {
                var stamp = NotificationsStamp();
                return (stamp, UnreadUpdatesOf(_services.Notifications.Load()));
            })
            .ContinueWith(task => RunOnUiThread(() =>
            {
                _readingUnread = false;
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    var (stamp, unread) = task.Result;
                    _unreadStamp = stamp;
                    SetUnreadUpdates(unread);
                }
                else if (task.Exception is { } exception)
                {
                    // 読めなければ札は前のまま（次に変わったときにまた読む）
                    Core.Diagnostics.AppLog.Error("検索：未読の更新の知らせを読む", exception.GetBaseException());
                }
            }), TaskScheduler.Default);
    }

    /// <summary>読み直しの裏で読んでおく（商品と同じ時点の知らせで札を出す）。</summary>
    private ((DateTime, long) Stamp, Dictionary<string, UnreadUpdate> Unread) ReadUnreadUpdates()
    {
        var stamp = NotificationsStamp();
        return (stamp, UnreadUpdatesOf(_services.Notifications.Load()));
    }

    private void SetUnreadUpdates(IReadOnlyDictionary<string, UnreadUpdate> unread)
    {
        // 種類が変わっても（同じ商品に価格の知らせが重なった）結果が変わる
        var changed = unread.Count != _unreadUpdates.Count
                      || unread.Any(pair => !_unreadUpdates.TryGetValue(pair.Key, out var before) || before.Kinds != pair.Value.Kinds);
        _unreadUpdates = unread;
        ApplyUpdatesToCards();

        // 条件「更新通知あり」を置いているか、検索欄に has:update などを書いていれば、結果と選択肢の件数が変わる
        if (changed && (Modules.Any(module => module.Kind == SearchModuleKind.Updated) || Core.Services.SearchQuery.DependsOnFacts(_queryNode)))
        {
            ApplyFilters();
        }
    }

    /// <summary>検索のカードと、外で作った札を出すカードの札を、今の表に合わせる。</summary>
    private void ApplyUpdatesToCards()
    {
        foreach (var card in _cards.Values)
        {
            ApplyUpdate(card);
        }

        _outsideUpdateCards.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _outsideUpdateCards)
        {
            if (reference.TryGetTarget(out var card))
            {
                ApplyUpdate(card);
            }
        }
    }

    /// <summary>カード1枚の札と、押したとき・右クリックの「既読にする」の道を入れる。</summary>
    private void ApplyUpdate(ItemCardViewModel card)
    {
        var has = _unreadUpdates.ContainsKey(card.Item.Id);
        card.HasUpdate = has;
        card.ShowUpdateCommand = has && _main is { } main ? new RelayCommand(() => main.ShowInboxFor(card.Item.Id)) : null;
        card.MarkUpdateReadCommand = has ? new RelayCommand(() => MarkUpdatesReadAsync(card).Forget()) : null;
    }

    /// <summary>
    /// 検索の外で作るカード（フォルダビューの右の欄）にも札を出す。<see cref="CreateCard"/> と同じ作り方に、札と「既読にする」を足す。
    /// 改変・アバターなどのほかの画面のカードには出さない（ユーザ判断で決めた置き場は検索・フォルダ・ショップ）
    /// </summary>
    public ItemCardViewModel CreateCardWithUpdates(ItemRecord item)
    {
        var card = CreateCard(item);
        ApplyUpdate(card);
        _outsideUpdateCards.Add(new WeakReference<ItemCardViewModel>(card));
        return card;
    }

    /// <summary>
    /// 右クリックの「既読にする」：この商品の未読の更新の知らせを既読にする（商品ページの「既読にする」・要確認と同じ命令）。
    /// 保存を待ってから札を下ろし、ナビの要確認の数を数え直す。ショップの画面のカードからも呼ぶ（<paramref name="card"/> の札を下ろす）
    /// </summary>
    public Task MarkUpdatesReadAsync(ItemCardViewModel card) => MarkUpdatesReadAsync([card]);

    /// <summary>既読にする命令を出した回数。まとめて既読にしたときに1回の命令で済ませたかを、試験で見る（命令の入口には数える口が無い）。</summary>
    internal int MarkReadCommandCount { get; private set; }

    /// <summary>
    /// 選んだカードの「既読にする」（ユーザ判断 2026-10-02「4は入れましょう」）。渡したカードの商品の未読の更新の知らせを、
    /// **1回の命令でまとめて**既読にする。1件ずつ命令にすると、知らせのファイルを件数の回数だけ書き直し、
    /// 途中で失敗したときに半分だけ既読になる。札・条件「更新あり」の結果・ナビの要確認の数は1件のときと同じ道で合わせる
    /// </summary>
    public async Task MarkUpdatesReadAsync(IReadOnlyList<ItemCardViewModel> cards)
    {
        if (cards.Count == 0)
        {
            return;
        }

        // 表に無い商品（ショップの画面のカードは、検索がまだ知らせを読んでいないことがある）は、ファイルを1回だけ読んで引く
        Dictionary<string, UnreadUpdate>? loaded = null;
        if (cards.Any(card => !_unreadUpdates.ContainsKey(card.Item.Id)))
        {
            loaded = UnreadUpdatesOf(await Task.Run(_services.Notifications.Load));
        }

        var ids = cards
            .Select(card => card.Item.Id)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(itemId => _unreadUpdates.TryGetValue(itemId, out var known)
                ? known.Ids
                : loaded?.GetValueOrDefault(itemId)?.Ids ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count > 0)
        {
            MarkReadCommandCount++;
        }

        if (ids.Count > 0
            && await _services.Commands.ExecuteAsync(new UiCommand.MarkNotificationsRead(ids)) is CommandResult.Failed failed)
        {
            Services.Notice.Show(failed.Message, "既読にする",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        foreach (var card in cards)
        {
            card.HasUpdate = false;
        }

        if (ids.Count == 0)
        {
            return;
        }

        var rest = new Dictionary<string, UnreadUpdate>(_unreadUpdates, StringComparer.Ordinal);
        foreach (var card in cards)
        {
            rest.Remove(card.Item.Id);
        }

        _unreadStamp = NotificationsStamp();
        SetUnreadUpdates(rest);
        _main?.RefreshBadges();
    }
}
