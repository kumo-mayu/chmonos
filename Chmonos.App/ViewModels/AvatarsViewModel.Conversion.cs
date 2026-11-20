using System.Windows.Data;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// アバターの管理の「変換」（<c>docs/spec/conversion.md</c>「変換扱いの画面」・ユーザ案「未確定のような形で「変換扱い」をリストで積んで順々に設定する」）。
/// 左にプロファイルを持つ商品の一覧（決めていない物を上に群で分ける）、右に商品ページと同じ「変換扱い」の欄（<see cref="ItemConversionEditor"/>）と、
/// 「確認済みにして次へ」「プロファイルではない」「後で」（ユーザ判断 2026-11-09）。
/// 一覧は検索の写し（全商品を読んである）から、プロファイルを持つかもしれない商品だけ控えを読んで作る。
/// </summary>
public sealed partial class AvatarsViewModel
{
    internal const string UndecidedGroup = "決めていない";

    /// <summary>
    /// 1つ以上のプロファイルのつなぎ先を確認済みにしたが、未設定か確認待ちが残る商品（ユーザ判断 2026-11-20・④）。
    /// プロファイルが何個も入った商品は、分かる範囲で埋めても「決めていない」に入り、手を付けていない商品と見分けが付かなかった
    /// </summary>
    internal const string PartialGroup = "途中";

    internal const string DecidedGroup = "決めた";
    internal const string NotProfileGroup = "扱わない";

    private bool _isConversionMode;
    private List<ConversionItemRowViewModel> _conversionItems = [];

    /// <summary>一覧に出す行（組を探す欄の語に当たる物。絞っていなければ <see cref="_conversionItems"/> と同じ）。</summary>
    private List<ConversionItemRowViewModel> _shownConversionItems = [];

    /// <summary>選びを外すのが、ListBox の書き戻しでなく画面の側の決めた事か（組を選んだ）。</summary>
    private bool _clearingConversion;
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

                    // 組を選んで離れた（元・先へ移った）なら、戻った時に組の選びを残す（右はどちらか1つ）
                    if (_conversionPairs?.SelectedRow is null)
                    {
                        SelectedConversion ??= FirstUndecided();
                    }

                    // 組はアバターの詳細の札・素体の改名・ID の変更でも変わるので、入るたびに読み直す
                    ConversionPairs.Reload();
                }
                else
                {
                    // 離れる時に待っているメモを書き切る（画面を離れる時の書き切りとは別に、見方の切り替えでも残さない）
                    _conversionPairs?.FlushPendingWritesAsync().Forget();
                }

                RaiseModes();
                CloseRightItemIfLeftMoved();
            }
        }
    }

    public string ConversionModeText => $"変換（{_conversionItems.Count}）";

    public RelayCommand ShowConversionModeCommand => _showConversionModeCommand ??= new RelayCommand(() => IsConversionMode = true);

    private RelayCommand? _showConversionModeCommand;

    /// <summary>左の一覧（群は「決めていない」「途中」「決めた」「扱わない」の順。並びが群の順になるよう、先に並べてから群に分ける）。</summary>
    public ListCollectionView ConversionItems { get; private set; } = Grouped([]);

    public bool HasConversionItems => _conversionItems.Count > 0;

    /// <summary>変換を見ているのに1件も無いとき。空欄ではなく、何をすれば並ぶかを出す。</summary>
    public bool ShowsNoConversionItems => IsConversionMode && !HasConversionItems;

    public bool ShowsConversionDetail => IsConversionMode && SelectedConversion is not null;

    /// <summary>選んでいる追加した組（履歴に控える。選んでいなければ null）。</summary>
    internal ConversionPair? SelectedConversionPair => _conversionPairs?.SelectedRow?.Pair;

    /// <summary>右に、選んだ追加した組の中身を出すか（プロファイルの商品とはどちらか1つ）。</summary>
    public bool ShowsConversionPairDetail => IsConversionMode && _conversionPairs?.SelectedRow is not null;

    public ConversionItemRowViewModel? SelectedConversion
    {
        get => _selectedConversion;
        set
        {
            // 探す欄の絞りで隠れた商品を選んでいる間は、ListBox が書き戻す null を受けない（選びは絞りで外さない。追加した組と同じ・
            // ユーザ判断 2026-11-20・②）。受けると、語を打つだけで右の欄が空になる。選びを外すのは組を選んだ時だけ（_clearingConversion）
            if (value is null && !_clearingConversion && _selectedConversion is { } current && !_shownConversionItems.Contains(current))
            {
                return;
            }

            if (SetField(ref _selectedConversion, value))
            {
                // 右に出すのはプロファイルの商品か追加した組のどちらか1つ（ユーザ判断 2026-11-20：組もプロファイルと同じに右で見る）
                if (value is not null && _conversionPairs is { } pairs)
                {
                    pairs.SelectedRow = null;
                }

                ConversionEditor.Show(value?.Item);
                SelectedConversionImage = value is null ? null : ItemConversionEditor.IconsFrom(_services, _main)(value.ItemId);
                OnPropertyChanged(nameof(SelectedConversionImage));
                OnPropertyChanged(nameof(HasSelectedConversionImage));
                OnPropertyChanged(nameof(ShowsConversionDetail));
                RelayCommand.RaiseCanExecuteChanged();
            }

            // 一覧の作り直しは一度 null を挟んで同じ商品へ選び直すので、ここでは ID で比べる（LeftKey）
            CloseRightItemIfLeftMoved();
        }
    }

    /// <summary>選んだ商品の1枚目の絵（ユーザ指示 2026-11-09：プロファイルの多くは、想定するアバターが商品の絵に写っている）。選んだときだけ読む。</summary>
    public System.Windows.Media.ImageSource? SelectedConversionImage { get; private set; }

    public bool HasSelectedConversionImage => SelectedConversionImage is not null;

    /// <summary>右の「変換扱い」の欄。商品ページと同じ部品で、書いた後は一覧の行と検索の写しも直す。</summary>
    public ItemConversionEditor ConversionEditor => _conversionEditor ??= new ItemConversionEditor(
        _services, RunConversionCommandAsync, () => false, _main.Thumbnails, _main.Search.FindItem)
    {
        ShowsReasons = true,
    };

    /// <summary>
    /// 「変換」の一覧の上の「追加した組」（人が足す変換の組・ユーザ判断 2026-11-20）。
    /// 足した・外したら検索を絞り直し（対応アバターの条件の「変換で使える物も含める」が組を使う）、アバターの詳細の札も読み直す
    /// </summary>
    public ConversionPairEditor ConversionPairs => _conversionPairs ??= CreateConversionPairs();

    private ConversionPairEditor CreateConversionPairs()
    {
        var editor = new ConversionPairEditor(
            _services, _main.Thumbnails, _main.Search.FindItem, () =>
            {
                _main.Search.NoteConversionPairsChanged();
                ConversionPairSummary.Refresh();
            },
            () => ConversionPairSummary.Refresh(),
            ConversionPairSelected,
            CanOpenConversionEnd,
            OpenConversionEnd);

        // 組を探す欄の語と探す所は、プロファイルの商品の一覧にも当てる（ユーザ判断 2026-11-20・②「他の段にも適用して良い」）
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ConversionPairEditor.Query) or nameof(ConversionPairEditor.Scope))
            {
                ArrangeConversionItems(keep: SelectedConversion);
            }
        };
        return editor;
    }

    /// <summary>
    /// 組の選びが替わった。組を選んだらプロファイルの商品の選びを外す（右はどちらか1つ）。
    /// 組の選びが無くなった（外して組が尽きた・読み替えで消えた）ら、右を空にせずプロファイルの決めていない商品へ戻す
    /// </summary>
    private void ConversionPairSelected(ConversionPairRow? row)
    {
        if (row is not null)
        {
            _clearingConversion = true;
            try
            {
                SelectedConversion = null;
            }
            finally
            {
                _clearingConversion = false;
            }
        }
        else if (IsConversionMode && SelectedConversion is null)
        {
            SelectedConversion = FirstUndecided();
        }

        OnPropertyChanged(nameof(ShowsConversionPairDetail));
        RelayCommand.RaiseCanExecuteChanged();
        CloseRightItemIfLeftMoved();
    }

    /// <summary>組の端へ移れるか。アバターは登録簿にある物、共通素体は一覧にある物だけ（消えた物は押せない）。</summary>
    private bool CanOpenConversionEnd(ConversionEnd end)
        => end.AvatarItemId is { } id ? _all.Any(row => row.ItemId == id)
        : end.BaseName is { } name && Bases.Any(row => AvatarBaseNames.Same(row.Name, name));

    /// <summary>
    /// 組の元・先を押した：アバターならアバターの見方でそれを選び、共通素体なら共通素体の見方でそれを選ぶ。
    /// 移る前の「変換」（選んでいた組）を履歴に積み、戻る（Alt+←）でその組へ戻れるようにする（素体の中のアバターを開くのと同じ・メモ68）
    /// </summary>
    private void OpenConversionEnd(ConversionEnd end)
    {
        if (!CanOpenConversionEnd(end))
        {
            return;
        }

        _main.RememberAvatarsStep(this);
        if (end.AvatarItemId is { } id)
        {
            var row = _all.First(other => other.ItemId == id);

            // 絞り込みで外れていると選べないので、そのときだけ絞り込みを外す
            if (!Rows.Contains(row))
            {
                Query = string.Empty;
            }

            IsBaseMode = false;
            IsConversionMode = false;
            Selected = row;
        }
        else if (end.BaseName is { } name)
        {
            IsBaseMode = true;
            SelectedBase = Bases.First(row => AvatarBaseNames.Same(row.Name, name));
        }
    }

    private ConversionPairEditor? _conversionPairs;

    /// <summary>アバターの詳細の「変換の組」（読むだけ）。「変換で編集」で「変換」の見方へ移る。</summary>
    public ConversionPairSummary ConversionPairSummary => _conversionPairSummary ??= new ConversionPairSummary(
        _services, () => IsConversionMode = true);

    private ConversionPairSummary? _conversionPairSummary;

    /// <summary>確認待ちのつなぎ先を全部確認済みにして、次の決めていない商品へ（Ctrl+Enter。未確定の「この内容で決める」と同じ流れ）。</summary>
    public RelayCommand DecideSuggestedCommand => _decideSuggestedCommand ??= new RelayCommand(
        () => DecideSuggestedAsync().Forget(),
        // 確認待ちが無ければ押せない（ユーザ判断 2026-11-09。押しても次へ進むだけで、「後で」と見分けが付かなかった）
        () => IsConversionMode && SelectedConversion is not null && ConversionEditor.UnconfirmedTargets.Count > 0 && !RightItem.IsOpen);

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
                // 右の欄の中に重ねる（左の選びを残す・open.md 95 行）。行は作った時の商品を持つので、
                // 重ねたページで書いた後に開き直しても古くならないよう検索の写しから引き直す
                OpenItemInRightPane(_main.Search.FindItem(item.Id) ?? item);
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

    // ---- まとめて確認済みにする（ユーザ判断 2026-11-09「押すと一覧の窓を出して外せる」） ----

    /// <summary>まとめて決められる確認待ちのつなぎ先の数（扱わないにした商品と、人が外した物は数えない）。</summary>
    public int BulkCount => _conversionItems
        .Where(row => row.Group != NotProfileGroup)
        .Sum(row => UnconfirmedOf(row.Item).Count);

    public string BulkText => $"まとめて確認済みにする（{BulkCount} 件）";

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
        // 登録簿を読み直した時（素体の改名・削除・ID の変更で組の端も書き換わる）。「変換」を見ていない間は入る時に読む
        if (IsConversionMode)
        {
            _conversionPairs?.Reload();
        }

        _conversionPairSummary?.Refresh();

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
                        // 組を選んでいる間は、読み直しで商品へ選びを奪わない（右はどちらか1つ）
                        SelectedConversion = _conversionItems.FirstOrDefault(row => row.ItemId == keepId)
                            ?? (IsConversionMode && _conversionPairs?.SelectedRow is null ? FirstUndecided() : null);
                        OnPropertyChanged(nameof(ConversionModeText));
                        OnPropertyChanged(nameof(HasConversionItems));
                        RaiseBulk();
                        RaiseModes();

                        // 戻るで変換の見方へ戻した時は、選び直した後に重ねていた商品ページを重ね直す
                        if (IsConversionMode)
                        {
                            ReopenRightItem();
                        }
                    });
                },
                TaskScheduler.Default)
            .Forget();
    }

    private IReadOnlyList<ProfileSlug> ProfilesOf(ItemRecord item)
        => ConversionScan.MayHoldProfiles(item) ? _services.Conversion.ProfilesOf(item) : [];

    /// <summary>
    /// 群の順（決めていない → 途中 → 決めた → 扱わない）と名前で並べ直し、組を探す欄の語で絞って一覧に当てる。選んでいる行はそのまま選んでおく
    /// （絞りで隠れても選びは外さない。右に出したまま一覧には出さない。追加した組と同じ）。
    /// </summary>
    private void ArrangeConversionItems(ConversionItemRowViewModel? keep)
    {
        _conversionItems = _conversionItems
            .OrderBy(row => row.GroupRank)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();

        var query = (_conversionPairs?.Query ?? string.Empty).Trim();
        var scope = _conversionPairs?.Scope ?? ConversionPairSearchScope.Both;
        if (query.Length == 0)
        {
            _shownConversionItems = _conversionItems;
        }
        else
        {
            var registry = _services.CachedAvatars.Load();
            var entries = registry.Entries
                .GroupBy(entry => entry.ItemId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var names = AvatarNames.Map(registry.Entries);
            _shownConversionItems = [.. _conversionItems.Where(row => ConversionItemSearch.Matches(row.Item, query, scope, entries, names))];
        }

        // 群の見出しの数：絞っている間は「当たった数/全部」（追加した組の見出しと同じ）。当たる行の無い群は見出しごと出ない
        var totals = _conversionItems.GroupBy(row => row.Group).ToDictionary(group => group.Key, group => group.Count());
        var shown = _shownConversionItems.GroupBy(row => row.Group).ToDictionary(group => group.Key, group => group.Count());
        foreach (var row in _conversionItems)
        {
            row.GroupHeading = query.Length == 0
                ? $"{row.Group}（{totals[row.Group]}）"
                : $"{row.Group}（{shown.GetValueOrDefault(row.Group)}/{totals[row.Group]}）";
        }

        ConversionItems = Grouped(_shownConversionItems);
        OnPropertyChanged(nameof(ConversionItems));
        OnPropertyChanged(nameof(ShowsNoMatchingConversionItems));
        if (keep is not null)
        {
            // 一覧を差し替えると ListBox が選びを外すので、同じ行を選び直したと知らせる
            _selectedConversion = null;
            SelectedConversion = keep;
        }
    }

    /// <summary>プロファイルの商品はあるのに、探す欄の語に当てはまる商品が無い（一覧の代わりに1行で言う。追加した組と同じ）。</summary>
    public bool ShowsNoMatchingConversionItems => IsConversionMode && HasConversionItems && _shownConversionItems.Count == 0;

    /// <summary>群は見出しの字（群の名前と数）で分ける。並びは先に群の順にしてあるので、見出しもその順に出る。</summary>
    private static ListCollectionView Grouped(List<ConversionItemRowViewModel> rows)
    {
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConversionItemRowViewModel.GroupHeading)));
        return view;
    }

    /// <summary>
    /// 変換を開いた時・組の選びが無くなった時に選ぶ商品。見えている行の決めていない・途中の行 → 見えている最初の行。
    /// 絞りで1件も見えていなければ、全部の中から（右を空にしない）
    /// </summary>
    private ConversionItemRowViewModel? FirstUndecided()
        => _shownConversionItems.FirstOrDefault(row => row.NeedsDecision)
            ?? _shownConversionItems.FirstOrDefault()
            ?? _conversionItems.FirstOrDefault(row => row.NeedsDecision)
            ?? _conversionItems.FirstOrDefault();

    /// <summary>
    /// 今の行の後ろから、決めていない行か途中の行を探して選ぶ（無ければ前から。1件も無ければ今の行のまま）。
    /// 途中の行も回る（ユーザ判断 2026-11-20・④：「後で」・次への流れは決めていないと途中の両方）。
    /// 絞っている間は見えている行の中で回る（追加した組の「外した後の次の組」と同じ。隠れた商品へ移ると、何を見ているか一覧で分からない）
    /// </summary>
    internal void SelectNextUndecided()
    {
        var rows = _shownConversionItems;
        var current = SelectedConversion;
        var index = current is null ? -1 : rows.IndexOf(current);
        var next = rows.Skip(index + 1).FirstOrDefault(row => row.NeedsDecision && !ReferenceEquals(row, current))
            ?? rows.Take(Math.Max(index, 0)).FirstOrDefault(row => row.NeedsDecision && !ReferenceEquals(row, current));
        if (next is not null)
        {
            SelectedConversion = next;
        }
    }
}

/// <summary>
/// 「変換」のプロファイルの商品を、組を探す欄の語と探す所で照らす（ユーザ判断 2026-11-20・②）。
/// つなぎ先は、向きで元と先に振り分ける：逆方向は元（その衣装を持っていく側）、順方向は先（着せる側。検索の条件「プロファイル」の
/// 「変換元は逆方向・変換先は順方向」と同じ）。両方向は両方、向きの分からない物も両方。外したつなぎ先は当てない。
/// 商品名と商品 ID は元でも先でもないので、探す所が「両方」の時だけ当てる（「元」で選んだのに、名前に先のアバター名を含む商品が残らないように）。
/// 端の当て方は組と同じ（<see cref="ConversionPairSearch.EndMatches"/>：アバターは名前・ID・正式名・呼び方、共通素体は名前）
/// </summary>
public static class ConversionItemSearch
{
    public static bool Matches(
        ItemRecord item, string query, ConversionPairSearchScope scope,
        IReadOnlyDictionary<string, AvatarRegistryEntry> entries, IReadOnlyDictionary<string, string> names)
    {
        if (query.Length == 0)
        {
            return true;
        }

        if (scope == ConversionPairSearchScope.Both
            && (item.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) || item.Id.Contains(query, StringComparison.Ordinal)))
        {
            return true;
        }

        foreach (var target in item.Local.Conversion?.Targets ?? [])
        {
            if (target.Rejected)
            {
                continue;
            }

            var unknown = !target.Forward && !target.Reverse;
            var asFrom = target.Reverse || unknown;
            var asTo = target.Forward || unknown;
            if (!((scope != ConversionPairSearchScope.To && asFrom) || (scope != ConversionPairSearchScope.From && asTo)))
            {
                continue;
            }

            var end = target.BaseName is { } baseName ? ConversionEnd.Base(baseName)
                : target.AvatarItemId is { } id ? ConversionEnd.Avatar(id)
                : null;
            if (end is null)
            {
                continue;
            }

            var entry = end.AvatarItemId is { } avatarId ? entries.GetValueOrDefault(avatarId) : null;
            var shown = end.BaseName ?? (end.AvatarItemId is { } shownId ? names.GetValueOrDefault(shownId) ?? shownId : string.Empty);
            if (ConversionPairSearch.EndMatches(end, shown, entry, query))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>「変換」の一覧の1行：プロファイルを持つ商品と、決めた度合い。</summary>
public sealed class ConversionItemRowViewModel : ViewModelBase
{
    public required string ItemId { get; init; }

    public ItemRecord Item { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;

    /// <summary>群の名前（「決めていない」「途中」「決めた」「扱わない」）。</summary>
    public string Group { get; private set; } = string.Empty;

    /// <summary>
    /// 群の見出しの字（「決めていない（3）」、絞っている間は「決めていない（1/3）」）。一覧は見出しの字で群に分けるので、並べ直す側が付ける
    /// （群の数え方を絞りの前と後で持ち分けるため。行の書き直しでは変えない）
    /// </summary>
    public string GroupHeading { get; internal set; } = string.Empty;

    public int GroupRank => Group switch
    {
        AvatarsViewModel.UndecidedGroup => 0,
        AvatarsViewModel.PartialGroup => 1,
        AvatarsViewModel.DecidedGroup => 2,
        AvatarsViewModel.NotProfileGroup => 3,
        _ => 4,
    };

    /// <summary>まだ決める事が残る（決めていない・途中）。最初に選ぶ行と「後で」・次への流れが回る行。</summary>
    public bool NeedsDecision => Group is AvatarsViewModel.UndecidedGroup or AvatarsViewModel.PartialGroup;

    /// <summary>決めていない物の数（「確認待ち 3・未設定 2」）。決めていない・途中の行だけに出す（札は群を分ける前と同じ）。</summary>
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
        var confirmed = targets.Count(target => target.Confirmed);
        var unconfirmed = targets.Count - confirmed;
        var unset = profiles.Count(profile => !targets.Any(target => string.Equals(target.Slug, profile.Slug, StringComparison.OrdinalIgnoreCase)));

        // 「決めた」の数え方は確認済みのつなぎ先（人が確かめた物）。外しただけ（Rejected）は決めた事に数えない——
        // 外すとその行は選ぶ欄に戻り、つなぎ先はまだ無いので（ユーザ判断 2026-11-20・④「1つ以上決めた」）
        Group = setting?.Mode == ConversionMode.NotProfile
            ? AvatarsViewModel.NotProfileGroup
            : unconfirmed + unset == 0 ? AvatarsViewModel.DecidedGroup
            : confirmed > 0 ? AvatarsViewModel.PartialGroup
            : AvatarsViewModel.UndecidedGroup;
        BadgeText = NeedsDecision
            ? string.Join("・", new[] { unconfirmed > 0 ? $"確認待ち {unconfirmed}" : null, unset > 0 ? $"未設定 {unset}" : null }.OfType<string>())
            : string.Empty;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(HasBadge));
    }
}
