using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace BoothAssetManager.Core.Models;

/// <summary>
/// 改変に使った構成物1件。
///
/// **順番は配列の順。**依存物を先に入れないと本体が通らないことがあるので
/// （<c>VRCHeartRate_Installer</c> の実例）、並びそのものが記録の一部。
/// </summary>
public sealed record ModificationMember
{
    public required string ItemId { get; init; }

    /// <summary>どの版か。分からなければ null。</summary>
    public long? VariationId { get; init; }

    /// <summary>
    /// 使ったzipのハッシュ。**これがバージョンにあたる。**
    ///
    /// 1年後に組み直すとき <c>v1.01</c> と <c>v1.06</c> は別物なので、
    /// 商品IDだけでは再現できない。
    ///
    /// **手で足した分は null。**推定で埋めない——
    /// 「どのファイルを使ったかは分からない」が正しい。
    /// </summary>
    public string? FileHash { get; init; }

    /// <summary>
    /// zip内のどの <c>.unitypackage</c> か。
    ///
    /// 1つのzipに2つ入っているものが実データで2件あり、どちらも片方が依存物だった。
    /// これが無いと**どちらを入れたのか**が再現できない。手で足した分は null。
    /// </summary>
    public string? Package { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    /// <summary>
    /// 外した構成物（ユーザ指示 2026-09-19：商品の手元のファイルと同じく、外しても行と記録を残して戻せるようにする）。
    /// 前は外すと行ごと消え、どのファイル・どの unitypackage を使ったかの記録も戻らなかった（足し直しても送るまで埋まらない）。
    /// 使った数・送る・絞り込みには数えない。完全に消すのは、外した行の「削除」から。
    /// 外していなければ書き出さない（全部の行に false が並ぶと読みにくい。手元のファイルの印と同じ）
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Detached { get; init; }

    /// <summary>Unityへ送ったときに自動で入った分か。空欄の意味を画面で言い分けるために見る。</summary>
    [JsonIgnore]
    public bool IsFromUnity => FileHash is not null;
}

/// <summary>改変に貼った画像1枚。商品の <see cref="UserImage"/> と同じ持ち方。</summary>
public sealed record ModificationImage
{
    public required string FileName { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    public string? Caption { get; init; }
}

/// <summary>
/// 改変1件。**アバター1体＋名前**で1つ（1体に複数持てる）。
///
/// 「Wendyの普段着」「Wendyの制服」が別の改変になる。
/// 1体に1つだと着替えるたびに上書きになり、記録の意味が薄れる。
///
/// 決めた理由は <c>docs/history/modifications.md</c> にある。
/// </summary>
public sealed record ModificationRecord
{
    public required string Id { get; init; }

    /// <summary>
    /// どのアバターの改変か。アバター登録簿のBOOTH商品ID。
    ///
    /// **必須。**住所が決まるのでアバター管理の中に置ける。
    /// 登録簿に無いアバターは、改変を作るときにその場で足す。
    /// </summary>
    public required string AvatarItemId { get; init; }

    public required string Name { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// 紐付けたUnityプロジェクトの場所。紐付けていなければ null。
    ///
    /// **改変の同一性ではない。**プロジェクトを消したり作り直したりしても
    /// 記録は残す。プロジェクト基準にすると、消した瞬間に記録の意味が変わる。
    ///
    /// 消えていても黙って外さない——消したのか移しただけなのかは人にしか分からない。
    /// </summary>
    public string? UnityProject { get; init; }

    public string? Memo { get; init; }

    /// <summary>
    /// VRChat にアップロードしたアバターの blueprint ID（<c>avtr_…</c>）。無ければ null（ユーザ指示 2026-09-19）。
    /// VRChat の OSC に <c>/avatar/change</c> で送ると、その改変のアバターに着替えられる。
    /// 1つの改変＝1つのアップロードなので改変に持つ（同じアバターでも改変ごとに別の ID になる）
    /// </summary>
    public string? BlueprintId { get; init; }

    /// <summary>
    /// 改変専用の画像。
    ///
    /// 商品に付けた <see cref="ImageRole.Modified"/> の画像とは別物——
    /// あちらは「この商品を改変に使うとこうなる」で、
    /// 改変の姿は**組み合わせた結果**。
    /// </summary>
    public IReadOnlyList<ModificationImage> Images { get; init; } = [];

    /// <summary>使ったもの。**並びが導入の順。**外した行（<see cref="ModificationMember.Detached"/>）も並びの中に残す。</summary>
    public IReadOnlyList<ModificationMember> Members { get; init; } = [];

    /// <summary>
    /// 今使っているもの（外した行を除く）。使った数・送る・絞り込み・「この商品を使った改変」はこちらで見る。
    /// 位置（何件目か）が要る所は <see cref="Members"/> の位置を使う（外した行を除くと位置がずれる）
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ModificationMember> UsedMembers => [.. Members.Where(member => !member.Detached)];

    [JsonIgnore]
    public bool HasUnityProject => !string.IsNullOrWhiteSpace(UnityProject);
}

/// <summary>
/// 改変のID。**<c>mod-{hash8}</c>。**
///
/// 商品の仮ID（<c>local-</c>）や自分で足した画像（<c>user-</c>）と同じ作法。
/// 接頭辞を付けるのは、**フォルダを開いた人に一目で分かるようにするため。**
///
/// **名前をファイル名にしない。**改変の名前は変えたくなるもの
/// （「テスト」→「普段着」）で、そのたびにファイルが動くのは避けたい。
/// 名前は中に入れておけば <c>grep</c> で辿れる。
///
/// **中身のハッシュにしない。**構成物を足すたびにIDが変わってしまう。
/// 作ったときの情報（アバター・名前・時刻）から1度だけ決めて、以後変えない。
/// </summary>
public static class ModificationId
{
    public const string Prefix = "mod-";

    public const int HashLength = 8;

    /// <summary>
    /// 作ったときの情報からIDを決める。
    ///
    /// 時刻を含めるのは、**同じアバターに同じ名前の改変を作れる**ようにしてあるため
    /// （「普段着」を作り直したいとき、古い方を消す前に新しい方を作れないと困る）。
    /// </summary>
    public static string For(string avatarItemId, string name, DateTimeOffset createdAt)
    {
        var seed = $"{avatarItemId}{name.Trim()}{createdAt.UtcTicks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Prefix + Convert.ToHexStringLower(hash)[..HashLength];
    }

    public static bool IsModification(string? id)
        => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal);
}
