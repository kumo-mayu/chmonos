using System.IO;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 未確定画面：zipを展開した中身を、元のzipを単位に扱う（ユーザ判断 2026-09-17）。
///
/// **基本は 1zip＝1商品。**中身を1件ずつ登録できると、同じ配布物が何件もの別々の登録に割れる。
/// - 元のzipが商品に登録済みで、今もディスクにある：中身は未確定に出さない（件数だけ上に出す）。
///   zipを消せば、中身は「zipが無い中身」として戻ってくるので、後から紐付け直せる。記録には何も書かず、開くたびに決める
/// - 元のzipが未確定にある：中身の確定・BOOTHに無い商品としての登録は押せず、「元zipで登録」へ案内する（フォルダのまま登録はできる）
/// - 元のzipが一覧に無い（消した・取り込んでいない）：同じzipの中身全件を1つの単位にする
/// - 例外（1つのzipに複数の商品が入っているなど）のために「このファイルだけで登録する」。選び直すと切れる
/// 管理対象から外すは登録ではないので、1件ずつのまま。
/// </summary>
public sealed partial class ResolveViewModel
{
    /// <summary>商品が持っているファイルのパス（開くたびに読み直す）。元のzipが登録済みかを見るのに使う。</summary>
    private HashSet<string> _ownedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>元のzipが登録済みなので出していない中身の件数。</summary>
    public int HiddenByRegisteredZipCount { get; private set; }

    public bool HasHiddenByRegisteredZip => HiddenByRegisteredZipCount > 0;

    /// <summary>黙って減らさない（欠けや推定を隠さない）。</summary>
    public string HiddenByRegisteredZipText => $"登録済みのzipを展開したファイル {HiddenByRegisteredZipCount} 件は出していません";

    /// <summary>商品が持っているファイルのパスを読む。画面のスレッドの外で呼ぶ。</summary>
    private async Task LoadOwnedPathsAsync()
    {
        var loaded = await _services.Store.Items.LoadAllAsync();
        _ownedPaths = loaded.Items
            .SelectMany(item => item.Local.OwnedFiles)
            .SelectMany(file => file.Paths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>元のzipが商品に登録されていて、今もディスクにある中身か（未確定に出さない）。</summary>
    private bool IsCoveredByRegisteredZip(Core.Models.UnresolvedFile file, Core.Scanning.ArchiveOrigin? origin)
        => origin is not null
           && file.Paths.Count > 0
           && !string.Equals(origin.ArchivePath, file.Paths[0], StringComparison.OrdinalIgnoreCase)
           && _ownedPaths.Contains(origin.ArchivePath)
           && OriginExists(origin.ArchivePath);

    /// <summary>
    /// zipを確定・登録した直後に、そのzipの中身を一覧から外す。開いたときにしか見ていなかったので、zipの行が消えた後に
    /// 残った中身が「zipが一覧に無い中身」として別の商品にも登録できてしまった（ユーザ判断 2026-09-17）。
    /// </summary>
    private async Task HideCoveredContentsAsync()
    {
        try
        {
            await LoadOwnedPathsAsync();
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            Core.Diagnostics.AppLog.Error("未確定の画面：登録済みのzipの中身を外す", exception);
            return;
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

        if (selectedWasCovered)
        {
            Selected = FilesView.Cast<UnresolvedRow>().FirstOrDefault();
        }
    }

    // ---- 外した直後に戻す（ユーザ判断 2026-09-17：戻す場所が設定の「隠したもの」だけだった） ----

    private List<Core.Models.UnresolvedFile> _lastExcluded = [];

    public bool HasUndoExclude => _lastExcluded.Count > 0;

    public string UndoExcludeText => $"外した {_lastExcluded.Count} 件を戻す";

    /// <summary>外した直後に呼ぶ。次に外すまで、上の帯に「戻す」を出す。</summary>
    private void RememberExcluded(IEnumerable<UnresolvedRow> rows)
    {
        _lastExcluded = rows.Select(row => row.File).ToList();
        OnPropertyChanged(nameof(HasUndoExclude));
        OnPropertyChanged(nameof(UndoExcludeText));
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

        Reload();
        var first = files[0].Hash;
        Selected = Files.FirstOrDefault(row => string.Equals(row.File.Hash, first, StringComparison.OrdinalIgnoreCase)) ?? Selected;
        StatusText = $"{files.Count} 件を未確定に戻しました。";
        OnPropertyChanged(nameof(HasStatus));
    }

    /// <summary>選んだファイルの場所をエクスプローラで開く（画面をまたいで同じ開き方）。</summary>
    private void RevealSelected() => Services.Shell.Reveal(Selected?.File.Paths.FirstOrDefault());

    private bool _singleFileOnly;

    /// <summary>「このファイルだけで登録する」。zipの単位にせず、選んだ1件だけを登録の対象にする。</summary>
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

    /// <summary>トグルを出すか。zipを展開した中身を選んでいるときだけ意味がある。</summary>
    public bool CanChooseSingleFile => Selected?.IsExpandedContent == true;

    private bool IsZipListed(UnresolvedRow row)
        => row.Origin is { } origin
           && Files.Any(other => other.IsOriginArchive
               && string.Equals(other.File.Paths[0], origin.ArchivePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>元のzipが未確定にあるので、中身の登録を止めているか。</summary>
    public bool IsBlockedByListedZip => Selected is { IsExpandedContent: true } row && !SingleFileOnly && IsZipListed(row);

    public string BlockedByZipText =>
        "元のzipが未確定にあります。「元zipで登録」でzipを登録してください（フォルダのまま登録もできます）。"
        + "このファイルだけを登録するときは「このファイルだけで登録する」を入れてください。";

    /// <summary>
    /// 選んだ行とトグルから、登録の単位を決め直す。元のzipが一覧に無い中身なら同じzipの中身全件を束として立てる。
    /// </summary>
    private void ApplyZipUnit()
    {
        if (Selected is { IsExpandedContent: true } row)
        {
            var mates = Files.Count(other => other.IsExpandedContent
                && string.Equals(other.GroupKey, row.GroupKey, StringComparison.OrdinalIgnoreCase));
            ActiveGroup = !SingleFileOnly && !IsZipListed(row) && mates > 1 ? row.GroupKey : null;
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

        var keys = checkedRows.Where(row => row.IsExpandedContent)
            .Select(row => row.GroupKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expanded = checkedRows
            .Concat(Files.Where(row => row.IsExpandedContent && keys.Contains(row.GroupKey)))
            .Distinct()
            .ToList();
        return (expanded, null);
    }
}
