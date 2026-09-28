using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothZipInspector;

namespace BoothAssetManager.App.ViewModels;

/// <summary>商品ページ：種類・手元のファイル・フォルダ（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ItemViewModel
{
    private void BuildVariations()
    {
        // 同じ版を複数回買っていることがあるので、版ごとにまとめて回数も出す
        // ToLookup は null の鍵を持てる（ToDictionary は持てない）。
        // 「どのバリエーションも指していない」記録がここに入る
        var ordered = Item.Local.Purchases.ToLookup(record => record.VariationId);

        foreach (var variation in Item.Booth.Variations)
        {
            var group = ordered[variation.Id].ToList();
            Variations.Add(new VariationRow
            {
                Name = variation.Name ?? "（名前のないバリエーション）",
                PriceText = group.Count > 0 ? PurchaseText(group) : $"¥{variation.Price:N0}",
                IsPurchased = group.Count > 0,
            });
        }

        // BOOTH側から消えた購入記録も、支出の記録として残っているので出す。
        // バリエーションを指していない記録（null）もここへ落ちる——
        // 指す先が無いので「現存する」側には入らない
        var currentIds = Item.Booth.Variations.Select(variation => (long?)variation.Id).ToHashSet();
        foreach (var group in ordered.Where(entry => !currentIds.Contains(entry.Key)))
        {
            var purchases = group.ToList();
            Variations.Add(new VariationRow
            {
                Name = purchases[0].NameSnapshot ?? DisplayText.VariationLabel(group.Key),
                PriceText = PurchaseText(purchases),
                IsPurchased = true,

                // 指していない記録は「消えた」わけではない。指す先が無いだけ
                IsGone = group.Key is not null,
            });
        }
    }

    /// <summary>
    /// 1つの版についての購入記録をまとめて1行にする。
    /// 同じ版を複数回買っていれば回数を出す（贈答・買い直しで起こる）。
    /// </summary>
    private static string PurchaseText(IReadOnlyList<Purchase> group)
    {
        var head = group[0];

        // 括弧の中は名詞、文の中は動詞。同じ語を両方に使うと
        // 「¥100 で自分用」か「価格未入力（買った）」のどちらかが崩れる
        var price = head.Price is null ? "価格未入力" : $"¥{head.Price:N0}";
        var text = head.Price is null
            ? $"{price}（{DisplayText.PurchaseKindLabel(head.Kind)}）"
            : $"{price} で{DisplayText.PurchaseKindVerb(head.Kind)}";

        return group.Count > 1 ? $"{text} ほか {group.Count - 1} 件" : text;
    }

    private void BuildLocalFiles()
    {
        // 外したファイルは灰色で後ろに残す（ユーザ判断 2026-09-12）。持っているファイルを先に
        foreach (var file in Item.Local.LocalFiles.OrderBy(file => file.Detached))
        {
            var variation = file.VariationId is null
                ? null
                : Item.Booth.Variations.FirstOrDefault(entry => entry.Id == file.VariationId)?.Name;
            // zip の中の unitypackage は、行を出した後で画面のスレッドの外で読む（LoadUnityPackagesAsync・技術的負債 4-2）
            IReadOnlyList<Core.Services.UnityPackageEntry> packages = [];

            // 外した後で別の商品へ紐付けてあれば戻せない（同じファイルが2つの商品の持ち物になる）
            var owner = file.Detached ? _main.Search.FindFileOwner(file.Hash, Item.Id) : null;

            LocalFiles.Add(new LocalFileRow
            {
                Hash = file.Hash,
                FileName = file.Paths.Count > 0 ? Path.GetFileName(file.Paths[0]) : "(見つかりません)",
                SizeText = Core.Models.DisplayText.Size(file.SizeBytes),
                Paths = file.Paths,
                VariationLabel = variation,
                VariationId = file.VariationId,
                UnityPackages = packages,
                IsDetached = file.Detached,
                CanReattach = file.Detached && owner is null,
                ReattachTip = owner is null
                    ? "このファイルをこの商品に戻します。未確定からは消えます。"
                    : $"「{owner.DisplayName}」に紐付けてあるので戻せません。先にそちらから外してください。",
                UnityPackageRows = packages.Select(package => new UnityPackageRow { Entry = package }).ToList(),
            });
        }

        MarkUnpackableFilesAsync().Forget();
    }

    /// <summary>
    /// 手元にある zip の行に「展開して開く」を出す。
    /// **在るかは画面のスレッドの外で見る**（技術的負債 4-2）。前は商品ページを組むときに画面のスレッドで見ていて、
    /// 外付け・ネットワークにある物は開くたびに画面が止まりえた。
    /// </summary>
    private async Task MarkUnpackableFilesAsync()
    {
        // 編集画面では「使う」操作を出さない。外したファイルも使う対象ではない
        if (!ShowsUseActions)
        {
            return;
        }

        var rows = LocalFiles.Where(row => !row.IsDetached).ToList();
        var unpackable = await Task.Run(() => rows
            .Select(row => row.Paths.Any(path =>
                path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Core.Services.DiskCheck.FileExists(path)))
            .ToList());

        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].CanUnpack = unpackable[i];
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Unityのどこに入るかを、行ごとに裏で読んで埋める。
    ///
    /// **画面を組むときに同期で読まない。**入る先は unitypackage を最後まで解かないと
    /// 分からない（手元の実測で 40MB の物が 0.2 秒ほど）。1つずつ順に読むのは、
    /// 同じzipを並んで開いてディスクを取り合わないため。
    /// </summary>
    private async Task LoadUnityDestinationsAsync()
    {
        foreach (var row in LocalFiles.SelectMany(file => file.UnityPackageRows).ToList())
        {
            var roots = await Task.Run(() => Core.Services.UnityHandoff.ReadDestinations(row.Entry));
            row.DestinationText = Core.Services.UnityHandoff.DescribeDestinations(roots);
        }
    }

    /// <summary>
    /// このファイルがzipなら、中の <c>.unitypackage</c> を数える。
    ///
    /// **1箇所目だけ見る。**同じ中身が複数箇所にあっても中身は同じなので、
    /// 全部開くのは無駄。zip以外（展開済みのフォルダやpdf）は対象外。
    /// </summary>
    /// <summary>
    /// zip の中の、Unityへ送れるものを読んで行に付け、続けて入る先を埋める。
    /// **画面のスレッドの外で読む**（技術的負債 4-2）。前は商品ページを組むときに画面のスレッドで zip を開いていた。
    /// 編集画面では「使う」操作を出さないので呼ばない（1件進むたびに開くことになる）。外したファイルも使う対象ではない。
    /// </summary>
    private async Task LoadUnityPackagesAsync()
    {
        var rows = LocalFiles.Where(row => !row.IsDetached).ToList();
        var files = rows
            .Select(row => Item.Local.LocalFiles.FirstOrDefault(file => string.Equals(file.Hash, row.Hash, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var found = await Task.Run(() => files.Select(file => file is null ? [] : FindUnityPackages(file)).ToList());

        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].UnityPackages = found[i];
            rows[i].UnityPackageRows = found[i].Select(package => new UnityPackageRow { Entry = package }).ToList();
        }

        await LoadUnityDestinationsAsync();
    }

    private static IReadOnlyList<Core.Services.UnityPackageEntry> FindUnityPackages(Core.Models.LocalFileRecord file)
    {
        var path = file.Paths.FirstOrDefault(File.Exists);

        // zip のハッシュを持たせる。入り先を取り込みの裏で読んだ控えから引ける（zip を解き直さない）
        return path is not null && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? Core.Services.UnityHandoff.FindPackages(path).Select(package => package with { ZipHash = file.Hash }).ToList()
            : [];
    }

    /// <summary>
    /// フォルダごと登録した物の行。**先にディスクを見ずに出し、在るか・zip が入ったかは画面のスレッドの外で確かめてから差し替える**
    /// （技術的負債 4-2）。前は商品ページを組むときに画面のスレッドで見ていた。
    /// </summary>
    private void BuildLocalFolders()
    {
        LocalFolders = Item.Local.LocalFolders
            .Select(folder => ToFolderRow(folder.Path, folder.FileCount, folder.TotalBytes, isMissing: false, archive: null))
            .ToList();

        if (LocalFolders.Count > 0)
        {
            FillLocalFolderStateAsync().Forget();
        }
    }

    private async Task FillLocalFolderStateAsync()
    {
        var folders = Item.Local.LocalFolders.ToList();
        var rows = await Task.Run(() => folders
            .Select(folder => ToFolderRow(
                folder.Path,
                folder.FileCount,
                folder.TotalBytes,
                isMissing: !Core.Services.DiskCheck.FolderExists(folder.Path),
                // zipが手に入っていればフォルダ登録は役目を終えている。
                // 気付かずに置いておくと容量が二重に数えられる。
                archive: RegisteredFolderSet.FindArchiveFor(folder.Path)))
            .ToList());

        LocalFolders = rows;
        OnPropertyChanged(nameof(LocalFolders));
    }

    private static LocalFolderRow ToFolderRow(string path, int fileCount, long totalBytes, bool isMissing, string? archive) => new()
    {
        Path = path,
        Name = System.IO.Path.GetFileName(path),
        SummaryText = $"{fileCount} ファイル / {Core.Models.DisplayText.Size(totalBytes)}",
        IsMissing = isMissing,
        HasArchive = archive is not null,
        ArchiveNoticeText = archive is null
            ? string.Empty
            : $"{System.IO.Path.GetFileName(archive)} が見つかりました。"
                + "zipを取り込めば展開先は対象から外れるので、このフォルダの登録は外してください。",
    };

    private static bool s_filesExpanded = true;

    /// <summary>
    /// ローカルファイルの欄を開いているか（ユーザ指示 2026-09-19：畳めるように）。既定は開く——商品ページで
    /// ファイルを見に来ることが多い。商品を移っても保つ（アプリを閉じるまで。ほかの欄の畳み方と同じ）
    /// </summary>
    public bool IsFilesExpanded
    {
        get => s_filesExpanded;
        set
        {
            if (s_filesExpanded != value)
            {
                s_filesExpanded = value;
                OnPropertyChanged(nameof(IsFilesExpanded));
            }
        }
    }

    /// <summary>zip を一時フォルダへ展開してエクスプローラで開く（#56）。</summary>
    public RelayCommand UnpackCommand { get; }

    private async Task UnpackAsync(LocalFileRow? row)
    {
        // 在るかは画面のスレッドの外で見る（技術的負債 4-2）
        var zip = row is null ? null : await Task.Run(() => row.Paths.FirstOrDefault(path =>
            path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Core.Services.DiskCheck.FileExists(path)));
        if (zip is null)
        {
            // 行を出した後で外付けを外すと、押しても黙って何も起きなかった（カードの右クリックは言っていた）
            if (row is not null)
            {
                TellNotFound(row.FileName, "一時的に展開して開く");
            }

            return;
        }

        // 展開して開く処理はカードの右クリックと共用（ItemFileActions）
        await ItemFileActions.UnpackAndOpenAsync(_services, zip);
    }

    /// <summary>
    /// 開き方はアプリ全体で1つ（<see cref="Shell.Reveal"/>）。フォルダはその中を、zip は中を、ほかのファイルは含むフォルダを選んだ状態で開く
    /// （ユーザ指示 2026-09-14）。前はここだけ自前の開き方を持っていた
    /// </summary>
    private void OpenInExplorer(object? parameter)
    {
        if (parameter is string path)
        {
            OpenInExplorerAsync(path).Forget();
        }
    }

    private static async Task OpenInExplorerAsync(string path)
    {
        // 行を出した後で外付けを外すと、記録の場所も親フォルダも無く、押しても黙って何も起きなかった
        if (!await Shell.TryRevealAsync(path))
        {
            TellNotFound(Path.GetFileName(path.TrimEnd('\\', '/')), "エクスプローラで開く");
        }
    }

    /// <summary>開く先が無いときの知らせ。カードの右クリック（<see cref="ItemFileActions"/>）と同じ窓・同じ言い方にそろえる</summary>
    private static void TellNotFound(string name, string title)
        => Services.Notice.Show(
            $"「{name}」が、記録にある場所に見つかりません。\n\n"
            + "外付けのドライブなら、つないでからもう一度お試しください。移した場合は、移した先のフォルダを取り込むと付け直します。",
            title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

    private static void TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 開けなくてもアプリは動き続ける
        }
    }
}
