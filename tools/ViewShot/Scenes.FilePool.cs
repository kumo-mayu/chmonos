using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// 選んだ商品の間でファイルを割り振るサブ画面（plan 30）。窓は出さずに中身を載せる（サブ画面の窓は既定で 940×640）。
/// 商品・ファイルの名前は作り物（実際のダウンロードの名前は写さない）。
/// </summary>
internal static partial class Scenes
{
    private const string PoolSetId = "9900881";
    private const string PoolPartAId = "9900882";
    private const string PoolPartBId = "9900883";

    private static IEnumerable<Scene> FilePoolScenes =>
    [
        FilePoolScene(
            "file-pool",
            "ファイルの割り振り：付ける先に単品Bを選び、「2 以下」で組を1つ広げて2つにチェック（中の unitypackage の見出し・1つだけの組は見出し無し）",
            async (pool, _) =>
            {
                await pool.OpenItemAsync(pool.Items.Single(row => row.Id == PoolPartBId));
                pool.Threshold = 2;
                var group = pool.Groups.Single(group => group.Name == "SampleShop");
                group.IsExpanded = true;
                group.Files[0].IsChecked = true;
                group.Files[1].IsChecked = true;
            }),

        FilePoolScene(
            "file-pool-set-selected",
            "ファイルの割り振り：セットを選び、下に付いているファイルと「外す」。「2 以上」で絞り、付ける先が持つ行は押せない・消えた商品の1行",
            async (pool, context) =>
            {
                await pool.OpenItemAsync(pool.Items.Single(row => row.Id == PoolSetId));
                pool.Filter = HolderCountFilter.AtLeast;
                pool.Threshold = 2;
                await context.Seed.Items.DeleteAsync("9900884");
                await pool.ReloadAsync();
            }),

        FilePoolScene(
            "file-pool-nothing-shown",
            "ファイルの割り振り：絞りで何も出ない（「3 のみ」）と、付けるが押せない理由",
            (pool, _) =>
            {
                pool.Filter = HolderCountFilter.Exactly;
                pool.Threshold = 3;
                return Task.CompletedTask;
            }),
    ];

    private static Scene FilePoolScene(string name, string title, Func<FilePoolViewModel, SceneContext, Task> arrange)
        => new(name, title, async context =>
        {
            static LocalFileRecord File(string name, params string[] contents) => new()
            {
                Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name))),
                Paths = [$@"D:\作り物\{name}"],
                SizeBytes = 1_234_567 * (name.Length % 7 + 1),
                Contents = contents,
            };

            var top = File("SampleShop_Top.zip", "SampleShop_Top.unitypackage");
            var skirt = File("SampleShop_Skirt.zip", "Skirt/SampleShop_Skirt.unitypackage", "Skirt/SampleShop_Skirt_Quest.unitypackage");
            var socks = File("SampleShop_Socks_v1.2.zip", "SampleShop_Socks.unitypackage");
            var hair = File("作り物の髪飾り_v1.2.zip", "作り物の髪飾り.unitypackage");
            var boots = File("【作り物】ブーツ.zip", "Boots.unitypackage");
            var manual = File("説明書.pdf");

            await context.Fake.ItemAsync(PoolSetId, "作り物の衣装フルセット", item => item with
            {
                Local = item.Local with { LocalFiles = [top, skirt, socks, hair, boots, manual] },
            });
            await context.Fake.ItemAsync(PoolPartAId, "作り物の上着", item => item with { Local = item.Local with { LocalFiles = [top] } });
            await context.Fake.ItemAsync(PoolPartBId, "作り物のスカート（単品）とても長い名前の商品は端で省く");
            await context.Fake.ItemAsync("9900884", "作り物の靴下", item => item with { Local = item.Local with { LocalFiles = [socks] } });

            var main = await context.StartAsync();
            var pool = new FilePoolViewModel(context.Services, main, [PoolSetId, PoolPartAId, PoolPartBId, "9900884"]);
            await pool.ReloadAsync();
            await arrange(pool, context);

            var root = SceneContext.Unwrap(new FilePoolWindow(pool));
            await context.PresentAsync(root);
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            // 窓の既定の大きさから、枠と題の帯の分を引いた中身の大きさ
            Width = 924,
            Height = 600,
        };
}
