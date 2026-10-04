using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>属性の名前を変える・統合する・消すと、設定の「カードに表示する属性」も同じ命令の中で追従する（ユーザ判断 2026-10-04）。</summary>
public sealed class CardAttributeRenameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-cardattr-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;
    private readonly SettingsService _settings;
    private readonly CommandHandler _handler;

    public CardAttributeRenameTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _settings = new SettingsService(_store);
        _handler = new CommandHandler(null!, null!, attributes: new AttributeService(_store), settings: _settings);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task ArrangeAsync(params string[] chosen)
    {
        await _store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "質感" }, new AttributeDefinition { Name = "かわいい" }, new AttributeDefinition { Name = "ふんわり" }],
        });
        await _settings.UpdateAsync(current => current with { CardAttributes = chosen });
    }

    [Fact]
    public async Task 名前を変えると設定で選んだ名前も変わる()
    {
        await ArrangeAsync("質感", "かわいい");

        await _handler.ExecuteAsync(new UiCommand.RenameAttribute("質感", "手触り"));

        Assert.Equal(["手触り", "かわいい"], _store.Settings.Load().CardAttributes);
    }

    [Fact]
    public async Task 統合すると統合先の名前で1つになる()
    {
        await ArrangeAsync("質感", "かわいい", "ふんわり");

        await _handler.ExecuteAsync(new UiCommand.RenameAttribute("ふんわり", "質感"));

        Assert.Equal(["質感", "かわいい"], _store.Settings.Load().CardAttributes);
    }

    [Fact]
    public async Task 消すと設定からも外れる()
    {
        await ArrangeAsync("質感", "かわいい");

        await _handler.ExecuteAsync(new UiCommand.DeleteAttribute("質感"));

        Assert.Equal(["かわいい"], _store.Settings.Load().CardAttributes);
    }

    [Fact]
    public async Task 選んでいない人の設定は空のまま()
    {
        await ArrangeAsync();

        await _handler.ExecuteAsync(new UiCommand.RenameAttribute("質感", "手触り"));

        Assert.Empty(_store.Settings.Load().CardAttributes);
    }
}
