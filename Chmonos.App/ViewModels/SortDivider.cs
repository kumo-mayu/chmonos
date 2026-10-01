namespace Chmonos.App.ViewModels;

/// <summary>
/// 並べ替えの区切りの札（ユーザ判断 2026-10-01「図書館やビデオショップの分類の為の偽アイテム」）。
/// カテゴリ順・ショップ順などで、まとまりの最初の商品の前に、カードと同じ升（リストでは同じ行の高さ）で入る。
///
/// **商品ではない。**件数・選ぶ・まとめて操作・右クリックのメニュー・キーボードの止まりのどれにも入らない。
/// 一覧の件数と選ぶ対象は今までどおり商品だけの並び（<c>SearchViewModel._matches</c>）から数え、札は画面に出す並びにだけ混ぜる。
///
/// 札は鍵ごとに使い回す。絞り込みを変えるたびに作り直すと、段の組み方（<c>CardRowLayout</c>）が同じ物と見なせず、
/// 最初の札から後ろの段を全部抜き差しすることになる（列数を変えたときに行を全部作り直していた U28 と同じ固まり）。
/// </summary>
public sealed class SortDivider : ViewModelBase
{
    private int _count;

    public SortDivider(string key, string kind, string label, string? parent)
    {
        Key = key;
        Kind = kind;
        Label = label;
        Parent = parent;
    }

    /// <summary>まとまりの鍵（<see cref="Core.Services.ItemGroup.Key"/>）。</summary>
    public string Key { get; }

    /// <summary>何で並べた札か（並べ替えの項目の名前：「カテゴリ」「ショップ」「入手日」）。</summary>
    public string Kind { get; }

    /// <summary>まとまりの名前（「衣装」「2026年9月」「ショップなし」）。</summary>
    public string Label { get; }

    /// <summary>カテゴリの親（「3Dモデル」）。無ければ null。</summary>
    public string? Parent { get; }

    public bool HasParent => Parent is not null;

    /// <summary>このまとまりの商品の数。絞り込みで変わるので、札を使い回すときに替える。</summary>
    public int Count
    {
        get => _count;
        set
        {
            if (SetField(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string CountText => $"{_count} 件";

    /// <summary>
    /// 読み上げの名前。親は「親 / 子」で読ませる（商品ページのカテゴリと同じ書き方：<see cref="Core.Models.DisplayText.CategoryText"/>）。
    /// 何の札かが分かるよう、頭に並べ替えの項目を付ける
    /// </summary>
    public string AutomationName
        => $"{Kind}：{Core.Models.DisplayText.CategoryText(Label, Parent)}、{CountText}";
}
