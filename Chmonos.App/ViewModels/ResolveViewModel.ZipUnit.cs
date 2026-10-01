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
/// - 例外（1つのzipに複数の商品が入っているなど）のために「このファイルだけを扱う」。選び直すと切れる
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

    // ---- 外した直後に戻す（ユーザ判断 2026-09-17：戻す場所が設定の「隠したもの」だけだった） ----

    private List<Core.Models.UnresolvedFile> _lastExcluded = [];

    public bool HasUndoExclude => _lastExcluded.Count > 0;

    public string UndoExcludeText => $"外した {_lastExcluded.Count} 件を戻す";

    /// <summary>
    /// フォルダビューに組み込んだときは上の帯が隠れるので、「戻す」と出していない件数を右側の上に出す
    /// （ユーザ判断 2026-09-17：組み込んだ側では外した直後に戻せず、減った理由も見えなかった）。
    /// </summary>
    public bool ShowsInlineUndo => IsEmbedded && HasUndoExclude;

    public bool ShowsInlineHidden => IsEmbedded && HasHiddenByRegisteredZip;

    /// <summary>外した直後に呼ぶ。次に外すまで、上の帯に「戻す」を出す。</summary>
    private void RememberExcluded(IEnumerable<UnresolvedRow> rows)
    {
        _lastExcluded = rows.Select(row => row.File).ToList();
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
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.UndoExclude(files));
        _lastExcluded = [];
        OnPropertyChanged(nameof(HasUndoExclude));
        OnPropertyChanged(nameof(UndoExcludeText));
        OnPropertyChanged(nameof(ShowsInlineUndo));

        await ReloadRowsAsync();

        // 除外したときと同じく、ナビの件数も合わせる。一覧だけ読み直すと、次に画面を移るまで減ったままの数が残っていた
        _main.RefreshBadges();

        var first = files[0].Hash;
        Selected = Files.FirstOrDefault(row => string.Equals(row.File.Hash, first, StringComparison.OrdinalIgnoreCase)) ?? Selected;
        StatusText = $"{files.Count} 件を未確定に戻しました。";
        OnPropertyChanged(nameof(HasStatus));
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

    /// <summary>「このファイルだけを扱う」。zipやフォルダの単位にせず、選んだ1件だけを登録・管理対象から除外するの対象にする。</summary>
    public bool SingleFileOnly
    {
        get => _singleFileOnly;
        set
        {
            if (SetField(ref _singleFileOnly, value))
            {
                ApplyZipUnit();
            }
        }
    }

    /// <summary>
    /// トグルを出すか。zipを展開した中身か、zipが無いフォルダのファイルを選んでいるときだけ意味がある
    /// （どちらもまとまりが単位で、登録にも「管理対象から除外する」にも効く・ユーザ判断 2026-09-17）。
    /// </summary>
    public bool CanChooseSingleFile => Selected is { IsExpandedContent: true } or { IsArchiveContent: true };

    private bool IsZipListed(UnresolvedRow row)
        => row.Origin is { } origin
           && Files.Any(other => other.IsOriginArchive
               && string.Equals(other.File.Paths[0], origin.ArchivePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>元のzipが未確定にあるので、中身の登録を止めているか。</summary>
    public bool IsBlockedByListedZip => Selected is { IsExpandedContent: true } row && !SingleFileOnly && IsZipListed(row);

    public string BlockedByZipText =>
        "元のzipが未確定にあります。「元zipで登録」でzipを登録してください（フォルダのまま登録もできます）。"
        + "1件だけを扱うときは「このファイルだけを扱う」を入れてください。";

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
            return (checkedRows, "元のzipが未確定にある中身が含まれています。「元zipで登録」でzipを登録してください。");
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
