using System.Text;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 「手元のファイル」の行の並べ方（設定の <see cref="AppSettings.FileSort"/>・ユーザ指示 2026-11-11）。
/// 商品ページと編集画面は同じ部品（ItemFilesPanel）で、行はどちらもここで並べる。Unity へ送る物を選ぶ窓も同じ。
///
/// **名前は行に出しているファイル名**（記録に表示名は無い）：記録の1つ目の場所のファイル名、古い版は置き換わった場所のファイル名。
/// 名前の無い物（場所の残っていないファイル）は、どの向きでもその組の後ろに置く。
///
/// 比べる前の寄せ方は検索の名前の並び（<see cref="NameCollation"/>）に合わせる：全角の英数字は半角、半角のカナは全角（NFKC）、
/// 英字は大文字小文字を区別せず、カタカナはひらがなに寄せる。ただし次の2つは合わせない。
/// <list type="bullet">
/// <item>記号を落とさず、区切り（. _ - 空白など）として残す。落とすと v1.10 が「v110」、v2.1 が「v21」になり、
/// 数で比べても v2.1 が v1.10 より前に来る</item>
/// <item>先頭の括弧を飛ばさず、漢字の読みも当てない。ファイル名の先頭の [ショップ名] は同じ作者の物をまとめる手掛かりで、
/// 読みの辞書を持ち回らずに済む（漢字は字の符号の順）</item>
/// </list>
/// 拡張子は後で比べる（A_v1.2.zip と A_v1.2.1.zip を、拡張子の「z」と数字の「1」の比べで決めないため）。
///
/// **同じ値の物は、向きによらず毎回同じ順**（ユーザ判断 2026-11-11）：向きを当てるのは並べる値（寄せた名前・大きさ）だけで、
/// 寄せた名前が同じなら元の名前の符号の順、それも同じ・大きさが同じなら記録の順（どちらも昇りのまま）。
/// 逆向きにしたとき同じ値の物まで逆になると、向きを切り替えるたびに同じ大きさの2つが入れ替わって見える。
/// </summary>
public static class LocalFileOrder
{
    /// <summary>区切りの字の代わりに置く字。数字・英字・かなより前に来る（短い名前・区切りの多い名前を前に置く）。</summary>
    private const char Separator = '\u0001';

    /// <summary>行に出すファイル名。場所も古い版の場所も無ければ null。</summary>
    public static string? NameOf(LocalFileRecord file)
        => file.Paths.Count > 0 ? Path.GetFileName(file.Paths[0])
            : file.Replaced is { } replaced ? Path.GetFileName(replaced.Path)
            : null;

    /// <summary>
    /// 行の順に並べる。外したファイルはどの順・どの向きでも後ろ（ユーザ判断 2026-09-12）。
    /// 追加した順の逆向きは記録の順を逆にするだけ（<c>registeredAt</c> は v1.3.0 より前の記録に無く、混ざると並べられないので使わない）。
    /// </summary>
    public static IReadOnlyList<LocalFileRecord> Sort(IEnumerable<LocalFileRecord> files, FileSort sort)
    {
        var indexed = files.Select((file, index) => (File: file, Index: index)).ToList();
        var byDetached = indexed.OrderBy(entry => entry.File.Detached);

        IOrderedEnumerable<(LocalFileRecord File, int Index)> sorted;
        switch (sort.Order)
        {
            case FileOrder.Name:
            case FileOrder.NameWithVersion:
                var versions = sort.Order == FileOrder.NameWithVersion;
                var keys = Comparer<FileNameKey>.Create((x, y) => sort.Descending ? y.CompareTo(x) : x.CompareTo(y));
                sorted = byDetached
                    .ThenBy(entry => NameOf(entry.File) is null)
                    .ThenBy(entry => KeyOf(NameOf(entry.File) ?? string.Empty, versions), keys)
                    .ThenBy(entry => NameOf(entry.File), StringComparer.Ordinal);
                break;
            case FileOrder.Size:
                sorted = sort.Descending
                    ? byDetached.ThenByDescending(entry => entry.File.SizeBytes)
                    : byDetached.ThenBy(entry => entry.File.SizeBytes);
                break;
            default:
                // 追加した順（と手で書いた知らない値）。向きはここだけ記録の順そのものに当てる
                return (sort.Descending ? byDetached.ThenByDescending(entry => entry.Index) : byDetached.ThenBy(entry => entry.Index))
                    .Select(entry => entry.File)
                    .ToList();
        }

        return sorted.ThenBy(entry => entry.Index).Select(entry => entry.File).ToList();
    }

    /// <summary>2つのファイル名を比べる（昇り）。寄せた名前が同じなら元の名前の符号の順（試験と、名前だけを並べる呼び手のため）。</summary>
    public static int Compare(string x, string y, bool versions)
    {
        var byKey = KeyOf(x, versions).CompareTo(KeyOf(y, versions));
        return byKey != 0 ? byKey : string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// 名前だけを持つ一覧（編集画面の「ファイルごとの種類」など）を、設定の名前の2つの順と向きで並べる比べ方。
    /// 名前で並べない物（追加した順・大きさ順）では null（呼び手は今までの並びのまま）。
    /// 寄せた名前が同じ物は、向きによらず元の名前の符号の順（<see cref="Sort"/> と同じ決まり）。
    /// </summary>
    public static IComparer<string>? NameComparer(FileSort sort)
    {
        if (sort.Order is not (FileOrder.Name or FileOrder.NameWithVersion))
        {
            return null;
        }

        var versions = sort.Order == FileOrder.NameWithVersion;
        return Comparer<string>.Create((x, y) =>
        {
            var byKey = sort.Descending ? KeyOf(y, versions).CompareTo(KeyOf(x, versions)) : KeyOf(x, versions).CompareTo(KeyOf(y, versions));
            return byKey != 0 ? byKey : string.CompareOrdinal(x, y);
        });
    }

    private static FileNameKey KeyOf(string name, bool versions)
    {
        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        return new FileNameKey(Fold(stem), Fold(extension), versions);
    }

    /// <summary>NFKC・小文字・ひらがなに寄せ、英数字とかな以外の続きを区切りの字1つにする。</summary>
    internal static string Fold(string text)
    {
        var folded = Nfkc.Fold(text);
        var builder = new StringBuilder(folded.Length);
        foreach (var raw in folded)
        {
            var c = Search.Kana.ToHiragana(char.ToLowerInvariant(raw));
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length == 0 || builder[^1] != Separator)
            {
                builder.Append(Separator);
            }
        }

        return builder.ToString();
    }

    /// <summary>寄せた名前（拡張子の前と拡張子）。元の名前の符号の順は向きを当てないので、ここでは比べない。</summary>
    private readonly record struct FileNameKey(string Stem, string Extension, bool Versions) : IComparable<FileNameKey>
    {
        public int CompareTo(FileNameKey other)
        {
            var byStem = Part(Stem, other.Stem);
            return byStem != 0 ? byStem : Part(Extension, other.Extension);
        }

        // 数字の続きを数で比べるのは検索の名前の並びと同じ道（先頭の 0 は読まない：v01 と v1 は同じ数）
        private int Part(string x, string y) => Versions ? NameCollation.CompareKeys(x, y) : string.CompareOrdinal(x, y);
    }
}
