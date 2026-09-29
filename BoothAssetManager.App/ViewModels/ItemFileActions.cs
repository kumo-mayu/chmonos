using System.Diagnostics;
using System.IO;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 商品の手元のファイルを「エクスプローラで開く」「一時的に展開して開く」。
/// 商品ページの「開く ▾」と、カードの右クリックの「開く」で同じ道を通す（ユーザ指示 2026-09-19）。
///
/// **ファイルが2つ以上あれば、どれにするかを選ばせる**（ユーザ指示 同日：カードの右クリックは最初の1件を黙って開いていた。
/// 別のファイルが開いても気付けない）。1つなら聞かずに進める
/// </summary>
internal static class ItemFileActions
{
    private sealed record Target(string Path, ListChoiceItem Label);

    /// <summary>展開している zip。足し引きは画面のスレッドだけなので錠は要らない。</summary>
    private static readonly HashSet<string> Unpacking = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>手元にある物（ファイルとフォルダ）を、選んでエクスプローラで開く。</summary>
    public static async Task RevealAsync(ItemRecord item)
    {
        const string title = "エクスプローラで開く";

        // 在るかは画面のスレッドの外で見る（技術的負債 4-2）
        var targets = await Task.Run(() => item.Local.OwnedFiles
            .Select(file => file.Paths.FirstOrDefault(DiskCheck.FileExists))
            .OfType<string>()
            .Select(path => new Target(path, new ListChoiceItem(Path.GetFileName(path), Path.GetDirectoryName(path))))
            .Concat(item.Local.LocalFolders
                .Where(folder => DiskCheck.FolderExists(folder.Path))
                .Select(folder => new Target(folder.Path, new ListChoiceItem($"{Path.GetFileName(folder.Path.TrimEnd('\\', '/'))}（フォルダ）", folder.Path))))
            .ToList());

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
    public static async Task UnpackAsync(AppServiceContainer services, ItemRecord item)
    {
        const string title = "一時的に展開して開く";

        var zips = await Task.Run(() => item.Local.OwnedFiles
            .Select(file => file.Paths.FirstOrDefault(path =>
                path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && DiskCheck.FileExists(path)))
            .OfType<string>()
            .Select(path => new Target(path, new ListChoiceItem(Path.GetFileName(path), Path.GetDirectoryName(path))))
            .ToList());

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
        if (!Unpacking.Add(zip))
        {
            return;
        }

        CommandResult result;
        try
        {
            result = await services.Commands.ExecuteAsync(new UiCommand.UnpackToTemporary(zip));
        }
        finally
        {
            Unpacking.Remove(zip);
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
