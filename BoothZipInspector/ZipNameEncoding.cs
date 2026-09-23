using System.Text;
using System.Text.Unicode;

namespace BoothZipInspector;

/// <summary>
/// zip の中の名前を読む文字コード。**UTF-8 の印（汎用フラグの 11 ビット目）が無い名前だけ**に使われる
/// （印のある名前は .NET が UTF-8 で読む）。
///
/// 印の無い名前は、Windows の圧縮なら CP932、Mac の圧縮（Finder・ditto）なら**印を付けない UTF-8** で入っている。
/// 前は印の無い名前を全部 CP932 で読んでいたので、Mac で作った zip の日本語の名前が化け、
/// unitypackage の見分けや展開先の名前が壊れていた（点検 2026-09-23）。
/// そこで、厳密な UTF-8 として読めればそれ、読めなければ CP932 にする（中身の文章を読む <see cref="TextDecoder"/> と同じ順）。
///
/// CP932 の名前がたまたま UTF-8 として正しく読める見込みは小さい。UTF-8 の多バイト文字は
/// 「C2〜F4 の後に 80〜BF が決まった数だけ続く」形に限られ、CP932 の漢字（先頭 81〜9F・E0〜FC、2バイト目 40〜FC）の並びが
/// 名前全体にわたってこの形に収まることはまず無い。測った（2026-09-23・同梱の JMdict の見出しと読み 49.8万語を CP932 にした）：
/// 1語だけで UTF-8 として読めたのは 210語（0.042%。「犇々」「龜甲」のような旧字・稀な字ばかり）、
/// 2語を「語_語_v1.0.unitypackage」の形に並べた20万通りでは 0件。
/// 英数字だけの名前はどちらで読んでも同じ。
///
/// 書き込みには使わない（書くときは UTF-8 で書き、印を付ける）。
/// </summary>
public sealed class ZipNameEncoding : Encoding
{
    public static ZipNameEncoding Instance { get; } = new();

    private static readonly Encoding PlainUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly Encoding ShiftJis;

    static ZipNameEncoding()
    {
        // 登録し忘れると GetEncoding(932) が投げる
        RegisterProvider(CodePagesEncodingProvider.Instance);
        ShiftJis = GetEncoding(932);
    }

    private ZipNameEncoding()
    {
    }

    public override string EncodingName => "UTF-8 or Shift_JIS (zip entry names)";

    private static Encoding Pick(ReadOnlySpan<byte> bytes) => Utf8.IsValid(bytes) ? PlainUtf8 : ShiftJis;

    public override int GetByteCount(char[] chars, int index, int count) => PlainUtf8.GetByteCount(chars, index, count);

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        => PlainUtf8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

    public override int GetCharCount(byte[] bytes, int index, int count)
        => Pick(bytes.AsSpan(index, count)).GetCharCount(bytes, index, count);

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        => Pick(bytes.AsSpan(byteIndex, byteCount)).GetChars(bytes, byteIndex, byteCount, chars, charIndex);

    public override string GetString(byte[] bytes, int index, int count)
        => Pick(bytes.AsSpan(index, count)).GetString(bytes, index, count);

    public override int GetMaxByteCount(int charCount) => PlainUtf8.GetMaxByteCount(charCount);

    public override int GetMaxCharCount(int byteCount)
        => Math.Max(PlainUtf8.GetMaxCharCount(byteCount), ShiftJis.GetMaxCharCount(byteCount));
}
