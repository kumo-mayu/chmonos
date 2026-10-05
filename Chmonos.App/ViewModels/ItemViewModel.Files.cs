using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Chmonos.App.Controls;
using Chmonos.App.Services;
using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using BoothZipInspector;

namespace Chmonos.App.ViewModels;

/// <summary>商品ページ：種類・手元のファイル・フォルダ（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ItemViewModel
{
    private void BuildVariations()
    {
        foreach (var row in VariationRows())
        {
            Variations.Add(row);
        }
    }

    private List<VariationRow> VariationRows()
    {
        var rows = new List<VariationRow>();

        // 同じ版を複数回買っていることがあるので、版ごとにまとめて回数も出す
        // ToLookup は null の鍵を持てる（ToDictionary は持てない）。
        // 「どのバリエーションも指していない」記録がここに入る
        var ordered = Item.Local.Purchases.ToLookup(record => record.VariationId);

        foreach (var variation in Item.Booth.Variations)
        {
            var group = ordered[variation.Id].ToList();
            rows.Add(new VariationRow
            {
                Name = DisplayText.VariationName(variation.Name),
                IsPurchased = group.Count > 0,

                // 払った額と BOOTH の今の値段は別の物（値上げ・値下げ・セールで開く）。
                // BOOTH の価格は1つなので行の右端に固定し、買った記録は札で全部並べる（メモ54）
                Purchases = group.Select(PurchaseLine).ToList(),
                BoothPrice = $"¥{variation.Price:N0}",
                VariationId = variation.Id,
                Key = LineDiff.NormalizeLine(variation.Name ?? string.Empty),
            });
        }

        // BOOTH側から消えた購入記録も、支出の記録として残っているので出す。
        // バリエーションを指していない記録（null）もここへ落ちる——
        // 指す先が無いので「現存する」側には入らない
        var currentIds = Item.Booth.Variations.Select(variation => (long?)variation.Id).ToHashSet();
        foreach (var group in ordered.Where(entry => !currentIds.Contains(entry.Key)))
        {
            var purchases = group.ToList();
            rows.Add(new VariationRow
            {
                Name = purchases[0].NameSnapshot ?? DisplayText.VariationLabel(group.Key),
                Purchases = purchases.Select(PurchaseLine).ToList(),
                IsPurchased = true,

                // 指していない記録は「消えた」わけではない。指す先が無いだけ
                IsGone = group.Key is not null,
                Key = group.Key is null ? string.Empty : LineDiff.NormalizeLine(purchases[0].NameSnapshot ?? string.Empty),
            });
        }

        return rows;
    }

    /// <summary>
    /// 足された・消えたバリエーションに帯を付ける（メモ17・ユーザ判断 2026-10-03「消えたバリエーションは元の位置に行として残し、赤の帯」）。
    /// 足された物は名前で今の行に当てる。消えた物は、買っていて「BOOTHに現存しない」行が既にあればその行に帯を付け
    /// （同じバリエーションを2行に出さない）、無ければ前にあった位置へ行を差し込む（<see cref="ChangedLines.Place{T}"/>）
    /// </summary>
    /// <param name="boothCount">先頭から何行が BOOTH にあるバリエーションか（<see cref="VariationRows"/> は BOOTH の並び → 買った記録だけの行の順）。</param>
    internal static List<VariationRow> WithBands(List<VariationRow> rows, int boothCount, ChangedLines lines)
    {
        if (!lines.HasAny)
        {
            return rows;
        }

        var result = rows.ToList();
        var next = 0;
        foreach (var added in lines.Lines.Where(line => line.Kind == NotificationLineKind.Added))
        {
            for (var index = next; index < result.Count; index++)
            {
                if (!result[index].IsGone && result[index].Band is null && ChangedLines.Matches(result[index].Key, added.Text))
                {
                    result[index] = result[index] with { Band = ChangeTone.Added };
                    next = index + 1;
                    break;
                }
            }
        }

        var inserts = new List<(string Text, string? Follows, VariationRow Row)>();
        foreach (var removed in lines.Lines.Where(line => line.Kind == NotificationLineKind.Removed))
        {
            var gone = result.FindIndex(row => row.IsGone && row.Band is null && ChangedLines.Matches(row.Key, removed.Text));
            if (gone >= 0)
            {
                result[gone] = result[gone] with { Band = ChangeTone.Removed };
                continue;
            }

            inserts.Add((removed.Text, removed.Follows, new VariationRow
            {
                Name = removed.Text,
                Key = removed.Text,
                Band = ChangeTone.Removed,
                IsNoticeOnly = true,
            }));
        }

        // 位置は BOOTH にある行（先頭の boothCount 行）の並びで決める。買った記録だけの行は下にまとめて出しているので、並びの手掛かりにしない。
        // 後ろから差し込むと、前の差し込み位置がずれない（同じ位置の物は差の順のまま並ぶ）
        var current = result.Take(boothCount).Select(row => row.Key).ToList();
        var placed = ChangedLines.Place(current, inserts);
        for (var index = placed.Count - 1; index >= 0; index--)
        {
            result.Insert(Math.Min(placed[index].Before, current.Count), placed[index].Item);
        }

        return result;
    }

    /// <summary>
    /// 値段の変わったバリエーションの行に青の帯を付け、前の値段 → 今の値段を行の中に出す（メモ27-⑤・ユーザ判断 2026-10-04）。
    /// 前は欄の見出しの「価格変更」の札だけで、どのバリエーションが変わったかが分からなかった。行は知らせの ID で当てる（名前は変わることがある）。
    /// 買っていない行は値段の欄をそのまま「前 → 今」にし、買った行は払った額を残して BOOTH の値段の変化を下の行に出す
    /// </summary>
    internal static List<VariationRow> WithPrices(List<VariationRow> rows, IReadOnlyDictionary<long, NotificationPrice> prices)
    {
        if (prices.Count == 0)
        {
            return rows;
        }

        return rows.Select(row => row.VariationId is { } id && !row.IsGone && prices.TryGetValue(id, out var price)
                ? row with
                {
                    Band = ChangeTone.Price,
                    BoothPrice = BoothChanges.PriceStep(price),
                }
                : row)
            .ToList();
    }

    /// <summary>
    /// 購入記録1件の札の文。同じバリエーションの購入は全部並べるので1件ずつ作る（前は1件目だけ「ほか n 件」だった）。
    /// 種類は動詞で分ける（「¥1,500 で贈った」）。貰った物は自分の支出ではないので、価格が無い（空欄か0円）ときは「貰った」だけにする。
    /// 括弧の中は名詞、文の中は動詞。同じ語を両方に使うと「¥100 で自分用」か「価格未入力（買った）」のどちらかが崩れる
    /// </summary>
    /// <remarks>
    /// 購入の日付（メモ45）を入れた物だけ、頭に日付を添える（「2025-03-10 に ¥1,500 で贈った」）。
    /// 空の物は入手日と同じ日なので、札ごとに同じ日付を並べない（入手日は「記録していること」に出ている）。
    /// </remarks>
    internal static string PurchaseLine(Purchase purchase)
    {
        var text = purchase.Kind == PurchaseKind.Received && purchase.Price is null or 0
            ? "貰った"
            : purchase.Price is null
                ? $"価格未入力（{DisplayText.PurchaseKindLabel(purchase.Kind)}）"
                : $"¥{purchase.Price:N0} で{DisplayText.PurchaseKindVerb(purchase.Kind)}";

        return purchase.PurchasedAt is not { } date
            ? text
            : purchase.Price is null && purchase.Kind != PurchaseKind.Received
                ? $"{date:yyyy-MM-dd} {text}"
                : $"{date:yyyy-MM-dd} に{(text.StartsWith('¥') ? " " : string.Empty)}{text}";
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
                // 古い版は場所が空なので、置き換わった場所の名前で出す（新しい版の行と同じ名前が並び、どの版の物か分かる）
                FileName = file.Paths.Count > 0 ? Path.GetFileName(file.Paths[0])
                    : file.IsOldVersion ? Path.GetFileName(file.Replaced!.Path)
                    : "(見つかりません)",
                IsOldVersion = file.IsOldVersion,
                SizeText = Core.Models.DisplayText.Size(file.SizeBytes),
                // 開く・在るかの確かめは今の場所で（ドライブ文字が変わった分は読み替える）。記録のパスは書き換えない
                Paths = [.. file.Paths.Select(_services.Volumes.Current)],
                VariationLabel = variation,
                VariationId = file.VariationId,
                UnityPackages = packages,
                IsDetached = file.Detached,
                IsBrokenArchive = file.ArchiveBroken,
                CanReattach = file.Detached && owner is null,
                ReattachTip = owner is null
                    ? "このファイルをこの商品に戻します。未確定からは消えます。"
                    : $"「{owner.DisplayName}」に紐付けてあるので戻せません。先にそちらから外してください。",
                UnityPackageRows = [],
            });
        }

        MarkMissingFilesAsync().Forget();
        MarkUnpackableFilesAsync().Forget();
    }

    /// <summary>
    /// 記録の場所に無いファイルに「見つかりません」を付ける（点検 2026-09-30 の B）。
    /// 前は記録のパスが空のときだけ出していたが、取り込みは移したファイルのパスをすぐには落とさないので、
    /// 移した後も普通の行と［開く ▾］が出ていた。**在るかは画面のスレッドの外で見る**（外付け・ネットワークで待たされないように）。
    /// 改変の画面の「プロジェクトが見つかりません」と同じく、読み込みのときに1回確かめて覚える。
    ///
    /// **見た結果は記録にも書く**（見つからなくなった日時。ユーザ判断 2026-10-04）。前は書かなかったので、
    /// ここで「見つかりません」と出ている商品が、カードの印・検索の条件・統計には出ていなかった。
    /// </summary>
    private async Task MarkMissingFilesAsync()
    {
        var files = Item.Local.LocalFiles.ToList();
        if (files.Count == 0)
        {
            return;
        }

        var sightings = await FilePresenceNotes.LookAsync(files, _services.Volumes);
        var byHash = new Dictionary<string, FileSighting>(StringComparer.OrdinalIgnoreCase);
        foreach (var sighting in sightings)
        {
            byHash.TryAdd(sighting.Hash, sighting);
        }

        // 場所の無い行は組むときから「見つかりません」なので、場所のある行にだけ当てる
        foreach (var row in LocalFiles.Where(row => row.Paths.Count > 0))
        {
            if (byHash.TryGetValue(row.Hash, out var sighting))
            {
                row.Presence = sighting.Presence;
            }
        }

        await NotePresenceAsync(sightings);
    }

    /// <summary>
    /// 見た在る・無いを記録へ（<see cref="FilePresenceNotes"/>）。書いたら、検索の写しにも知らせる
    /// （知らせないと、戻ってもカードの印と条件が古いまま）。
    /// </summary>
    private async Task NotePresenceAsync(IReadOnlyList<FileSighting> sightings)
    {
        if (await FilePresenceNotes.NoteAsync(_services, Item, sightings) is { } reloaded)
        {
            OnPresenceNoted(reloaded);
        }
    }

    /// <summary>
    /// 見つからなくなった日時を書いた後。持っている写しを差し替え、検索へ知らせる。
    /// 行は作り直さない（在る・無いの札は見た結果でもう付いている。作り直すとまた見に行く）
    /// </summary>
    private void OnPresenceNoted(ItemRecord reloaded)
    {
        Item = reloaded;
        _main.Search.NoteItemChanged(reloaded);
    }

    /// <summary>開く・展開するで無かったとき、その行のファイルを見直して記録へ（行を出した後で消した・外付けを外した）。</summary>
    private async Task NotePresenceOfAsync(Func<LocalFileRecord, bool> which)
    {
        var files = Item.Local.LocalFiles.Where(which).ToList();
        if (files.Count > 0)
        {
            await NotePresenceAsync(await FilePresenceNotes.LookAsync(files, _services.Volumes));
        }
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
    /// <remarks>
    /// item に入る先が書いてある物（<see cref="Core.Services.UnityPackagePlace.Roots"/>）は読まずにそれを出し、無い物だけ読む。
    /// 読むときは控えを zip ごとに1回だけ読む（<see cref="Core.Services.UnityPackageReads"/>。前は包みごとに控えを丸ごと読んでいた）
    /// </remarks>
    private async Task LoadUnityDestinationsAsync(IReadOnlyDictionary<UnityPackageRow, IReadOnlyList<string>> known)
    {
        var reads = new Core.Services.UnityPackageReads();
        foreach (var row in LocalFiles.SelectMany(file => file.UnityPackageRows).ToList())
        {
            var roots = known.TryGetValue(row, out var written)
                ? written
                : await Task.Run(() => reads.ReadDestinations(row.Entry));
            row.DestinationText = Core.Services.UnityHandoff.DescribeDestinations(roots);
        }
    }

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
        // zip の中の一覧は、item に書いてあればそれを使い、zip を開かない（Core.Services.UnityHandoff.PlacesOf）。
        // 1箇所目の在る zip だけ見る。同じ中身が複数箇所にあっても中身は同じ。zip以外（展開済みのフォルダやpdf）は対象外
        var found = await Task.Run(() => files
            .Select(file => file is null ? [] : Core.Services.UnityHandoff.PlacesOf(file))
            .ToList());

        var known = new Dictionary<UnityPackageRow, IReadOnlyList<string>>();
        for (var i = 0; i < rows.Count; i++)
        {
            var packageRows = found[i].Select(place => (Place: place, Row: new UnityPackageRow { Entry = place.Entry, FileRow = rows[i] })).ToList();
            foreach (var (place, row) in packageRows)
            {
                if (place.Roots is { } roots)
                {
                    known[row] = roots;
                }
            }

            rows[i].UnityPackages = found[i].Select(place => place.Entry).ToList();
            rows[i].UnityPackageRows = packageRows.Select(pair => pair.Row).ToList();
        }

        // 「Unityが開いていません」の行は、送れる物が1つでも在るときだけ出す。行を出した後でここで付くので、付いたことを知らせる
        // （知らせないと、使い回された画面でその行が出ない）
        OnPropertyChanged(nameof(HasAnyUnityPackage));

        await LoadUnityDestinationsAsync(known);
    }

    /// <summary>
    /// フォルダごと登録した物の行。**先にディスクを見ずに出し、在るか・zip が入ったかは画面のスレッドの外で確かめてから差し替える**
    /// （技術的負債 4-2）。前は商品ページを組むときに画面のスレッドで見ていた。
    /// </summary>
    private void BuildLocalFolders()
    {
        LocalFolders = Item.Local.LocalFolders
            .Select(folder => ToFolderRow(folder.Path, _services.Volumes.Current(folder.Path), folder.FileCount, folder.TotalBytes, isMissing: false, archive: null))
            .ToList();

        if (LocalFolders.Count > 0)
        {
            FillLocalFolderStateAsync().Forget();
        }
    }

    private async Task FillLocalFolderStateAsync()
    {
        var folders = Item.Local.LocalFolders.ToList();
        var remap = _services.Volumes.Current;
        var rows = await Task.Run(() => folders
            .Select(folder => ToFolderRow(
                folder.Path,
                remap(folder.Path),
                folder.FileCount,
                folder.TotalBytes,
                isMissing: !Core.Services.DiskCheck.FolderExists(remap(folder.Path)),
                // zipが手に入っていればフォルダ登録は役目を終えている。
                // 気付かずに置いておくと容量が二重に数えられる。
                archive: RegisteredFolderSet.FindArchiveFor(remap(folder.Path))))
            .ToList());

        LocalFolders = rows;
        OnPropertyChanged(nameof(LocalFolders));
    }

    private static LocalFolderRow ToFolderRow(string path, string currentPath, int fileCount, long totalBytes, bool isMissing, string? archive) => new()
    {
        Path = path,
        OpenPath = currentPath,
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
                await NotePresenceOfAsync(file => string.Equals(file.Hash, row.Hash, StringComparison.OrdinalIgnoreCase));
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

    private async Task OpenInExplorerAsync(string path)
    {
        // 行を出した後で外付けを外すと、記録の場所も親フォルダも無く、押しても黙って何も起きなかった
        if (!await Shell.TryRevealAsync(path))
        {
            TellNotFound(Path.GetFileName(path.TrimEnd('\\', '/')), "エクスプローラで開く");
        }

        // 開けても、近くのフォルダを開いただけでファイルは無いことがある（Shell.TryRevealAsync は親を開く）。
        // その場所を持つファイルを見直して記録へ。フォルダの行の場所はファイルに当たらないので何もしない
        await NotePresenceOfAsync(file => file.Paths.Any(recorded => string.Equals(_services.Volumes.Current(recorded), path, StringComparison.OrdinalIgnoreCase)));
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
