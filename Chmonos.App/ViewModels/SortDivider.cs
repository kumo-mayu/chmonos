namespace Chmonos.App.ViewModels;

/// <summary>
/// 並べ替えの区切りの札（ユーザ判断 2026-10-01「図書館やビデオショップの分類の為の偽アイテム」）。
/// カテゴリ順・ショップ順などで、まとまりの最初の商品の前に、カードと同じ升（リストでは同じ行の高さ）で入る。
///
/// **商品ではない。**件数・選ぶ・まとめて操作・商品の右クリックのメニュー・キーボードの止まりのどれにも入らない（ショップの札の操作は下）。
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

    // ---- ショップの札（ユーザ判断 2026-10-02・メモ2-⑤） ----
    // ショップ順の札だけ、ショップのアイコンを出し、押すとアプリのショップの画面、中クリックで BOOTH のショップのページ、
    // 右クリックでその2つのメニューを出す。**キーボードの止まりにはしない**（2026-10-01 の判断のまま。キーボードからは
    // 商品のカードの右クリックの「ショップを開く」とショップの一覧から同じ所へ届く）。カテゴリ・公開日・入手日の札は今のまま。

    /// <summary>ショップのアイコンのファイル。無ければ null で、**何も出さない**（既定の絵・頭文字のタイルは出さない。メモ2-⑤）。</summary>
    public string? IconPath { get; init; }

    /// <summary>アイコンを読む口（検索の画面と同じ読み手）。</summary>
    public Services.ThumbnailLoader? Thumbnails { get; init; }

    public System.Windows.Media.Imaging.BitmapSource? Icon => IconPath is { } path
        ? Thumbnails?.PeekForIcon(path, () => OnPropertyChanged(nameof(Icon)))
        : null;

    public bool HasIcon => IconPath is not null;

    /// <summary>アプリのショップの画面を開く（左クリック・メニュー）。ショップの札で、ショップが分かるときだけ。</summary>
    public RelayCommand? OpenShopCommand { get; init; }

    /// <summary>BOOTH のショップのページを開く（中クリック・メニュー）。手元だけのショップでは押せない（BOOTH にページが無い）。</summary>
    public RelayCommand? OpenInBoothCommand { get; init; }

    /// <summary>押せる札か（ショップの札で、ショップが分かる）。マウスの形・右クリックのメニュー・読み上げの「押す」を出し分ける。</summary>
    public bool IsPressable => OpenShopCommand is not null;

    /// <summary>押せる札の吹き出し。</summary>
    public string? PressHint => !IsPressable ? null
        : OpenInBoothCommand is null ? "押すとショップを開きます。"
        : "押すとショップを開きます。中クリックでBOOTHのページを開きます。";
}
