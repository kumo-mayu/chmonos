using System.Diagnostics;
using System.IO;
using Chmonos.App.Services;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品の手元のファイルを「エクスプローラで開く」「一時的に展開して開く」。
/// 商品ページの「開く ▾」と、カードの右クリックの「開く」で同じ道を通す（ユーザ指示 2026-09-19）。
///
/// **ファイルが2つ以上あれば、どれにするかを選ばせる**（ユーザ指示 同日：カードの右クリックは最初の1件を黙って開いていた。
/// 別のファイルが開いても気付けない）。1つなら聞かずに進める
/// </summary>
/// <summary>一時展開の今の様子（下の帯に出す分）。</summary>
/// <param name="Count">展開している zip の数。</param>
/// <param name="Name">1つだけ展開しているときの zip の名前。2つ以上なら空。</param>
/// <param name="IsStopping">中止を押して、片付けが済むのを待っているだけか。</param>
internal sealed record UnpackingStatus(int Count, string Name, long DoneBytes, long TotalBytes, bool IsStopping);

internal static class ItemFileActions
{
    private sealed record Target(string Path, ListChoiceItem Label);

    /// <summary>展開している1本。止める口と、最後に届いた進み具合。</summary>
    private sealed class UnpackJob
    {
        public CancellationTokenSource Stop { get; } = new();

        public TemporaryUnpackProgress Progress { get; set; }
    }

    /// <summary>展開している zip。足し引きは画面のスレッドだけなので錠は要らない。</summary>
    private static readonly Dictionary<string, UnpackJob> Unpacking = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>取り込みの進み具合と同じ間隔（1秒に10回まで）。人の目で追えるのはそのくらいまで。</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 展開の始まり・進み・終わりを知らせる（画面のスレッドで呼ぶ）。主画面が下の帯に出す。
    /// 商品ページからもカードの右クリックからも始まるので、Unity へ送るときの帯と同じく、アプリに1つの知らせにする
    /// </summary>
    public static event Action? UnpackingChanged;

    /// <summary>
    /// 今の展開の様子。展開していなければ null。
    /// 別の zip を続けて押すと並んで走るので、数と大きさの合計でまとめる（帯は1本のまま）。
    /// </summary>
    public static UnpackingStatus? CurrentUnpacking
    {
        get
        {
            if (Unpacking.Count == 0)
            {
                return null;
            }

            // 中止を押した後に片付けを待っている分は、数にも大きさにも入れない（残りの展開の進み具合が読めなくなる）
            var running = Unpacking.Where(pair => !pair.Value.Stop.IsCancellationRequested).ToList();
            if (running.Count == 0)
            {
                return new UnpackingStatus(Unpacking.Count, string.Empty, 0, 0, IsStopping: true);
            }

            return new UnpackingStatus(
                running.Count,
                running.Count == 1 ? Path.GetFileName(running[0].Key) : string.Empty,
                running.Sum(pair => pair.Value.Progress.DoneBytes),
                running.Sum(pair => pair.Value.Progress.TotalBytes),
                IsStopping: false);
        }
    }

    /// <summary>展開をやめる。並んで走っている分も全部止める（帯の「中止」は1つなので）。</summary>
    public static void StopUnpacking()
    {
        foreach (var job in Unpacking.Values)
        {
            job.Stop.Cancel();
        }

        UnpackingChanged?.Invoke();
    }

    /// <summary>
    /// 使おうとして見たファイルの在る・無いを記録へ（見つからなくなった日時。ユーザ判断 2026-10-04）。
    /// 書いたら <paramref name="changed"/> で読み直した商品を渡す（検索の写しへ知らせる）。
    /// </summary>
    private static async Task NotePresenceAsync(AppServiceContainer services, ItemRecord item, Action<ItemRecord>? changed)
    {
        if (await FilePresenceNotes.LookAndNoteAsync(services, item, item.Local.LocalFiles) is { } reloaded)
        {
            changed?.Invoke(reloaded);
        }
    }

    /// <summary>手元にある物（ファイルとフォルダ）を、選んでエクスプローラで開く。</summary>
    public static async Task RevealAsync(AppServiceContainer services, ItemRecord item, Action<ItemRecord>? changed = null)
    {
        const string title = "エクスプローラで開く";

        // 在るかは画面のスレッドの外で見る（技術的負債 4-2）
        var targets = await Task.Run(() => item.Local.OwnedFiles
            .Select(file => file.Paths.Select(path => services.Volumes.CurrentOf(file, path)).FirstOrDefault(DiskCheck.FileExists))
            .OfType<string>()
            .Select(path => new Target(path, new ListChoiceItem(Path.GetFileName(path), Path.GetDirectoryName(path))))
            .Concat(item.Local.LocalFolders
                .Select(folder => services.Volumes.CurrentOf(folder))
                .Where(DiskCheck.FolderExists)
                .Select(folder => new Target(folder, new ListChoiceItem($"{Path.GetFileName(folder.TrimEnd('\\', '/'))}（フォルダ）", folder))))
            .ToList());

        // 窓を出す前に書く（窓の間に検索の印が古いまま残らないように）
        await NotePresenceAsync(services, item, changed);

        if (targets.Count == 0)
        {
            Services.Notice.Show(
                $"「{item.DisplayName}」のファイルが、記録にある場所に見つかりません。\n\n"
                + "移した場合は、移した先のフォルダを取り込むと付け直します。",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (Choose(title, $"「{item.DisplayName}」には手元のファイルが {targets.Count} 件あります。どれを開きますか？", targets, "エクスプローラで開く")
            is { } picked)
        {
            Shell.Reveal(picked.Path);
        }
    }

    /// <summary>手元にある zip を、選んで一時フォルダへ展開して開く。</summary>
    public static async Task UnpackAsync(AppServiceContainer services, ItemRecord item, Action<ItemRecord>? changed = null)
    {
        const string title = "一時的に展開して開く";

        var zips = await Task.Run(() => item.Local.OwnedFiles
            .Select(file => file.Paths.Select(path => services.Volumes.CurrentOf(file, path)).FirstOrDefault(path =>
                path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && DiskCheck.FileExists(path)))
            .OfType<string>()
            .Select(path => new Target(path, new ListChoiceItem(Path.GetFileName(path), Path.GetDirectoryName(path))))
            .ToList());

        await NotePresenceAsync(services, item, changed);

        if (zips.Count == 0)
        {
            Services.Notice.Show(
                $"「{item.DisplayName}」には、一時的に展開できるzipが手元にありません。\n\n"
                + "zip以外のファイルやフォルダは、「エクスプローラで開く」でそのまま開けます。",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (Choose(title, $"「{item.DisplayName}」にはzipが {zips.Count} 件あります。どれを展開しますか？", zips, "展開して開く")
            is { } picked)
        {
            await UnpackAndOpenAsync(services, picked.Path);
        }
    }

    /// <summary>
    /// zip を一時フォルダへ展開してエクスプローラで開く（#56）。unitypackage でない配布物（テクスチャ等）を Unity に入れるため。
    /// アプリを閉じると消す
    /// </summary>
    public static async Task UnpackAndOpenAsync(AppServiceContainer services, string zip)
    {
        // 展開の途中の2度押しは弾く（大容量の確かめ #3・2026-09-30。要確認の「商品情報を取り直す」と同じ守り・40b3863）。
        // 1本目が終われば開くので、2本目まで通すとエクスプローラが2つ開く。
        // 商品ページとカードの右クリックはどちらもここを通るので、画面をまたいだ押し直しもここで止まる
        // （展開そのものの重なりは TemporaryUnpacker が錠で防いでいる）
        if (Unpacking.ContainsKey(zip))
        {
            return;
        }

        // 押してからエクスプローラが開くまで何も変わらず、遅いディスクでは数十秒「押したのに何も起きない」と見えた
        // （大容量の確かめ #3）。下の帯に進み具合と「中止」を出す（ユーザ判断 2026-09-30）。
        // 進み具合は 81,920 バイトごとに届くので、最新だけを1秒に10回まで出す（LatestProgress に理由）
        var job = new UnpackJob();
        Unpacking.Add(zip, job);
        var progress = new LatestProgress<TemporaryUnpackProgress>(
            report =>
            {
                job.Progress = report;
                UnpackingChanged?.Invoke();
            },
            ProgressInterval,
            System.Windows.Threading.Dispatcher.CurrentDispatcher);
        UnpackingChanged?.Invoke();

        CommandResult? result = null;
        try
        {
            result = await services.Commands.ExecuteAsync(
                new UiCommand.UnpackToTemporary(zip, progress), cancellationToken: job.Stop.Token);
        }
        catch (OperationCanceledException)
        {
            // 中止は人が押した結果なので、窓では知らせない（帯が消えるのが返事）。
            // 書きかけは展開の側が消してから投げているので、もう一度押せば最初から展開し直す
        }
        finally
        {
            // 命令が戻るのは片付けまで済んだ後。それまで同じ zip の押し直しを弾き続ける
            // （片付けの途中に次の1本を通しても、錠で待つだけで帯が「中止しています…」のまま増える）
            progress.Complete();
            Unpacking.Remove(zip);
            UnpackingChanged?.Invoke();
        }

        // 書き終わる間際に中止を押すと、展開は終わって返ってくる。止めたつもりの人の前にエクスプローラを開かない
        // （展開した物は残るので、もう一度押せばすぐ開く）
        var stopped = job.Stop.IsCancellationRequested;
        job.Stop.Dispose();
        if (stopped)
        {
            return;
        }

        if (result is CommandResult.Unpacked unpacked)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = unpacked.Folder, UseShellExecute = true });
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // 開けなくてもアプリは動き続ける
            }
        }
        else if (result is CommandResult.Failed failed)
        {
            Services.Notice.Show(failed.Message, "一時的に展開して開く",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 商品の unitypackage から1つを選ぶ（2つ以上のときだけ聞く）。無ければ理由を出して null。
    /// zip を開いて数えるので、画面のスレッドの外で読む
    /// </summary>
    public static async Task<UnityPackageEntry?> PickPackageAsync(ItemRecord item, string title, string okText)
    {
        var packages = await Task.Run(() => UnityImportQueue.PackagesOf(item));
        if (packages.Count == 0)
        {
            Services.Notice.Show(
                $"「{item.DisplayName}」には、Unityに入れられるもの（zipの中の .unitypackage）が手元にありません。",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return null;
        }

        if (packages.Count == 1)
        {
            return packages[0];
        }

        var picked = ListChoice.Ask(
            title,
            $"「{item.DisplayName}」にはunitypackageが {packages.Count} 件あります。どれにしますか？",
            PackageLabels(packages),
            okText);

        return picked is { } index ? packages[index] : null;
    }

    /// <summary>unitypackage を選ぶ一覧の行。名前と、どの zip のどこに入っているか（右クリックと検索の複数選択で共用）。</summary>
    public static List<ListChoiceItem> PackageLabels(IReadOnlyList<UnityPackageEntry> packages)
        => [.. packages.Select(package => new ListChoiceItem(
            package.Name,
            package.Folder.Length > 0 ? $"{Path.GetFileName(package.ZipPath)} の中の {package.Folder}" : Path.GetFileName(package.ZipPath)))];

    private static Target? Choose(string title, string message, IReadOnlyList<Target> targets, string okText)
    {
        if (targets.Count == 1)
        {
            return targets[0];
        }

        return ListChoice.Ask(title, message, [.. targets.Select(target => target.Label)], okText) is { } index
            ? targets[index]
            : null;
    }
}
