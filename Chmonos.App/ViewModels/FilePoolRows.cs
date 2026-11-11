using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>ファイルの割り振り（plan 30）の左の一覧の1行：選んだ商品1件と、その商品に付いているファイル。</summary>
public sealed class FilePoolItemRow : ViewModelBase
{
    private bool _isSelected;
    private bool _isExpanded = true;
    private IReadOnlyList<FilePoolHeldFile> _files = [];

    public FilePoolItemRow(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; private set; }

    /// <summary>
    /// 行の頭の商品の絵（ユーザ指摘 2026-11-11「選んだ商品にはサムネを着けましょう」）。通知の行と同じ作り：画面が行を作って読んだときに初めて、
    /// 絵の場所を裏で決めて縮めて読む（左の一覧は仮想化してあり、見えない行は何も読まない）。絵が無い商品は頭文字。
    /// 行は読み直しでも使い回すので、場所を決め直さない
    /// </summary>
    public ResultThumbnail? Picture { get; init; }

    /// <summary>付ける先として選んでいるか。色と札で示すだけで、下のファイルの開閉（<see cref="IsExpanded"/>）とは連動しない。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>
    /// 付いているファイルを下に並べるか。既定は広げる（ユーザ指摘 2026-11-11「選択したものだけが開くのではなく全て開いたり個別で閉じたり」）。
    /// 付ける先とは別に持つ：付ける先を選び直すたびに開閉が変わると、見比べていた商品の行が畳まれて見失う。
    /// 行は読み直しでも使い回すので、開閉はここに持てば残る
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandName));
            }
        }
    }

    /// <summary>三角の読み上げの名前（押すと起きること）。</summary>
    public string ExpandName => IsExpanded ? $"{Name}のファイルを折りたたむ" : $"{Name}のファイルを広げる";

    /// <summary>この商品に付いているファイル（外した・古い版は除く。所持の答えと同じ）。</summary>
    public IReadOnlyList<FilePoolHeldFile> Files
    {
        get => _files;
        private set
        {
            if (SetField(ref _files, value))
            {
                OnPropertyChanged(nameof(FileCountText));
                OnPropertyChanged(nameof(HasFiles));
            }
        }
    }

    public bool HasFiles => Files.Count > 0;

    public string FileCountText => Files.Count == 0 ? "ファイルなし" : $"ファイル {Files.Count} 個";

    /// <summary>行を押すと何が起きるか（読み上げの名前）。</summary>
    public string OpenName => $"{Name}を選び、メインの画面で開く";

    internal void Update(ItemRecord item)
    {
        if (Name != item.DisplayName)
        {
            Name = item.DisplayName;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(OpenName));
            OnPropertyChanged(nameof(ExpandName));
        }

        Files = item.Local.OwnedFiles
            .Select(file => new FilePoolHeldFile(
                item.Id,
                item.DisplayName,
                file.Hash,
                file.Paths.Count > 0 ? System.IO.Path.GetFileName(file.Paths[0]) : FilePoolViewModel.UnnamedFileText,
                DisplayText.Size(file.SizeBytes)))
            .OrderBy(file => file.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <summary>左の一覧で、商品の下に並べる付いているファイル1つ（「外す」の行）。</summary>
public sealed record FilePoolHeldFile(string ItemId, string ItemName, string Hash, string FileName, string SizeText)
{
    public string DetachName => $"{FileName}を{ItemName}から外す";
}

/// <summary>右の溜まりの組（名前の頭が同じファイル）。1つだけの組は見出しを出さずにファイルの行だけを出す。</summary>
public sealed class FilePoolGroupRow : ViewModelBase
{
    private bool _isExpanded;

    public FilePoolGroupRow(PooledGroup group, IReadOnlyList<FilePoolFileRow> files, bool isExpanded)
    {
        Name = group.Name.Length > 0 ? group.Name : FilePoolViewModel.UnnamedFileText;
        Files = files;
        _isExpanded = isExpanded;
        PackagesText = PackagesTextOf(group.PackageNames);
        foreach (var file in files)
        {
            file.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(FilePoolFileRow.IsChecked))
                {
                    OnPropertyChanged(nameof(IsChecked));
                }
            };
        }
    }

    /// <summary>組の名前。開閉を覚える鍵にも使う。</summary>
    public string Name { get; }

    public IReadOnlyList<FilePoolFileRow> Files { get; }

    public bool IsSingle => Files.Count == 1;

    public FilePoolFileRow First => Files[0];

    public string HeaderText => $"{Name}（{Files.Count}）";

    /// <summary>中の unitypackage の名前（ユーザ指示 2026-11-11「組の見出しに、中の unitypackage の名前を添える」）。3つまで書き、残りは数で言う。</summary>
    public string PackagesText { get; }

    public bool HasPackages => PackagesText.Length > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>
    /// 組のチェック：チェックできる行が全部入っていれば入、1つも無ければ外、間なら中間（null）。
    /// 入れると、チェックできる行を全部入れる（この商品に付いている行は入らない）。
    /// </summary>
    public bool? IsChecked
    {
        get
        {
            var checkable = Files.Where(file => file.CanCheck).ToList();
            if (checkable.Count == 0 || checkable.All(file => !file.IsChecked))
            {
                return false;
            }

            return checkable.All(file => file.IsChecked) ? true : null;
        }
        set
        {
            // 中間の後に押すと null が来る（三つの状態のチェック）。中間から押したら全部入れる
            var check = value ?? true;
            foreach (var file in Files.Where(file => file.CanCheck))
            {
                file.IsChecked = check;
            }

            OnPropertyChanged();
        }
    }

    public bool CanCheck => Files.Any(file => file.CanCheck);

    public string CheckName => $"{Name}の組のファイルをすべて選ぶ";

    private const int PackagesShown = 3;

    internal static string PackagesTextOf(IReadOnlyList<string> names)
        => names.Count == 0 ? string.Empty
            : names.Count <= PackagesShown ? "unitypackage：" + string.Join("、", names)
            : "unitypackage：" + string.Join("、", names.Take(PackagesShown)) + $" ほか {names.Count - PackagesShown} 個";
}

/// <summary>右の溜まりのファイル1つ。</summary>
public sealed class FilePoolFileRow : ViewModelBase
{
    private bool _isChecked;

    public FilePoolFileRow(PooledFile file, string? sourceItemId, string fileName, bool heldBySelected, string holdersTip)
    {
        Hash = file.Hash;
        SourceItemId = sourceItemId;
        FileName = fileName;
        SizeText = DisplayText.Size(file.SizeBytes);
        HolderCount = file.HolderCount;
        HeldBySelected = heldBySelected;
        HoldersTip = holdersTip;
    }

    public string Hash { get; }

    /// <summary>付けるときに記録を写す元（持っている商品。誰も持たなくなった物は最後に持っていた商品）。</summary>
    public string? SourceItemId { get; }

    public string FileName { get; }

    public string SizeText { get; }

    /// <summary>重複回数（選んだ商品の中で、このファイルが付いている商品の数）。</summary>
    public int HolderCount { get; }

    /// <summary>付ける先に選んでいる商品に、もう付いているか。付いていればチェックできない。</summary>
    public bool HeldBySelected { get; }

    public bool CanCheck => !HeldBySelected && SourceItemId is not null;

    public string HolderText => HeldBySelected ? "この商品に付いています" : HolderCount == 0 ? "どの商品にも付いていません" : $"{HolderCount} 件に付いています";

    /// <summary>付いている商品の名前（吹き出し）。</summary>
    public string HoldersTip { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value && CanCheck);
    }

    public string CheckName => $"{FileName}を選ぶ";
}
