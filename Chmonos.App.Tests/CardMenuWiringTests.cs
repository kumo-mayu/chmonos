using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// カードの右クリックのメニューの命令が、カードを並べるどの画面でも結ばれていること（R1・2026-11-10）。
///
/// 前はメニューがカードの Tag（並べている画面）から命令を名前で引き、画面ごとに11本を写していた。
/// 1本書き忘れても組み立ては通り、メニューは出るのに押して何も起きなかった（2回起きた）。
/// 今は <see cref="IItemCardHost.MenuCommands"/>（<see cref="CardMenuCommands"/>）1つに結ぶ。ここでは
/// メニューの項目が引く名前を XAML の結び付けから取り、7つの画面それぞれの上で実際にたどって、命令に届くことを見る
/// </summary>
public class CardMenuWiringTests
{
    private const string TagPrefix = "PlacementTarget.Tag.";

    /// <summary>メニューの全項目（入れ子の「開く」「Unity」の中も）のうち、並べている画面から命令を引く物の結び付けの道。</summary>
    private static List<string> HostCommandPaths()
    {
        var menu = (ContextMenu)new ItemCardResources()["CardMenu"];
        var paths = new List<string>();
        void Walk(ItemsControl parent)
        {
            foreach (var item in parent.Items.OfType<MenuItem>())
            {
                if (BindingOperations.GetBinding(item, MenuItem.CommandProperty) is { } binding
                    && binding.Path.Path.StartsWith(TagPrefix, StringComparison.Ordinal))
                {
                    paths.Add(binding.Path.Path[TagPrefix.Length..]);
                }

                Walk(item);
            }
        }

        Walk(menu);
        return paths;
    }

    /// <summary>道（<c>MenuCommands.OpenBoothCommand</c>）を画面の上でたどる。WPF の結び付けと同じく、公開の性質を名前で引く。</summary>
    private static object? Resolve(object host, string path)
    {
        object? current = host;
        foreach (var name in path.Split('.'))
        {
            current = current?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(current);
        }

        return current;
    }

    [Fact]
    public Task メニューは命令をすべてMenuCommandsから引き_一式の命令はどれもメニューのどこかが押す() => UiThread.Run(() =>
    {
        var paths = HostCommandPaths();

        // 画面から名前で直に引く項目が残っていれば、その画面ごとの写しが要ることになる（書き忘れても気付けない形に戻る）
        Assert.All(paths, path => Assert.StartsWith("MenuCommands.", path, StringComparison.Ordinal));

        var names = typeof(CardMenuCommands).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(RelayCommand))
            .Select(property => "MenuCommands." + property.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(names, paths.Order(StringComparer.Ordinal).ToList());
    });

    [Fact]
    public Task カードを並べる7つの画面のどれでも_メニューの全項目が命令に届く() => TestApp.Run(async app =>
    {
        var file = app.NewFile(@"shop\作り物.zip");
        var item = Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(file));
        item = item with { Local = item.Local with { UserTags = [new UserTagAssignment { Top = "作り物の甲" }] } };
        await app.AddItemAsync(item);
        await app.Store.UserTags.SaveAsync(new UserTagMaster { Tops = [new UserTagTop { Name = "作り物の甲" }] });
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "作り物の属性" }] });
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "1000001", BoothName = "作り物の衣装", AvatarOverride = true }],
        });
        var main = await app.StartAsync();

        var hosts = new List<(string Name, IItemCardHost Host)> { ("検索", main.Search) };

        await main.ShowShopAsync("sample-shop");
        hosts.Add(("ショップ", Assert.IsType<ShopViewModel>(main.CurrentViewModel)));

        main.ShowTagManageCommand.Execute(null);
        var tags = Assert.IsType<TagManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => tags.TopCount > 0, "タグの管理の読み込みが済む");
        hosts.Add(("タグの管理", tags));

        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count > 0, "属性の管理の読み込みが済む");
        hosts.Add(("属性の管理", attributes));

        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        hosts.Add(("アバター", avatars));

        var created = Assert.IsType<Core.Commands.CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new Core.Commands.UiCommand.CreateModification("1000001", "作り物の改変")));
        hosts.Add(("改変", new ModificationViewModel(created.Record, app.Services, main, main.Thumbnails)));

        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.Rows.Count > 0, "フォルダビューの読み込みが済む");
        folders.Selected = folders.Rows[0];
        await UiThread.Until(() => folders.Detail is FolderViewDetail, "フォルダの右の欄が出る");
        hosts.Add(("フォルダビュー", (FolderViewDetail)folders.Detail!));

        var paths = HostCommandPaths();
        Assert.NotEmpty(paths);
        foreach (var (name, host) in hosts)
        {
            foreach (var path in paths)
            {
                Assert.True(Resolve(host, path) is RelayCommand, $"{name}：{path} が命令に届かない");
            }
        }

        // 画面ごとに差し替えた所は、差し替えたまま（借り物に戻っていない）
        var search = main.Search.MenuCommands;
        Assert.NotSame(search.HideItemCommand, hosts.Single(entry => entry.Name == "ショップ").Host.MenuCommands.HideItemCommand);
        Assert.NotSame(search.HideItemCommand, hosts.Single(entry => entry.Name == "フォルダビュー").Host.MenuCommands.HideItemCommand);
        Assert.NotSame(search.HideItemCommand, tags.MenuCommands.HideItemCommand);
        Assert.NotSame(search.HideItemCommand, attributes.MenuCommands.HideItemCommand);
        Assert.Same(avatars.OpenBoothCommand, avatars.MenuCommands.OpenBoothCommand);
    });
}
