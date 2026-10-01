// 検索の条件の部品は型で見た目を選ぶので、作り物ではなく本物の条件を載せる（値を当てる関数は空でよい）
using System.Collections.ObjectModel;
using Chmonos.App.ViewModels;

namespace PeerProbe;

public static class RealModules
{
    public static void AddTo(object? dataContext)
    {
        if (dataContext is not IDictionary<string, object?> bag)
        {
            return;
        }

        var path = new ListModule(SearchModuleKind.Path, allowsAnd: true, "フォルダの名前で絞り込む", "手元にファイルのある商品がまだありません。", (_, _, _, _) => false);
        path.Suggestions.Add("X:\\作り物");
        path.AddKey("X:\\作り物\\服", notify: false, text: "X:\\作り物\\服");
        bag["Modules"] = new ObservableCollection<object>
        {
            path,
            new RangeModule(SearchModuleKind.Price, (_, _) => [], "円"),
            new DateModule(SearchModuleKind.AcquiredAt, _ => null),
            new AttributeModule(),
            new UserTagModule(),
            new RecentModule(),
        };
        bag["HasModules"] = true;
    }
}
