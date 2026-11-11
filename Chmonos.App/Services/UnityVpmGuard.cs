using System.IO;
using Chmonos.App.ViewModels;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>送る前に VPM との重なりを聞いた答え。<see cref="Skipped"/> は送る列の中の位置。</summary>
internal sealed record VpmScreen(bool Cancelled, IReadOnlySet<int> Skipped)
{
    public static VpmScreen None { get; } = new(false, new HashSet<int>());
}

/// <summary>
/// 「Unityへ送る」の前に、送る unitypackage と送り先のプロジェクトの VPM の重なりを言う（plan 40・ユーザ判断 2026-11-11）。
///
/// 同じ GUID・違う中身の物を取り込むと、Unity は VPM のファイルを黙って上書きする（<c>docs/research/vpm-duplicate-guid.md</c>）。
/// 取り込み画面で重なるフォルダのチェックを外せば避けられるので、送るのを止めはせず、外す場所を言って選ばせる。
///
/// - 重なりが無ければ何も聞かない。中身の控えが無い包み（単体の unitypackage・フォルダの中の物・まだ読んでいない zip）は照らせないので、黙って送る
/// - **静かにする設定（<c>QuietUnitySend</c>）でも窓で聞く**：VPM が壊れても取り込みの後に何も出ず、通知では押す前に読めない
/// - 送る物が1つなら［送る］［やめる］、2つ以上なら重なる包みごとに［送る］［これを飛ばす］［残りすべてを中止］（1件ずつ聞く窓の決まり。ui-dialogs.md D11）。
///   **既定のボタンは置かない**（Enter で送らない）。Esc と × はやめる側
/// </summary>
internal static class UnityVpmGuard
{
    /// <summary>飛ばした包みの結果の理由（人が選んだので、失敗と言わない。<see cref="UnityImportQueue.IsStopped"/>）。</summary>
    public const string SkippedMessage = "VPMと同じファイルがあるので、飛ばしました。";

    /// <summary>やめた・残りを中止したときの結果の理由。</summary>
    public const string DeclinedMessage = "送るのをやめました。";

    /// <summary>照らしている間の帯の文（初めての送りでは、プロジェクトの .meta を数千読むので1〜2秒かかることがある）。</summary>
    public const string CheckingText = "送る前に、VPMとの重なりを確かめています…";

    /// <summary>
    /// 照らして聞く。照らすのは裏のスレッド、聞くのは呼んだスレッド（画面のスレッドから呼ぶ）。
    /// <paramref name="project"/> が分からない（題から場所を言い当てられないエディタ）ときは照らせないので、聞かずに送る。
    /// </summary>
    public static async Task<VpmScreen> ScreenAsync(
        string? project, IReadOnlyList<UnityPackageEntry> packages, UnityPackageReads reads, CancellationToken cancellationToken)
    {
        if (project is null || packages.Count == 0)
        {
            return VpmScreen.None;
        }

        IReadOnlyList<VpmOverlapPackage> found;
        try
        {
            found = await Task.Run(() => VpmOverlaps.FindInProject(project, packages, reads), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 照らせなかったら今まで通り送る（取り込み画面は人が見るので、そこで止められる）
            Core.Diagnostics.AppLog.Error("Unityへ送る：VPMとの重なりを確かめる", exception);
            return VpmScreen.None;
        }

        return Decide(found, packages.Count, ChoiceQuestion.Ask);
    }

    /// <summary>
    /// 先に聞いて、送る物を絞る（「改変に追加して送る」は記録を書いてから送るので、記録の前に聞く。やめたのに記録だけ残らないように）。
    /// やめたら null。
    /// </summary>
    public static async Task<IReadOnlyList<UnityPackageEntry>?> FilterAsync(string? project, IReadOnlyList<UnityPackageEntry> packages)
    {
        var screen = await ScreenAsync(project, packages, new UnityPackageReads(), CancellationToken.None);
        return screen.Cancelled ? null : packages.Where((_, index) => !screen.Skipped.Contains(index)).ToList();
    }

    /// <summary>重なりの見つかった包みを、送る順に1つずつ聞く（試験は <paramref name="ask"/> を差し替える）。</summary>
    internal static VpmScreen Decide(IReadOnlyList<VpmOverlapPackage> found, int total, Func<ChoiceRequest, Views.ChoiceDialogResult> ask)
    {
        if (found.Count == 0)
        {
            return VpmScreen.None;
        }

        var skipped = new HashSet<int>();
        for (var number = 0; number < found.Count; number++)
        {
            var answer = ask(Question(found[number], number + 1, found.Count, many: total > 1));
            switch (answer)
            {
                case Views.ChoiceDialogResult.First:
                    break;
                case Views.ChoiceDialogResult.Second when total > 1:
                    skipped.Add(found[number].Index);
                    break;
                default:
                    // 1つのときの「やめる」（2つ目）、2つ以上のときの「残りすべてを中止」（3つ目）、Esc
                    return new VpmScreen(true, skipped);
            }
        }

        return new VpmScreen(false, skipped);
    }

    /// <summary>
    /// 聞く中身。1行目に何がどれだけ重なるか、2行目に何が起きるかと、取り込み画面で何をするか。
    /// 重なるフォルダがまとまらない（Assets の直下に散らばる）ときは、VPM の名前で外す物を言う。
    /// </summary>
    internal static ChoiceRequest Question(VpmOverlapPackage hit, int number, int count, bool many)
    {
        var names = string.Join("・", hit.Overlaps.Select(overlap => overlap.Package.Shown));
        var roots = hit.Overlaps.Select(overlap => overlap.Root).ToList();
        var uncheck = roots.All(root => root is not null)
            ? $"Unityの取り込み画面で{string.Join("と", roots.Distinct(StringComparer.OrdinalIgnoreCase).Select(root => $"「{root}」"))}のチェックを外してください。"
            : $"Unityの取り込み画面で、{names}のファイルのチェックを外してください。";
        var title = count > 1 ? $"Unityへ送る（{number}/{count}）" : "Unityへ送る";

        return new ChoiceRequest(
            title,
            $"「{hit.Entry.Name}」の中に、VPMで入っている{names}と同じファイルが {hit.Count} 個あります。",
            $"取り込むと、VPMの{names}が上書きされます。{uncheck}",
            "送る",
            many ? "これを飛ばす" : "やめる",
            many ? "残りすべてを中止" : null,
            LastCancels: true);
    }
}
