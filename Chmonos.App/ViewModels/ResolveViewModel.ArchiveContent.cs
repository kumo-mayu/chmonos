using System.Collections.ObjectModel;
using System.IO;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>未確定画面：展開した中身の見分けとフォルダごとの登録（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ResolveViewModel
{
    /// <summary>
    /// 判定はフォルダ単位で同じになるので、フォルダをキーに覚えておく。
    /// 読み直しは裏のスレッドで組むので、覚える表は呼び手が渡す（組み終えてから画面の表へ移す）
    /// </summary>
    /// <param name="pass">この1回の組み立ての間だけの見分け。親のフォルダの列挙を、フォルダの数だけ繰り返さないために渡す。</param>
    private static ArchiveContentJudgement JudgeCached(
        string path,
        Dictionary<string, ArchiveContentJudgement> judgements,
        ArchiveContentDetector.Pass pass)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (judgements.TryGetValue(directory, out var cached))
        {
            return cached;
        }

        var judgement = pass.Judge(path);
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
            // 押したボタンの横で言う（前は「商品IDを決める」の欄へ画面を送っていた。押した所から飛ぶ作りはやめた。メモ22）
            FolderStatusText = "先に「商品IDを決める」で商品IDを確認してください。";
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
    /// <summary>試験の口：登録の前にフォルダを数える処理を差し替える（数えている間に選び直す場面を作る）。</summary>
    internal Func<string, CancellationToken, (int FileCount, long TotalBytes)?>? MeasureFolderForTest { get; set; }

    private async Task RegisterFolderAsync()
    {
        if (RegisterTargetFolder is not { } folder || Preview is not { } target)
        {
            return;
        }

        // **始めた時の商品に固定する**（外部の点検 2026-10-06）。数えている間に別の行や候補を選べるので、
        // 待った後に Preview を読み直すと、数えたフォルダを選び直した別の商品へ登録していた（選び直して空なら落ちていた）
        var targetName = RegisterTargetName;

        // 数えるのは裏で（大きなフォルダや HDD では数十秒かかる。前は画面のスレッドで数え、その間ずっと固まっていた。外部の点検 2026-10-06）。
        // 数えている間は登録のボタンを止める（同じ登録を二度始めない）。画面を離れたら数えるのをやめる
        IsBusy = true;
        FolderStatusText = "フォルダの中を数えています…";
        (int FileCount, long TotalBytes)? measured;
        try
        {
            var token = _leaving.Token;
            measured = await Task.Run(() => (MeasureFolderForTest ?? RegisteredFolderSet.Measure)(folder, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            IsBusy = false;
        }

        // 数えている間に選び直していたら、確かめの窓を出さずにやめる（もう一度押せば今の選択で数え直す）
        if (!ReferenceEquals(Preview, target))
        {
            FolderStatusText = string.Empty;
            return;
        }

        if (measured is not (int count, long bytes))
        {
            FolderStatusText = "フォルダの中を読めませんでした。";
            return;
        }

        FolderStatusText = string.Empty;

        var answer = Services.Notice.Show(
            $"次のフォルダを「{target.Name}」（ID {target.Id}）として登録します。\n\n"
            + $"{folder}\n{count} ファイル / {Core.Models.DisplayText.Size(bytes)}\n\n"
            + "このフォルダの中は、以降の取り込みと未確定の対象から外れます。\n"
            + "フォルダを移動したときは、登録し直してください。",
            "フォルダを商品として登録",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        StartRegistering(RegisteringArea.Folder, 1);
        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.RegisterFolder(target.Id, folder, RequestsLeftProgress));

            if (result is CommandResult.Failed failed)
            {
                FolderStatusText = failed.Message;
                return;
            }

            // itemの中身が変わったので、持ち回っているライブラリの写しにもその1件を当てる。
            // これをしないと商品ページに登録したフォルダが出てこない。全件は読み直さない（2000件で数秒。確定のたびに重ねると固まる）
            await NoteSettledAsync(target.Id);

            await ReloadAsync();

            // ほかの片付け方（RemoveRows・AfterSettled）と同じく、ナビの未確定の数をその場で数え直す。
            // ここは一覧を読み直すだけで行を外す道を通らないので、呼ばないと次に数え直すまで古い数が残っていた
            _main.RefreshBadges();
            // 配下の行ごと消えるので、一覧の見出しの近くに出す
            ListNoticeText = $"「{targetName}」を登録しました。配下の未確定は一覧から外れます。";
        }
        finally
        {
            EndRegistering();
            IsBusy = false;
        }
    }
}
