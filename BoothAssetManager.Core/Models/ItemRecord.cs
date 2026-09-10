using System.Text.Json.Serialization;

namespace BoothAssetManager.Core.Models;

/// <summary>
/// 1つのBOOTH商品に対応するローカル記録。<c>items/{id}.json</c> として1商品につき1ファイルで保存する。
/// BOOTH由来の <see cref="Booth"/> と、ユーザ入力の <see cref="Local"/> を分けているのは、
/// 再取得時に <see cref="Booth"/> を丸ごと差し替えるだけで済ませ、ユーザの入力を壊さないため。
/// </summary>
public sealed record ItemRecord
{
    /// <summary>BOOTH商品ID。ファイル名と一致する正のキー。</summary>
    public required string Id { get; init; }

    public required BoothBlock Booth { get; init; }

    public required LocalBlock Local { get; init; }

    /// <summary>
    /// 画面に出す名前。**ユーザが付けた名前を優先する。**
    ///
    /// BOOTHから取れない商品はユーザが名付けるしかなく、
    /// 後でBOOTHが復活しても、その人が選んだ呼び方の方が探しやすい。
    /// ユーザによる命名であることは画面側で明記する（<c>Local.DisplayName is not null</c>）。
    /// </summary>
    [JsonIgnore]
    public string DisplayName => Local.DisplayName ?? Booth.Name ?? Id;

    /// <summary>
    /// BOOTHに無い商品として登録したものか。**BOOTHへは問い合わせない。**
    /// </summary>
    [JsonIgnore]
    public bool IsLocalOnly => LocalItemId.IsLocal(Id);

    /// <summary>
    /// 画面に出すショップ名。**ユーザが入れたものを優先する。**
    /// 名前と同じ理由——BOOTHから取れない商品はユーザが入れるしかない。
    /// </summary>
    [JsonIgnore]
    public string? ShopName => Local.Shop?.Name ?? Booth.Shop?.Name;

    /// <summary>
    /// ショップを束ねる鍵。ショップ画面のグループ分けと、アイコン・バナーの
    /// ファイル名がこれ1つで決まる。持っていなければ null（ショップ画面に出ない）。
    /// </summary>
    [JsonIgnore]
    public string? ShopSubdomain => Local.Shop?.Subdomain ?? Booth.Shop?.Subdomain;

    /// <summary>ショップをユーザが入れたか。観測と入力の区別を隠さないために出す。</summary>
    [JsonIgnore]
    public bool HasUserShop => Local.Shop is not null;

    /// <summary>ファイルを1つ以上持っているか。全画面で「所持している」の定義に使う。</summary>
    [JsonIgnore]
    public bool IsDownloaded => Local.LocalFiles.Count > 0;

    /// <summary>論理容量。同じ中身のファイルが複数箇所にあっても1回だけ数える（商品ページの表示用）。</summary>
    [JsonIgnore]
    public long LogicalSizeBytes => Local.LocalFiles.Sum(file => file.SizeBytes);

    /// <summary>実占有量。重複コピーを含めて実際にドライブを食っている量（統計の表示用）。</summary>
    [JsonIgnore]
    public long ActualDiskBytes => Local.LocalFiles.Sum(file => file.SizeBytes * Math.Max(1, file.Paths.Count));
}
