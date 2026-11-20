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

    /// <summary>
    /// 切り替えの数は、絞る前のプロファイルの商品と追加した組の合計（ユーザ判断 2026-11-20・⑤「変換には手で増やしたものを含めましょう」）。
    /// 組だけで商品が無い時も数が出る。「変換」に1件も無いか（<see cref="HasConversionItems"/>）は前のまま商品だけで見る
    /// </summary>
    public string ConversionModeText => $"変換（{_conversionItems.Count + _conversionPairCount}）";

    /// <summary>
    /// 追加した組の数。組の欄（<see cref="ConversionPairs"/>）は「変換」に入った時に初めて読むので、それまでは一覧を作る時にファイルから数える。
    /// 欄を読んだ後は、足す・外す・読み直すたびに欄の行の数で替える
    /// </summary>
    private int _conversionPairCount;

    private void SetConversionPairCount(int count)
    {
        if (_conversionPairCount != count)
        {
            _conversionPairCount = count;
            OnPropertyChanged(nameof(ConversionModeText));
        }
    }

    public RelayCommand ShowConversionModeCommand => _showConversionModeCommand ??= new RelayCommand(() => IsConversionMode = true);

    private RelayCommand? _showConversionModeCommand;

    /// <summary>左の一覧（群は「決めていない」「途中」「決めた」「扱わない」の順。並びが群の順になるよう、先に並べてから群に分ける）。</summary>
    public ListCollectionView ConversionItems { get; private set; } = Grouped([]);

    /// <summary>
    /// 左の欄の探す欄より下の、1つの流れ（ユーザ判断 2026-11-20・①「左側の検索より下で一個で良い」）。先頭に追加した組の節（<see cref="ConversionPairsSection"/>）、
    /// 続けて組のまとまりの見出しと組の行（<see cref="ConversionPairEditor.Entries"/>。節を開いている時だけ）、商品が1件も見えない時の1行（<see cref="ConversionListEnd"/>）、
    /// 続けて <see cref="ConversionItems"/> と同じ行を同じ群で。節と組も群の1つにして、同じ一覧の中で流す。
    /// 外に流れを1つ置いて節と一覧を積むと、一覧の高さが無限になって見える行だけ作るのが効かず、数百件の行と絵を全部作る。
    /// 組の行も節の中の入れ子の一覧に置くと組の数だけ全部作る（組40個で開く 0.05→0.24秒）ので、外の一覧の行に平らに並べ、見える分だけ作る
    /// （ユーザ判断 2026-11-20「組は大きくなる箇所なので b で」）
    /// </summary>
    public ListCollectionView ConversionList => _conversionList ??= Listed([]);

    private ListCollectionView? _conversionList;

    /// <summary><see cref="ConversionList"/> の元の並び。組の出し入れは差分でここへ当てる（丸ごと差し替えると流した位置とフォーカスが飛ぶ）。</summary>
    private System.Collections.ObjectModel.ObservableCollection<object> _conversionFlow = [];

    /// <summary><see cref="_conversionFlow"/> の中の組の物（見出しと行）の数。節の次から、この数だけ並ぶ。</summary>
    private int _flowPairCount;

    /// <summary>流れを作り直している・組を写している間か。この間に ListBox が書き戻す選びは受けない。</summary>
    private bool _syncingFlow;

    /// <summary>追加した組の節（<see cref="ConversionList"/> の先頭の1行）。</summary>
    public ConversionPairsSection PairsSection => _pairsSection ??= new ConversionPairsSection(ConversionPairs);

    private ConversionPairsSection? _pairsSection;

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

                OnPropertyChanged(nameof(SelectedConversionEntry));
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

        // つなぎ先の札の右クリックの「開く」は、組の端と同じ移り方（ユーザ判断 2026-11-20「共通素体から共通素体のページに飛べた方が良い」）
        OpenTarget = (CanOpenConversionEnd, OpenConversionEnd),
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
            else if (args.PropertyName is nameof(ConversionPairEditor.Rows))
            {
                SetConversionPairCount(editor.Rows.Count);
            }
            else if (args.PropertyName is nameof(ConversionPairEditor.IsExpanded))
            {
                SyncPairFlow();
            }
        };

        // 組の見出しと行は外の一覧（ConversionList）の行として平らに並べる。組の欄の出し入れを流れへ写す
        editor.Entries.CollectionChanged += (_, _) => SyncPairFlow();
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
        OnPropertyChanged(nameof(SelectedConversionEntry));
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

    // ユーザ判断 2026-11-20・④：ボタンは効く相手のそばに置く。群「決めていない」「途中」の見出しの右に、その群の分だけを対象にして出す
    // （前は左の上に1つで、効く相手の一覧から離れていた）。決めた群は確認待ちを持たず、扱わない群は対象にしないので出さない。
    // 絞り（探す欄）の間は**見えている行だけ**を数える：見出しは「当たった数/全部」を出すので、隠れた行まで数えると横の数と食い違い、
    // 当たらない群は見出しごと消えて押す口も無くなる（「後で」・次へも見えている行の中で回る。前の d「隠れた行も数える」から替えた）

    /// <summary>群の見出しにまとめて確認済みにするボタンを出す群か（決めていない・途中）。</summary>
    internal static bool OffersBulk(string group) => group is UndecidedGroup or PartialGroup;

    /// <summary>その群の見えている行の、確認待ちのつなぎ先の数（人が外した物は数えない）。</summary>
    internal int BulkCountOf(string group) => _shownConversionItems
        .Where(row => row.Group == group)
        .Sum(row => UnconfirmedOf(row.Item).Count);

    private static IReadOnlyList<ConversionTarget> UnconfirmedOf(ItemRecord item)
        => (item.Local.Conversion?.Targets ?? []).Where(target => !target.Confirmed && !target.Rejected).ToList();

    /// <summary>群の見出しのボタン：その群の見えている行の確認待ちを窓に並べ、チェックを残した物を確認済みにする。</summary>
    internal async Task BulkConfirmAsync(string group)
    {
        if (!IsConversionMode || !OffersBulk(group))
        {
            return;
        }

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

        var sections = _shownConversionItems
            .Where(row => row.Group == group && UnconfirmedOf(row.Item).Count > 0)
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

    /// <summary>まとめて確認済みにして書けなかったときの1行（一覧の上。ボタンは群の見出しにあり、見出しは書いた後に作り直されるので、そこには置かない）。</summary>
    public AreaNotice BulkNotice { get; } = new();

    // 群の見出し（とボタンの数）は一覧を並べ直すたびに作り直す（ArrangeConversionItems）。ここは押せるかを知らせ直すだけ
    private void RaiseBulk() => RelayCommand.RaiseCanExecuteChanged();

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
                        _conversionItems = task.Result
                            .Select(pair => ConversionItemRowViewModel.Create(pair.Item, pair.Profiles, ConversionItemPicture(pair.Item)))
                            .ToList();
                        ArrangeConversionItems(keep: null);
                        _selectedConversion = null;
                        // 組を選んでいる間は、読み直しで商品へ選びを奪わない（右はどちらか1つ）
                        SelectedConversion = _conversionItems.FirstOrDefault(row => row.ItemId == keepId)
                            ?? (IsConversionMode && _conversionPairs?.SelectedRow is null ? FirstUndecided() : null);
                        // 組の欄の行は空の組を飛ばすので、ファイルから数える時も同じく飛ばす
                        _conversionPairCount = _services.Store.ConversionPairs.Load().Pairs.Count(pair => !pair.IsEmpty);
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

    /// <summary>
    /// プロファイルの商品の行の絵。絵の決め方は検索のカードと同じ（<see cref="ResultThumbnail.PathOf"/>）で、手元の記録を使う
    /// （<see cref="ResultThumbnail.ForItem"/> と違い、商品をもう一度読まない）。絵の一覧は読み手の写しを使う。
    /// 絵はタイルの控えで読む（隣の組の一覧の端の絵・ItemRows と同じ読み方と控え。ユーザ判断 2026-11-20）
    /// </summary>
    private ResultThumbnail ConversionItemPicture(ItemRecord item)
    {
        var loader = _main.Thumbnails;
        return new ResultThumbnail(item.DisplayName, () => Task.Run(() =>
        {
            if (!Chmonos.Core.Storage.StoreIds.IsItemId(item.Id))
            {
                return (string?)null;
            }

            var directory = _services.Paths.ItemImagesDir(item.Id);
            return ResultThumbnail.PathOf(item, directory, loader.ListFiles(directory), _services.Settings.ThumbnailRole, _services.Settings.ShowRemovedBoothImages);
        }), loader, readsAsTile: true);
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

        // 見出しは群ごとに1つの物（字と、その群のまとめて確認済みにするボタン）。一覧はこの物で群に分ける
        var headers = totals.Keys.ToDictionary(
            group => group,
            group => new ConversionItemGroupHeader(
                group,
                query.Length == 0 ? $"{group}（{totals[group]}）" : $"{group}（{shown.GetValueOrDefault(group)}/{totals[group]}）",
                OffersBulk(group) ? BulkCountOf(group) : 0,
                () => BulkConfirmAsync(group).Forget()));
        foreach (var row in _conversionItems)
        {
            row.GroupHeader = headers[row.Group];
        }

        ConversionItems = Grouped(_shownConversionItems);
        OnPropertyChanged(nameof(ConversionItems));
        _syncingFlow = true;
        try
        {
            _conversionList = Listed(_shownConversionItems);
            OnPropertyChanged(nameof(ConversionList));
        }
        finally
        {
            _syncingFlow = false;
        }

        OnPropertyChanged(nameof(ShowsNoMatchingConversionItems));
        if (keep is not null)
        {
            // 一覧を差し替えると ListBox が選びを外すので、同じ行を選び直したと知らせる
            _selectedConversion = null;
            SelectedConversion = keep;
        }

        OnPropertyChanged(nameof(SelectedConversionEntry));
    }

    /// <summary>プロファイルの商品はあるのに、探す欄の語に当てはまる商品が無い（一覧の代わりに1行で言う。追加した組と同じ）。</summary>
    public bool ShowsNoMatchingConversionItems => IsConversionMode && HasConversionItems && _shownConversionItems.Count == 0;

    /// <summary>群は見出しの物（群の名前と数・ボタン）で分ける。並びは先に群の順にしてあるので、見出しもその順に出る。</summary>
    private static ListCollectionView Grouped(List<ConversionItemRowViewModel> rows)
    {
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConversionItemRowViewModel.GroupHeader)));
        return view;
    }

    /// <summary>左の欄の流れ：先頭に追加した組の節、組の見出しと行、商品が見えない時の1行、続けて商品の行（群は節の群と、行の見出し）。</summary>
    private ListCollectionView Listed(List<ConversionItemRowViewModel> rows)
    {
        var pairs = PairFlowEntries();
        _conversionFlow = new System.Collections.ObjectModel.ObservableCollection<object>(
            [PairsSection, .. pairs, .. rows.Count == 0 ? new object[] { ConversionListEnd.Instance } : [], .. rows]);
        _flowPairCount = pairs.Count;
        var view = new ListCollectionView(_conversionFlow);
        view.GroupDescriptions.Add(new ConversionListGrouping(PairsSection));
        return view;
    }

    /// <summary>流れに出す組の物：節を開いていれば、まとまりの見出しと開いたまとまりの組の行（組の欄の <see cref="ConversionPairEditor.Entries"/> と同じ）。</summary>
    private List<object> PairFlowEntries()
        => _conversionPairs is { IsExpanded: true } pairs ? [.. pairs.Entries] : [];

    /// <summary>
    /// 組の欄の出し入れ（畳む・広げる・並び替え・絞り・足す・外す・節の開閉）を、流れの組の所へ差分で写す。
    /// 一覧ごと作り直すと、見出しの三角をキーで畳んだ直後にフォーカスが飛び、流した位置も先頭へ戻る
    /// </summary>
    private void SyncPairFlow()
    {
        if (_conversionList is null)
        {
            return;
        }

        var wanted = PairFlowEntries();
        _syncingFlow = true;
        try
        {
            var keep = new HashSet<object>(wanted, ReferenceEqualityComparer.Instance);
            for (var index = _flowPairCount; index >= 1; index--)
            {
                if (!keep.Contains(_conversionFlow[index]))
                {
                    _conversionFlow.RemoveAt(index);
                    _flowPairCount--;
                }
            }

            for (var index = 0; index < wanted.Count; index++)
            {
                var at = 1 + index;
                if (index < _flowPairCount && ReferenceEquals(_conversionFlow[at], wanted[index]))
                {
                    continue;
                }

                var found = -1;
                for (var other = at + 1; other <= _flowPairCount; other++)
                {
                    if (ReferenceEquals(_conversionFlow[other], wanted[index]))
                    {
                        found = other;
                        break;
                    }
                }

                if (found >= 0)
                {
                    _conversionFlow.Move(found, at);
                }
                else
                {
                    _conversionFlow.Insert(at, wanted[index]);
                    _flowPairCount++;
                }
            }
        }
        finally
        {
            _syncingFlow = false;
        }

        OnPropertyChanged(nameof(SelectedConversionEntry));
    }

    /// <summary>
    /// 左の一覧（ListBox）の選び。組の行なら組を、商品の行なら商品を選ぶ（右はどちらか1つ）。節・組のまとまりの見出し・商品が無い時の1行は選ばない。
    /// ListBox が書き戻す null は受けない：流れの作り直し・組の出し入れ・絞りで選んだ物が一覧から消えた時に来る物で、
    /// 受けると、組を選んだまま探す語を打つだけで右が商品へ替わる（選びは絞りで外さない。ユーザ判断 2026-11-20・②）
    /// </summary>
    public object? SelectedConversionEntry
    {
        get => _conversionPairs?.SelectedRow is { } pair
            ? (_conversionFlow.Contains(pair) ? pair : null)
            : SelectedConversion;
        set
        {
            if (!_syncingFlow)
            {
                SelectConversionEntry(value);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>キーで渡る物：組のまとまりの見出し・組の行・商品の行（流れの順）。</summary>
    private List<object> KeyEntries()
        => [.. _conversionFlow.Where(entry => entry is ConversionPairGroup or ConversionPairRow or ConversionItemRowViewModel)];

    /// <summary>
    /// 左の一覧のキーの移り先（↑↓ は ±1、Home・End は端）。組の見出し・組の行・商品の行を1つの並びとして渡り、端で止まる（回り込まない・ui-keyboard.md）。
    /// 組の見出しは選ぶ行ではないので、ListBox の既定の ↑↓（止まれない入れ物を飛ばす）では届かない。動かない時は null。選びは替えない
    /// </summary>
    internal object? ConversionEntryTarget(object? current, int step, bool toEdge = false)
        => ConversionEntryKeys.Target(KeyEntries(), current, step, toEdge);

    /// <summary>移った先・押した行を選ぶ（組の行なら組、商品の行なら商品）。見出し・節・商品が無い時の1行は止まるだけで選ばない。</summary>
    internal void SelectConversionEntry(object? entry)
    {
        switch (entry)
        {
            case ConversionPairRow pair:
                ConversionPairs.SelectedRow = pair;
                break;
            case ConversionItemRowViewModel row:
                SelectedConversion = row;
                break;
        }
    }

    /// <summary>キーで移って選ぶ（<see cref="ConversionEntryTarget"/> と <see cref="SelectConversionEntry"/>）。動かない時は null。</summary>
    internal object? MoveConversionEntry(object? current, int step, bool toEdge = false)
    {
        var target = ConversionEntryTarget(current, step, toEdge);
        SelectConversionEntry(target);
        return target;
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
    /// 群の見出し（字「決めていない（3）」、絞っている間は「決めていない（1/3）」と、その群のまとめて確認済みにするボタン）。
    /// 一覧は見出しの物で群に分けるので、並べ直す側が付ける（群の数え方を絞りの前と後で持ち分けるため。行の書き直しでは変えない）
    /// </summary>
    public ConversionItemGroupHeader? GroupHeader { get; internal set; }

    /// <summary>群の見出しの字。</summary>
    public string GroupHeading => GroupHeader?.Text ?? string.Empty;

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

    /// <summary>
    /// 行の頭の商品の絵（ユーザ判断 2026-11-20・③）。取り込みの探した結果・通知・ファイルの割り振りの行と同じ部品で、
    /// 見えた行だけが初めて場所を決めて縮めて読む（仮想化された一覧で、流しても読むのは見えている行の分だけ）。試験では無し
    /// </summary>
    public ResultThumbnail? Picture { get; private init; }

    public static ConversionItemRowViewModel Create(ItemRecord item, IReadOnlyList<ProfileSlug> profiles, ResultThumbnail? picture = null)
    {
        var row = new ConversionItemRowViewModel { ItemId = item.Id, Picture = picture };
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

/// <summary>
/// 「変換」の左の流れの先頭の1行：追加した組の節（探す欄より下を1つの流れにするため、節を一覧の1行として持つ。ユーザ判断 2026-11-20・①）。
/// 中身は節の見出し・組を追加する欄・並び・当てはまる組が無い時の1行。組の見出しと行は節の後ろの行として並ぶ（<see cref="AvatarsViewModel.ConversionList"/>）。
/// 選ぶ行ではない（画面は選べない入れ物で出す）。群は自分自身（見出しを出さない群）
/// </summary>
public sealed class ConversionPairsSection
{
    internal ConversionPairsSection(ConversionPairEditor pairs) => Pairs = pairs;

    public ConversionPairEditor Pairs { get; }

    // 群の入れ物と行の読み上げの名前に使われる（無いと型の名前が読まれる）
    public override string ToString() => "追加した組";
}

/// <summary>
/// 「変換」の左の流れの、商品が1件も見えない時の1行（「まだありません」「当てはまる商品がありません」）。組の行の後ろ、商品の行が来る所に出す。
/// 選ぶ行ではない。節の中に置くと、組の行が節の後ろに並ぶので節と組の間に挟まる
/// </summary>
public sealed class ConversionListEnd
{
    public static readonly ConversionListEnd Instance = new();

    private ConversionListEnd()
    {
    }

    // 行の読み上げの名前に使われる（無いと型の名前が読まれる）
    public override string ToString() => "商品の一覧";
}

/// <summary>
/// 左の流れの群の分け方：節・組のまとまりの見出しと組の行・商品が無い時の1行は節の群（見出しを出さない群）、商品の行は見出しの物（<see cref="ConversionItemGroupHeader"/>）で。
/// </summary>
internal sealed class ConversionListGrouping(ConversionPairsSection section) : System.ComponentModel.GroupDescription
{
    public override object? GroupNameFromItem(object item, int level, System.Globalization.CultureInfo culture) => item switch
    {
        ConversionItemRowViewModel row => row.GroupHeader,
        ConversionPairsSection or ConversionPairGroup or ConversionPairRow or ConversionListEnd => section,
        _ => null,
    };
}

/// <summary>キーの移り先の決め方（組の欄の一覧と、左の流れで同じ）：±1 か端へ、端で止まる（回り込まない）。動かない時は null。</summary>
internal static class ConversionEntryKeys
{
    public static object? Target(IReadOnlyList<object> entries, object? current, int step, bool toEdge)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        var index = -1;
        for (var at = 0; current is not null && at < entries.Count; at++)
        {
            if (ReferenceEquals(entries[at], current))
            {
                index = at;
                break;
            }
        }

        var target = toEdge ? (step < 0 ? 0 : entries.Count - 1)
            : index < 0 ? (step < 0 ? entries.Count - 1 : 0)
            : Math.Clamp(index + step, 0, entries.Count - 1);
        return target == index ? null : entries[target];
    }
}

/// <summary>
/// 「変換」のプロファイルの商品の群の見出し（字と、その群のまとめて確認済みにするボタン。ユーザ判断 2026-11-20・④）。
/// ボタンは群「決めていない」「途中」で、確認待ちが1件以上の時だけ出す。一覧を並べ直すたびに作り直す（数は作った時の値）
/// </summary>
public sealed class ConversionItemGroupHeader
{
    internal ConversionItemGroupHeader(string group, string text, int bulkCount, Action bulk)
    {
        Group = group;
        Text = text;
        BulkCount = bulkCount;
        BulkConfirmCommand = new RelayCommand(bulk, () => BulkCount > 0);
    }

    /// <summary>群の名前（「決めていない」など）。</summary>
    public string Group { get; }

    /// <summary>見出しの字（「決めていない（3）」・絞っている間は「決めていない（1/3）」）。</summary>
    public string Text { get; }

    /// <summary>この群の見えている行の確認待ちのつなぎ先の数。</summary>
    public int BulkCount { get; }

    public bool ShowsBulk => BulkCount > 0;

    public string BulkText => $"まとめて確認済みにする（{BulkCount} 件）";

    public RelayCommand BulkConfirmCommand { get; }

    // 群の入れ物の読み上げの名前に使われる（無いと型の名前が読まれる）
    public override string ToString() => Text;
}
