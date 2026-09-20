using System.IO;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>送る候補の unitypackage 1件。**最初はチェックを付けない**（ユーザ判断 2026-09-13：本来は1つずつ押す類のもの）。</summary>
public sealed class PackageChoiceRow : ViewModelBase
{
    private bool _isChecked;

    public required UnityPackageEntry Package { get; init; }

    /// <summary>この unitypackage を包んでいる手元のファイル。記録するときのハッシュと種類はここから取る。</summary>
    public required LocalFileRecord Owner { get; init; }

    public string Name => Package.Name;

    public bool HasFolder => Package.Folder.Length > 0;

    public string FolderText => HasFolder ? $"zip の中の {Package.Folder}" : string.Empty;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetField(ref _isChecked, value))
            {
                CheckedChanged?.Invoke();
            }
        }
    }

    internal Action? CheckedChanged { get; set; }
}

/// <summary>
/// 横に添えるもの（種類、無ければ zip の名前）が同じ塊。見出しで畳める（ユーザ指示 2026-09-13）。
/// </summary>
public sealed class PackageChoiceGroup : ViewModelBase
{
    private bool _isExpanded = true;

    public required string Label { get; init; }

    public required IReadOnlyList<PackageChoiceRow> Rows { get; init; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandGlyph));
            }
        }
    }

    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    /// <summary>畳んでいても、中で何件選んでいるかが分かるように。</summary>
    public string CountText => Rows.Count(row => row.IsChecked) is var picked and > 0
        ? $"{Rows.Count} 件（{picked} 件を選択）"
        : $"{Rows.Count} 件";

    internal void NoteChecked() => OnPropertyChanged(nameof(CountText));
}

/// <summary>
/// 商品1つぶんの候補。どのファイルを使ったか記録が無く、送れる unitypackage が2つ以上あるときに選ばせる。
///
/// **前は全部送っていた**ので、古い版の zip や、持っている別の種類まで Unity に入った（ユーザ指摘 2026-09-13）。
/// 並びは種類ごと（種類の分かる物を BOOTH の種類の順に）、種類の分からない物は zip ごと（手元のファイルの順）にまとめる。
/// 手元のファイルに種類が付いていることはまず無い（本人のデータ 23 個中 0 個・友人のデータ 324 個中 0 個）ので、ほとんどは zip の名前で分かれる。
/// 種類を名前から推して出すことはしない（外れても気付けない。案 B を採らなかった）。
/// </summary>
public sealed class PackageChoiceSection
{
    public required ItemRecord Item { get; init; }

    public string Title => Item.DisplayName;

    public required IReadOnlyList<PackageChoiceGroup> Groups { get; init; }

    /// <summary>改変の中の位置。改変から送るとき、選んだファイルをこの行に記録する。</summary>
    public int? MemberIndex { get; init; }

    /// <summary>チェックした物。見えている順（上から）に送る。</summary>
    public IReadOnlyList<PackageChoiceRow> Checked => Groups.SelectMany(group => group.Rows).Where(row => row.IsChecked).ToList();

    public IReadOnlyList<UnityPackageEntry> CheckedPackages => Checked.Select(row => row.Package).ToList();

    /// <summary>記録する行。商品ページの「改変に足して送る」が作る行と同じ形。</summary>
    public IReadOnlyList<ModificationMember> CheckedMembers => Checked
        .Select(row => new ModificationMember
        {
            ItemId = Item.Id,
            VariationId = row.Owner.VariationId,
            FileHash = row.Owner.Hash,
            Package = row.Package.EntryPath,
        })
        .ToList();

    /// <summary>候補が2つ以上あるときだけ作る。1つなら選ぶまでもないので null（今までどおり聞かずに送る）。</summary>
    public static PackageChoiceSection? Build(ItemRecord item, int? memberIndex = null)
    {
        var variations = item.Booth.Variations;
        var candidates = new List<(PackageChoiceRow Row, string Key, string Label, int Order)>();
        var fileOrder = 0;

        // 手元のファイルの順に見る（全部送っていたときの順 UnityImportQueue.PackagesOf と同じ）
        foreach (var file in item.Local.OwnedFiles)
        {
            var zip = file.Paths.FirstOrDefault(File.Exists);
            if (zip is not null && zip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var variationIndex = file.VariationId is { } id
                    ? variations.Select((variation, index) => (variation, index)).FirstOrDefault(pair => pair.variation.Id == id) is { variation: not null } found
                        ? found.index
                        : -1
                    : -1;

                var (key, label, order) = variationIndex >= 0
                    ? ($"バリエーション:{file.VariationId}",
                        $"バリエーション：{(variations[variationIndex].Name is { Length: > 0 } name ? name : "名前が分かりません")}",
                        variationIndex)
                    : ($"zip:{zip}", $"zip：{Path.GetFileName(zip)}", variations.Count + fileOrder);

                foreach (var package in UnityHandoff.FindPackages(zip))
                {
                    candidates.Add((new PackageChoiceRow { Package = package with { ZipHash = file.Hash }, Owner = file }, key, label, order));
                }
            }

            fileOrder++;
        }

        if (candidates.Count < 2)
        {
            return null;
        }

        return new PackageChoiceSection
        {
            Item = item,
            MemberIndex = memberIndex,
            Groups = candidates
                .GroupBy(candidate => candidate.Key, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.First().Order)
                .Select(group => new PackageChoiceGroup
                {
                    Label = group.First().Label,
                    Rows = group.Select(candidate => candidate.Row).ToList(),
                })
                .ToList(),
        };
    }
}

/// <summary>
/// 送る unitypackage を選ぶ窓。送り先の Unity を決めた後に出す（ユーザ指示 2026-09-13：複数開いていたときの選択の後）。
/// チェックした物は、いつもの連続送りの列に積む。
/// </summary>
public sealed class PickPackagesDialogViewModel : ViewModelBase
{
    private readonly int _othersCount;

    /// <param name="headingText">どこへ送るか（紐付けと違うプロジェクトへ送るときの断りも含めて呼ぶ側で作る）。</param>
    /// <param name="othersCount">選ぶまでもなく送る物の数（候補が1つの商品・ファイルを記録済みの使ったもの）。</param>
    /// <param name="records">選んだ物を改変の使ったものとして記録するか（改変から送るとき・ユーザ判断 2026-09-13）。</param>
    public PickPackagesDialogViewModel(
        string title,
        string headingText,
        IReadOnlyList<PackageChoiceSection> sections,
        int othersCount,
        bool records)
    {
        Title = title;
        HeadingText = headingText;
        Sections = sections;
        _othersCount = othersCount;

        NoteText = (records
                ? "次の使ったものは、どのファイルを使ったかの記録が無く、Unityへ送れるものが2つ以上あります。"
                : "次の商品には、Unityへ送れるものが2つ以上あります。")
            + "送る物にチェックを付けてください。チェックした物を上から順に積み、1件ずつ取り込み画面を出します。チェックの無い商品は送りません。"
            + (records ? "\n選んだ物は、改変の使ったものとして記録します（次からは聞かずにそれを送ります）。" : string.Empty);

        foreach (var group in sections.SelectMany(section => section.Groups))
        {
            foreach (var row in group.Rows)
            {
                row.CheckedChanged = () =>
                {
                    group.NoteChecked();
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(CanCommit));
                    OnPropertyChanged(nameof(CommitHint));
                };
            }
        }
    }

    public string Title { get; }

    public string HeadingText { get; }

    public string NoteText { get; }

    public IReadOnlyList<PackageChoiceSection> Sections { get; }

    private int CheckedCount => Sections.Sum(section => section.Checked.Count);

    public string SummaryText => _othersCount > 0
        ? $"{CheckedCount} 件を選んでいます。ほかの {_othersCount} 件は、いつもどおり送ります。"
        : $"{CheckedCount} 件を選んでいます。";

    public bool CanCommit => CheckedCount + _othersCount > 0;

    /// <summary>押せないときに、何が足りないかを書く（`ui-dialogs.md`・E9）。</summary>
    public string CommitHint => CanCommit ? string.Empty : "送るものを1つ以上選んでください。";
}
