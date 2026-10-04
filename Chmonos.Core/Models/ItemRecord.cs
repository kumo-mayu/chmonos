using System.Text.Json.Serialization;

namespace Chmonos.Core.Models;

/// <summary>
/// 1つのBOOTH商品に対応するローカル記録。<c>items/{id}.json</c> として1商品につき1ファイルで保存する。
/// BOOTH由来の <see cref="Booth"/> と、ユーザ入力の <see cref="Local"/> を分けているのは、
/// 再取得時に <see cref="Booth"/> を丸ごと差し替えるだけで済ませ、ユーザの入力を壊さないため。
/// </summary>
public sealed record ItemRecord
{
    /// <summary>BOOTH商品ID。ファイル名と一致する正のキー。</summary>
    public required string Id { get; init; }

    /// <summary>
    /// BOOTHから取った物。**必須にしない**（ユーザ判断 2026-09-21・J3）。
    ///
    /// 入れ物なので、欄ごと消えている＝「まだ何も取っていない」と読むのが自然。
    /// 必須にしていたので、手で <c>"booth": {}</c> を消しただけでその商品が読み込みで落ち、
    /// 一覧から丸ごと消えていた（「読めなかった N 件」には出るが、中身は失われていないのに開けない）。
    /// </summary>
    public BoothBlock Booth { get; init; } = new();

    /// <summary>人と取り込みが決めた物。<see cref="Booth"/> と同じく入れ物なので必須にしない。</summary>
    public LocalBlock Local { get; init; } = new();

    /// <summary>
    /// 画面に出す名前。**ユーザが付けた名前を優先する。**
    ///
    /// BOOTHから取れない商品はユーザが名付けるしかなく、
    /// 後でBOOTHが復活しても、その人が選んだ呼び方の方が探しやすい。
    /// ユーザによる命名であることは画面側で明記する（<c>Local.DisplayName is not null</c>）。
    /// </summary>
    [JsonIgnore]
    public string DisplayName => DisplayText.ItemName(Local.DisplayName, Booth.Name, Id);

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
    public string? ShopName => DisplayText.Prefer(Local.Shop?.Name, Booth.Shop?.Name);

    /// <summary>
    /// ショップを束ねる鍵。ショップ画面のグループ分けと、アイコン・バナーの
    /// ファイル名がこれ1つで決まる。持っていなければ null（ショップ画面に出ない）。
    /// </summary>
    [JsonIgnore]
    public string? ShopSubdomain => Local.Shop?.Subdomain ?? Booth.Shop?.Subdomain;

    /// <summary>ショップをユーザが入れたか。観測と入力の区別を隠さないために出す。</summary>
    [JsonIgnore]
    public bool HasUserShop => Local.Shop is not null;

    /// <summary>
    /// 分類。**ユーザが入れたものを優先する。**子の名前1つ。
    /// 絞り込みも統計もこの1つで動く。
    /// </summary>
    [JsonIgnore]
    public string? CategoryName => DisplayText.Prefer(Local.Category, Booth.Category?.Name);

    /// <summary>分類をユーザが入れたか。</summary>
    [JsonIgnore]
    public bool HasUserCategory => Local.Category is { Length: > 0 };

    /// <summary>ファイルを1つ以上持っているか。全画面で「所持している」の定義に使う。</summary>
    [JsonIgnore]
    public bool IsDownloaded => Local.OwnedFiles.Count > 0;

    /// <summary>
    /// 壊れていて開けない zip を持っているか（検索の条件「壊れたzip」・ユーザ判断 2026-09-30）。
    ///
    /// 外したファイルは数えない——所持・容量と同じく、この商品の持ち物ではない（商品ページの外した行には札が出るが、
    /// 絞り込みで出ると「ダウンロードし直す」相手がこの商品に見える）。
    /// **記録だけで決め、ディスクは見ない。**検索は全商品を条件の数だけ照らすので、ここでファイルの有無を確かめると打鍵のたびにディスクを叩く。
    /// そのため、壊れた zip を消しただけで取り込み直していない商品も当たる（商品ページの札は、在る物にだけ出す）。
    /// </summary>
    [JsonIgnore]
    public bool HasBrokenArchive => Local.LocalFiles.Any(file => file is { Detached: false, ArchiveBroken: true });

    /// <summary>
    /// 記録の上では持っているが、置き場がどこにも無いファイルがあるか（カードとリストの印「見つかりません」・
    /// 検索の条件「見つからないファイル」・統計の件数。ユーザ判断 2026-10-04）。
    /// 3か所が別々に同じ式を書くと、数が食い違って「印はあるのに絞ると出ない」になるので、ここ1か所で決める。
    ///
    /// 外したファイルは数えない（<see cref="HasBrokenArchive"/> と同じ）。**記録だけで決め、ディスクは見ない**：
    /// パスが空になるのは、取り込みがディスクに無い場所を全部外したとき（「見つからないファイルを探す」は中身で探して場所を足す）。
    /// フォルダ登録（<see cref="LocalBlock.LocalFolders"/>）は、取り込みが「無い」と見たときに書く
    /// <see cref="LocalFolderRecord.MissingSince"/> を数える（ユーザ判断 2026-10-04。前は記録に「無い」状態が無く、数えていなかった）。
    /// </summary>
    [JsonIgnore]
    public bool HasMissingFile
        => Local.LocalFiles.Any(file => !file.Detached && file.Paths.Count == 0)
        || Local.LocalFolders.Any(folder => folder.MissingSince is not null);

    /// <summary>論理容量。同じ中身のファイルが複数箇所にあっても1回だけ数える（商品ページの表示用）。</summary>
    [JsonIgnore]
    public long LogicalSizeBytes => Local.OwnedFiles.Sum(file => file.SizeBytes);

    /// <summary>実占有量。重複コピーを含めて実際にドライブを食っている量（統計の表示用）。</summary>
    [JsonIgnore]
    public long ActualDiskBytes => Local.OwnedFiles.Sum(file => file.SizeBytes * Math.Max(1, file.Paths.Count));
}
