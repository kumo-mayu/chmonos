using System.IO;
using System.Windows;
using Chmonos.App.Services;
using Chmonos.Core.Commands;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 改変の画面：Unity プロジェクトの場所を付け替える（ユーザ判断 2026-11-10「付け替えの操作は足しても良さそうですね」）。
///
/// フォルダを移した・名前を変えると、改変の紐付け・検索の条件が古い場所のまま黙って切れる。
/// 置き場はプロジェクトの見方の行：見つからないプロジェクトの行にボタン、見つかるプロジェクトでも右クリックから。
/// **候補は出さず、選ぶだけ**（ユーザ判断 同日）。移した先を推して出すと、取り違えた先を押してしまう
/// </summary>
public sealed partial class ModificationHubViewModel
{
    private RelayCommand? _relocateProjectCommand;

    /// <summary>プロジェクトの行・右の欄の「場所を付け替える…」。引数はプロジェクトの見出しか場所。</summary>
    public RelayCommand RelocateProjectCommand
        => _relocateProjectCommand ??= new RelayCommand(parameter => RelocateProjectAsync(PathOf(parameter)).Forget());

    /// <summary>
    /// フォルダを選ぶ窓を出す代わりに答える口。**アプリでは null のまま**（窓を出す）。試験が入れる
    /// （窓を出すと答える人がいないので止まる。<see cref="Notice.Intercept"/> と同じ）。引数は古い場所、返すのは選んだ場所（やめたら null）
    /// </summary>
    internal static Func<string, string?>? PickFolderIntercept { get; set; }

    /// <summary>付け替えの途中か。押し直しで2本走らせない（2本目は古い場所を読み、1本目の書いた後と食い違う）。</summary>
    private bool _relocating;

    private async Task RelocateProjectAsync(string? from)
    {
        if (string.IsNullOrWhiteSpace(from) || _relocating)
        {
            return;
        }

        // 押した行（と同じ鍵の右の欄）の下に出す
        var pressedKey = $"project:{from}";
        if (PickFolder(from) is not { } picked)
        {
            return;
        }

        // 同じ場所を選び直したのは、やめたのと同じ（書く物が無い）
        if (ModificationService.SamePath(picked, from))
        {
            return;
        }

        if (!await Task.Run(() => UnityProjects.LooksLikeProject(picked)))
        {
            // 窓を開き直す輪にはしない：知らせは窓に隠れて読めず、閉じない限り抜けられない。行の下で断り、押し直してもらう
            ShowNotice(pressedKey, NotProjectText, warning: true);
            return;
        }

        var others = _records.Count(record => ModificationService.SamePath(record.UnityProject, picked));
        if (others > 0 && !ConfirmMerge(picked, others))
        {
            return;
        }

        _relocating = true;
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.RelocateUnityProject(from, picked));
            if (result is not CommandResult.UnityProjectRelocated { Result: var relocation })
            {
                ShowNotice(pressedKey, (result as CommandResult.Failed)?.Message ?? "付け替えられませんでした。", warning: true);
                return;
            }

            // 書いた先と同じ綴りで、ほかの画面の写しにも当てる（命令の側も同じ関数で揃えている）
            var to = UnityProjectPath.Normalize(picked) ?? picked;
            _main.Search.NoteModificationsChanged();
            _main.Search.RelocateUnityProject(from, to);
            if (relocation.SavedSearches > 0)
            {
                _main.Search.RestoreSavedSearches();
            }

            // 使っている商品の見張りは場所を自分で持たず、改変の記録から引く。新しい場所を調べるよう頼むだけ
            _main.UsedItems.RequestAsync().Forget();

            await RefreshRecordsAsync();

            // 書けなかった改変があれば、古い場所の行が残っている。そこで言えば、もう一度押せる
            var failed = relocation.FailedModifications > 0;
            ShowProject(failed ? from : to);
            var key = (Detail as HubProjectDetail)?.NoticeKey ?? pressedKey;
            ShowNotice(key, ResultText(relocation), warning: failed || relocation.SavedSearchesFailed);
        }
        finally
        {
            _relocating = false;
        }
    }

    private static string? PickFolder(string from)
    {
        if (PickFolderIntercept is { } intercept)
        {
            return intercept(from);
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Unityプロジェクトの新しい場所を選択" };
        try
        {
            // 古い場所の1つ上が在れば、そこから開く（同じフォルダの中で名前を変えたときは、すぐ隣に見える）
            if (Path.GetDirectoryName(from) is { } parent && Directory.Exists(parent))
            {
                dialog.InitialDirectory = parent;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException)
        {
            // 開き始める所が決まらないだけ（窓の既定の所から選べる）
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>
    /// 既に別の改変が使っているプロジェクトへ付け替えるときに聞く。統合は分け直す手段が無い（どちらから来た改変かを残さない）ので、
    /// 既定はキャンセル（`ui-dialogs.md`「取り返しがつくか」）
    /// </summary>
    private static bool ConfirmMerge(string to, int others)
        => Notice.Show(
            MergeQuestion(DisplayText(to), others),
            "場所を付け替える",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private static string DisplayText(string path) => Core.Models.DisplayText.FolderName(path);

    internal static string MergeQuestion(string name, int others)
        => $"「{name}」には、既に改変が {others} 件紐付いています。付け替えると、両方の改変が「{name}」に統合されます。\n\n"
            + "統合した後に分けるには、改変ごとに紐付け直してください。";

    internal const string NotProjectText = "選んだフォルダはUnityのプロジェクトではありません。AssetsとProjectSettingsのあるフォルダを選んでください。";

    /// <summary>付け替えた結果の文。書けた物を先に、書けなかった物を後に言う（書けた分は戻していない）。</summary>
    internal static string ResultText(UnityProjectRelocation result)
    {
        var done = (result.Modifications, result.SavedSearches) switch
        {
            ( > 0, > 0) => $"改変 {result.Modifications} 件と保存した検索 {result.SavedSearches} 件を、新しい場所に付け替えました。",
            ( > 0, _) => $"改変 {result.Modifications} 件を、新しい場所に付け替えました。",
            (_, > 0) => $"保存した検索 {result.SavedSearches} 件を、新しい場所に付け替えました。",
            _ => null,
        };

        var text = done ?? string.Empty;
        if (result.FailedModifications > 0)
        {
            text += $"改変 {result.FailedModifications} 件は書き込めませんでした。もう一度押すと、残りを付け替えます。";
        }

        if (result.SavedSearchesFailed)
        {
            text += "保存した検索は書き込めず、古い場所のままです。";
        }

        return text.Length > 0 ? text : "付け替える改変と保存した検索はありませんでした。";
    }
}
