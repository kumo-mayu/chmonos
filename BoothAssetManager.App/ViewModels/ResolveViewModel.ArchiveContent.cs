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
    /// <summary>
    /// 判定はフォルダ単位で同じになるので、フォルダをキーに覚えておく。
    /// 読み直しは裏のスレッドで組むので、覚える表は呼び手が渡す（組み終えてから画面の表へ移す）
    /// </summary>
    private static ArchiveContentJudgement JudgeCached(string path, Dictionary<string, ArchiveContentJudgement> judgements)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (judgements.TryGetValue(directory, out var cached))
        {
            return cached;
        }

        var judgement = ArchiveContentDetector.Judge(path);
        judgements[directory] = judgement;
        return judgement;
    }

    // ---- 「展開元のzipファイルが無い」フォルダの枠（右の「分かっていること」の下） ----
    //
    // 前は右のいちばん上に1枚で出していて、どのフォルダの話か・何に使う枠かが読めなかった。左の束の中へ移すと束ごとに大きな枠が並んで
    // ごちゃごちゃしたので、選んだファイルのフォルダの話として右に出す（ユーザ指示 2026-09-17）。押すとそのフォルダのファイルが対象。

    /// <summary>束のファイルのどれかを選ぶ。既にその束の行を選んでいれば選び直さない（選び直すと確かめた商品IDが消える）。</summary>
    /// <param name="groupKey">束の鍵（zipの名前・展開物の根・フォルダ）。</param>
    private bool FocusFolder(string groupKey)
    {
        if (Selected is { } current && string.Equals(current.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var first = Files.FirstOrDefault(row => string.Equals(row.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase));
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
        if (parameter is not string groupKey || !FocusFolder(groupKey))
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

    /// <summary>読み直しの間だけ使う、展開物の根を探すための控え（裏のスレッドで行を組むので、画面の欄には置かない）。</summary>
    /// <param name="UnpackRoots">目印のフォルダごとの根。</param>
    /// <param name="ForeignPaths">根に入っていてはいけない場所（zipの中身・zip自身・商品が持っているファイル）。</param>
    /// <param name="ImportFolders">取り込み元（ここ以上には広げない）。</param>
    private sealed record UnpackRootContext(
        Dictionary<string, string> UnpackRoots,
        IReadOnlyList<string> ForeignPaths,
        IReadOnlyList<string> ImportFolders);

    /// <summary>
    /// zipが無い展開物の根。目印（.unitypackage・.url）の見つかった一番外側から、中身がそのフォルダしか無い親を遡る
    /// （zipを展開すると rurune_v1.1.3/rurune のように1段包まれることが多く、配布の単位は外側）。
    /// **広くなりすぎないようにする**：ドライブの直下や取り込み元そのもの（またはその上）になったら、目印のフォルダ、
    /// それも駄目ならファイルが入っているフォルダに戻す。取り込み元の直下まで広げる決め方はやめた（別の展開物まで巻き込む）。
    /// </summary>
    private static string UnpackRootFor(string path, string? marker, UnpackRootContext context)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (marker is null)
        {
            return directory;
        }

        if (!context.UnpackRoots.TryGetValue(marker, out var root))
        {
            var climbed = ClimbSingleChildFolders(marker);
            root = IsUsableRoot(climbed, context) ? climbed : IsUsableRoot(marker, context) ? marker : string.Empty;
            context.UnpackRoots[marker] = root;
        }

        return root.Length > 0 && (directory.Equals(root, StringComparison.OrdinalIgnoreCase)
                || directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            ? root
            : directory;
    }

    /// <summary>
    /// 根にしてよいか。広すぎず、**中に別の物が入っていない**こと——zipを展開した中身・zip自身・商品が持っているファイルが入っていれば、
    /// 無関係なファイルまで1つの束・1つの登録にまとめてしまう（作り物を %TEMP% に置くと、元からある .url を目印に Temp 全体が根になった・2026-09-17）。
    /// </summary>
    private static bool IsUsableRoot(string folder, UnpackRootContext context)
    {
        if (IsTooWide(folder, context.ImportFolders))
        {
            return false;
        }

        var prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return !context.ForeignPaths.Any(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTooWide(string folder, IReadOnlyList<string> importFolders)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(folder);
        if (string.Equals(trimmed, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(folder) ?? string.Empty), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return importFolders
            .Select(Path.TrimEndingDirectorySeparator)
            .Any(importRoot => string.Equals(importRoot, trimmed, StringComparison.OrdinalIgnoreCase)
                || importRoot.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 登録の対象にするフォルダ。zipの中身は同じzipの中身が共通して入っているフォルダ、zipが無い展開物はその根
    /// （左の束・まとめて扱う単位と同じ・ユーザ判断 2026-09-17 案A）。
    /// </summary>
    public string? RegisterTargetFolder
    {
        get
        {
            var row = Selected ?? Files.FirstOrDefault(entry => entry.IsArchiveContent);

            // 元のzipが分かる中身は、同じzipの中身が共通して入っているフォルダを登録する（元のzipが未確定にあってもフォルダでの登録はできる）。
            // 目印（.url など）から遡って決めると、別のzipを展開したフォルダまで巻き込む（作り物で「Temp」全体が対象になった）
            if (row is { IsExpandedContent: true } && CommonDirectoryOf(row.GroupKey) is { } common)
            {
                return common;
            }

            return row?.UnpackRoot;
        }
    }

    /// <summary>フォルダのまま登録するときの対象を、押す前に見せる（どこまで広いかをボタンの名前だけで判断させない）。</summary>
    public string RegisterTargetSummary => RegisterTargetFolder is { } folder && Selected is { } row
        ? $"対象：{folder}（未確定 {Files.Count(other => string.Equals(other.GroupKey, row.GroupKey, StringComparison.OrdinalIgnoreCase))} 件）"
        : string.Empty;

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

        var answer = Services.Notice.Show(
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
