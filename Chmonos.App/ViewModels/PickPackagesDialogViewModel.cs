using System.IO;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>送る候補の unitypackage 1件。**最初はチェックを付けない**（ユーザ判断 2026-09-13：本来は1つずつ押す類のもの）。</summary>
public sealed class PackageChoiceRow : ViewModelBase
{
    private bool _isChecked;

    public required UnityPackageEntry Package { get; init; }

    /// <summary>
    /// この unitypackage を包んでいる手元のファイル。記録するときのハッシュと種類はここから取る。
    /// 登録したフォルダの中の物は null（フォルダはハッシュも種類も持たない。メモ65-③）。
    /// </summary>
    public required LocalFileRecord? Owner { get; init; }

    public string Name => Package.Name;

    public bool HasFolder => Package.Folder.Length > 0;

    public string FolderText => HasFolder ? $"{(Package.InFolder ? "フォルダ" : "zip")}の中の {Package.Folder}" : string.Empty;

    /// <summary>
    /// 塊に1件だけのとき、見出しの代わりに行に添える塊の名前（「zip：衣装A.zip」）。2件以上の塊の行では空（見出しに出ている）。
    /// 見出しを出さないので、どの zip の物かをここで言う
    /// </summary>
    public string GroupLabel { get; internal set; } = string.Empty;

    public bool HasGroupLabel => GroupLabel.Length > 0;

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

    private bool _isVisible = true;

    /// <summary>探す欄に当たっているか。外れても選んだ物は選んだまま（見えないだけ。送る数にも入る）。</summary>
    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetField(ref _isVisible, value);
    }
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

    /// <summary>
    /// 1件だけの塊は畳まず、見出しを出さずに行を直に見せる（ユーザ判断 2026-11-13「zipの中に一つのunitypackageしか入ってなかったら
    /// 折り畳みにせずに直接選択できるようにして良いと思う」）。1件を畳んでも見渡せる物は増えず、見出しの行と押す手間だけが増える
    /// </summary>
    public bool IsSingle => Rows.Count == 1;

    /// <summary>畳んでいても、中で何件選んでいるかが分かるように。</summary>
    public string CountText => Rows.Count(row => row.IsChecked) is var picked and > 0
        ? $"{Rows.Count} 件中 {picked} 件を選択"
        : $"{Rows.Count} 件";

    internal void NoteChecked() => OnPropertyChanged(nameof(CountText));

    /// <summary>探す欄で中の1件でも当たっているか（当たる物の無い塊は見出しごと隠す）。</summary>
    public bool IsVisible => Rows.Any(row => row.IsVisible);

    internal void NoteVisible() => OnPropertyChanged(nameof(IsVisible));
}

/// <summary>
/// 商品1つぶんの候補。どのファイルを使ったか記録が無く、送れる unitypackage が2つ以上あるときに選ばせる。
///
/// **前は全部送っていた**ので、古い版の zip や、持っている別の種類まで Unity に入った（ユーザ指摘 2026-09-13）。
/// 並びは種類ごと（種類の分かる物を BOOTH の種類の順に）、種類の分からない物は zip ごと（手元のファイルの順）にまとめる。
/// 手元のファイルに種類が付いていることはまず無い（本人のデータ 23 個中 0 個・友人のデータ 324 個中 0 個）ので、ほとんどは zip の名前で分かれる。
/// 種類を名前から推して出すことはしない（外れても気付けない。案 B を採らなかった）。
/// </summary>
public sealed class PackageChoiceSection : ViewModelBase
{
    public required ItemRecord Item { get; init; }

    public string Title => Item.DisplayName;

    public required IReadOnlyList<PackageChoiceGroup> Groups { get; init; }

    /// <summary>探す欄で中の1件でも当たっているか（当たる物の無い商品は名前ごと隠す）。</summary>
    public bool IsVisible => Groups.Any(group => group.IsVisible);

    internal void NoteVisible() => OnPropertyChanged(nameof(IsVisible));

    /// <summary>チェックした物。見えている順（上から）に送る。</summary>
    public IReadOnlyList<PackageChoiceRow> Checked => Groups.SelectMany(group => group.Rows).Where(row => row.IsChecked).ToList();

    public IReadOnlyList<UnityPackageEntry> CheckedPackages => Checked.Select(row => row.Package).ToList();

    /// <summary>記録する行。商品ページの「改変に足して送る」が作る行と同じ形。</summary>
    public IReadOnlyList<ModificationMember> CheckedMembers => Checked
        .Select(row => ModificationSendPlan.MemberFor(Item.Id, row.Owner, row.Package))
        .ToList();

    /// <summary>
    /// 候補が2つ以上あるときだけ作る。1つなら選ぶまでもないので null（今までどおり聞かずに送る）。
    ///
    /// **包みの一覧は item の要約から引く**（<see cref="UnityHandoff.PlacesOf(LocalFileRecord)"/>。2026-09-29）。前は商品の zip を1つずつ開いて
    /// 数えていて、1GB 級の zip が HDD にあると窓が出るまで止まって見えた。要約が無い・欠けているときだけ zip を読むが、
    /// それでも止まらないよう**画面のスレッドで呼ばない**（呼び手が裏へ出す）。
    /// </summary>
    /// <param name="displayOrder">
    /// 設定の「ファイルの表示順」（ユーザ判断 2026-11-11）。商品ページの手元のファイルと同じ順で zip を並べる
    /// （同じ商品の中で、ページと窓でファイルの順が違うと探し直すことになる）。バリエーションの塊は BOOTH の種類の順のまま
    /// </param>
    public static PackageChoiceSection? Build(ItemRecord item, FileSort displayOrder)
    {
        var variations = item.Booth.Variations;
        var candidates = new List<(PackageChoiceRow Row, string Key, string Label, int Order)>();
        var fileOrder = 0;

        // 手元のファイルの表示順に見る。追加した順なら、全部送っていたときの順（UnityImportQueue.PackagesOf）と同じ
        foreach (var file in LocalFileOrder.Sort(item.Local.OwnedFiles, displayOrder))
        {
            var places = UnityHandoff.PlacesOf(file);
            if (places.Count > 0)
            {
                var zip = places[0].Entry.ZipPath;
                var variationIndex = file.VariationId is { } id
                    ? variations.Select((variation, index) => (variation, index)).FirstOrDefault(pair => pair.variation.Id == id) is { variation: not null } found
                        ? found.index
                        : -1
                    : -1;

                var (key, label, order) = variationIndex >= 0
                    ? ($"バリエーション:{file.VariationId}",
                        variations[variationIndex].Name is { Length: > 0 } name ? $"バリエーション：{name}" : DisplayText.NoVariationName,
                        variationIndex)
                    : ($"zip:{zip}", $"zip：{Path.GetFileName(zip)}", variations.Count + fileOrder);

                foreach (var place in places)
                {
                    candidates.Add((new PackageChoiceRow { Package = place.Entry, Owner = file }, key, label, order));
                }
            }

            fileOrder++;
        }

        // 登録したフォルダの中の物（メモ65-③）。種類は分からないので、zip と同じくフォルダの名前で塊にし、zip の後に並べる
        foreach (var folder in item.Local.LocalFolders)
        {
            foreach (var place in UnityHandoff.PlacesOf(folder))
            {
                candidates.Add((new PackageChoiceRow { Package = place.Entry, Owner = null },
                    $"folder:{folder.Path}", $"フォルダ：{Core.Models.DisplayText.FolderName(folder.Path)}",
                    variations.Count + fileOrder));
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
            Groups = candidates
                .GroupBy(candidate => candidate.Key, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.First().Order)
                .Select(group => Group(group.First().Label, group.Select(candidate => candidate.Row).ToList()))
                .ToList(),
        };
    }

    private static PackageChoiceGroup Group(string label, IReadOnlyList<PackageChoiceRow> rows)
    {
        var group = new PackageChoiceGroup { Label = label, Rows = rows };
        if (group.IsSingle)
        {
            rows[0].GroupLabel = label;
        }

        return group;
    }
}

/// <summary>
/// 送る unitypackage を選ぶ窓。送り先の Unity を決めた後に出す（ユーザ指示 2026-09-13：複数開いていたときの選択の後）。
/// チェックした物は、いつもの連続送りの列に積む。
///
/// **探す欄と「すべて選択」「選択を解除」を持つ**（ユーザ指示 2026-11-03）。1つの商品に unitypackage が何十と入っていると、
/// 目で探して1つずつ押すのでは選び切れない。「すべて選択」「選択を解除」は**見えている物だけ**に効く
/// （探して絞った物をまとめて選ぶための物。見えない物まで変えると、絞った意味が無くなる）。
/// </summary>
public sealed class PickPackagesDialogViewModel : ViewModelBase
{
    private readonly int _othersCount;
    private string _filterText = string.Empty;

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
                ? "次の使ったものには、Unityへ送れるものが2つ以上あります。"
                : "次の商品には、Unityへ送れるものが2つ以上あります。")
            + "送るものにチェックを付けてください。上から順に1件ずつ送ります。"
            + (records ? "\n選んだものは記録し、次からは聞かずに送ります。" : string.Empty);

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

        SelectAllCommand = new RelayCommand(() => SetVisibleChecked(true));
        ClearSelectionCommand = new RelayCommand(() => SetVisibleChecked(false));
        ClearFilterCommand = new RelayCommand(() => FilterText = string.Empty);
    }

    /// <summary>探す欄の中身。名前・zip やフォルダの中の場所・塊の名前（zip の名前・バリエーション）で絞る。</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value ?? string.Empty))
            {
                ApplyFilter();
                OnPropertyChanged(nameof(HasFilterText));
            }
        }
    }

    public bool HasFilterText => FilterText.Length > 0;

    /// <summary>探したが1件も当たらなかったか。</summary>
    public bool HasNoMatch => HasFilterText && !VisibleRows.Any();

    /// <summary>見えている物を全部選ぶ。</summary>
    public RelayCommand SelectAllCommand { get; }

    /// <summary>見えている物の選択を全部外す。</summary>
    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand ClearFilterCommand { get; }

    private IEnumerable<PackageChoiceRow> VisibleRows
        => Sections.SelectMany(section => section.Groups).SelectMany(group => group.Rows).Where(row => row.IsVisible);

    private void SetVisibleChecked(bool value)
    {
        foreach (var row in VisibleRows.ToList())
        {
            row.IsChecked = value;
        }
    }

    /// <summary>
    /// 探す語を空白で区切り、**全部の語が**名前・中の場所・塊の名前のどれかに入っている物だけを見せる。
    /// 大文字小文字・かなの種類・全角半角は区別しない（改変を選ぶ窓の探す欄と同じ）。
    /// </summary>
    private void ApplyFilter()
    {
        var words = ModificationSearchText.Words(FilterText);
        foreach (var section in Sections)
        {
            foreach (var group in section.Groups)
            {
                foreach (var row in group.Rows)
                {
                    row.IsVisible = words.All(word =>
                        ModificationSearchText.Contains(row.Name, word)
                        || ModificationSearchText.Contains(row.Package.Folder, word)
                        || ModificationSearchText.Contains(Path.GetFileName(row.Package.ZipPath), word)
                        || ModificationSearchText.Contains(group.Label, word));
                }

                group.NoteVisible();
            }

            section.NoteVisible();
        }

        OnPropertyChanged(nameof(HasNoMatch));
    }

    public string Title { get; }

    /// <summary>決めるボタンの文（既定「送る」）。</summary>
    public string CommitText { get; init; } = "送る";

    /// <summary>
    /// その商品だけ見送るボタンの文。null なら出さない（検索の複数選択で、商品ごとに順に聞くときだけ出す）。
    /// </summary>
    public string? SkipText { get; init; }

    public bool HasSkip => SkipText is not null;

    /// <summary>見送るボタンで閉じたか（「キャンセル」は全体をやめる）。</summary>
    public bool Skipped { get; internal set; }

    /// <summary>
    /// 窓を出す代わりに答える口。**アプリでは null のまま**（窓を出す）。試験が入れる
    /// （窓を出すと答える人がいないので止まる。<see cref="Services.Notice.Intercept"/> と同じ）
    /// </summary>
    internal static Func<PickPackagesDialogViewModel, bool>? Intercept { get; set; }

    /// <summary>選ばせる。「送る」なら true（チェックは各候補の <see cref="PackageChoiceRow.IsChecked"/> に残る）。</summary>
    public static bool Ask(PickPackagesDialogViewModel model)
        => Intercept is { } intercept ? intercept(model) : new Views.PickPackagesDialog(model).ShowDialog() == true;

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
