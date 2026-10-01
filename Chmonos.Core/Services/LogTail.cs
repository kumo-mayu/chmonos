using System.Text;

namespace Chmonos.Core.Services;

/// <summary>
/// 書き足されていくログを、前回の続きから1行ずつ読む（#69 の Unity の Editor.log）。
///
/// **他のプロセスが書いている最中のファイルを読む。**書き手を邪魔しないよう、読むたびに開いて閉じ、
/// 共有は読み書き・削除まで許す（Unity は開いたまま書き続けている）。
///
/// **縮んだら先頭から読み直す。**Editor.log は開いている全エディタが共有し、後から起動したエディタは
/// 起動時に先頭から書き直す（§11-3）。前回の位置のままだと、書き直された分を読み飛ばす。
///
/// 行の途中で読み終えたら、残りは次に回す（1行が2回に割れて届くと、パスの突き合わせを誤る）。
/// </summary>
public sealed class LogTail
{
    private readonly string _path;
    private long _position;
    private string _pending = string.Empty;

    /// <summary>今の末尾から読み始める（それより前の行は、送る前の出来事なので要らない）。</summary>
    public LogTail(string path)
    {
        _path = path;
        _position = LengthOf(path);
    }

    /// <summary>前回から増えた行。読めなければ空（ログが無くても送ることはできる）。</summary>
    public IReadOnlyList<string> ReadNewLines()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < _position)
            {
                _position = 0;
                _pending = string.Empty;
            }

            if (stream.Length == _position)
            {
                return [];
            }

            stream.Position = _position;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            var text = _pending + reader.ReadToEnd();
            _position = stream.Length;

            var lines = text.Split('\n');
            _pending = lines[^1];
            return lines[..^1]
                .Select(line => line.TrimEnd('\r').Replace("\0", string.Empty))
                .Where(line => line.Length > 0)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static long LengthOf(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
