namespace Chmonos.App.ViewModels;

/// <summary>列の中身の型。決まった型（XAML の資源）か、文字だけの列（<see cref="ItemListColumnDef.RowProperty"/> を出す）。</summary>
public enum ItemListCell
{
    /// <summary>文字だけ。左に寄せる。</summary>
    Text,

    /// <summary>文字だけ。右に寄せる（数・額・日付の桁をそろえる）。</summary>
    Number,

    /// <summary>XAML の資源の型（<see cref="ItemListColumnDef.Template"/>）。</summary>
    Template,
}

/// <param name="Key">覚える名前（ui-state の並びと、幅の鍵 <c>{画面}.col.{Key}</c>）。前からある列は前の名前のまま（覚えた幅をそのまま使う）。</param>
/// <param name="Header">見出しの文字。狭い列に入る短い言い方。</param>
/// <param name="MenuLabel">見出しの右クリックのメニューに出す名前（見出しより詳しくてよい）。</param>
/// <param name="Heading">メニューのどの見出しの下に出すか。検索の「条件を追加」の見出しと同じ（<see cref="SearchModuleCatalog"/>）。null はメニューに出さない（選ぶ列）。</param>
/// <param name="Template">決まった型の資源の名前（<see cref="ItemListCell.Template"/> のとき）。</param>
/// <param name="RowProperty">文字の列が出す <see cref="ItemListRow"/> の値の名前。</param>
/// <param name="HasToolTip">切れた分を吹き出しで全文出すか（長くなり得る文字の列）。</param>
public sealed record ItemListColumnDef(
    string Key,
    string Header,
    string MenuLabel,
    string? Heading,
    double Width,
    double MinWidth,
    double MaxWidth,
    ItemListCell Cell,
    string? Template = null,
    string? RowProperty = null,
    bool HasToolTip = false);

/// <summary>
/// 商品のリストに出せる列の一覧と、画面ごとの既定の並び（ユーザ判断 2026-11-20：検索・ショップ・フォルダの右で、出す列と順を自分で選ぶ）。
///
/// **候補は行に記録してある要素の全部**。見出しの右クリックのメニューは、検索の「条件を追加」と同じ見出しで分ける（全部を平らに並べると多過ぎる）。
/// 並びはメニューに出す順（見出しの中で、条件を追加の並びに近い順）。
/// </summary>
public static class ItemListColumnCatalog
{
    /// <summary>選ぶ列（まとめて操作のチェック）。いつも左端に置き、メニューには出さない（列ではなく操作なので）。</summary>
    public const string Select = "select";

    /// <summary>列の候補を持つ画面（リストの列を選べる画面）。改変の「使ったもの」は自分の列を持つので含めない。</summary>
    public static IReadOnlyList<string> Screens { get; } = ["search", "folder", "shop"];

    /// <summary>メニューの見出しの並び。</summary>
    public static IReadOnlyList<string> Headings { get; } =
        [SearchModuleCatalog.BoothInfo, SearchModuleCatalog.ItemInfo, SearchModuleCatalog.FileInfo, SearchModuleCatalog.ModificationRecord];

    private const string Booth = SearchModuleCatalog.BoothInfo;
    private const string Item = SearchModuleCatalog.ItemInfo;
    private const string File = SearchModuleCatalog.FileInfo;
    private const string Modification = SearchModuleCatalog.ModificationRecord;

    /// <summary>
    /// 全部の列。幅の既定は中身の典型が「…」にならない幅（日付は「2026-11-20」、数は4桁）、最小は見出しが読める幅、
    /// 最大は1列で窓を占めない幅。前からある列（選ぶ・★・名前・ショップ・状態・ユーザータグ・属性・払った額・対応）は前の値のまま
    /// </summary>
    public static IReadOnlyList<ItemListColumnDef> All { get; } =
    [
        new(Select, string.Empty, string.Empty, null, 36, 28, 60, ItemListCell.Template, "ItemListSelectCell"),

        // ---- BOOTHの情報（条件を追加と同じ流れ：何の商品か → お金と人気 → 誰が出したか → BOOTH の側の時の流れ → 中身の数） ----
        new("id", "ID", "商品ID", Booth, 90, 56, 200, ItemListCell.Text, RowProperty: nameof(ItemListRow.Id)),
        new("category", "カテゴリ", "カテゴリ", Booth, 120, 60, 400, ItemListCell.Text, RowProperty: nameof(ItemListRow.Category), HasToolTip: true),
        new("boothTags", "BOOTHタグ", "BOOTHタグ", Booth, 200, 80, 600, ItemListCell.Text, RowProperty: nameof(ItemListRow.BoothTags), HasToolTip: true),
        new("adult", "R-18", "R-18", Booth, 52, 40, 100, ItemListCell.Text, RowProperty: nameof(ItemListRow.Adult)),
        new("price", "BOOTH価格", "BOOTH価格", Booth, 96, 56, 240, ItemListCell.Number, RowProperty: nameof(ItemListRow.Price), HasToolTip: true),
        new("wish", "スキ数", "スキ数", Booth, 64, 48, 140, ItemListCell.Number, RowProperty: nameof(ItemListRow.WishLists)),
        new("shop", "ショップ", "ショップ", Booth, 200, 80, 500, ItemListCell.Template, "ItemListShopCell"),
        new("published", "公開日", "公開日", Booth, 92, 70, 160, ItemListCell.Number, RowProperty: nameof(ItemListRow.Published)),
        new("status", "公開状況", "公開状況", Booth, 110, 60, 200, ItemListCell.Text, RowProperty: nameof(ItemListRow.Status)),
        new("variations", "バリエーション", "バリエーションの数", Booth, 64, 44, 140, ItemListCell.Number, RowProperty: nameof(ItemListRow.Variations)),
        new("images", "画像", "画像の数", Booth, 56, 44, 140, ItemListCell.Number, RowProperty: nameof(ItemListRow.Images)),

        // ---- 商品の情報（自分の整理 → 手に入れ方 → 使い方 → 日付 → 入力の片付け → 取得の予定） ----
        new("icon", string.Empty, "サムネイル", Item, 52, 28, 400, ItemListCell.Template, "ItemListIconCell"),
        new("fav", "★", "お気に入り", Item, 36, 28, 60, ItemListCell.Template, "ItemListFavCell"),
        new("name", "名前", "名前", Item, 420, 160, 1000, ItemListCell.Template, "ItemListNameCell"),
        new("tags", "ユーザータグ", "ユーザータグ", Item, 160, 80, 500, ItemListCell.Template, "ItemListTagsCell"),
        new("attrs", "属性", "属性", Item, 160, 80, 500, ItemListCell.Template, "ItemListAttributesCell"),
        new("hidden", "非表示", "非表示", Item, 64, 48, 120, ItemListCell.Text, RowProperty: nameof(ItemListRow.Hidden)),
        new("paid", "払った額", "払った額", Item, 76, 56, 160, ItemListCell.Template, "ItemListPaidCell"),
        new("gift", "ギフト", "ギフト", Item, 100, 56, 200, ItemListCell.Text, RowProperty: nameof(ItemListRow.Gift)),
        new("avatars", "対応", "対応アバターの数", Item, 56, 44, 120, ItemListCell.Template, "ItemListAvatarsCell"),
        new("avatarNames", "対応アバター", "対応アバターの名前", Item, 200, 80, 600, ItemListCell.Text, RowProperty: nameof(ItemListRow.AvatarNames), HasToolTip: true),
        new("bases", "共通素体", "共通素体", Item, 140, 60, 400, ItemListCell.Text, RowProperty: nameof(ItemListRow.Bases), HasToolTip: true),
        new("profile", "プロファイル", "プロファイル", Item, 160, 80, 500, ItemListCell.Text, RowProperty: nameof(ItemListRow.Profile), HasToolTip: true),
        new("acquired", "入手日", "入手日", Item, 120, 70, 600, ItemListCell.Text, RowProperty: nameof(ItemListRow.Acquired), HasToolTip: true),
        new("chips", "状態", "状態の札", Item, 220, 100, 420, ItemListCell.Template, "ItemListChipsCell"),
        new("editStatus", "編集状況", "編集状況", Item, 160, 70, 500, ItemListCell.Text, RowProperty: nameof(ItemListRow.EditStatus), HasToolTip: true),
        new("memo", "メモ", "メモ", Item, 200, 60, 800, ItemListCell.Text, RowProperty: nameof(ItemListRow.Memo), HasToolTip: true),
        new("notify", "更新通知", "更新通知", Item, 80, 56, 160, ItemListCell.Text, RowProperty: nameof(ItemListRow.Notify)),
        new("lastFetched", "最終取得", "最終取得", Item, 92, 70, 160, ItemListCell.Number, RowProperty: nameof(ItemListRow.LastFetched)),
        new("nextFetch", "次回更新予定", "次回更新予定", Item, 96, 70, 160, ItemListCell.Number, RowProperty: nameof(ItemListRow.NextFetch)),

        // ---- ファイルの情報（手元に在るか → 量 → 記録の物が在るか → 開けるか） ----
        new("owned", "所持", "所持", File, 64, 44, 120, ItemListCell.Text, RowProperty: nameof(ItemListRow.Owned)),
        new("size", "容量", "容量", File, 80, 56, 160, ItemListCell.Number, RowProperty: nameof(ItemListRow.Size)),
        new("files", "ファイル", "ファイルの数", File, 64, 44, 140, ItemListCell.Number, RowProperty: nameof(ItemListRow.Files)),
        new("folders", "フォルダ", "フォルダの数", File, 64, 44, 140, ItemListCell.Number, RowProperty: nameof(ItemListRow.Folders)),
        new("missing", "見つからない", "見つからないファイル", File, 110, 60, 200, ItemListCell.Text, RowProperty: nameof(ItemListRow.Missing)),
        new("brokenZip", "壊れたzip", "壊れたzip", File, 80, 56, 160, ItemListCell.Text, RowProperty: nameof(ItemListRow.BrokenZip)),

        // ---- 改変登録 ----
        new("modifications", "改変", "使った改変の数", Modification, 56, 44, 120, ItemListCell.Number, RowProperty: nameof(ItemListRow.Modifications)),
        new("newVersion", "新しい版", "新しい版あり", Modification, 96, 60, 160, ItemListCell.Text, RowProperty: nameof(ItemListRow.NewVersion)),
    ];

    private static readonly Dictionary<string, ItemListColumnDef> s_byKey = All.ToDictionary(column => column.Key, StringComparer.Ordinal);

    public static ItemListColumnDef? Find(string key) => s_byKey.GetValueOrDefault(key);

    /// <summary>
    /// 画面ごとの既定の並び（ユーザ判断 2026-11-20：最初に出す列は今のまま）。
    /// 検索は名前の直後に足した4列（ユーザ判断 2026-10-04・案C）、ショップはショップの列の場所に入手日（店名は全部同じなので）
    /// </summary>
    public static IReadOnlyList<string> DefaultsOf(string screen) => screen switch
    {
        "search" => [Select, "icon", "fav", "name", "tags", "attrs", "paid", "avatars", "shop", "chips"],
        "shop" => [Select, "icon", "fav", "name", "acquired", "chips"],
        _ => [Select, "icon", "fav", "name", "shop", "chips"],
    };

    /// <summary>
    /// 覚えた並びを読む。知らない名前と重なった名前は飛ばし、選ぶ列は左端に置き直す（手で書き換えた ui-state でも崩れない）。
    /// 何も残らなければ既定（列が1本も無い一覧は押す所が無い）
    /// </summary>
    public static IReadOnlyList<string> Normalize(string screen, IEnumerable<string>? saved)
    {
        if (saved is null)
        {
            return DefaultsOf(screen);
        }

        var keys = saved.Where(key => key is not null && s_byKey.ContainsKey(key) && key != Select).Distinct(StringComparer.Ordinal).ToList();
        return keys.Count == 0 ? DefaultsOf(screen) : [Select, .. keys];
    }
}
