using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chmonos.Core.Storage;

/// <summary>
/// 動いている Chmonos の1本（<c>%LOCALAPPDATA%\Chmonos-running\&lt;プロセスの番号&gt;.json</c>。docs/spec/running-instances.md）。
/// 外の道具が、どの保存先のアプリが動いていて、どの <c>api.json</c> を読めばよいかを知るための札。**トークンは書かない**（場所だけを指す）。
/// </summary>
public sealed record RunningInstanceRecord
{
    public int ProcessId { get; init; }

    /// <summary>
    /// プロセスが始まった時刻（書いた時刻ではない）。番号は使い回されるので、読む側はこれとプロセスの始まりを照らして本人かを見る。
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    public string StoreRoot { get; init; } = string.Empty;

    /// <summary>
    /// API が待っているポート。止まっている（オフ・始められなかった・始める途中）なら null。
    /// null でも欄を省かない（外の道具に「API が止まっている」と「古い版で欄が無い」を分けて読ませるため）
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? Port { get; init; }

    /// <summary>保存先の <c>api.json</c> の場所（トークンはそこにある）。API が止まっていても書く。</summary>
    public string ApiFile { get; init; } = string.Empty;

    public string? ExePath { get; init; }

    public string? Version { get; init; }

    /// <summary>画面を出さない起動（<c>--headless</c>）か。</summary>
    public bool Headless { get; init; }
}

/// <summary>
/// 動いている Chmonos の一覧の置き場（保存先によらない決まった場所）。1本ごとに1ファイル。
///
/// 置き場は <c>%LOCALAPPDATA%\Chmonos-running</c>。<c>%LOCALAPPDATA%\Chmonos</c>（<c>location.json</c> の置き場）の下にしないのは、
/// location.json の無い人ではそこが保存先そのものだから：引越しは元の中身を全部運んでから消し、置き換え・戻すは中身を退け、
/// 戻す先は空であることを求める（<see cref="StoreMover"/>・<see cref="BackupArchive"/>・<see cref="StoreLocation.IsEmpty"/>）。
/// 下に置くと、ほかのアプリ（写しで開いた物）の札まで運ぶ・消す・「空でない」で断ることになる。隣のフォルダならどの保存先にもならない
/// </summary>
public static class RunningInstances
{
    /// <summary>置き場を差し替える環境変数（試験・確かめの道具）。</summary>
    public const string DirectoryVariable = "CHMONOS_RUNNING_DIR";

    public const string FolderName = "Chmonos-running";

    /// <summary>
    /// 札を書く置き場。環境変数があればそこ。無ければ、アプリ本体として起動したときだけ本物の場所（写しの保存先で開いた確かめのアプリも並ぶ）。
    /// それ以外（試験・ViewShot などの道具がアプリの一式を組んだとき）は null で、書かない。
    /// 道具が環境変数を入れ忘れても本物の一覧を汚さないよう、閉じた側に倒す（<see cref="StoreLocation.AllowsUserStore"/> と同じ考え）
    /// </summary>
    public static string? Directory
        => Environment.GetEnvironmentVariable(DirectoryVariable) is { } value && !string.IsNullOrWhiteSpace(value)
            ? Path.GetFullPath(value.Trim())
            : StoreLocation.AllowsUserStore
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName)
                : null;

    /// <summary>
    /// 書き方。日本語のパスをそのまま書き、null も省かない（<see cref="JsonStore.Options"/> は null を省く）。
    /// 保存先の外のファイルなので、形式の版の印も保存先の門も通さない
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string FileFor(string directory, int processId) => Path.Combine(directory, $"{processId}.json");

    public static void Write(string directory, RunningInstanceRecord record)
    {
        System.IO.Directory.CreateDirectory(directory);
        var path = FileFor(directory, record.ProcessId);

        // 読む側が書きかけを読まないよう、一時ファイルに書いてから名前を付け替える
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(record, Options));
            if (!AtomicReplace.TryReplace(temporary, path))
            {
                File.Move(temporary, path, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static void Remove(string directory, int processId)
    {
        var path = FileFor(directory, processId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>置き場の札を全部読む（生きているかは見ない。壊れた・名前の違うファイルは飛ばす）。</summary>
    public static IReadOnlyList<RunningInstanceRecord> ReadAll(string directory)
    {
        var records = new List<RunningInstanceRecord>();
        foreach (var (_, path) in Files(directory))
        {
            if (TryRead(path) is { } record)
            {
                records.Add(record);
            }
        }

        return records;
    }

    /// <summary>
    /// 落ちて残った札を片付ける（起動したときに呼ぶ）。消すのは、番号のプロセスが無い・始まりの時刻が違う（番号が別のプログラムに使い回された）物。
    /// 権限などで確かめられない物は残す（動いている別の本の札を消さない）。自分の番号の札は、後で書き直すので触らない。
    /// 消した数を返す
    /// </summary>
    /// <param name="isAlive">札のプロセスが今も動いているか。試験は作り物を渡す。</param>
    public static int Sweep(string directory, Func<int, DateTimeOffset?, bool?>? isAlive = null)
    {
        isAlive ??= IsAlive;
        var removed = 0;
        foreach (var (processId, path) in Files(directory))
        {
            if (processId == Environment.ProcessId)
            {
                continue;
            }

            // 壊れた札は始まりの時刻を読めないので、プロセスが無いときだけ消す
            var startedAt = TryRead(path)?.StartedAt;
            if (isAlive(processId, startedAt) is false)
            {
                try
                {
                    File.Delete(path);
                    removed++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // ほかの起動が同時に片付けた・読む側が開いている。次の起動でまた見る
                    Diagnostics.AppLog.Warn("動いている Chmonos の一覧の片付け", exception.GetType().Name);
                }
            }
        }

        return removed;
    }

    /// <summary>
    /// 番号のプロセスが、札を書いた本人として動いているか。無ければ false、始まりの時刻が1秒より離れていれば false（別のプログラム）、
    /// 確かめられなければ null。<paramref name="startedAt"/> が null（札が壊れている）なら、プロセスがあるだけで null（分からない）。
    /// 1秒は、<see cref="Process.StartTime"/> の細かさとの差に余裕を見た幅（番号の使い回しは前のプロセスが終わった後なので、離れ方はずっと大きい）
    /// </summary>
    public static bool? IsAlive(int processId, DateTimeOffset? startedAt)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        using (process)
        {
            try
            {
                if (process.HasExited)
                {
                    return false;
                }

                if (startedAt is not { } recorded)
                {
                    return null;
                }

                var actual = new DateTimeOffset(process.StartTime);
                return (actual - recorded).Duration() <= TimeSpan.FromSeconds(1);
            }
            catch (InvalidOperationException)
            {
                // 調べている間に終わった
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }
    }

    /// <summary>このプロセスの始まりの時刻（札の <see cref="RunningInstanceRecord.StartedAt"/>）。</summary>
    public static DateTimeOffset CurrentProcessStartedAt()
    {
        using var process = Process.GetCurrentProcess();
        return new DateTimeOffset(process.StartTime);
    }

    private static IEnumerable<(int ProcessId, string Path)> Files(string directory)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in System.IO.Directory.EnumerateFiles(directory, "*.json"))
        {
            if (int.TryParse(Path.GetFileNameWithoutExtension(path), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var processId))
            {
                yield return (processId, path);
            }
        }
    }

    private static RunningInstanceRecord? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<RunningInstanceRecord>(stream, Options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// このプロセスの札を持つ。起動したときに書き、API の状態が変わったら書き直し、閉じるときに消す。
/// 書けなくてもアプリは止めない（一覧は外の道具の手助けで、無くても api.json は読める）。失敗はログに残す
/// </summary>
public sealed class RunningInstanceRecorder : IDisposable
{
    private readonly string _directory;
    private readonly Lock _gate = new();
    private RunningInstanceRecord _record;
    private bool _closed;

    private RunningInstanceRecorder(string directory, RunningInstanceRecord record)
    {
        _directory = directory;
        _record = record;
    }

    /// <summary>落ちて残った札を片付けてから、自分の札を書く。置き場が無ければ（<see cref="RunningInstances.Directory"/> が null）何もせず null。</summary>
    public static RunningInstanceRecorder? Start(string? directory, RunningInstanceRecord record, Func<int, DateTimeOffset?, bool?>? isAlive = null)
    {
        if (directory is null)
        {
            return null;
        }

        try
        {
            RunningInstances.Sweep(directory, isAlive);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Error("動いている Chmonos の一覧の片付け", exception);
        }

        var recorder = new RunningInstanceRecorder(directory, record);
        recorder.Save();
        return recorder;
    }

    public RunningInstanceRecord Record
    {
        get
        {
            lock (_gate)
            {
                return _record;
            }
        }
    }

    /// <summary>
    /// ポートを今の値で書き直す。値は錠の中で読む（API の知らせは裏のスレッドから続けて来るので、
    /// 錠の外で読むと、後の状態を先に書いてから古い状態で上書きし得る）
    /// </summary>
    public void UpdatePort(Func<int?> port)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            var next = port();
            if (next == _record.Port)
            {
                return;
            }

            _record = _record with { Port = next };
            SaveLocked();
        }
    }

    /// <summary>札を消す（正しく閉じたとき）。この後の書き直しはしない。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            try
            {
                RunningInstances.Remove(_directory, _record.ProcessId);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Diagnostics.AppLog.Error("動いている Chmonos の一覧から消す", exception);
            }
        }
    }

    private void Save()
    {
        lock (_gate)
        {
            SaveLocked();
        }
    }

    private void SaveLocked()
    {
        try
        {
            RunningInstances.Write(_directory, _record);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.AppLog.Error("動いている Chmonos の一覧に書く", exception);
        }
    }
}
