using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Chmonos.Core.Models;

namespace Chmonos.Core.Storage;

/// <summary>保管庫から読んだ知らせと、読めなかった行の数（知らずに捨てないよう数を返す）。</summary>
public sealed record NotificationArchiveRead(IReadOnlyList<NotificationRecord> Records, int UnreadableLines);

/// <summary>
/// 知らせの保管庫（<c>notification-archive/yyyy-MM.jsonl.gz</c>。ユーザ判断 2026-11-10）。
///
/// 通知に残す件数を超えた古い既読・解消済みを、捨てる代わりにここへ移す。商品の更新の履歴は後から作り直せないため。
/// 中身は知らせ1件を1行の JSON（JSON Lines）にして gzip で縮める。
///
/// - **月は知らせを作った日（<see cref="NotificationRecord.CreatedAt"/>）で分ける。**月で引くと「その月に起きたこと」になり、
///   容量を超えて古い月から消すと古い出来事から消える。保管した日で分けると、どの月に入るかは件数の上限を超えた時期しだいで、月で引く意味が無い。
///   月は記録が持つ時差のままの年月で決める（読む PC の時刻帯で変わらないように）。
/// - **足すときは、今のファイルのバイト列を一時ファイルへ写し、後ろに gzip のメンバーを1つ足して据える。**
///   その場で後ろに足すと、書きかけで止まったときにちぎれたメンバーが残り、それより後に足した物まで読めなくなる。
///   全部を解いて書き直すと、上限を超えた後は知らせ1件ごとに月を丸ごと縮め直す。写して足すなら縮め直さず、据えるまで前の中身は無傷。
/// - **版はメンバーごとの1行目に置く**（<c>{"formatVersion":N}</c>）。形式を上げた後に古い月のファイルへ足しても、
///   行ごとにどの版で書いたかが分かる。ファイル名は月だけにして、並べ替えと消す順に使う。
/// </summary>
public sealed class NotificationArchive
{
    public const string DirName = "notification-archive";

    private const string Extension = ".jsonl.gz";

    /// <summary>このアプリが書く名前だけを扱う（使う人が置いた別のファイルは数えず、消さない）。</summary>
    private static readonly Regex MonthFileName = new(@"^(\d{4}-\d{2})\.jsonl\.gz$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>1行に1件。今の保存と同じ書き方（camelCase・列挙は文字・日本語をそのまま）で、改行だけ外す。</summary>
    private static readonly JsonSerializerOptions LineOptions = new(JsonStore.Options) { WriteIndented = false };

    /// <summary>足すと消すを1本にする（終了時の削除が、足している途中のファイルを消さないように）。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NotificationArchive(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; }

    /// <summary>試験だけが使う：新しいメンバーを半分書いたところで呼ぶ（書きかけで止まったときを作る）。</summary>
    internal Func<Task>? MidWriteForTests { get; set; }

    public static string MonthOf(DateTimeOffset createdAt) => $"{createdAt.Year:D4}-{createdAt.Month:D2}";

    public string FileFor(string month) => Path.Combine(Directory, month + Extension);

    /// <summary>
    /// 知らせを月ごとのファイルへ足す。投げたら、そのとき書いていた月のファイルは前の中身のまま
    /// （前の月までに足した分は残る。呼び手は全部を残すので、重なった分は読むときに1つにまとめる）。
    /// </summary>
    public async Task AppendAsync(IReadOnlyCollection<NotificationRecord> records, CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var month in records.GroupBy(record => MonthOf(record.CreatedAt)).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var member = Member(month);
                var path = FileFor(month.Key);
                await JsonStore.WriteStreamAsync(
                    path,
                    async (target, token) =>
                    {
                        if (File.Exists(path))
                        {
                            await using var existing = JsonStore.OpenShared(path);
                            await existing.CopyToAsync(target, token);
                        }

                        var half = member.Length / 2;
                        await target.WriteAsync(member.AsMemory(0, half), token);
                        if (MidWriteForTests is { } stop)
                        {
                            await stop();
                        }

                        await target.WriteAsync(member.AsMemory(half), token);
                    },
                    cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>gzip のメンバー1つ：版の行と、知らせ1件ずつの行。</summary>
    private static byte[] Member(IEnumerable<NotificationRecord> records)
    {
        var text = new StringBuilder();
        text.Append("{\"").Append(StoreFormat.VersionProperty).Append("\":").Append(StoreFormat.Current).Append("}\n");
        foreach (var record in records)
        {
            text.Append(JsonSerializer.Serialize(record, LineOptions)).Append('\n');
        }

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(new UTF8Encoding(false).GetBytes(text.ToString()));
        }

        return buffer.ToArray();
    }

    /// <summary>保管庫にある月（古い順）。</summary>
    public IReadOnlyList<string> Months()
        => MonthFiles().Select(file => file.Month).ToList();

    /// <summary>月のファイルの数と大きさの合計（設定の画面に出す。件数は全部を解かないと数えられないので数えない）。</summary>
    public (int Months, long Bytes) Usage()
    {
        var files = MonthFiles();
        return (files.Count, files.Sum(file => file.Length));
    }

    /// <summary>
    /// 月を指定して知らせを並べる（作った順）。<paramref name="itemId"/> を渡せばその商品の物だけ。
    /// 読めない行（壊れた行・このアプリより新しい版の行・知らない種類）は飛ばして数える。1行のために月ごと読めなくしない。
    /// 足すのに失敗してやり直した分などで同じ行が2回あれば、1つにまとめる。
    /// </summary>
    public async Task<NotificationArchiveRead> ReadAsync(string month, string? itemId = null, CancellationToken cancellationToken = default)
    {
        var path = FileFor(month);
        if (!File.Exists(path))
        {
            return new NotificationArchiveRead([], 0);
        }

        var records = new List<NotificationRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unreadable = 0;
        var version = StoreFormat.Unversioned;

        await using var stream = JsonStore.OpenShared(path);
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false));
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (InvalidDataException)
            {
                // gzip が途中で壊れている（手で切った・ディスクの事故）。そこまでに読めた分は返す
                unreadable++;
                break;
            }

            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (VersionLine(line) is { } stamped)
            {
                version = stamped;
                continue;
            }

            if (version > StoreFormat.Current)
            {
                unreadable++;
                continue;
            }

            if (!seen.Add(line))
            {
                continue;
            }

            if (Parse(line, version, path) is not { } record)
            {
                unreadable++;
                continue;
            }

            if (itemId is null || string.Equals(record.ItemId, itemId, StringComparison.Ordinal))
            {
                records.Add(record);
            }
        }

        return new NotificationArchiveRead(records.OrderBy(record => record.CreatedAt).ToList(), unreadable);
    }

    /// <summary>版の行（欄が版だけのオブジェクト）なら版を返す。</summary>
    private static int? VersionLine(string line)
    {
        if (!line.Contains(StoreFormat.VersionProperty, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(line) is JsonObject { Count: 1 } stamp
                && stamp[StoreFormat.VersionProperty] is JsonValue value && value.TryGetValue<int>(out var number)
                ? number
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NotificationRecord? Parse(string line, int version, string path)
    {
        try
        {
            if (version == StoreFormat.Current || StoreFormat.LeavesAsIs(version))
            {
                return JsonSerializer.Deserialize<NotificationRecord>(line, JsonStore.Options);
            }

            return JsonNode.Parse(line) is { } node
                ? StoreFormat.Migrate(node, version, typeof(NotificationRecord), path).Deserialize<NotificationRecord>(JsonStore.Options)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 合計が容量を超えていたら、古い月のファイルから丸ごと消す（月の途中は消さない）。0 なら全部消す。
    /// 呼ぶのはアプリを閉じるときの1回だけ（設定で容量を小さくしてから戻しても、閉じるまでは消えない）。
    /// 新しい月1つだけで容量を超えるときも消す：容量は使う人が決めた上限なので、超えたまま残さない。
    /// </summary>
    /// <returns>消したファイルの数。</returns>
    public async Task<int> TrimAsync(long capacityBytes, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var files = MonthFiles();
            var total = files.Sum(file => file.Length);
            if (capacityBytes > 0 && total <= capacityBytes)
            {
                return 0;
            }

            // 引越し・バックアップが保存先を運んでいる間は消さない（運んだ一覧と突き合わせが食い違う）
            using var writing = await StoreWriteGate.EnterAsync(cancellationToken);
            var deleted = 0;
            foreach (var file in files)
            {
                if (capacityBytes > 0 && total <= capacityBytes)
                {
                    break;
                }

                File.Delete(file.Path);
                total -= file.Length;
                deleted++;
            }

            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record MonthFile(string Month, string Path, long Length);

    private List<MonthFile> MonthFiles()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        // リンクの先（保存先の外）は数えない・消さない（StoreTree）
        var directory = Path.GetFullPath(Directory);
        return StoreTree.FilesWithLength(directory)
            .Select(file => (file, match: MonthFileName.Match(Path.GetFileName(file.Path))))
            .Where(pair => pair.match.Success && string.Equals(Path.GetDirectoryName(pair.file.Path), directory, StringComparison.OrdinalIgnoreCase))
            .Select(pair => new MonthFile(pair.match.Groups[1].Value, pair.file.Path, pair.file.Length))
            .OrderBy(file => file.Month, StringComparer.Ordinal)
            .ToList();
    }
}
