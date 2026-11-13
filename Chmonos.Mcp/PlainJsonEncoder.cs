using System.Text.Encodings.Web;

namespace Chmonos.Mcp;

/// <summary>
/// JSON の文字列で、逃がさないと JSON が壊れる文字（引用符・バックスラッシュ・制御文字）だけを <c>\uXXXX</c> に逃がす書き方。
/// 既定の書き方も <c>UnsafeRelaxedJsonEscaping</c> も、全角空白（U+3000）と絵文字（サロゲートの組）は必ず逃がす
/// （空白の仲間と補助面の文字は、範囲を広げても逃がす決め事。2026-11-13 に Claude Code から呼んで <c>　</c> <c>🐾</c> で返るのを確かめた）。
/// 逃がすと AI が読む字数が増えて読みにくいので、自分で決める。
/// HTML・JavaScript に埋めない前提（MCP の答えは stdio の JSON-RPC の文字の中身で、AI が読むだけ）なので、&lt; &gt; &amp; ' は逃がさなくてよい。
/// 対にならないサロゲートは、書き手が先に置き換え文字にするので、ここまで来ない（来ても逃がす）
/// </summary>
internal sealed class PlainJsonEncoder : JavaScriptEncoder
{
    public static readonly PlainJsonEncoder Instance = new();

    // \uXXXX の6文字
    public override int MaxOutputCharactersPerInputCharacter => 6;

    public override bool WillEncode(int unicodeScalar)
        => unicodeScalar < 0x20 || unicodeScalar == '"' || unicodeScalar == '\\' || unicodeScalar == 0x7F
            || (unicodeScalar >= 0xD800 && unicodeScalar <= 0xDFFF);

    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        for (var i = 0; i < textLength; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < textLength && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }

            if (WillEncode(c))
            {
                return i;
            }
        }

        return -1;
    }

    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
    {
        if (!WillEncode(unicodeScalar))
        {
            // 補助面の文字はそのまま2文字で書く
            if (unicodeScalar > 0xFFFF)
            {
                if (bufferLength < 2)
                {
                    numberOfCharactersWritten = 0;
                    return false;
                }

                var pair = char.ConvertFromUtf32(unicodeScalar);
                buffer[0] = pair[0];
                buffer[1] = pair[1];
                numberOfCharactersWritten = 2;
                return true;
            }

            if (bufferLength < 1)
            {
                numberOfCharactersWritten = 0;
                return false;
            }

            buffer[0] = (char)unicodeScalar;
            numberOfCharactersWritten = 1;
            return true;
        }

        if (bufferLength < 6)
        {
            numberOfCharactersWritten = 0;
            return false;
        }

        const string hex = "0123456789ABCDEF";
        buffer[0] = '\\';
        buffer[1] = 'u';
        buffer[2] = hex[(unicodeScalar >> 12) & 0xF];
        buffer[3] = hex[(unicodeScalar >> 8) & 0xF];
        buffer[4] = hex[(unicodeScalar >> 4) & 0xF];
        buffer[5] = hex[unicodeScalar & 0xF];
        numberOfCharactersWritten = 6;
        return true;
    }
}
