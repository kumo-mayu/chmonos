using System.Text.RegularExpressions;
using Chmonos.Core.Models;

namespace Chmonos.Core.Storage;

/// <summary>
/// 保存先の中の場所の名前になる ID の形と、組んだ場所が所定のフォルダに収まるかの検査（2026-10-06 外部の点検・L106）。
///
/// **ID は場所の名前になる。**商品ID は <c>items/{id}.json</c> と <c>images/{id}/</c>、改変ID は <c>modifications/{id}.json</c> と
/// <c>images/_mods/{id}/</c>。保存先の JSON は人が手で直せる決まりなので、ID に何が書かれていてもおかしくない。
/// 絶対パスを <see cref="Path.Combine(string, string)"/> に渡すと前の部分が捨てられ、<c>..</c> は上へ抜ける。
/// 前はそのまま組んでいたので、登録簿の ID に絶対パスを書くと、持っていないアバターの画像の片付けが保存先の外を丸ごと消せた。
///
/// **形は作る側から決める**（ずれたら作る側と一緒に直す）：
/// 商品ID は BOOTH の番号（数字だけ）か <see cref="LocalItemId"/>（<c>local-</c> と小文字の16進）、
/// 改変ID は <see cref="ModificationId"/>（<c>mod-</c> と小文字の16進）、unitypackage の控えは zip のハッシュ（16進）。
/// 区切り・<c>..</c>・ドライブ名・UNC はどれも形に合わないので、形を見るだけで場所の外へは組めない。
///
/// 形を見るのに加えて、**消す・移す直前に <see cref="IsInside"/> でも確かめる**。形の決まりを後で緩めたときや、
/// 呼ぶ側が別の道で場所を組んだときにも、保存先の外を消さないための2枚目の守り。
/// </summary>
public static class StoreIds
{
    // 桁の上限は、場所の名前が長くなりすぎないための物。BOOTH の番号は今8桁、仮IDと改変IDのハッシュは8桁
    private static readonly Regex ItemIdPattern = new(
        @"\A(?:[0-9]{1,18}|local-[0-9a-f]{1,64})\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex ModificationIdPattern = new(
        @"\Amod-[0-9a-f]{1,64}\z",
        RegexOptions.CultureInvariant);

    // 控えの名前は大文字にそろえて組む（AppPaths.UnityPackageFile）ので、読むときは大小どちらも受ける
    private static readonly Regex PackageHashPattern = new(
        @"\A[0-9A-Fa-f]{1,128}\z",
        RegexOptions.CultureInvariant);

    /// <summary>商品ID（BOOTH の番号か仮ID）の形か。持っていないアバターの ID も同じ形。</summary>
    public static bool IsItemId(string? id) => id is not null && ItemIdPattern.IsMatch(id);

    /// <summary>改変ID の形か。</summary>
    public static bool IsModificationId(string? id) => id is not null && ModificationIdPattern.IsMatch(id);

    /// <summary>unitypackage の控えの鍵（zip のハッシュ）の形か。</summary>
    public static bool IsPackageHash(string? hash) => hash is not null && PackageHashPattern.IsMatch(hash);

    /// <summary>商品ID の形でなければ投げる。場所を組む前に呼ぶ（<see cref="AppPaths"/>）。</summary>
    public static string RequireItemId(string? id)
        => IsItemId(id) ? id! : throw new InvalidStoreIdException("商品ID", id);

    public static string RequireModificationId(string? id)
        => IsModificationId(id) ? id! : throw new InvalidStoreIdException("改変ID", id);

    public static string RequirePackageHash(string? hash)
        => IsPackageHash(hash) ? hash! : throw new InvalidStoreIdException("unitypackage の控えの鍵", hash);

    /// <summary>
    /// <paramref name="path"/> を正規化した場所が <paramref name="folder"/> の**中**（同じ場所は含まない）にあるか。
    /// 区切りを付けて比べる（<c>images</c> と <c>images-old</c> を取り違えない）。大文字小文字は区別しない（Windows の場所）。
    /// </summary>
    public static bool IsInside(string path, string folder)
    {
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            return full.Length > parent.Length
                && full.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// 消す・移す直前の確かめ。<paramref name="folder"/> の中でなければ投げる
    /// （黙って飛ばすと「消したつもり」で先へ進むので、呼ぶ側の失敗として扱わせる）。
    /// </summary>
    public static void EnsureInside(string path, string folder)
    {
        if (!IsInside(path, folder))
        {
            throw new InvalidStoreIdException($"保存先の外の場所（{path}）は消さない・動かさない");
        }
    }
}

/// <summary>
/// 保存先の中の場所の名前にならない ID。手で直した JSON から来る。
/// <see cref="ArgumentException"/> の仲間にして、引数の誤りとして扱わせる。
/// </summary>
public sealed class InvalidStoreIdException : ArgumentException
{
    public InvalidStoreIdException(string kind, string? value)
        : base($"{kind}の形ではない値（{Describe(value)}）からは場所を組まない")
    {
    }

    public InvalidStoreIdException(string message)
        : base(message)
    {
    }

    private static string Describe(string? value)
        => value is null ? "null" : value.Length > 80 ? value[..80] + "…" : value;
}
