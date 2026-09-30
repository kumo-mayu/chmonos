// カードとリストの行を、本物の行（ItemCardViewModel）と、押されたことを控えるだけの入れ物の画面で確かめる。
// 「押す」が星・カード・行のそれぞれで、マウスのときと同じ入口（IItemCardHost）まで届くかを見る
using System.Collections.ObjectModel;
using System.Dynamic;
using System.IO;
using BoothAssetManager.App.Services;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Models;

namespace PeerProbe;

public sealed class ProbeHost : IItemCardHost
{
    public ObservableCollection<object> Rows { get; } = [];

    public ObservableCollection<object> ListItems { get; } = [];

    public object ListColumns { get; }

    public bool IsCardMode => true;

    public bool IsListMode => true;

    public ProbeHost(int rows = 1)
    {
        IDictionary<string, object?> columns = new ExpandoObject();
        columns["SelectWidth"] = 30d;
        columns["IconWidth"] = 40d;
        columns["FavWidth"] = 30d;
        columns["NameWidth"] = 200d;
        columns["ShopWidth"] = 100d;
        columns["ChipsWidth"] = 100d;
        columns["HasSelect"] = true;
        columns["IconSize"] = 30d;
        columns["ShopHeader"] = "ショップ";
        ListColumns = columns;

        // 絵の置き場は空の作業用フォルダ（本番の保存先は指さない）
        var images = Path.Combine(Path.GetTempPath(), "peerprobe-empty-images");
        Directory.CreateDirectory(images);
        var loader = new ThumbnailLoader();
        var cards = new ObservableCollection<object>();
        foreach (var id in new[] { "9000001", "9000002" })
        {
            var card = new ItemCardViewModel(new ItemRecord { Id = id }, loader, images) { Name = "作り物の服" + id };
            cards.Add(card);
            ListItems.Add(card);
        }

        IDictionary<string, object?> row = new ExpandoObject();
        row["Cards"] = cards;
        Rows.Add(row);

        // 2段目から先（矢印の上下・Tab が段をまたぐのを見るとき）。カードの一覧だけに足す
        for (var index = 1; index < rows; index++)
        {
            var more = new ObservableCollection<object>();
            foreach (var id in new[] { $"90000{index}3", $"90000{index}4" })
            {
                more.Add(new ItemCardViewModel(new ItemRecord { Id = id }, loader, images) { Name = "作り物の服" + id });
            }

            IDictionary<string, object?> next = new ExpandoObject();
            next["Cards"] = more;
            Rows.Add(next);
        }
    }

    public void OpenItem(ItemCardViewModel card) => ProbeLog.Lines.Add($"入れ物の画面: 商品ページを開く（{card.Name}）");

    public void OpenBooth(ItemCardViewModel? card) => ProbeLog.Lines.Add($"入れ物の画面: BOOTHで開く（{card?.Name}）");

    public Task ToggleFavoriteAsync(ItemCardViewModel card)
    {
        ProbeLog.Lines.Add($"入れ物の画面: お気に入りを切り替える（{card.Name}）");
        return Task.CompletedTask;
    }
}
