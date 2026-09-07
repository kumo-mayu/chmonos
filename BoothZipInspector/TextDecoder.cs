using System.Text;

namespace BoothZipInspector;

/// <summary>
/// バイト列からテキストを推定してデコードする。
/// 1. BOMがあればBOMに従う
/// 2. UTF-8として妥当ならUTF-8
/// 3. UTF-8として不正ならShift-JIS(コードページ932)
/// </summary>
public static class TextDecoder
{
    public static string Decode(byte[] bytes)
    {
        var bom = DetectBom(bytes, out var preambleLength);
        if (bom is not null)
        {
            return bom.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }

        var strictUtf8 = Encoding.GetEncoding(
            "utf-8",
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        try
        {
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            var shiftJis = Encoding.GetEncoding(932);
            return shiftJis.GetString(bytes);
        }
    }

    private static Encoding? DetectBom(byte[] bytes, out int preambleLength)
    {
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            preambleLength = 4;
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            preambleLength = 4;
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            preambleLength = 3;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            preambleLength = 2;
            return Encoding.Unicode; // UTF-16 LE
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            preambleLength = 2;
            return Encoding.BigEndianUnicode; // UTF-16 BE
        }

        preambleLength = 0;
        return null;
    }
}
