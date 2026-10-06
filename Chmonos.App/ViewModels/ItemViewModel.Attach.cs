using Chmonos.App.Services;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品ページ：選んだ・落としたファイルをこの商品に紐付ける（ユーザ指示 2026-10-06）。
///
/// 作者が前の商品を消して同じ物を新しいIDで出し直すと、ファイルの手掛かり（Zone.Identifier・ファイル名）は古いIDを指したままで、
/// 取り込むと古い商品の方へ行くか未確定に出る。商品ページからファイルを直にその商品へ紐付ける道が無かった。
/// 事情（除外・ほかの持ち主）は通知の「zipで登録し直す」と同じに聞く（`InboxViewModel.SwapFolderForArchiveAsync`）。
/// </summary>
public sealed partial class ItemViewModel
{
    /// <summary>試験だけが差し替える（ファイルを選ぶ窓は答える人がいないと止まる）。返すのは選んだファイル、閉じたら null。</summary>
    internal static Func<IReadOnlyList<string>?>? PickFilesToAttachIntercept { get; set; }

    /// <summary>ファイルの欄の「追加…」。</summary>
    public RelayCommand AttachFilesCommand { get; private set; } = null!;

    private bool _isAttachingFiles;

    private void InitAttachCommand()
        => AttachFilesCommand = new RelayCommand(
            () => PickAndAttachAsync().Forget(),

            // 取り込みの③が済むまでは、商品の中身を変える操作を塞ぐ（外す・戻すと同じ）。読んでいる間の2度押しで同じファイルを2回読ませない
            () => !IsEditLocked && !_isAttachingFiles);

    /// <summary>
    /// 選ぶ窓を出して紐付ける。返すのは紐付いたファイルの場所（選ばなかった・断られた物は入らない）。
    /// 編集画面のバリエーションの行も、紐付いた物だけをその種類に結ぶためにこれを通す
    /// </summary>
    /// <param name="title">窓の題。既定は「この商品に追加するファイルを選ぶ」。</param>
    internal async Task<IReadOnlyList<string>> PickAndAttachAsync(string? title = null)
    {
        var picked = PickFilesToAttachIntercept is { } intercept ? intercept() : PickFiles(title ?? "この商品に追加するファイルを選ぶ");
        return picked is { Count: > 0 } ? await AttachFilesAsync(picked) : [];
    }

    /// <summary>取り込む拡張子と同じ物だけを選ばせる（単体の unitypackage などは、紐付けても送れも開けもしない行になる）。</summary>
    private static IReadOnlyList<string>? PickFiles(string title)
    {
        var patterns = string.Join(";", Core.Scanning.FolderScanner.TargetExtensions.Order().Select(extension => "*" + extension));
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = $"BOOTHのファイル|{patterns}",
            Multiselect = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    /// <summary>
    /// 紐付けてファイルの行を組み直した後に呼ぶ（編集画面が、右のバリエーション分けの一覧を開き直さずに足すため）。
    /// 引数は読み直した商品。null（単独の商品ページ）なら何もしない
    /// </summary>
    public Action<ItemRecord>? FilesAttached { get; set; }

    /// <summary>1本ずつ紐付けた結果。<see cref="OpenItemId"/> は「その商品を開く」を選んだときの行き先。</summary>
    private readonly record struct AttachStep(bool Written, string? Failure, IReadOnlyList<ArchiveHolder> TakenFrom, string? OpenItemId);

    /// <summary>
    /// ファイルをこの商品に紐付ける。選んだ場合と落とした場合で同じ道を通す。
    /// 1本ずつ順に読む（ハッシュに数秒かかる物もあるが、窓で聞く問いはファイルごとに違う）。
    /// 紐付けたら**ファイルの行だけをその場で組み直す**（ページ全体を組み直すと、読んでいた位置や開いた欄が動く）。
    /// </summary>
    /// <returns>紐付いたファイルの場所。断られた・やめた・もう紐付いていた物は入らない。</returns>
    public async Task<IReadOnlyList<string>> AttachFilesAsync(IReadOnlyList<string> paths)
    {
        if (_isAttachingFiles || paths.Count == 0)
        {
            return [];
        }

        _isAttachingFiles = true;
        RelayCommand.RaiseCanExecuteChanged();

        // 結果の行は欄の中にあるので、畳んでいたら開く（何も起きなかったように見えないように）
        IsFilesExpanded = true;
        FilesNotice.Show(paths.Count == 1 ? "ファイルを読んでいます…" : $"{paths.Count} 件のファイルを読んでいます…");

        var attached = new List<string>();
        var failures = new List<string>();
        var others = new List<ArchiveHolder>();
        string? openItemId = null;
        try
        {
            foreach (var path in paths)
            {
                var step = await AttachOneAsync(path);
                if (step.Written)
                {
                    attached.Add(path);
                }

                others.AddRange(step.TakenFrom);
                if (step.Failure is { } failure)
                {
                    failures.Add(failure);
                }

                if (step.OpenItemId is { } open)
                {
                    // 「その商品を開く」を選んだら、残りは聞かずにやめる（ページを移るので、残りの結果を出す所が無い）
                    openItemId = open;
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            // 受けないと「読んでいます…」のまま残り、止まったように見える
            Core.Diagnostics.AppLog.Error("商品ページでのファイルの紐付け", exception);
            failures.Add($"紐付けられませんでした。{FailureText.Cause(exception)}");
        }
        finally
        {
            _isAttachingFiles = false;
            RelayCommand.RaiseCanExecuteChanged();
        }

        // 紐付いたことは行が出ることで分かるので言わない（D7）。言うのは紐付かなかった物だけ
        if (failures.Count == 0)
        {
            FilesNotice.Clear();
        }
        else
        {
            FilesNotice.Warn(failures.Count == 1
                ? failures[0]
                : $"{failures[0]}ほか {failures.Count - 1} 件も紐付けられませんでした。");
        }

        if (attached.Count > 0)
        {
            await AfterAttachAsync(others);
        }

        if (openItemId is not null && await _services.Store.Items.LoadAsync(openItemId) is { } holder)
        {
            _main.ShowItem(holder);
        }

        return attached;
    }

    private async Task<AttachStep> AttachOneAsync(string path)
    {
        // 除外している・ほかの商品に付いているときは、Core は何も書かずにそう返す。人が窓で選んだときだけ、頼みを足して呼び直す
        // （除外も持ち主も人が決めたこと。黙っては上書きしない。ユーザ判断 2026-10-05）。除外を解いた後でほかの持ち主が分かることがあるので、聞くのは多くても2回
        var command = new UiCommand.AttachFile(Item.Id, path);
        IReadOnlyList<ArchiveHolder> takenFrom = [];
        var result = await _services.Commands.ExecuteAsync(command);
        for (var asked = 0; asked < 2 && result is CommandResult.FileAttached { Outcome: { } pending }; asked++)
        {
            if (pending.Result == FileAttachResult.Excluded && !command.LiftExclusion)
            {
                if (Notice.Show(
                        ExcludedQuestion(pending.FileName),
                        "ファイルを紐付ける",
                        System.Windows.MessageBoxButton.OKCancel,
                        System.Windows.MessageBoxImage.Question,
                        System.Windows.MessageBoxResult.Cancel) != System.Windows.MessageBoxResult.OK)
                {
                    return new AttachStep(false, null, [], null);
                }

                command = command with { LiftExclusion = true };
            }
            else if (pending.Result == FileAttachResult.OwnedElsewhere && !command.TakeFromOtherItems && pending.Holders.Count > 0)
            {
                var choice = ChoiceQuestion.Ask(ChoiceQuestion.OwnedElsewhere("ファイルを紐付ける", pending.FileName, pending.Holders));
                if (choice == Views.ChoiceDialogResult.First)
                {
                    return new AttachStep(false, null, [], pending.Holders[0].ItemId);
                }

                if (choice != Views.ChoiceDialogResult.Second)
                {
                    return new AttachStep(false, null, [], null);
                }

                command = command with { TakeFromOtherItems = true };
                takenFrom = pending.Holders;
            }
            else
            {
                break;
            }

            result = await _services.Commands.ExecuteAsync(command);
        }

        return result is CommandResult.FileAttached { Outcome: { } outcome }
            ? outcome.Result == FileAttachResult.Attached
                ? new AttachStep(true, null, takenFrom, null)
                : new AttachStep(false, FailureOf(outcome), [], null)
            : new AttachStep(false, result is CommandResult.Failed failed ? failed.Message : "紐付けられませんでした。", [], null);
    }

    /// <summary>除外しているファイルを紐付けるかの確かめ。既定はやめる側（除外も人が決めたこと）。</summary>
    internal static string ExcludedQuestion(string fileName)
        => $"「{fileName}」は管理対象から除外しています。除外を解除して、この商品に紐付けますか？\n\n"
            + "あとで「この商品から外す」で外せます。";

    /// <summary>紐付かなかったときの1行。紐付いたときは null（行が出ることで分かる）。</summary>
    internal static string? FailureOf(FileAttachOutcome outcome) => outcome.Result switch
    {
        FileAttachResult.Attached => null,
        FileAttachResult.AlreadyAttached => $"「{outcome.FileName}」は、もうこの商品に紐付いています。",
        FileAttachResult.FileMissing => $"「{outcome.FileName}」が見つかりません。移動したか削除した可能性があります。",
        FileAttachResult.FileUnreadable => $"「{outcome.FileName}」を読めませんでした。ほかのアプリが開いている可能性があります。",
        FileAttachResult.NotTarget => $"「{outcome.FileName}」は紐付けられない種類のファイルです。",

        // 聞いた後でまた別の事情が返った（間に除外・登録された）。黙って進めず、そのままにする
        FileAttachResult.Excluded or FileAttachResult.OwnedElsewhere
            => $"「{outcome.FileName}」の扱いが変わったので、紐付けませんでした。もう一度お試しください。",
        _ => "この商品は見つかりませんでした。",
    };

    /// <summary>
    /// 紐付けた後。ファイルの行をその場で組み直し、検索の写しへ知らせ、ナビの数を数え直す（未確定から消えることがある）。
    /// 付け直した相手の商品も外した印が付いたので知らせる（容量・所持の絞り込みが古いまま残らないように）。
    /// 中の unitypackage の控えは、命令の後で裏で読まれる（登録の後と同じ）。この行の Unity へ送れる物は、行を出した後で zip を見て付ける
    /// </summary>
    private async Task AfterAttachAsync(IReadOnlyList<ArchiveHolder> takenFrom)
    {
        _main.RefreshBadges();
        foreach (var holder in takenFrom.DistinctBy(holder => holder.ItemId))
        {
            if (await _services.Store.Items.LoadAsync(holder.ItemId) is { } other)
            {
                _main.Search.NoteItemChanged(other);
            }
        }

        if (await _services.Store.Items.LoadAsync(Item.Id) is not { } reloaded)
        {
            return;
        }

        _main.Search.NoteItemChanged(reloaded);
        RebuildLocalFilesInPlace(reloaded);
        FilesAttached?.Invoke(reloaded);
    }

    private void RebuildLocalFilesInPlace(ItemRecord reloaded)
    {
        Item = reloaded;
        LocalFiles.Clear();
        BuildLocalFiles();
        OnPropertyChanged(nameof(FileSummary));
        if (ShowsUseActions)
        {
            LoadUnityPackagesAsync().Forget();
        }
    }
}
