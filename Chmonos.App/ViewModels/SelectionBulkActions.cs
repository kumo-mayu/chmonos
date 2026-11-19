using System.Windows;
using Chmonos.App.Services;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 選択の帯の「お気に入り ▾」「タグ・属性 ▾」「その他 ▾」の中身（ユーザ判断 2026-11-20）。
/// 帯は検索・ショップ・フォルダの右で同じ型なので、操作は1つの物に集め、3つの画面はこれを <c>Bulk</c> として持つ
/// （同じ名前の命令を3つの画面に並べると、どれかで名前を違えても結び付けが黙って外れる）。
/// </summary>
/// <remarks>
/// 書くのは命令（<see cref="UiCommand.ChangeItems"/>）で、商品ごとに錠の中で今の値に当てる。カードの写しは古いことがあるので、
/// 押せるか・何件かの目安にだけ使い、書いた後は記録から読み直して画面へ渡す。
/// </remarks>
public sealed class SelectionBulkActions : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel? _main;
    private readonly Func<IReadOnlyList<ItemCardViewModel>> _selected;
    private readonly Action<IReadOnlyList<ItemRecord>> _applied;
    private readonly Action _refreshingChanged;

    /// <summary>
    /// 書いた後に読み直した記録。ショップ・フォルダの右のカードは作り直さないので、押せるかの目安はこちらを先に見る
    /// （例：非表示にした後も「表示に戻す」が押せないままになる）。選択が空になったら捨てる
    /// </summary>
    private readonly Dictionary<string, ItemRecord> _latest = new(StringComparer.Ordinal);

    /// <param name="selected">選んだカード（表示の並び）。</param>
    /// <param name="applied">書いた後に読み直した記録を、画面のカード・一覧へ当てる。</param>
    /// <param name="refreshingChanged">取得し直しを始めた・終えた（入れ物の画面が帯を出すかを出し直す）。</param>
    internal SelectionBulkActions(
        AppServiceContainer services,
        MainViewModel? main,
        Func<IReadOnlyList<ItemCardViewModel>> selected,
        Action<IReadOnlyList<ItemRecord>> applied,
        Action refreshingChanged)
    {
        _services = services;
        _main = main;
        _selected = selected;
        _applied = applied;
        _refreshingChanged = refreshingChanged;

        AddFavoritesCommand = Command(() => ChangeAsync(new BulkItemChange.SetFavorite(true)), () => Selected().Any(card => !card.IsFavorite));
        RemoveFavoritesCommand = Command(() => ChangeAsync(new BulkItemChange.SetFavorite(false)), () => Selected().Any(card => card.IsFavorite));
        AddTagCommand = Command(AddTagAsync, () => Selected().Count > 0);
        RemoveTagCommand = Command(RemoveTagAsync, () => Items().Any(item => item.Local.UserTags.Count > 0 || item.Local.Attributes.Count > 0));
        HideCommand = Command(() => ChangeAsync(new BulkItemChange.SetHidden(true)), () => Items().Any(item => !item.Local.IsHidden));
        UnhideCommand = Command(() => ChangeAsync(new BulkItemChange.SetHidden(false)), () => Items().Any(item => item.Local.IsHidden));
        NotifyOnCommand = Command(() => ChangeAsync(new BulkItemChange.SetNotifyOnUpdate(true)), () => Items().Any(item => !item.IsLocalOnly && !item.Local.NotifyOnUpdate));
        NotifyOffCommand = Command(() => ChangeAsync(new BulkItemChange.SetNotifyOnUpdate(false)), () => Items().Any(item => !item.IsLocalOnly && item.Local.NotifyOnUpdate));
        ConfirmAvatarsCommand = Command(() => ChangeAsync(new BulkItemChange.ConfirmAvatars()), () => Items().Any(HasWaitingAvatars));
        FindMissingCommand = Command(FindMissingAsync, () => MissingCount > 0 && _main is not null);
        RefreshFromBoothCommand = Command(RefreshFromBoothAsync, () => Items().Any(item => !item.IsLocalOnly));
        CopyIdsCommand = new RelayCommand(() => Copy(IdsText(Selected())), () => !IsBusy && Selected().Count > 0);
        CopyLinksCommand = new RelayCommand(() => Copy(LinksText(Selected())), () => !IsBusy && Selected().Any(card => card.HasBoothPage));
        StopCommand = new RelayCommand(() => _stop?.Cancel(), () => _stop is not null);
    }

    public RelayCommand AddFavoritesCommand { get; }

    public RelayCommand RemoveFavoritesCommand { get; }

    /// <summary>「タグ・属性 ▾」の「追加…」。</summary>
    public RelayCommand AddTagCommand { get; }

    /// <summary>「タグ・属性 ▾」の「削除…」。</summary>
    public RelayCommand RemoveTagCommand { get; }

    public RelayCommand HideCommand { get; }

    public RelayCommand UnhideCommand { get; }

    public RelayCommand NotifyOnCommand { get; }

    public RelayCommand NotifyOffCommand { get; }

    public RelayCommand ConfirmAvatarsCommand { get; }

    public RelayCommand FindMissingCommand { get; }

    public RelayCommand RefreshFromBoothCommand { get; }

    public RelayCommand CopyIdsCommand { get; }

    public RelayCommand CopyLinksCommand { get; }

    /// <summary>BOOTH からの取得をやめる（今の1件を終えてから止まる）。</summary>
    public RelayCommand StopCommand { get; }

    /// <summary>選んだ物のうち、見つからないファイル・フォルダを持つ商品の数。</summary>
    public int MissingCount => Items().Count(item => item.HasMissingFile);

    /// <summary>「その他 ▾」の項目の文。数を出すのはユーザ判断 2026-11-20（押す前に何件を探すかが分かる）。</summary>
    public string FindMissingText => MissingCount > 0 ? $"見つからないファイルを探す…（{MissingCount} 件）" : "見つからないファイルを探す…";

    private bool _isBusy;

    /// <summary>書いている・取得している最中か。その間は帯の操作を押させない（同じ組へ2回当てない）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _statusText = string.Empty;

    /// <summary>帯の左に出す、終わった操作の件数や取得の進み具合。選び直したら消す。</summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetField(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatusText));
            }
        }
    }

    public bool HasStatusText => StatusText.Length > 0;

    private CancellationTokenSource? _stop;

    /// <summary>BOOTH から取得している最中か（帯に「中止」を出し、選択を外しても帯を残す）。</summary>
    public bool IsRefreshing => _stop is not null;

    /// <summary>選択が変わった（入れ物の画面が呼ぶ）。押せるかと件数を出し直し、前の操作の件数は消す。</summary>
    public void NoteSelectionChanged()
    {
        if (!IsBusy)
        {
            StatusText = string.Empty;
        }

        if (Selected().Count == 0)
        {
            _latest.Clear();
        }

        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(FindMissingText));
    }

    private IReadOnlyList<ItemCardViewModel> Selected() => _selected();

    /// <summary>選んだ商品の記録。書いた後に読み直した物があればそちらを使う。</summary>
    private IEnumerable<ItemRecord> Items() => Selected().Select(card => _latest.TryGetValue(card.Item.Id, out var item) ? item : card.Item);

    private static bool HasWaitingAvatars(ItemRecord item) => item.Local.Avatars.Any(link => !link.Confirmed && !link.Rejected);

    private RelayCommand Command(Func<Task> run, Func<bool> can)
        => new(() => run().Forget(), () => !IsBusy && can());

    /// <summary>
    /// 選んだ物へまとめて当て、件数を帯に出す（ユーザ判断 2026-11-20：既にある物の追加・無い物の削除は何もせず、後で件数を出す）。
    /// 試験から待てるよう Task を返す
    /// </summary>
    internal async Task ChangeAsync(BulkItemChange change)
    {
        var ids = Selected().Select(card => card.Item.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.ChangeItems(ids, change));
            if (result is CommandResult.Failed failed)
            {
                Notice.Show(failed.Message, "まとめて変更", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 書いた件数が0でも読み直す：カードの写しが古かった（既に付いていた）なら、押せるかを今の記録に揃える
            await ApplyLatestAsync(ids);
            if (result is CommandResult.ItemsChanged changed)
            {
                StatusText = OutcomeText(change, changed.Outcome);
            }
        }
        catch (Exception exception)
        {
            // 命令は書けなかった例外（ファイルを掴まれた・ドライブが外れた）をそのまま投げる。受けないと帯が押せないまま残る
            Core.Diagnostics.AppLog.Error("選択の帯のまとめた変更", exception);
            Notice.Show($"保存できませんでした。{FailureText.Cause(exception)}", "まとめて変更", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>名指しした商品を記録から読み直し、画面へ渡す（全件は読み直さない。2000件で数秒かかる）。</summary>
    private async Task ApplyLatestAsync(IReadOnlyList<string> ids)
    {
        var items = new List<ItemRecord>();
        foreach (var id in ids)
        {
            if (await _services.Store.Items.LoadAsync(id) is { } item)
            {
                items.Add(item);
                _latest[id] = item;
            }
        }

        _applied(items);
        RelayCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(FindMissingText));
    }

    /// <summary>終わった後の1文。例「5件に追加しました（2件は既に付いていました）」（ユーザ判断 2026-11-20 の言い方）。</summary>
    public static string OutcomeText(BulkItemChange change, BulkItemChangeOutcome outcome)
    {
        var (done, already) = change switch
        {
            BulkItemChange.AddUserTag => ("に追加しました", "は既に付いていました"),
            BulkItemChange.RemoveUserTag or BulkItemChange.RemoveAttribute => ("から外しました", "には付いていませんでした"),
            BulkItemChange.SetFavorite { Value: true } => ("をお気に入りに追加しました", "は既に入っていました"),
            BulkItemChange.SetFavorite => ("をお気に入りから外しました", "は入っていませんでした"),
            BulkItemChange.SetHidden { Value: true } => ("を非表示にしました", "は既に非表示でした"),
            BulkItemChange.SetHidden => ("を表示に戻しました", "は非表示ではありませんでした"),
            BulkItemChange.SetNotifyOnUpdate { Value: true } => ("の更新の通知を入れました", "は既に入っていました"),
            BulkItemChange.SetNotifyOnUpdate => ("の更新の通知を切りました", "は既に切れていました"),
            BulkItemChange.ConfirmAvatars => ("の対応アバターを確認済みにしました", "には確認待ちがありませんでした"),
            _ => ("を変更しました", "は変わりませんでした"),
        };

        var text = outcome switch
        {
            { Changed: 0, AlreadyDone: > 0 } => $"選んだ {outcome.AlreadyDone} 件{already}。",
            { Changed: > 0, AlreadyDone: > 0 } => $"{outcome.Changed} 件{done}（{outcome.AlreadyDone} 件{already}）。",
            _ => $"{outcome.Changed} 件{done}。",
        };

        return outcome.NotFound > 0 ? $"{text}{outcome.NotFound} 件は記録から削除されていました。" : text;
    }

    private async Task AddTagAsync()
    {
        var model = BulkTagDialogViewModel.ForAdding(_services.CachedUserTags.Load(), [.. Items()]);
        if (!BulkTagDialogViewModel.Ask(model) || model.Change is not { } change || model.Top is not { } top)
        {
            return;
        }

        // 候補に無い語はタグの一覧へも足す（編集画面の欄と同じ。足さないと、タグの管理と絞り込みの候補に出ない）
        if (model.IsNewTop)
        {
            await _services.Commands.ExecuteAsync(new UiCommand.AddUserTag(top));
        }

        if (model.IsNewSub)
        {
            await _services.Commands.ExecuteAsync(new UiCommand.AddUserTag(top, model.Sub));
        }

        // 追加は確かめない（ユーザ判断 2026-11-20：既に付いている物には何もしないので、取り返しのつかない事が起きない）
        await ChangeAsync(change);
    }

    private async Task RemoveTagAsync()
    {
        var model = BulkTagDialogViewModel.ForRemoving([.. Items()]);
        if (!BulkTagDialogViewModel.Ask(model) || model.Change is not { } change)
        {
            return;
        }

        // 外すのは1回だけ確かめ、何件から外すかを出す（ユーザ判断 2026-11-20）。属性は付け直しても値は戻らない
        var having = model.CountHaving();
        var text = model.RemovesAttribute
            ? $"{having} 件から属性「{model.ChoiceText}」を外します。外した属性の値は元に戻せません。"
            : $"{having} 件からタグ「{model.ChoiceText}」を外します。";
        if (Notice.Show(text, model.RemovesAttribute ? "属性を削除" : "タグを削除", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel)
            != MessageBoxResult.OK)
        {
            return;
        }

        await ChangeAsync(change);
    }

    /// <summary>
    /// 選んだ商品の分だけ、見つからないファイルを探す（商品ページの「探す…」と同じ窓と命令。ユーザ判断 2026-11-20）。
    /// 結果は窓で言う：探し終えると検索の一覧を読み直すので、選択と帯が畳まれて帯の文では読めない
    /// </summary>
    private async Task FindMissingAsync()
    {
        if (_main is null)
        {
            return;
        }

        var ids = Items().Where(item => item.HasMissingFile).Select(item => item.Id).ToList();
        IsBusy = true;
        try
        {
            var result = await MissingSearchFlow.RunAsync(_services, _main, ids, started: () => StatusText = "見つからないファイルを探しています…");
            StatusText = string.Empty;
            if (result is null)
            {
                return;
            }

            // 帯からはフォルダの候補を選べない（並べる場所が無い）。候補があったときは、選べる場所を言う
            var summary = MissingSearchFlow.SummaryOf(result, _services.Settings.WatchedFolders ?? []);
            if (result is CommandResult.MissingFilesSearched { Result.MissingFolders: var folders } && folders.Any(folder => folder.Candidates.Count > 0))
            {
                summary += "フォルダの候補は、商品ページの「探す…」で選べます。";
            }

            Notice.Show(summary, "見つからないファイルを探す", MessageBoxButton.OK,
                result is CommandResult.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
            await ApplyLatestAsync(ids);
        }
        catch (Exception exception)
        {
            Core.Diagnostics.AppLog.Error("選択の帯から見つからないファイルを探す", exception);
            Notice.Show($"探せませんでした。{FailureText.Cause(exception)}", "見つからないファイルを探す", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            StatusText = string.Empty;
            IsBusy = false;
        }
    }

    /// <summary>
    /// 1件にかかる時間の目安（秒）。人が押した取得は JSON と商品ページの2本を通し、門は1本ずつ1.5秒空けるので、少なくとも3秒。
    /// 新しい画像があれば、その分も続けて取る
    /// </summary>
    internal const int SecondsPerItem = 3;

    /// <summary>取得にかかる時間の目安の文（確かめの窓に出す）。</summary>
    public static string EstimateText(int count)
    {
        var seconds = count * SecondsPerItem;
        return seconds < 60 ? $"約{seconds}秒" : $"約{(seconds + 59) / 60}分";
    }

    /// <summary>
    /// 選んだ商品の情報を BOOTH から取得し直す（ユーザ判断 2026-11-20）。**1件ずつ待ってから次を頼む**：
    /// 間隔と並びは BOOTH の門（<c>BoothClient</c>）が守るが、ここで並べて投げると門の前に何百件も積もり、
    /// 止められなくなる。1件ごとに止める合図を見る
    /// </summary>
    internal async Task RefreshFromBoothAsync()
    {
        var targets = Items().Where(item => !item.IsLocalOnly).Select(item => item.Id).ToList();
        var local = Selected().Count - targets.Count;
        var text = $"選んだ {targets.Count} 件の情報をBOOTHから取得し直します。1件ずつ取得するので、{EstimateText(targets.Count)}以上かかります。"
            + (local > 0 ? $"BOOTHに無い商品 {local} 件は取得しません。" : string.Empty);
        if (Notice.Show(text, "BOOTHの情報を取得し直す", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.OK) != MessageBoxResult.OK)
        {
            return;
        }

        var stop = new CancellationTokenSource();
        _stop = stop;
        OnPropertyChanged(nameof(IsRefreshing));
        _refreshingChanged();
        IsBusy = true;
        var failures = new List<string>();
        var done = 0;
        try
        {
            foreach (var id in targets)
            {
                if (stop.IsCancellationRequested)
                {
                    break;
                }

                StatusText = $"BOOTHから取得しています（{done + 1}/{targets.Count}）…";
                var result = await _services.Commands.ExecuteAsync(new UiCommand.RefreshItem(id));
                if (result is CommandResult.Failed failed)
                {
                    failures.Add(failed.Message);
                }

                done++;
                await ApplyLatestAsync([id]);
            }
        }
        catch (Exception exception)
        {
            Core.Diagnostics.AppLog.Error("選択の帯からの BOOTH の取得し直し", exception);
            failures.Add($"取得できませんでした。{FailureText.Cause(exception)}");
        }
        finally
        {
            _stop = null;
            stop.Dispose();
            OnPropertyChanged(nameof(IsRefreshing));
            _refreshingChanged();
            IsBusy = false;
        }

        StatusText = RefreshOutcomeText(done, targets.Count, failures.Count);
        if (failures.Count > 0)
        {
            // 理由は件ごとに違い得るが、窓は1回にする（同じ理由が何十件も並ぶ）。最初の理由を言う
            Notice.Show($"{failures.Count} 件は取得できませんでした。{failures[0]}", "BOOTHの情報を取得し直す", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>取得し終えた後の帯の1文。</summary>
    public static string RefreshOutcomeText(int done, int total, int failed)
    {
        var text = done < total ? $"{total} 件のうち {done} 件を取得して中止しました。" : $"{done} 件の情報を取得し直しました。";
        return failed > 0 ? $"{text}{failed} 件は取得できませんでした。" : text;
    }

    /// <summary>ID を改行でつないだ文（ユーザ判断 2026-11-20）。BOOTHに無い商品は仮のIDを写す（カードの「IDをコピー」と同じ）。</summary>
    public static string IdsText(IEnumerable<ItemCardViewModel> cards) => string.Join(Environment.NewLine, cards.Select(card => card.Item.Id));

    /// <summary>商品ページのリンクを改行でつないだ文。BOOTHに無い商品はリンクが無いので飛ばす。</summary>
    public static string LinksText(IEnumerable<ItemCardViewModel> cards)
        => string.Join(Environment.NewLine, cards.Select(card => Core.Booth.BoothLinks.ItemPage(card.Item)).OfType<string>());

    private void Copy(string text)
    {
        // 他のアプリがクリップボードを掴んでいることがある。次に押せば入る
        StatusText = text.Length > 0 && _services.CopyText(text)
            ? $"{text.Split(Environment.NewLine).Length} 件をコピーしました。"
            : "コピーできませんでした。もう一度押してください。";
    }
}
