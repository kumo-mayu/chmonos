using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 条件「改変プロジェクト」の1回目の改変の読み込み（ユーザ判断 2026-11-20：初めて使ったとき一瞬止まり、落ちたかと思った → 裏へ移す）。
/// 読むのは裏のスレッド・読む間は空の文の代わりに「読んでいます」・読込中に足す／外す／改変が変わるが重なっても壊れない。
/// 待ちは継ぎ目（<see cref="SearchViewModel.AfterModificationsRead"/>）の門だけで、時間では待たない。作り物の名前と番号（9900300番台）
/// </summary>
public sealed class SearchModificationLoadTests
{
    private static async Task<string> CreateWithProjectAsync(TestApp app, string name, string project)
    {
        var record = (await app.Services.Modifications.CreateAsync("9900301", name))!;
        await app.Services.Modifications.AddMemberAsync(record.Id, new ModificationMember { ItemId = "9900302" });
        await app.Services.Modifications.SetProjectAsync(record.Id, project);
        return record.Id;
    }

    private static async Task AddItemsAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("9900301", "作り物のアバター"));
        await app.AddItemAsync(Make.Item("9900302", "作り物の衣装"));
    }

    [Fact]
    public Task 改変は画面のスレッドの外で読み_読む間は空の文の代わりに読んでいますを出し_足しても外しても二重に読まない() => TestApp.Run(async app =>
    {
        await AddItemsAsync(app);
        await CreateWithProjectAsync(app, "普段着", @"D:\作り物\UnityProjects\撮影A");
        var search = (await app.StartAsync()).Search;

        var uiThread = Environment.CurrentManagedThreadId;
        int? readThread = null;
        var reading = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        search.AfterModificationsRead = async () =>
        {
            readThread = Environment.CurrentManagedThreadId;
            reading.TrySetResult();
            await gate.Task;
        };

        var first = (ListModule)SearchModules.Add(search, SearchModuleKind.UnityProject);
        await reading.Task;

        // 読んだのは画面のスレッドではない（前の版は画面のスレッドで1ファイルずつ読み、その間は画面が止まった）
        Assert.NotEqual(uiThread, readThread);

        // 読む前の空を「紐付けた改変がまだありません」と言わない
        Assert.False(first.HasSuggestions);
        Assert.Equal(SearchViewModel.ModificationsWaitingText, first.EmptyText);

        // 読込中にもう1つ足し、最初の方を外す。読み始めるのは1回だけ
        var second = (ListModule)SearchModules.Add(search, SearchModuleKind.UnityProject);
        Assert.Equal(SearchViewModel.ModificationsWaitingText, second.EmptyText);
        first.RemoveCommand!.Execute(null);
        Assert.Equal(1, search.ModificationLoadsStarted);

        gate.SetResult();
        await UiThread.Until(() => second.HasSuggestions, "読み終えて候補が入る");

        Assert.Contains(second.Suggestions, entry => entry.Contains("撮影A", StringComparison.Ordinal));
        Assert.Equal("Unityプロジェクトを紐付けた改変がまだありません。", second.EmptyText);
        Assert.Null(second.WaitingText);
        Assert.Equal(1, search.ModificationLoadsStarted);
    });

    [Fact]
    public Task 読んでいる間に改変が変わったら_変わる前に読んだ結果で上書きせずに読み直す() => TestApp.Run(async app =>
    {
        await AddItemsAsync(app);
        await CreateWithProjectAsync(app, "普段着", @"D:\作り物\UnityProjects\撮影A");
        var search = (await app.StartAsync()).Search;

        // 門は読んだ後に置く（前に置くと、変わった後の改変を読んでしまい、古い結果で上書きする場面にならない）
        var reading = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        var calls = 0;
        search.AfterModificationsRead = async () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                reading.TrySetResult();
                await gate.Task;
            }
        };

        var module = (ListModule)SearchModules.Add(search, SearchModuleKind.UnityProject);
        await reading.Task;

        // 読み終えた後・反映する前に、別の画面で改変が増えた
        await CreateWithProjectAsync(app, "制服", @"D:\作り物\UnityProjects\撮影B");
        search.NoteModificationsChanged();
        gate.SetResult();

        await UiThread.Until(
            () => module.Suggestions.Any(entry => entry.Contains("撮影B", StringComparison.Ordinal)),
            "変わった後の改変で候補が入る");
        Assert.Contains(module.Suggestions, entry => entry.Contains("撮影A", StringComparison.Ordinal));
        Assert.Null(module.WaitingText);
    });

    [Fact]
    public Task 読み直す間は古い控えで絞り続け_一瞬0件にしない() => TestApp.Run(async app =>
    {
        await AddItemsAsync(app);
        await CreateWithProjectAsync(app, "普段着", @"D:\作り物\UnityProjects\撮影A");
        var search = (await app.StartAsync()).Search;
        var module = (ListModule)SearchModules.Add(search, SearchModuleKind.UnityProject);
        await UiThread.Until(() => module.HasSuggestions, "1回目を読み終える");
        module.AddKey(@"D:\作り物\UnityProjects\撮影A");
        Assert.Equal(["9900302"], search.ListItems.Select(card => card.Item.Id));

        var reading = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        search.AfterModificationsRead = async () =>
        {
            reading.TrySetResult();
            await gate.Task;
        };

        // 検索へ戻るたびに読み直す（MainViewModel.ShowSearch）。読む間に絞り直しても、古い控えで同じ結果を出す
        search.NoteModificationsChanged();
        await reading.Task;
        module.MatchAll = true;
        Assert.Equal(["9900302"], search.ListItems.Select(card => card.Item.Id));
        Assert.Null(module.WaitingText);

        gate.SetResult();
        await UiThread.Settle();
        Assert.Equal(2, search.ModificationLoadsStarted);
        Assert.Equal(["9900302"], search.ListItems.Select(card => card.Item.Id));
    });
}
