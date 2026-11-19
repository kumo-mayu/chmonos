using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 編集の画面の、付いているが値はまだの属性（選択の帯の「属性を追加…」で足した物。ユーザ判断 2026-11-20 の (a)）。
/// 「未評価」の行で出し、触らずに保存しても消さない。動かすと値になり、外すとどちらからも消える。
/// 既定で並べてあるだけの行（付いていない）は、今までどおり保存で書かない。
/// </summary>
public class EditPendingAttributeTests
{
    private const string Id = "9900801";

    private static async Task<(MainViewModel Main, EditViewModel Edit)> OpenAsync(TestApp app, LocalBlock local)
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "かわいさ" }, new AttributeDefinition { Name = "既定の属性", IsDefault = true }],
        });
        await app.AddItemAsync(Make.Item(Id, "作り物の衣装") with { Local = local });
        var main = await app.StartAsync();
        await main.ShowEditAsync([Id]);
        await app.SettleAsync();
        return (main, Assert.IsType<EditViewModel>(main.CurrentViewModel));
    }

    private static async Task<LocalBlock> SaveAndLoadAsync(TestApp app, EditViewModel edit)
    {
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();
        return (await app.Store.Items.LoadAsync(Id))!.Local;
    }

    private static LocalBlock Pending(params string[] names) => Make.Item(Id, "作り物の衣装").Local with
    {
        Attributes = new Dictionary<string, int> { ["露出"] = 40 },
        PendingAttributes = names,
    };

    [Fact]
    public Task 値はまだの属性は未評価の行で出て_触らずに保存しても残る() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenAsync(app, Pending("かわいさ"));

        Assert.Equal(["露出", "かわいさ", "既定の属性"], edit.Attributes.Select(row => row.Name));
        var row = edit.Attributes.Single(row => row.Name == "かわいさ");
        Assert.True(row.IsSuggested);
        Assert.True(row.IsPendingOnItem);
        Assert.Equal("未評価。動かすと値が付きます", row.SuggestedNote);
        Assert.Equal("未評価。動かすと付きます", edit.Attributes.Single(row => row.Name == "既定の属性").SuggestedNote);

        // 触っていないので書きかけでもない（離れても控えたと知らせない）
        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();
        Assert.False(main.HasDraftNotice);

        await main.ShowEditAsync([Id]);
        await app.SettleAsync();
        var saved = await SaveAndLoadAsync(app, Assert.IsType<EditViewModel>(main.CurrentViewModel));
        Assert.Equal(["かわいさ"], saved.PendingAttributes!);
        Assert.Equal(new Dictionary<string, int> { ["露出"] = 40 }, saved.Attributes);
    });

    [Fact]
    public Task 動かすと値が付き_値はまだから外れる() => TestApp.Run(async app =>
    {
        var (_, edit) = await OpenAsync(app, Pending("かわいさ"));

        edit.Attributes.Single(row => row.Name == "かわいさ").Value = 0;
        var saved = await SaveAndLoadAsync(app, edit);

        // 0% も値（未評価と 0 は別）
        Assert.Equal(0, saved.Attributes["かわいさ"]);
        Assert.Null(saved.PendingAttributes);
        Assert.False(saved.Attributes.ContainsKey("既定の属性"));
    });

    [Fact]
    public Task 外すと付いていない状態に戻る() => TestApp.Run(async app =>
    {
        var (_, edit) = await OpenAsync(app, Pending("かわいさ"));

        edit.Attributes.Single(row => row.Name == "かわいさ").RemoveCommand!.Execute(null);
        var saved = await SaveAndLoadAsync(app, edit);

        Assert.Null(saved.PendingAttributes);
        Assert.False(saved.Attributes.ContainsKey("かわいさ"));
        Assert.Equal(40, saved.Attributes["露出"]);
    });
}
