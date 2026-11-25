using System.Globalization;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// リストの列のうち、文字だけを出す列の中身（ユーザ判断 2026-11-20：列を自分で選ぶ）。
///
/// **カードごとに1つを持ち、値は列が初めて読んだときに1回だけ作って持つ**（<see cref="CardInfo"/> と同じ考え）。
/// 出していない列の値は作らないので、既定の列のままなら今までと重さが変わらない。流して戻っても作り直さない。
/// 記録の外の材料（使った改変の数・アバターの名前）は <see cref="ItemListRowSources"/> から引く——行ごとに改変や記録を読み直さない。
/// 記録が替わればカードごと作り直される（検索の読み直し）ので、ここでは見張らない
/// </summary>
public sealed class ItemListRow
{
    private readonly ItemRecord _item;
    private readonly ItemListRowSources _sources;
    private readonly bool _awaitingDetection;
    private readonly string[] _values = new string[(int)ItemListRowField.Count];

    /// <summary>フォルダの行（フォルダビュー）が持つ空の行。商品の列には何も出さない。</summary>
    public static ItemListRow Empty { get; } = new();

    private ItemListRow()
    {
        _item = new ItemRecord { Id = string.Empty };
        _sources = ItemListRowSources.None;
        Array.Fill(_values, string.Empty);
    }

    /// <param name="awaitingDetection">取り込みの③（対応アバターの検出）がまだか。編集状況を「取り込み中」と出す（カードの札と同じ）。</param>
    public ItemListRow(ItemRecord item, ItemListRowSources sources, bool awaitingDetection)
    {
        _item = item;
        _sources = sources;
        _awaitingDetection = awaitingDetection;
    }

    private string Get(ItemListRowField field, Func<string> build)
        => _values[(int)field] ??= build();

    // ---- BOOTHの情報 ----

    public string Id => Get(ItemListRowField.Id, () => _item.Id);

    public string Category => Get(ItemListRowField.Category, () => _item.CategoryName ?? string.Empty);

    public string BoothTags => Get(ItemListRowField.BoothTags, () => string.Join("　", _item.Booth.Tags));

    /// <summary>ショップの名前（自分で直した名前を先に）。ショップ画面のカードは2行目に入手日を入れるので、カードの値は使わない。</summary>
    public string Shop => Get(ItemListRowField.Shop, () => _item.ShopName ?? string.Empty);

    /// <summary>BOOTH の価格。BOOTH が返した整形済みの文字（「¥ 2,500」「¥ 500 〜」）をそのまま出す。</summary>
    public string Price => Get(ItemListRowField.Price, () => _item.Booth.PriceText ?? string.Empty);

    /// <summary>スキ数。BOOTH から一度も取れていない商品（BOOTH に無い商品を含む）は 0 と書かず空にする。</summary>
    public string WishLists => Get(ItemListRowField.WishLists, () => _item.Booth.WasEverFetched
        ? _item.Booth.WishListsCount.ToString("N0", CultureInfo.InvariantCulture)
        : string.Empty);

    /// <summary>公開状況。条件「公開状況」と同じ分け方（販売終了と非公開を1つに・BOOTH に無い商品は空）。</summary>
    public string Status => Get(ItemListRowField.Status, () => _item.IsLocalOnly
        ? string.Empty
        : SearchConditions.IsDelisted(_item) ? "販売終了・非公開" : "公開中");

    public string Published => Get(ItemListRowField.Published, () => _item.Booth.PublishedAt is { } published
        ? DateText(DateOnly.FromDateTime(published.LocalDateTime))
        : string.Empty);

    public string Adult => Get(ItemListRowField.Adult, () => _item.Booth.IsAdult ? "R-18" : string.Empty);

    public string Variations => Get(ItemListRowField.Variations, () => CountText(_item.Booth.Variations.Count));

    public string Images => Get(ItemListRowField.Images, () => CountText(_item.Booth.Images.Count));

    // ---- 商品の情報 ----

    /// <summary>入手日。並べ替え・ショップ画面の入手日と同じ代表の日（最も早い購入。無ければファイルの日付で代え、そうと書く）。</summary>
    public string Acquired => Get(ItemListRowField.Acquired, () =>
    {
        var acquired = PurchaseDates.ResolveEarliest(_item);
        return acquired.Value is null ? string.Empty : DisplayText.AcquiredDate(acquired.Value, acquired.IsFallback);
    });

    /// <summary>ギフト。条件「購入・ギフト」の選択肢と同じ語。自分で買っただけの商品は空（ほとんどの商品がそれなので、違う物だけが目に付くように）。</summary>
    public string Gift => Get(ItemListRowField.Gift, () =>
    {
        var parts = new List<string>(2);
        if (Purchases.WasGiven(_item))
        {
            parts.Add("ギフトした");
        }

        if (Purchases.WasReceived(_item))
        {
            parts.Add("ギフトされた");
        }

        return string.Join("・", parts);
    });

    /// <summary>対応アバターの名前。対応の数（<see cref="CardInfo.AvatarCountOf"/>）と同じく、消した物は出さない。</summary>
    public string AvatarNames => Get(ItemListRowField.AvatarNames, () => string.Join("、", _item.Local.Avatars
        .Where(link => !link.Rejected)
        .Select(link => link.Name ?? _sources.AvatarNameOf(link.AvatarItemId) ?? link.AvatarItemId)));

    public string Bases => Get(ItemListRowField.Bases, () => string.Join("、", _item.Local.AvatarBases
        .Where(link => !link.Rejected)
        .Select(link => link.BaseName)));

    /// <summary>
    /// プロファイル。条件「プロファイル」と同じ見分けでプロファイルの商品だけ、つなぎ先の名前を出す。
    /// つなぎ先をまだ決めていない物は「つなぎ先なし」（プロファイルの商品だとは分かるように）
    /// </summary>
    public string Profile => Get(ItemListRowField.Profile, () =>
    {
        if (!SearchModuleConditions.IsProfileItem(_item))
        {
            return string.Empty;
        }

        var names = (_item.Local.Conversion?.Targets ?? [])
            .Where(target => !target.Rejected && !target.IsEmpty)
            .Select(target => target.BaseName ?? _sources.AvatarNameOf(target.AvatarItemId!) ?? target.AvatarItemId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return names.Count > 0 ? string.Join("、", names) : "つなぎ先なし";
    });

    public string Hidden => Get(ItemListRowField.Hidden, () => _item.Local.IsHidden ? "非表示" : string.Empty);

    /// <summary>
    /// 編集状況。条件「編集状況」の項目のうち、まだ入れていない物の名前（編集画面の欄の名前）。全部入れてあれば空。
    /// 取り込みの途中の商品は「取り込み中」（カードの札と同じ。検出が終わるまで編集できない）
    /// </summary>
    public string EditStatus => Get(ItemListRowField.EditStatus, () => _awaitingDetection
        ? "取り込み中"
        : string.Join("・", EditFieldsMissing.All
            .Where(editField => EditFieldsMissing.IsMissing(_item, editField))
            .Select(EditFieldToggle.LabelOf)));

    /// <summary>商品ページの「更新通知」のチェック。既定（知らせる）は空にし、切った商品だけを書く。</summary>
    public string Notify => Get(ItemListRowField.Notify, () => _item.Local.NotifyOnUpdate ? string.Empty : "通知しない");

    /// <summary>メモ。改行は1行に詰める（行の高さは変えない）。</summary>
    public string Memo => Get(ItemListRowField.Memo, () => string.IsNullOrWhiteSpace(_item.Local.Memo)
        ? string.Empty
        : string.Join(" ", _item.Local.Memo.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

    /// <summary>最終取得。商品ページの「最終取得」と同じ値。</summary>
    public string LastFetched => Get(ItemListRowField.LastFetched, () => _item.Local.LastFetchedAt is { } at
        ? DateText(DateOnly.FromDateTime(at.LocalDateTime))
        : string.Empty);

    /// <summary>次回更新予定。商品ページと同じ値。</summary>
    public string NextFetch => Get(ItemListRowField.NextFetch, () => _item.Local.NextFetchDueAt is { } at
        ? DateText(DateOnly.FromDateTime(at.LocalDateTime))
        : string.Empty);

    // ---- ファイルの情報 ----

    /// <summary>所持。カードの札と同じ所持の定義（ファイルかフォルダを1つ以上持つ）。</summary>
    public string Owned => Get(ItemListRowField.Owned, () => _item.IsOwned ? "所持" : "未所持");

    /// <summary>ファイルの数。手元に持っている物だけ（外した物・消した物は数えない。所持の定義と同じ）。</summary>
    public string Files => Get(ItemListRowField.Files, () => CountText(_item.Local.OwnedFiles.Count));

    /// <summary>容量。カードの容量と同じ値。未所持は空（カードの「未取得」が列に並ぶと、ある物が目に入らない）。</summary>
    public string Size => Get(ItemListRowField.Size, () => _item.IsOwned ? DisplayText.Size(_item.OwnedSizeBytes) : string.Empty);

    public string Folders => Get(ItemListRowField.Folders, () => CountText(_item.Local.LocalFolders.Count));

    /// <summary>見つからないファイル。カードの札と同じ分け方（全部か一部か）。</summary>
    public string Missing => Get(ItemListRowField.Missing, () => !_item.HasMissingFile
        ? string.Empty
        : _item.HasAllFilesMissing ? "見つからない" : "一部見つからない");

    public string BrokenZip => Get(ItemListRowField.BrokenZip, () => _item.HasBrokenArchive ? "壊れたzip" : string.Empty);

    // ---- 改変登録 ----

    /// <summary>使った改変の数。外した行だけの改変は数えない（商品ページの「改変 n 件で使っています」と同じ数）。</summary>
    public string Modifications => Get(ItemListRowField.Modifications, () => CountText(_sources.UsedItems().Of(_item.Id).Modifications));

    /// <summary>
    /// 新しい版あり。改変に使っている商品に、確かめていない新しい版が出ている（改変の行の札「新しい版あり」と同じ語）。
    /// 使っているかは改変だけで見る（見出しが改変登録なので。Unity のプロジェクトだけで使っている物は商品ページの帯で分かる）
    /// </summary>
    public string NewVersion => Get(ItemListRowField.NewVersion, () =>
        _item.Local.HasPendingContentUpdate && _sources.UsedItems().Of(_item.Id).Modifications > 0 ? "新しい版あり" : string.Empty);

    /// <summary>数の列。0 は空にする（「0」が並ぶと、ある物が目に入らない）。</summary>
    private static string CountText(int count) => count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>値を持つ欄の番号（<see cref="ItemListRow"/> の控えの並び）。</summary>
internal enum ItemListRowField
{
    Id,
    Category,
    BoothTags,
    Shop,
    Price,
    WishLists,
    Status,
    Published,
    Adult,
    Variations,
    Images,
    Acquired,
    Gift,
    AvatarNames,
    Bases,
    Profile,
    Hidden,
    EditStatus,
    Notify,
    Memo,
    LastFetched,
    NextFetch,
    Owned,
    Size,
    Files,
    Folders,
    Missing,
    BrokenZip,
    Modifications,
    NewVersion,
    Count,
}

/// <summary>
/// リストの行が記録の外から引く材料。画面が1つを持ち、全部の行が同じ物を指す（行ごとに改変を読まない）。
/// </summary>
/// <param name="UsedItems">今の「使っている所」（主の画面の見張りの答え）。読むたびに今の物を返す。</param>
/// <param name="AvatarNameOf">アバターの商品ID → 名前（プロファイルのつなぎ先・名前の控えの無い対応アバター）。分からなければ null。</param>
public sealed record ItemListRowSources(Func<UsedItems> UsedItems, Func<string, string?> AvatarNameOf)
{
    public static ItemListRowSources None { get; } = new(() => Core.Services.UsedItems.Empty, _ => null);
}
