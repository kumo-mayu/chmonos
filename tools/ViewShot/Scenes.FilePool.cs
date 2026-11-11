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

        FilePoolAfterChanges,
    ];

    /// <summary>
    /// 窓を載せた後に付ける・外す・ほかの道の書き込みで読み直す（ほかの場面は載せる前に並べ終えるので、表示した後の組み直しを通らない）。
    /// 選ぶ欄が空にならないこと・行と「外す」の結び付けが空でないことを、撮る前に確かめる（外れていたら場面ごと失敗にする）
    /// </summary>
    private static Scene FilePoolAfterChanges => FilePoolScene(
        "file-pool-after-changes",
        "ファイルの割り振り：表示した後に、付ける→外す→ほかの道で書き込み（読み直し）を通した姿（結果の1行・選ぶ欄・結び付けを確かめる）",
        arrange: null,
        afterPresent: async (pool, context, root) =>
        {
            await pool.OpenItemAsync(pool.Items.Single(row => row.Id == PoolPartBId));
            pool.Groups.SelectMany(group => group.Files).First(file => file.CanCheck).IsChecked = true;
            await pool.AttachAsync();
            await pool.DetachAsync(pool.Items.Single(row => row.Id == PoolPartAId).Files.FirstOrDefault());
            await context.Seed.Items.ChangeLocalAsync(
                PoolPartAId, local => local with { Memo = "読み直しを起こすための作り物のメモ" }, [LocalField.Memo]);
            await context.SettleAsync();

            var combos = Look.All<System.Windows.Controls.ComboBox>(root).ToList();
            if (combos.Count < 2 || combos.Any(combo => combo.SelectedItem is null))
            {
                throw new InvalidOperationException("付いている商品の数の欄か比べ方の欄が空になりました。");
            }

            await pool.OpenItemAsync(pool.Items.Single(row => row.Id == PoolSetId));
            await context.SettleAsync();
            var detach = Look.All<System.Windows.Controls.Button>(root).Where(button => (string?)button.Content == "外す").ToList();
            if (detach.Count == 0 || detach.Any(button => button.Command is null))
            {
                throw new InvalidOperationException("「外す」のボタンに命令が結び付いていません。");
            }

            var rows = Look.All<Chmonos.App.Controls.PressableBorder>(root).ToList();
            if (rows.Count == 0 || rows.Any(row => row.Command is null))
            {
                throw new InvalidOperationException("左の商品の行に命令が結び付いていません。");
            }
        });

    private static Scene FilePoolScene(
        string name,
        string title,
        Func<FilePoolViewModel, SceneContext, Task>? arrange,
        Func<FilePoolViewModel, SceneContext, System.Windows.FrameworkElement, Task>? afterPresent = null)
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
            if (arrange is not null)
            {
                await arrange(pool, context);
            }

            var root = SceneContext.Unwrap(new FilePoolWindow(pool));
            await context.PresentAsync(root);
            await context.SettleAsync();
            if (afterPresent is not null)
            {
                await afterPresent(pool, context, root);
            }

            return new Shot(root);
        })
        {
            // 窓の既定の大きさから、枠と題の帯の分を引いた中身の大きさ
            Width = 924,
            Height = 600,
        };
}
