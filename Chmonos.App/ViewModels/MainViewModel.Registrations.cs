namespace Chmonos.App.ViewModels;

/// <summary>
/// 主画面：未確定の画面の登録の列と自動検索の進み（ユーザ判断 2026-10-05 メモ60）。
/// どちらも画面を離れても続くので、開くたびに作り直す未確定の画面ではなく、ここが持つ。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// 「このIDで登録」の順番待ち。1件ずつ流す。持つのはアプリの一式（画面の無い起動でも続きを流すため。docs/history/headless-core-2026-10-13.md）。
    /// 主画面は、登録できた商品を控えと検索の写しへ足す役と、未確定の画面が受け止めなかった終わりを帯で知らせる役を受け持つ
    /// </summary>
    public RegistrationQueue Registrations => _services.Registrations;

    /// <summary>主画面の役を列へ差し込む（作るときに1回）。</summary>
    private void ListenToRegistrations()
    {
        _services.RegistrationSettled = NoteResolveSettledAsync;
        Registrations.Unhandled += outcome => NoteRegistrationAwayAsync(outcome).Forget();
    }

    /// <summary>自動検索の進み。走るのは1本だけ。</summary>
    public ResolveSearchState ResolveSearch { get; } = new();

    /// <summary>
    /// 確定・登録した商品を控え（まとめて編集へ送る分）に足し、その商品だけを読んで検索の写しに足す。
    /// 未確定の画面を離れた後に済んだ登録も、戻ったときに「確定して、まだ編集へ送っていない商品」に入っているように主画面で行う。
    /// </summary>
    public async Task NoteResolveSettledAsync(string itemId)
    {
        if (!ResolveSettledItemIds.Contains(itemId))
        {
            ResolveSettledItemIds.Add(itemId);
        }

        if (await _services.Store.Items.LoadAsync(itemId) is { } item)
        {
            Search.NoteItemSaved(item);
        }

        RefreshBadges();
    }

    /// <summary>
    /// 未確定の画面を開いていない間に終わった登録を、下の帯で知らせる。
    /// 押した人は別の画面にいるので、画面は替えない（落とした URL の登録と同じ帯）。
    /// </summary>
    private async Task NoteRegistrationAwayAsync(RegistrationOutcome outcome)
    {
        if (outcome.SettledHashes.Count > 0 && await _services.Store.Items.LoadAsync(outcome.Job.ItemId) is { } item)
        {
            NoteRegistered(item);
        }

        if (outcome.Failure is not null)
        {
            NoteRegistrationFailed(outcome.Job.ItemName);
        }
    }

    /// <inheritdoc cref="AppServiceContainer.ResumeRegistrationsAsync"/>
    public Task ResumeRegistrationsAsync() => _services.ResumeRegistrationsAsync();

    /// <summary>
    /// 閉じる前に、終わっていない登録があれば聞く（ユーザ判断 2026-10-05「4」）。閉じるのをやめるなら true。
    /// 閉じても列は記録にあり、次の起動で続くので、聞くのは「今は待つか」だけ。
    /// </summary>
    public bool ShouldCancelCloseForRegistrations()
    {
        if (RegistrationQueue.CloseConfirm(Registrations.Jobs.Count, Registrations.SecondsLeftAll()) is not { } confirm)
        {
            return false;
        }

        var answer = Views.ChoiceDialog.AskTwo(confirm.Title, confirm.Question, confirm.Detail, "閉じる", "閉じない");
        return answer != Views.ChoiceDialogResult.First;
    }
}
