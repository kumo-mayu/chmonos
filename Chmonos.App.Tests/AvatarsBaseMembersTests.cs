using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 共通素体の側からアバターを足す・外す（メモ46・ユーザ判断 2026-10-05）と、アバターの詳細に推した素体を薄く出すところ。
/// </summary>
public sealed class AvatarsBaseMembersTests
{
    /// <summary>名前の「#MARUBODY」から MARUBODY に推されるアバター。</summary>
    private const string Inferred = "9900201";

    /// <summary>手で「作り物の素体」に決めたアバター。</summary>
    private const string Manual = "9900202";

    /// <summary>どの素体にも入っていないアバター。</summary>
    private const string Loose = "9900203";

    /// <summary>「アバターとして扱わない」にした物。</summary>
    private const string Excluded = "9900204";

    private const string Marubody = "MARUBODY";
    private const string Handmade = "作り物の素体";

    private static async Task<AvatarsViewModel> OpenAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = Inferred, BoothName = "作り物アバターA #MARUBODY", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = Manual, BoothName = "作り物アバターB", AvatarOverride = true, BaseName = Handmade },
                new AvatarRegistryEntry { ItemId = Loose, BoothName = "作り物アバターC", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = Excluded, BoothName = "作り物の小物", AvatarOverride = false },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = Marubody }, new AvatarBaseGroup { Name = Handmade, IsManual = true }],
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        return avatars;
    }

    private static void SelectBase(AvatarsViewModel avatars, string name)
    {
        avatars.ShowBaseModeCommand.Execute(null);
        avatars.SelectedBase = avatars.Bases.Single(row => row.Name == name);
    }

    private static AvatarRowViewModel Row(AvatarsViewModel avatars, string id) => avatars.Rows.Single(row => row.ItemId == id);

    private static string Candidate(AvatarsViewModel avatars, string id) => AvatarSuggestionText.Format(Row(avatars, id).Name, id);

    private static AvatarRegistryEntry Entry(TestApp app, string id) => app.Store.Avatars.Load().Entries.Single(entry => entry.ItemId == id);

    private static IEnumerable<string> MemberIds(AvatarsViewModel avatars) => avatars.SelectedBaseMembers.Select(row => row.ItemId);

    [Fact]
    public Task 足す欄の候補は_この素体に入っていないアバターだけで_扱わない物は出ない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        SelectBase(avatars, Handmade);

        var candidates = avatars.MemberCandidates;

        Assert.Contains(Candidate(avatars, Inferred), candidates);
        Assert.Contains(Candidate(avatars, Loose), candidates);
        Assert.DoesNotContain(candidates, text => text.EndsWith($"（{Manual}）", StringComparison.Ordinal));
        Assert.DoesNotContain(candidates, text => text.EndsWith($"（{Excluded}）", StringComparison.Ordinal));
        Assert.Equal(AvatarSuggestionText.OtherGroup, avatars.MemberInfoSelector(Candidate(avatars, Loose))?.Group);
    });

    [Fact]
    public Task 候補を選ぶと_その素体に入り_知らせは出さない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        SelectBase(avatars, Handmade);

        avatars.AddMemberCommand.Execute(Candidate(avatars, Loose));
        await app.SettleAsync();
        await UiThread.Until(() => MemberIds(avatars).Contains(Loose), "足したアバターが並ぶ");

        Assert.Equal(Handmade, Entry(app, Loose).BaseName);
        Assert.Equal(string.Empty, avatars.SelectedBase!.MemberNote.Text);
        Assert.Equal(string.Empty, avatars.Status);
    });

    [Fact]
    public Task ほかの素体に入っているアバターは聞かずに移し_欄の下で知らせる() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        SelectBase(avatars, Handmade);
        var name = Row(avatars, Inferred).Name;

        avatars.AddMemberCommand.Execute(Candidate(avatars, Inferred));
        await app.SettleAsync();
        await UiThread.Until(() => MemberIds(avatars).Contains(Inferred), "移したアバターが並ぶ");

        Assert.Equal(Handmade, Entry(app, Inferred).BaseName);
        Assert.Empty(app.Notices);
        Assert.Equal($"「{name}」を「{Marubody}」から移しました。", avatars.SelectedBase!.MemberNote.Text);
        Assert.False(avatars.SelectedBase.MemberNote.IsWarning);
        Assert.Equal(string.Empty, avatars.Status);
        Assert.DoesNotContain(Inferred, avatars.Bases.Single(row => row.Name == Marubody).Summary.MemberIds);
    });

    [Fact]
    public Task 右クリックで推した仲間を外すと_印が立って一覧から消える() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        SelectBase(avatars, Marubody);
        Assert.Contains(Inferred, MemberIds(avatars));

        avatars.RemoveMemberCommand.Execute(avatars.SelectedBaseMembers.Single(row => row.ItemId == Inferred));
        await app.SettleAsync();
        await UiThread.Until(() => !MemberIds(avatars).Contains(Inferred), "外したアバターが消える");

        Assert.True(Entry(app, Inferred).NoBase);
        Assert.Null(Entry(app, Inferred).BaseName);
    });

    [Fact]
    public Task 右クリックで手で決めた所属を外すと_素体名が空になる() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        SelectBase(avatars, Handmade);

        avatars.RemoveMemberCommand.Execute(avatars.SelectedBaseMembers.Single(row => row.ItemId == Manual));
        await app.SettleAsync();
        await UiThread.Until(() => !MemberIds(avatars).Contains(Manual), "外したアバターが消える");

        Assert.Null(Entry(app, Manual).BaseName);
        Assert.False(Entry(app, Manual).NoBase);
    });

    [Fact]
    public Task アバターの詳細は_推した素体を欄に入れず薄い字で出す() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        avatars.Selected = Row(avatars, Inferred);
        Assert.Equal($"名前から「{Marubody}」に入っています。", avatars.SelectedInferredBaseText);
        Assert.True(avatars.HasSelectedInferredBase);
        Assert.Equal(string.Empty, avatars.BaseInput);

        avatars.Selected = Row(avatars, Manual);
        Assert.Equal(string.Empty, avatars.SelectedInferredBaseText);
        Assert.False(avatars.HasSelectedInferredBase);
        Assert.Equal(Handmade, avatars.BaseInput);
    });

    [Fact]
    public Task 一覧の行は_推した素体も薄い字で出し_手で決めた素体は今のまま() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        var inferred = Row(avatars, Inferred);
        Assert.Equal($"名前から「{Marubody}」", inferred.InferredBaseText);
        Assert.True(inferred.HasInferredBase);
        Assert.False(inferred.HasBase);

        var manual = Row(avatars, Manual);
        Assert.Equal(string.Empty, manual.InferredBaseText);
        Assert.False(manual.HasInferredBase);
        Assert.True(manual.HasBase);
        Assert.Equal(Handmade, manual.BaseText);
    });

    [Fact]
    public Task アバターの側の素体から外すは_推した素体も外し_素体を選び直すと印を下ろす() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.Selected = Row(avatars, Inferred);
        Assert.True(avatars.ClearBaseCommand.CanExecute(null));

        avatars.ClearBaseCommand.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => !avatars.HasSelectedInferredBase, "推した素体の字が消える");

        Assert.True(Entry(app, Inferred).NoBase);
        Assert.False(avatars.ClearBaseCommand.CanExecute(null));

        avatars.PickBaseCommand.Execute(Handmade);
        await app.SettleAsync();
        await UiThread.Until(() => Entry(app, Inferred).BaseName == Handmade, "選んだ素体が書かれる");

        Assert.False(Entry(app, Inferred).NoBase);
    });

    [Fact]
    public Task どの素体にも入っていないアバターでは_素体から外すを押せない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        avatars.Selected = Row(avatars, Loose);

        Assert.False(avatars.ClearBaseCommand.CanExecute(null));
    });

    [Fact]
    public Task 足すと足した行だけが増え_元の行は作り直されない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        SelectBase(avatars, Handmade);
        var before = avatars.SelectedBaseMembers.Single(row => row.ItemId == Manual);
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        avatars.SelectedBaseMembers.CollectionChanged += (_, e) => actions.Add(e.Action);

        avatars.AddMemberCommand.Execute(Candidate(avatars, Loose));
        await app.SettleAsync();
        await UiThread.Until(() => MemberIds(avatars).Contains(Loose), "足したアバターが並ぶ");

        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Add], actions);
        Assert.Same(before, avatars.SelectedBaseMembers.Single(row => row.ItemId == Manual));
    });

    [Fact]
    public Task 外すと外した行だけが消え_残る行は作り直されない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        SelectBase(avatars, Marubody);
        avatars.AddMemberCommand.Execute(Candidate(avatars, Loose));
        await app.SettleAsync();
        await UiThread.Until(() => MemberIds(avatars).Contains(Loose), "足したアバターが並ぶ");
        var kept = avatars.SelectedBaseMembers.Single(row => row.ItemId == Loose);
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        avatars.SelectedBaseMembers.CollectionChanged += (_, e) => actions.Add(e.Action);

        avatars.RemoveMemberCommand.Execute(avatars.SelectedBaseMembers.Single(row => row.ItemId == Inferred));
        await app.SettleAsync();
        await UiThread.Until(() => !MemberIds(avatars).Contains(Inferred), "外したアバターが消える");

        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Remove], actions);
        Assert.Same(kept, avatars.SelectedBaseMembers.Single(row => row.ItemId == Loose));
    });

    [Fact]
    public Task 素体の詳細から開くと_戻るで素体の詳細へ戻る() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        SelectBase(avatars, Handmade);

        avatars.OpenMemberCommand.Execute(avatars.SelectedBaseMembers.Single(row => row.ItemId == Manual));

        Assert.False(avatars.IsBaseMode);
        Assert.Equal(Manual, avatars.Selected?.ItemId);
        Assert.True(app.Main.CanGoBack);

        app.Main.GoBack();
        var restored = Assert.IsType<AvatarsViewModel>(app.Main.CurrentViewModel);
        await UiThread.Until(() => !restored.IsLoading && restored.IsBaseMode, "素体の詳細で開き直る");

        Assert.Equal(Handmade, restored.SelectedBase?.Name);
    });
}
