using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>未確定画面：展開した中身の見分けとフォルダごとの登録（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ResolveViewModel
{
    /// <summary>判定はフォルダ単位で同じになるので、フォルダをキーに覚えておく。</summary>
    private ArchiveContentJudgement JudgeCached(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (_judgements.TryGetValue(directory, out var cached))
        {
            return cached;
        }

        var judgement = ArchiveContentDetector.Judge(path);
        _judgements[directory] = judgement;
        return judgement;
    }

    // ---- 「展開元のzipファイルが無い」フォルダの枠（右の「分かっていること」の下） ----
    //
    // 前は右のいちばん上に1枚で出していて、どのフォルダの話か・何に使う枠かが読めなかった。左の束の中へ移すと束ごとに大きな枠が並んで
    // ごちゃごちゃしたので、選んだファイルのフォルダの話として右に出す（ユーザ指示 2026-09-17）。押すとそのフォルダのファイルが対象。

    /// <summary>束のファイルのどれかを選ぶ。既にその束の行を選んでいれば選び直さない（選び直すと確かめた商品IDが消える）。</summary>
    private bool FocusFolder(string directory)
    {
        if (Selected is { } current && string.Equals(current.DirectoryText, directory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var first = Files.FirstOrDefault(row => string.Equals(row.DirectoryText, directory, StringComparison.OrdinalIgnoreCase));
        if (first is null)
        {
            return false;
        }

        Selected = first;
        return true;
    }

    /// <summary>
    /// フォルダのまま商品として登録する。商品IDが要るので、まだ確かめていなければ確かめる欄へ案内する
    /// （押せない顔にすると、何をすれば押せるのかが分からない）。
    /// </summary>
    private async Task RegisterFolderOfAsync(object? parameter)
    {
        if (parameter is not string directory || !FocusFolder(directory))
        {
            return;
        }

        if (!HasPreview)
        {
            StatusText = "先に「商品IDを決める」で商品IDを確認してください。";
            OnPropertyChanged(nameof(HasStatus));
            DecisionFocusRequested?.Invoke();
            return;
        }

        await RegisterFolderAsync();
    }

    private Task ExcludeFolderAsync(object? parameter)
        => parameter is string directory
            ? ExcludeRowsAsync(
                Files.Where(row => string.Equals(row.DirectoryText, directory, StringComparison.OrdinalIgnoreCase)).ToList(),
                "このフォルダを管理対象から外す")
            : Task.FromResult(false);

    /// <summary>
    /// 登録の対象にするフォルダ。
    /// 取り込み元フォルダの直下の子を選ぶ（zipが展開されたときの単位と一致するため）。
    /// 取り込み元が分からなければ、目印のあるフォルダをそのまま使う。
    /// </summary>
    public string? RegisterTargetFolder
    {
        get
        {
            var row = Selected ?? Files.FirstOrDefault(entry => entry.IsArchiveContent);

            // 元のzipが分かる中身は、同じzipの中身が共通して入っているフォルダを登録する（ユーザ判断 2026-09-17：元のzipが未確定にあってもフォルダでの登録はできる）。
            // 取り込み元の直下や目印（.url など）から遡って決めると、別のzipを展開したフォルダまで巻き込む（作り物で「Temp」全体が対象になった）
            if (row is { IsExpandedContent: true } && CommonDirectoryOf(row.GroupKey) is { } common)
            {
                return common;
            }

            if (row?.ProductFolder is not { } marker || row.File.Paths.Count == 0)
            {
                return null;
            }

            var path = row.File.Paths[0];

            foreach (var root in _services.Settings.ImportFolders)
            {
                var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
                if (!path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rest = path[(normalizedRoot.Length + 1)..];
                var firstSegment = rest.Split(Path.DirectorySeparatorChar)[0];
                return Path.Combine(normalizedRoot, firstSegment);
            }

            return ClimbSingleChildFolders(marker);
        }
    }

    /// <summary>
    /// 取り込み元が分からないときの当て。
    /// 「そのフォルダしか入っていない親」が続く限り遡る。
    /// zipを展開すると rurune_v1.1.3/rurune のように1段包まれることが多く、
    /// 配布の単位は外側だから。中に他のものが混ざった時点で止める。
    /// </summary>
    private static string ClimbSingleChildFolders(string folder)
    {
        var current = folder;

        for (var depth = 0; depth < 4; depth++)
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                return current;
            }

            try
            {
                var entries = Directory.EnumerateFileSystemEntries(parent).Take(2).ToList();
                if (entries.Count != 1)
                {
                    return current;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return current;
            }

            current = parent;
        }

        return current;
    }

    /// <summary>同じ束（同じzipの中身）のファイルが共通して入っている、いちばん深いフォルダ。無ければ null。</summary>
    private string? CommonDirectoryOf(string groupKey)
    {
        var directories = Files
            .Where(row => row.IsExpandedContent && string.Equals(row.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase)
                && row.DirectoryText.Length > 0)
            .Select(row => row.DirectoryText.Split(Path.DirectorySeparatorChar))
            .ToList();
        if (directories.Count == 0)
        {
            return null;
        }

        var common = directories[0].AsEnumerable();
        foreach (var segments in directories.Skip(1))
        {
            common = common.Zip(segments).TakeWhile(pair => string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.First)
                .ToList();
        }

        var joined = string.Join(Path.DirectorySeparatorChar, common);

        // ドライブの直下（D:）まで遡ったものはフォルダの登録にしない（ドライブごと巻き込む）
        return joined.Count(ch => ch == Path.DirectorySeparatorChar) >= 1 ? joined : null;
    }

    public string RegisterTargetName => Path.GetFileName(RegisterTargetFolder ?? string.Empty);

    public bool CanRegisterFolder => RegisterTargetFolder is not null && HasPreview && !IsBusy;

    public string RegisterFolderText => RegisterTargetName.Length > 0
        ? $"「{RegisterTargetName}」を商品として登録"
        : "このフォルダを商品として登録";

    /// <summary>
    /// フォルダを商品に紐付ける。zipを落とし直せない場合の受け皿。
    /// 紐付けると配下がスキャン対象から外れるので、未確定も一緒に片付く。
    /// </summary>
    private async Task RegisterFolderAsync()
    {
        if (RegisterTargetFolder is not { } folder || Preview is null)
        {
            return;
        }

        var (count, bytes) = RegisteredFolderSet.Measure(folder);

        var answer = System.Windows.MessageBox.Show(
            $"次のフォルダを「{Preview.Name}」（ID {Preview.Id}）として登録します。\n\n"
            + $"{folder}\n{count} ファイル / {Core.Models.DisplayText.Size(bytes)}\n\n"
            + "以降このフォルダの中は取り込みで読まなくなり、未確定にも出てこなくなります。\n"
            + "フォルダを動かすとつながりが切れるので、そのときは登録し直してください。",
            "フォルダを商品として登録",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.RegisterFolder(Preview.Id, folder));

            if (result is CommandResult.Failed failed)
            {
                StatusText = failed.Message;
                OnPropertyChanged(nameof(HasStatus));
                return;
            }

            if (!_settledItemIds.Contains(Preview.Id))
            {
                _settledItemIds.Add(Preview.Id);
            }

            // itemの中身が変わったので、持ち回っているライブラリも読み直す。
            // これをしないと商品ページに登録したフォルダが出てこない
            await _main.ReloadLibraryAsync();

            await ReloadAsync();
            StatusText = $"「{RegisterTargetName}」を登録しました。配下の未確定は一覧から外れます。";
            OnPropertyChanged(nameof(HasStatus));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
