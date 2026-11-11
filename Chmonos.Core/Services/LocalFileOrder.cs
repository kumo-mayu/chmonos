using System.Text;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 「手元のファイル」の行の並べ方（設定の <see cref="AppSettings.FileOrder"/>・ユーザ指示 2026-11-11）。
/// 商品ページと編集画面は同じ部品（ItemFilesPanel）で、行はどちらもここで並べる。
///
/// **名前は行に出しているファイル名**（記録に表示名は無い）：記録の1つ目の場所のファイル名、古い版は置き換わった場所のファイル名。
/// 名前の無い物（場所の残っていないファイル）は、その組の後ろに記録の順で置く。
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
/// 寄せた名前が同じなら元の名前の符号の順、それも同じなら記録の順（並べ替えは安定）。
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
    /// 行の順に並べる。外したファイルはどの順でも後ろ（ユーザ判断 2026-09-12）。<see cref="FileOrder.Added"/> は記録の順のまま。
    /// 同じ順位の物は記録の順（並べ替えは安定）。
    /// </summary>
    public static IReadOnlyList<LocalFileRecord> Sort(IEnumerable<LocalFileRecord> files, FileOrder order)
    {
        var byDetached = files.OrderBy(file => file.Detached);
        switch (order)
        {
            case FileOrder.Name:
            case FileOrder.NameWithVersion:
                break;
            case FileOrder.Newest:
                // 日時の無い記録（v1.3.0 より前）は後ろへ。並べ替えは安定なので、その中は記録の順のまま
                return byDetached
                    .ThenBy(file => file.RegisteredAt is null)
                    .ThenByDescending(file => file.RegisteredAt)
                    .ToList();
            case FileOrder.Largest:
                return byDetached.ThenByDescending(file => file.SizeBytes).ToList();
            default:
                // 追加した順と、手で書いた知らない値
                return byDetached.ToList();
        }

        var versions = order == FileOrder.NameWithVersion;
        return byDetached
            .ThenBy(file => NameOf(file) is null)
            .ThenBy(file => KeyOf(NameOf(file) ?? string.Empty, versions))
            .ToList();
    }

    /// <summary>2つのファイル名を比べる（試験と、名前だけを並べる呼び手のため）。</summary>
    public static int Compare(string x, string y, bool versions) => KeyOf(x, versions).CompareTo(KeyOf(y, versions));

    /// <summary>
    /// 名前だけを持つ一覧（編集画面の「ファイルごとの種類」など）を、設定の名前の2つの順で並べる比べ方。
    /// 名前で並べない順（追加した順・新しい順・大きい順）では null（呼び手は今までの並びのまま）。
    /// </summary>
    public static IComparer<string>? NameComparer(FileOrder order) => order switch
    {
        FileOrder.Name => Comparer<string>.Create((x, y) => Compare(x, y, versions: false)),
        FileOrder.NameWithVersion => Comparer<string>.Create((x, y) => Compare(x, y, versions: true)),
        _ => null,
    };

    private static FileNameKey KeyOf(string name, bool versions)
    {
        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        return new FileNameKey(Fold(stem), Fold(extension), name, versions);
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

    private readonly record struct FileNameKey(string Stem, string Extension, string Name, bool Versions) : IComparable<FileNameKey>
    {
        public int CompareTo(FileNameKey other)
        {
            var byStem = Part(Stem, other.Stem);
            if (byStem != 0)
            {
                return byStem;
            }

            var byExtension = Part(Extension, other.Extension);
            return byExtension != 0 ? byExtension : string.CompareOrdinal(Name, other.Name);
        }

        // 数字の続きを数で比べるのは検索の名前の並びと同じ道（先頭の 0 は読まない：v01 と v1 は同じ数）
        private int Part(string x, string y) => Versions ? NameCollation.CompareKeys(x, y) : string.CompareOrdinal(x, y);
    }
}
