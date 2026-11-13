using System.Windows.Data;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// アバターの管理の「変換」（<c>docs/spec/conversion.md</c>「変換扱いの画面」・ユーザ案「未確定のような形で「変換扱い」をリストで積んで順々に設定する」）。
/// 左にプロファイルを持つ商品の一覧（決めていない物を上に群で分ける）、右に商品ページと同じ「変換扱い」の欄（<see cref="ItemConversionEditor"/>）と、
/// 「推した通りに決めて次へ」「プロファイルではない」「後で」（ユーザ判断 2026-11-09）。
/// 一覧は検索の写し（全商品を読んである）から、プロファイルを持つかもしれない商品だけ控えを読んで作る。
/// </summary>
public sealed partial class AvatarsViewModel
{
    internal const string UndecidedGroup = "決めていない";
    internal const string DecidedGroup = "決めた";
    internal const string NotProfileGroup = "扱わない";

    private bool _isConversionMode;
    private List<ConversionItemRowViewModel> _conversionItems = [];
    private ConversionItemRowViewModel? _selectedConversion;
    private ItemConversionEditor? _conversionEditor;
    private int _conversionBuild;
    private string? _openConversionWith;

    /// <summary>左の一覧に、変換ツールのプロファイルを持つ商品を並べているか。</summary>
    public bool IsConversionMode
    {
        get => _isConversionMode;
        set
        {
            if (SetField(ref _isConversionMode, value))
            {
                if (value)
                {
                    IsBaseMode = false;
                    SelectedConversion ??= FirstUndecided();
                }

                RaiseModes();
            }
        }
    }

    public string ConversionModeText => $"変換（{_conversionItems.Count}）";

    public RelayCommand ShowConversionModeCommand => _showConversionModeCommand ??= new RelayCommand(() => IsConversionMode = true);

    private RelayCommand? _showConversionModeCommand;

    /// <summary>左の一覧（群は「決めていない」「決めた」「扱わない」の順。並びが群の順になるよう、先に並べてから群に分ける）。</summary>
    public ListCollectionView ConversionItems { get; private set; } = Grouped([]);

    public bool HasConversionItems => _conversionItems.Count > 0;

    /// <summary>変換を見ているのに1件も無いとき。空欄ではなく、何をすれば並ぶかを出す。</summary>
    public bool ShowsNoConversionItems => IsConversionMode && !HasConversionItems;

    public bool ShowsConversionDetail => IsConversionMode && SelectedConversion is not null;

    public ConversionItemRowViewModel? SelectedConversion
    {
        get => _selectedConversion;
        set
        {
            if (SetField(ref _selectedConversion, value))
            {
                ConversionEditor.Show(value?.Item);
                SelectedConversionImage = value is null ? null : ItemConversionEditor.IconsFrom(_services, _main)(value.ItemId);
                OnPropertyChanged(nameof(SelectedConversionImage));
                OnPropertyChanged(nameof(HasSelectedConversionImage));
                OnPropertyChanged(nameof(ShowsConversionDetail));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>選んだ商品の1枚目の絵（ユーザ指示 2026-11-09：プロファイルの多くは、想定するアバターが商品の絵に写っている）。選んだときだけ読む。</summary>
    public System.Windows.Media.ImageSource? SelectedConversionImage { get; private set; }

    public bool HasSelectedConversionImage => SelectedConversionImage is not null;

    /// <summary>右の「変換扱い」の欄。商品ページと同じ部品で、書いた後は一覧の行と検索の写しも直す。</summary>
    public ItemConversionEditor ConversionEditor => _conversionEditor ??= new ItemConversionEditor(
        _services, RunConversionCommandAsync, () => false, ItemConversionEditor.IconsFrom(_services, _main), _main.Search.FindItem)
    {
        ShowsReasons = true,
    };

    /// <summary>確認待ちのつなぎ先を全部確認済みにして、次の決めていない商品へ（Ctrl+Enter。未確定の「この内容で決める」と同じ流れ）。</summary>
    public RelayCommand DecideSuggestedCommand => _decideSuggestedCommand ??= new RelayCommand(
        () => DecideSuggestedAsync().Forget(),
        // 確認待ちが無ければ押せない（ユーザ判断 2026-11-09。押しても次へ進むだけで、「後で」と見分けが付かなかった）
        () => IsConversionMode && SelectedConversion is not null && ConversionEditor.UnconfirmedTargets.Count > 0);

    private RelayCommand? _decideSuggestedCommand;

    /// <summary>「プロファイルではない」：扱わないにして次へ。</summary>
    public RelayCommand MarkNotProfileCommand => _markNotProfileCommand ??= new RelayCommand(
        () => MarkNotProfileAsync().Forget(),
        () => IsConversionMode && SelectedConversion is not null);

    private RelayCommand? _markNotProfileCommand;

    /// <summary>「後で」：何も書かずに次の決めていない商品へ（ユーザ判断 2026-11-09。開き直すとまた上の群に出る）。</summary>
    public RelayCommand LaterCommand => _laterCommand ??= new RelayCommand(
        () => SelectNextUndecided(),
        () => IsConversionMode && SelectedConversion is not null);

    private RelayCommand? _laterCommand;

    public RelayCommand OpenConversionItemCommand => _openConversionItemCommand ??= new RelayCommand(
        () =>
        {
            if (SelectedConversion?.Item is { } item)
            {
                _main.ShowItem(item);
            }
        },
        () => SelectedConversion is not null);

    private RelayCommand? _openConversionItemCommand;

    /// <summary>
    /// BOOTH の商品ページを開く（ユーザ指示 2026-11-09）。どのアバター向けかは、商品の説明や他の絵を見て決めることが多い。
    /// 手元だけの商品は BOOTH のページが無いので押せない。
    /// </summary>
    public RelayCommand OpenConversionBoothCommand => _openConversionBoothCommand ??= new RelayCommand(
        () =>
        {
            if (ConversionBoothPage() is { } url)
            {
                Services.Shell.OpenUrl(url);
            }
        },
        () => ConversionBoothPage() is not null);

    private RelayCommand? _openConversionBoothCommand;

    private string? ConversionBoothPage()
        => SelectedConversion?.Item is { } item ? Core.Booth.BoothLinks.ItemPage(item) : null;

    private async Task DecideSuggestedAsync()
    {
        var deciding = SelectedConversion?.ItemId;
        if (ConversionEditor.UnconfirmedTargets is { Count: > 0 } shown
            && !await ConversionEditor.ChangeAsync(ConversionEdits.ConfirmMany(shown)))
        {
            return;
        }

        SelectNextIfStillOn(deciding);
    }

    private async Task MarkNotProfileAsync()
    {
        var deciding = SelectedConversion?.ItemId;
        if (await ConversionEditor.ChangeAsync(ConversionEdits.SetMode(ConversionMode.NotProfile)))
        {
            SelectNextIfStillOn(deciding);
        }
    }

    /// <summary>
    /// 書き終えた後に次へ進む。保存を待つ間に人が別の商品を選び直していたら、その選びを尊重して動かさない
    /// （決めた商品とは別の商品から「次」へ飛ぶと、見ていた商品を勝手に離れる）。失敗したときは呼ばない（同じ商品に留まり、失敗の知らせを読める）。
    /// </summary>
    private void SelectNextIfStillOn(string? decidedItemId)
    {
        if (SelectedConversion?.ItemId == decidedItemId)
        {
            SelectNextUndecided();
        }
    }

    /// <summary>
    /// 欄の書き込み。命令を通し、書いた後の記録を読み直して、右の欄・一覧の行・検索の写しを直す
    /// （検索の写しへ知らせないと、衣装の商品ページの「変換経由」が全件の読み直しまで変わらない）。
    /// </summary>
    private async Task<bool> RunConversionCommandAsync(UiCommand command)
    {
        if (command is not UiCommand.ChangeConversion change)
        {
            return false;
        }

        var saved = true;
        try
        {
            var result = await _services.Commands.ExecuteAsync(command);
            if (result is CommandResult.Failed failed)
            {
                ConversionEditor.Notice.Warn(failed.Message);
                saved = false;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            saved = false;
            Core.Diagnostics.AppLog.Error("変換扱いの保存", exception);
            ConversionEditor.Notice.Warn($"保存できませんでした。{FailureText.Cause(exception)}");
        }

        await AfterConversionWriteAsync([change.ItemId]);
        return saved;
    }

    /// <summary>書いた商品を読み直し、検索の写し・一覧の行・右の欄を今の値に合わせる。</summary>
    private async Task AfterConversionWriteAsync(IReadOnlyCollection<string> itemIds)
    {
        foreach (var itemId in itemIds)
        {
            if (await _services.Store.Items.LoadAsync(itemId) is not { } reloaded)
            {
                continue;
            }

            _main.Search.NoteItemChanged(reloaded);
            if (_conversionItems.FirstOrDefault(candidate => candidate.ItemId == reloaded.Id) is { } row)
            {
                row.Update(reloaded, ProfilesOf(reloaded));
            }
        }

        ArrangeConversionItems(keep: SelectedConversion);
        if (SelectedConversion is { } selected)
        {
            ConversionEditor.Show(selected.Item);
        }

        RaiseBulk();
    }

    // ---- 推した通りにまとめて決める（ユーザ判断 2026-11-09「押すと一覧の窓を出して外せる」） ----

    /// <summary>まとめて決められる確認待ちのつなぎ先の数（扱わないにした商品と、人が外した物は数えない）。</summary>
    public int BulkCount => _conversionItems
        .Where(row => row.Group != NotProfileGroup)
        .Sum(row => UnconfirmedOf(row.Item).Count);

    public string BulkText => $"推した通りにまとめて決める（{BulkCount} 件）";

    public RelayCommand BulkConfirmCommand => _bulkConfirmCommand ??= new RelayCommand(
        () => BulkConfirmAsync().Forget(),
        () => IsConversionMode && BulkCount > 0);

    private RelayCommand? _bulkConfirmCommand;

    private static IReadOnlyList<ConversionTarget> UnconfirmedOf(ItemRecord item)
        => (item.Local.Conversion?.Targets ?? []).Where(target => !target.Confirmed && !target.Rejected).ToList();

    private async Task BulkConfirmAsync()
    {
        var registry = _services.CachedAvatars.Load();
        var names = AvatarNames.Map(registry.Entries);
        string TargetName(ConversionTarget target)
            => target.BaseName is { } baseName
                ? $"{baseName}（共通素体）"
                : target.AvatarItemId is { } id ? names.TryGetValue(id, out var name) ? name : id : string.Empty;
        string Direction(ConversionTarget target) => (target.Forward, target.Reverse) switch
        {
            (true, true) => "両方向",
            (true, false) => "順方向",
            (false, true) => "逆方向",
            _ => "向きが分かりません",
        };

        var sections = _conversionItems
            .Where(row => row.Group != NotProfileGroup && UnconfirmedOf(row.Item).Count > 0)
            .Select(row => new ConfirmConversionsSection
            {
                ItemId = row.ItemId,
                Name = row.Name,
                Rows = UnconfirmedOf(row.Item)
                    .Select(target => new ConfirmConversionsRow
                    {
                        Target = target,
                        Text = $"{(target.Slug is { } slug ? slug + "（" + Direction(target) + "）" : Direction(target))} → {TargetName(target)}",
                    })
                    .ToList(),
            })
            .ToList();
        if (sections.Count == 0)
        {
            return;
        }

        var model = new ConfirmConversionsDialogViewModel(sections);
        if (!ConfirmConversionsDialogViewModel.Ask(model))
        {
            return;
        }

        var written = new List<string>();
        foreach (var section in sections)
        {
            var chosen = section.Rows.Where(row => row.IsChecked).Select(row => row.Target).ToList();
            if (chosen.Count == 0)
            {
                continue;
            }

            try
            {
                var result = await _services.Commands.ExecuteAsync(new UiCommand.ChangeConversion(section.ItemId, ConversionEdits.ConfirmMany(chosen)));
                if (result is CommandResult.Failed failed)
                {
                    BulkNotice.Warn(failed.Message);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 1件書けなくても残りは続ける（書けた分は確認済みのまま。書けなかった物は一覧に確認待ちで残る）
                Core.Diagnostics.AppLog.Error("変換扱いをまとめて決める", exception);
                BulkNotice.Warn($"保存できなかった商品があります。{FailureText.Cause(exception)}");
            }

            written.Add(section.ItemId);
        }

        await AfterConversionWriteAsync(written);
    }

    /// <summary>まとめて決めるボタンの下の1行（書けなかったときだけ）。</summary>
    public AreaNotice BulkNotice { get; } = new();

    private void RaiseBulk()
    {
        OnPropertyChanged(nameof(BulkCount));
        OnPropertyChanged(nameof(BulkText));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 検索の写しから一覧を作り直す。控えを読むので裏で回し、間に作り直しが重なったら古い方の結果は捨てる。
    /// 対象はプロファイルのファイルを持つ商品と、変換扱いを既に持つ商品（商品ページの欄を出す商品と同じ）。
    /// </summary>
    private void RefreshConversionItems()
    {
        var build = ++_conversionBuild;
        var items = _main.Search.SnapshotItems();
        var registry = _services.CachedAvatars.Load();
        Task.Run(() =>
            {
                var rows = items
                    .Where(item => item.Local.Conversion is not null || ConversionScan.MayHoldProfiles(item))
                    .Select(item => (Item: item, Profiles: ProfilesOf(item)))
                    .Where(pair => pair.Profiles.Count > 0 || pair.Item.Local.Conversion is not null)
                    .ToList();

                // 右の欄の推した理由が使う FBX の索引の元（アバターの中身の控え）を、ここで先に読んで覚えさせる。
                // 選んだときに画面のスレッドで初めて読むと、アバター100体で100ms を超えて固まる（2026-11-13 の Codex の点検の件3）。
                // 一覧があるときだけ（無ければ右の欄は開かない）。同じ ID が2件あっても投げないよう、先の1件を使う（見回りと同じ）
                // 先読みは早めるためだけなので、しくじっても一覧は出す（選んだときに読み直す）
                if (rows.Count > 0)
                {
                    try
                    {
                        var byId = items.GroupBy(item => item.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                        _services.Conversion.FbxIndex(registry, id => byId.GetValueOrDefault(id));
                    }
                    catch (Exception exception)
                    {
                        Core.Diagnostics.AppLog.Error("変換の FBX の索引を先に読む", exception);
                    }
                }

                return rows;
            })
            .ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        Core.Diagnostics.AppLog.Error("変換の一覧を作る", task.Exception!.GetBaseException());
                        return;
                    }

                    RunOnUiThread(() =>
                    {
                        if (build != _conversionBuild)
                        {
                            return;
                        }

                        // 戻るで開き直したときは、控えた商品を選ぶ（初めの1回だけ）
                        var keepId = SelectedConversion?.ItemId ?? _openConversionWith;
                        _openConversionWith = null;
                        _conversionItems = task.Result.Select(pair => ConversionItemRowViewModel.Create(pair.Item, pair.Profiles)).ToList();
                        ArrangeConversionItems(keep: null);
                        _selectedConversion = null;
                        SelectedConversion = _conversionItems.FirstOrDefault(row => row.ItemId == keepId)
                            ?? (IsConversionMode ? FirstUndecided() : null);
                        OnPropertyChanged(nameof(ConversionModeText));
                        OnPropertyChanged(nameof(HasConversionItems));
                        RaiseBulk();
                        RaiseModes();
                    });
                },
                TaskScheduler.Default)
            .Forget();
    }

    private IReadOnlyList<ProfileSlug> ProfilesOf(ItemRecord item)
        => ConversionScan.MayHoldProfiles(item) ? _services.Conversion.ProfilesOf(item) : [];

    /// <summary>群の順（決めていない → 決めた → 扱わない）と名前で並べ直す。選んでいる行はそのまま選んでおく。</summary>
    private void ArrangeConversionItems(ConversionItemRowViewModel? keep)
    {
        _conversionItems = _conversionItems
            .OrderBy(row => row.GroupRank)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ConversionItems = Grouped(_conversionItems);
        OnPropertyChanged(nameof(ConversionItems));
        if (keep is not null)
        {
            // 一覧を差し替えると ListBox が選びを外すので、同じ行を選び直したと知らせる
            _selectedConversion = null;
            SelectedConversion = keep;
        }
    }

    private static ListCollectionView Grouped(List<ConversionItemRowViewModel> rows)
    {
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConversionItemRowViewModel.Group)));
        return view;
    }

    private ConversionItemRowViewModel? FirstUndecided()
        => _conversionItems.FirstOrDefault(row => row.Group == UndecidedGroup) ?? _conversionItems.FirstOrDefault();

    /// <summary>今の行の後ろから、決めていない行を探して選ぶ（無ければ前から。1件も無ければ今の行のまま）。</summary>
    internal void SelectNextUndecided()
    {
        var current = SelectedConversion;
        var index = current is null ? -1 : _conversionItems.IndexOf(current);
        var next = _conversionItems.Skip(index + 1).FirstOrDefault(row => row.Group == UndecidedGroup && !ReferenceEquals(row, current))
            ?? _conversionItems.Take(Math.Max(index, 0)).FirstOrDefault(row => row.Group == UndecidedGroup);
        if (next is not null)
        {
            SelectedConversion = next;
        }
    }
}

/// <summary>「変換」の一覧の1行：プロファイルを持つ商品と、決めた度合い。</summary>
public sealed class ConversionItemRowViewModel : ViewModelBase
{
    public required string ItemId { get; init; }

    public ItemRecord Item { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;

    /// <summary>群の名前（「決めていない」「決めた」「扱わない」）。</summary>
    public string Group { get; private set; } = string.Empty;

    public int GroupRank => Group switch
    {
        AvatarsViewModel.UndecidedGroup => 0,
        AvatarsViewModel.DecidedGroup => 1,
        _ => 2,
    };

    /// <summary>決めていない物の数（「確認待ち 3・未設定 2」）。決めた物は空。</summary>
    public string BadgeText { get; private set; } = string.Empty;

    public bool HasBadge => BadgeText.Length > 0;

    public static ConversionItemRowViewModel Create(ItemRecord item, IReadOnlyList<ProfileSlug> profiles)
    {
        var row = new ConversionItemRowViewModel { ItemId = item.Id };
        row.Update(item, profiles);
        return row;
    }

    /// <summary>書いた後に、読み直した記録で数と群を直す。</summary>
    public void Update(ItemRecord item, IReadOnlyList<ProfileSlug> profiles)
    {
        Item = item;
        Name = item.DisplayName;
        var setting = item.Local.Conversion;
        var targets = (setting?.Targets ?? []).Where(target => !target.Rejected).ToList();
        var unconfirmed = targets.Count(target => !target.Confirmed);
        var unset = profiles.Count(profile => !targets.Any(target => string.Equals(target.Slug, profile.Slug, StringComparison.OrdinalIgnoreCase)));

        Group = setting?.Mode == ConversionMode.NotProfile
            ? AvatarsViewModel.NotProfileGroup
            : unconfirmed + unset > 0 ? AvatarsViewModel.UndecidedGroup : AvatarsViewModel.DecidedGroup;
        BadgeText = Group == AvatarsViewModel.UndecidedGroup
            ? string.Join("・", new[] { unconfirmed > 0 ? $"確認待ち {unconfirmed}" : null, unset > 0 ? $"未設定 {unset}" : null }.OfType<string>())
            : string.Empty;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(HasBadge));
    }
}
