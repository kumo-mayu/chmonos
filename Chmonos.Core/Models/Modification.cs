using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Chmonos.Core.Models;

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
    /// **手で足して、窓で選ばなかった分は null。**推定で埋めない——
    /// 「どのファイルを使ったかは分からない」が正しい（メモ26-②：手で足すときは窓で人が選べる。選んだ物だけが入る）。
    /// </summary>
    public string? FileHash { get; init; }

    /// <summary>
    /// zip内のどの <c>.unitypackage</c> か。
    ///
    /// 1つのzipに2つ入っているものが実データで2件あり、どちらも片方が依存物だった。
    /// これが無いと**どちらを入れたのか**が再現できない。手で足して選ばなかった分と、unitypackage の無いファイルを選んだ分は null。
    /// 登録したフォルダの中の物を送った分は、<see cref="FileHash"/> が null のままここだけ入る（フォルダからの場所。メモ65-③。フォルダはハッシュを持たない）。
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

    /// <summary>
    /// どのファイルを使ったかの記録があるか。空欄の意味を画面で言い分けるために見る。
    /// Unityへ送って入った分も、手で足すときに窓で選んだ分も同じに扱う（どちらも人が選んだファイルで、送るときも同じに使う。メモ26-②）
    /// </summary>
    /// <remarks>登録したフォルダの中の物はハッシュが無く、包みの場所だけを持つ（<see cref="Package"/>）ので、それも記録ありと数える。</remarks>
    [JsonIgnore]
    public bool HasFile => FileHash is not null || Package is not null;

    /// <summary>
    /// この改変で、使ったものの更新の促しに「確認した」を押した日時（改変ごと・ユーザ判断 2026-11-09）。
    /// 商品の控え（<see cref="ContentUpdate.FoundAt"/>）より後なら、その更新はこの改変では促さない。後でまた更新が来れば出る。
    /// 押していなければ書き出さない。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? UpdateCheckedAt { get; init; }
}

/// <summary>
/// 使ったものの行に記録する「どのファイルか」だけの組（選び直しの命令が運ぶ。<c>docs/spec/modifications.md</c>）。
/// 行のほかの欄（足した日時・外した印・確認した日時）は持たない：選び直しはファイルの欄だけを替え、ほかは元の行のまま残す
/// </summary>
public sealed record ModificationMemberFile(string? FileHash, string? Package, long? VariationId);

/// <summary>使ったファイルを選び直した結果。画面が「行が無い」と「ほかの行と区別できなくなる」を言い分けるために分ける。</summary>
public enum MemberFilesChange
{
    Changed,

    /// <summary>選んでいる間にほかで消された・並びが変わって、指した行が無い。</summary>
    RowMissing,

    /// <summary>
    /// 書くと同じ商品・同じ足した日時・同じファイルの行が2つになる（2行に分けた行の片方を、もう片方と同じにしたとき）。
    /// 行は位置でなく中身で指すので、見分けられない2行ができると、後の外す・並べ替えが別の行に当たる
    /// </summary>
    Collides,

    /// <summary>改変そのものが無い（消された）。</summary>
    ModificationMissing,
}

/// <summary>改変に貼った画像1枚。商品の <see cref="UserImage"/> と同じ持ち方。</summary>
public sealed record ModificationImage
{
    public required string FileName { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    public string? Caption { get; init; }
}

/// <summary>
/// 使ったものを記録した時の VPM の写し1つ（<see cref="ModificationRecord.VpmSnapshots"/>）。
/// </summary>
public sealed record ModificationVpmSnapshot
{
    /// <summary>
    /// この中身を最初に控えた時（使ったものを記録した時）。同じ中身の記録が続いても足さないので、「この時から、次の写しまでの記録はこの姿」と読む。
    /// 使ったものの行の <c>addedAt</c> とは結び付けない：選び直し・送る時の記録は行の <c>addedAt</c> を元のまま残す。
    /// </summary>
    public DateTimeOffset At { get; init; }

    /// <summary>入っていた VPM。名前の順。</summary>
    public IReadOnlyList<ModificationVpmPackage?>? Packages { get; init; } = [];

    /// <summary>欠けた行を除いた <see cref="Packages"/>。</summary>
    [JsonIgnore]
    public IReadOnlyList<ModificationVpmPackage> Items => [.. (Packages ?? []).OfType<ModificationVpmPackage>().Where(package => !string.IsNullOrWhiteSpace(package.Name))];

    internal ModificationVpmSnapshot Cleaned() => this with { Packages = Items };

    /// <summary>
    /// 同じ中身か（名前と版だけを見る。表示名の違いは見ない：表示名だけ変わった姿を別の写しにしても、原因探しの役に立たない）。
    /// </summary>
    public bool SameContent(IReadOnlyList<ModificationVpmPackage> other)
    {
        static string Key(IEnumerable<ModificationVpmPackage> packages)
            => string.Join('\n', packages
                .Select(package => $"{package.Name.ToLowerInvariant()}\t{package.Version}")
                .Order(StringComparer.Ordinal));
        return Key(Items) == Key(other);
    }
}

/// <summary>写しの中の VPM 1つ。読んだ時の名前・表示名・版だけを持つ（フォルダや manifest の印は、記録の後で版を比べるのに要らない）。</summary>
/// <param name="Name">パッケージの名前（<c>jp.lilxyzw.liltoon</c>）。</param>
/// <param name="DisplayName">表示名。読めなければ書かない。</param>
/// <param name="Version">版。読めなければ書かない。</param>
public sealed record ModificationVpmPackage(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Version)
{
    /// <summary>人に見せる名前（表示名、無ければ名前）。</summary>
    [JsonIgnore]
    public string Shown => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
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
    /// 使ったものを記録した時の、紐付けたプロジェクトの VPM（ユーザ判断 2026-11-12「控えられるようにしておきましょう」）。古い順。
    /// 記録するたびにその場で読み、**最後の写しと同じ中身なら足さない**（同じ姿が記録の数だけ並ぶと読めない）。
    /// 改変に1つで上書きしないのは、版を入れ替えながら作った改変で前の版が消え、壊れた原因を探せなくなるから。
    /// 行ごとに持たないのは、同じ十数行が使ったものの数だけ並ぶから。
    /// **欠けていれば控えなし**（この欄を足す前の改変・プロジェクトを紐付けずに記録した改変）。省略できる欄を足しただけなので形式の版は上げない。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ModificationVpmSnapshot?>? VpmSnapshots { get; init; }

    /// <summary>手で直した控えの欠けた行・欠けた配列を除いた <see cref="VpmSnapshots"/>（欠けた配列は空として受ける決まり）。</summary>
    [JsonIgnore]
    public IReadOnlyList<ModificationVpmSnapshot> RecordedVpm
        => [.. (VpmSnapshots ?? []).OfType<ModificationVpmSnapshot>().Select(snapshot => snapshot.Cleaned()).OrderBy(snapshot => snapshot.At)];

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

    internal static bool IsModification(string? id)
        => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal);
}
