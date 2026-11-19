using System.IO;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 未確定画面：zipを展開した中身を、元のzipを単位に扱う（ユーザ判断 2026-09-17）。
///
/// **基本は 1zip＝1商品。**中身を1件ずつ登録できると、同じ配布物が何件もの別々の登録に割れる。
/// - 元のzipが商品に登録済みで、今もディスクにある：中身は未確定に出さない（件数だけ上に出す）。
///   zipを消せば、中身は「zipが無い中身」として戻ってくるので、後から紐付け直せる。記録には何も書かず、開くたびに決める
/// - 元のzipが未確定にある：中身の確定・BOOTHに無い商品としての登録は押せず、「元zipで登録」へ案内する（フォルダのまま登録はできる）
/// - 元のzipが一覧に無い（消した・取り込んでいない）：同じzipの中身全件を1つの単位にする
/// - 例外（1つのzipに複数の商品が入っているなど）のために「このファイルだけを扱う」。同じ束の中で選び直しても保ち、別の束へ移ると切れる（ユーザ判断 2026-11-18）
/// - zipが無いフォルダのファイルも同じく、同じフォルダのファイル全件を単位にする
/// 単位は確定・BOOTHに無い商品としての登録・管理対象から除外するのどれにも効く（ユーザ判断 2026-09-17）。
/// </summary>
public sealed partial class ResolveViewModel
{
    /// <summary>商品が持っているファイルのパス（開くたびに読み直す）。元のzipが登録済みかを見るのに使う。</summary>
    private HashSet<string> _ownedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>元のzipが登録済みなので出していない中身の件数。</summary>
    public int HiddenByRegisteredZipCount { get; private set; }

    public bool HasHiddenByRegisteredZip => HiddenByRegisteredZipCount > 0;

    /// <summary>黙って減らさない（欠けや推定を隠さない）。</summary>
    public string HiddenByRegisteredZipText => $"登録済みのzipを展開したファイル {HiddenByRegisteredZipCount} 件は表示していません";

    /// <summary>
    /// 商品が持っているファイルのパスを集める。主画面が読み込み済みの一覧を使い、まだ読み込んでいなければファイルから読む
    /// （2000件で全商品を読むと約0.6秒。開くたびに整理の処理と合わせて2回読んでいた・2026-09-17 に測った）。
    /// </summary>
    private async Task LoadOwnedPathsAsync(CancellationToken token = default)
    {
        var items = _main.Search.SnapshotItems();
        if (items.Count == 0)
        {
            items = (await _services.Store.Items.LoadAllAsync(cancellationToken: token)).Items;
        }

        _ownedPaths = items
            .SelectMany(item => item.Local.OwnedFiles)
            .SelectMany(file => file.Paths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 「同じ場所にあった商品」の候補に名前を出すため（AddSamePathCandidates）。同じ一覧から引くので、読む回数は増えない。
        // 手で直した JSON で同じ ID が2件あっても落ちないよう、先の1件を使う
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            names.TryAdd(item.Id, item.DisplayName);
        }

        _itemNames = names;
    }

    /// <summary>手元の商品の名前（商品ID → 名前。開くたびに読み直す）。</summary>
    private Dictionary<string, string> _itemNames = new(StringComparer.Ordinal);

    /// <summary>元のzipが商品に登録されていて、今もディスクにある中身か（未確定に出さない）。</summary>
    private bool IsCoveredByRegisteredZip(Core.Models.UnresolvedFile file, Core.Scanning.ArchiveOrigin? origin)
        => IsCoveredByRegisteredZip(file, origin, _ownedPaths, _originExists);

    /// <param name="owned">商品が持っているファイルの場所。</param>
    /// <param name="originExists">元のzipが今もあるかの控え（zipごとに1回だけ見る）。読み直しは裏で組むので、呼び手が渡す。</param>
    private static bool IsCoveredByRegisteredZip(
        Core.Models.UnresolvedFile file,
        Core.Scanning.ArchiveOrigin? origin,
        IReadOnlySet<string> owned,
        Dictionary<string, bool> originExists)
        => origin is not null
           && file.Paths.Count > 0
           && !string.Equals(origin.ArchivePath, file.Paths[0], StringComparison.OrdinalIgnoreCase)
           && owned.Contains(origin.ArchivePath)
           && OriginExists(origin.ArchivePath, originExists);

    /// <summary>
    /// zipを確定・登録した直後に、そのzipの中身を一覧から外す。開いたときにしか見ていなかったので、zipの行が消えた後に
    /// 残った中身が「zipが一覧に無い中身」として別の商品にも登録できてしまった（ユーザ判断 2026-09-17）。
    /// </summary>
    /// <param name="registered">今登録した行。そのファイルの場所を登録済みに足すだけで、全商品は読み直さない（2000件で約0.6秒かかっていた）。</param>
    private void HideCoveredContents(IEnumerable<UnresolvedRow> registered)
    {
        foreach (var path in registered.SelectMany(row => row.File.Paths))
        {
            _ownedPaths.Add(path);
        }

        var covered = Files.Where(row => IsCoveredByRegisteredZip(row.File, row.Origin)).ToList();
        if (covered.Count == 0)
        {
            return;
        }

        var selectedWasCovered = Selected is not null && covered.Contains(Selected);
        RemoveRows(covered);
        HiddenByRegisteredZipCount += covered.Count;
        OnPropertyChanged(nameof(HasHiddenByRegisteredZip));
        OnPropertyChanged(nameof(HiddenByRegisteredZipText));
        OnPropertyChanged(nameof(ShowsInlineHidden));

        if (selectedWasCovered)
        {
            Selected = FilesView.Cast<UnresolvedRow>().FirstOrDefault();
        }
    }

    // ---- 除外の解除（open.md 132・ユーザ判断 2026-11-19） ----
    // 前は外した直後だけ「外した n 件を戻す」を出していた（ユーザ判断 2026-09-17）。直前の物しか戻せず、前に外した物は設定の奥まで
    // 行かないと戻せなかったので、除外が1件でもあれば「除外の解除…」を出し、設定と同じ除外の一覧（新しい順・探す・選んでまとめて解除）を窓に出す

    private int _excludedCount;
    private int _excludedCountTicket;

    /// <summary>除外の記録の件数（「除外の解除…」を出すか）。一覧を読むたびと、除外した後に読み直す。</summary>
    public bool HasExcluded => _excludedCount > 0;

    /// <summary>
    /// フォルダビューに組み込んだときは上の帯が隠れるので、「除外の解除…」を右側の上に出す
    /// （ユーザ判断 2026-09-17：組み込んだ側では外した直後に戻せず、減った理由も見えなかった）。
    /// </summary>
    public bool ShowsInlineExclusions => IsEmbedded && HasExcluded;

    public bool ShowsInlineHidden => IsEmbedded && HasHiddenByRegisteredZip;

    // 外した直後だけ、元の記録のまま戻すボタンを残す（open.md 132・ユーザ判断 2026-11-20）。
    // 「除外の解除…」は今のファイルから記録を組み直すので、初めて見た日時・同じパスの商品ID・「BOOTH に無い」の印が消える。
    // 外した直後は UndoExclude で外す前の記録をそのまま戻せる。別の操作（登録・次の除外・解除の窓・一覧の読み直し）と画面を離れた時に消す
    private List<Core.Models.UnresolvedFile> _lastExcluded = [];

    /// <summary>今回除外の記録に足したハッシュ。戻すときはこれだけを除外から消す（前から除外していた物の記録を残す。2026-10-05）。</summary>
    private IReadOnlyCollection<string> _lastExcludedHashes = [];

    public bool HasUndoExclude => _lastExcluded.Count > 0;

    public string UndoExcludeText => $"外した {_lastExcluded.Count} 件を戻す";

    /// <summary>組み込んだ画面では上の帯が隠れるので、右側の上にも出す。</summary>
    public bool ShowsInlineUndo => IsEmbedded && HasUndoExclude;

    /// <summary>外した直後に呼ぶ。ほかの操作で <see cref="ForgetExcluded"/> されるまで、「外した n 件を戻す」を出す。</summary>
    private void RememberExcluded(IEnumerable<UnresolvedRow> rows, Core.Commands.CommandResult excluded)
    {
        _lastExcluded = rows.Select(row => row.File).ToList();
        _lastExcludedHashes = excluded is Core.Commands.CommandResult.FilesExcluded { AddedHashes: var added } ? added : [];
        NotifyUndoExcludeChanged();
    }

    /// <summary>別の操作をした後は、元の記録で戻す道を閉じる（古い記録で戻すと、その間の登録と食い違う）。</summary>
    private void ForgetExcluded()
    {
        if (_lastExcluded.Count == 0)
        {
            return;
        }

        _lastExcluded = [];
        _lastExcludedHashes = [];
        NotifyUndoExcludeChanged();
    }

    private void NotifyUndoExcludeChanged()
    {
        OnPropertyChanged(nameof(HasUndoExclude));
        OnPropertyChanged(nameof(UndoExcludeText));
        OnPropertyChanged(nameof(ShowsInlineUndo));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private async Task UndoExcludeAsync()
    {
        if (_lastExcluded.Count == 0)
        {
            return;
        }

        var files = _lastExcluded;
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.UndoExclude(files, _lastExcludedHashes));
        ForgetExcluded();
        RefreshExcludedCountAsync().Forget();

        await ReloadRowsAsync();

        // 除外したときと同じく、ナビの件数も合わせる
        _main.RefreshBadges();

        var first = files[0].Hash;
        Selected = Files.FirstOrDefault(row => string.Equals(row.File.Hash, first, StringComparison.OrdinalIgnoreCase)) ?? Selected;
        // 「戻す」は一覧の見出しの近くにあるので、結果もそこに出す
        ListNoticeText = $"{files.Count} 件を未確定に戻しました。";
    }

    /// <summary>除外の件数を読み直す。記録は上限なく溜まるので画面のスレッドの外で読む（LoadExcludedAsync）。古い答えは捨てる。</summary>
    private async Task RefreshExcludedCountAsync()
    {
        var ticket = ++_excludedCountTicket;
        var count = (await _services.SettingsStore.LoadExcludedAsync()).Count;
        if (ticket != _excludedCountTicket)
        {
            return;
        }

        _excludedCount = count;
        OnPropertyChanged(nameof(HasExcluded));
        OnPropertyChanged(nameof(ShowsInlineExclusions));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 「除外の解除…」。窓の中で解除するたびに除外の一覧を読み直し、閉じたら未確定の一覧とナビの件数を合わせる
    /// （窓の中で解除した物は、その場で未確定の記録に戻っている）。
    /// </summary>
    private async Task OpenExclusionsAsync()
    {
        ForgetExcluded();
        var lifted = false;
        ExclusionListViewModel? list = null;
        list = ExclusionLifting.ForExcluded(_services, async () =>
        {
            lifted = true;
            list!.SetEntries(ExclusionLifting.Entries(await _services.SettingsStore.LoadExcludedAsync()));
        });
        list.SetEntries(ExclusionLifting.Entries(await _services.SettingsStore.LoadExcludedAsync()));

        await ExclusionDialog.ShowAsync(list);

        if (lifted)
        {
            await ReloadRowsAsync();
            _main.RefreshBadges();
            ListNoticeText = list.Note;
        }
        else
        {
            await RefreshExcludedCountAsync();
        }
    }

    /// <summary>選んだファイルの場所をエクスプローラで開く（画面をまたいで同じ開き方）。</summary>
    private void RevealSelected() => ExplorerReveal.RevealAsync(Selected?.File.Paths.FirstOrDefault()).Forget();

    private string _copyNote = string.Empty;

    /// <summary>コピーした直後の一言。押した場所（「分かっていること」）の横に出し、選び直すと消す。</summary>
    public string CopyNote
    {
        get => _copyNote;
        private set
        {
            if (SetField(ref _copyNote, value))
            {
                OnPropertyChanged(nameof(HasCopyNote));
            }
        }
    }

    public bool HasCopyNote => CopyNote.Length > 0;

    /// <summary>
    /// ファイル名をクリップボードへ（ユーザ指示 2026-09-29：BOOTHやブラウザで探すときに貼りたい）。
    /// **名前だけを写す**（フォルダまで写すと、検索欄に貼ったときに消す手間が要る）。ほかの画面の「リンクをコピー」と同じく、
    /// 他のアプリがクリップボードを掴んでいて入らなければ、もう一度押してもらう
    /// </summary>
    private void CopyFileName()
    {
        if (Selected is not { } row)
        {
            return;
        }

        CopyNote = Services.ClipboardText.TrySet(row.FileName)
            ? "コピーしました"
            : "コピーできませんでした。もう一度押してください。";
    }

    private bool _singleFileOnly;

    /// <summary>「このファイルだけ」を選んだ束の鍵。同じ束の別の行を選んでも「このファイルだけ」を保つために覚える。</summary>
    private string? _singleFileGroupKey;

    /// <summary>「このファイルだけを扱う」。zipやフォルダの単位にせず、選んだ1件だけを登録・管理対象から除外するの対象にする。</summary>
    public bool SingleFileOnly
    {
        get => _singleFileOnly;
        set
        {
            if (SetField(ref _singleFileOnly, value))
            {
                _singleFileGroupKey = value && CanChooseSingleFile ? Selected!.GroupKey : null;
                ApplyZipUnit();
            }
        }
    }

    /// <summary>選び直した行で「このファイルだけ」を保つか。前に「このファイルだけ」にした束と同じ束の行なら保つ。</summary>
    private bool KeepsSingleFile(UnresolvedRow? row)
        => _singleFileGroupKey is { } key
           && row is { IsExpandedContent: true } or { IsArchiveContent: true }
           && string.Equals(row.GroupKey, key, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 束を明示して選んだとき（「まとめて扱う」・束の見出し）は「このファイルだけ」を下ろす。
    /// 下ろさないと、帯は「このファイルだけ」なのに対象は束、という食い違いが残る。
    /// </summary>
    private void DropSingleFile()
    {
        _singleFileGroupKey = null;
        if (_singleFileOnly)
        {
            _singleFileOnly = false;
            OnPropertyChanged(nameof(SingleFileOnly));
        }
    }

    /// <summary>
    /// トグルを出すか。zipを展開した中身か、zipが無いフォルダのファイルを選んでいるときだけ意味がある
    /// （どちらもまとまりが単位で、登録にも「管理対象から除外する」にも効く・ユーザ判断 2026-09-17）。
    /// </summary>
    public bool CanChooseSingleFile => Selected is { IsExpandedContent: true } or { IsArchiveContent: true };

    /// <summary>各行に「元のzipが一覧にあるか」を持たせ直す。一覧が変わるたびに呼ぶ（束ごとに何十行もあり、行ごとに探すと重い）。</summary>
    private void RefreshOriginZipListed()
    {
        var zipPaths = new HashSet<string>(
            Files.Where(row => row.IsOriginArchive).Select(row => row.File.Paths[0]), StringComparer.OrdinalIgnoreCase);
        foreach (var row in Files)
        {
            row.IsOriginZipListed = row.Origin is { } origin && zipPaths.Contains(origin.ArchivePath);
        }
    }

    private bool IsZipListed(UnresolvedRow row)
        => row.Origin is { } origin
           && Files.Any(other => other.IsOriginArchive
               && string.Equals(other.File.Paths[0], origin.ArchivePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>元のzipが未確定にあるので、中身の登録を止めているか。</summary>
    public bool IsBlockedByListedZip => Selected is { IsExpandedContent: true } row && !SingleFileOnly && IsZipListed(row);

    /// <summary>帯の3行目に出す、登録できない理由と次の一手。切り替えは同じ帯にある。</summary>
    public string BlockedByZipText => "元のzipが未確定にあります。元zipで登録するか、このファイルだけにしてください。";

    /// <summary>
    /// 選んだ行とトグルから、登録の単位を決め直す。元のzipが一覧に無い中身なら同じzipの中身全件を束として立てる。
    /// </summary>
    private void ApplyZipUnit()
    {
        if (Selected is { IsExpandedContent: true } row)
        {
            var mates = Files.Count(other => other.IsExpandedContent
                && string.Equals(other.GroupKey, row.GroupKey, StringComparison.OrdinalIgnoreCase));
            // 元のzipが未確定にあるときも束を立てる。登録は止めているが、「管理対象から除外する」は中身全件に効かせる
            ActiveGroup = !SingleFileOnly && mates > 1 ? row.GroupKey : null;
        }
        else if (Selected is { IsArchiveContent: true } folderRow)
        {
            // zipが無いフォルダのファイルは、同じフォルダのファイル全件を単位にする（確定・登録・管理対象から除外するのどれも）
            var mates = Files.Count(other => other.IsArchiveContent
                && string.Equals(other.GroupKey, folderRow.GroupKey, StringComparison.OrdinalIgnoreCase));
            ActiveGroup = !SingleFileOnly && mates > 1 ? folderRow.GroupKey : null;
        }

        OnPropertyChanged(nameof(CanChooseSingleFile));
        OnPropertyChanged(nameof(IsBlockedByListedZip));
        OnPropertyChanged(nameof(AssignOutcomeText));
        RaiseTargetChanged();
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// まとめて確定するときの単位を揃える。元のzipが未確定にある中身が混ざっていれば止め（zipで登録する）、
    /// zipが一覧に無い中身は同じzipの中身全件まで広げる。止めるときは理由を返す。
    /// </summary>
    private (List<UnresolvedRow> Targets, string? Blocked) ExpandToZipUnits(List<UnresolvedRow> checkedRows)
    {
        if (checkedRows.Any(row => row.IsExpandedContent && IsZipListed(row)))
        {
            return (checkedRows, "元のzipが未確定にある中身が含まれています。zipの行を選んで登録してください。");
        }

        // zipの中身は同じzipの中身全件、zipが無いフォルダのファイルは同じフォルダのファイル全件まで広げる
        var keys = checkedRows.Where(row => row.IsExpandedContent || row.IsArchiveContent)
            .Select(row => row.GroupKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expanded = checkedRows
            .Concat(Files.Where(row => (row.IsExpandedContent || row.IsArchiveContent) && keys.Contains(row.GroupKey)))
            .Distinct()
            .ToList();
        return (expanded, null);
    }
}
