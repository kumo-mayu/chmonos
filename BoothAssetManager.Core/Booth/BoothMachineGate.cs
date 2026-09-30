using System.Globalization;
using System.Text;

namespace BoothAssetManager.Core.Booth;

/// <summary>
/// PC で1つの門に書き置く中身。**同じ PC で動くアプリ全部が、これを見て間を空ける。**
///
/// 時刻は壁の時計ではなく、起動してからの刻み（<see cref="TimeProvider.GetTimestamp"/>）で書く。
/// 刻みは PC で1つなのでプロセスをまたいで比べられ、時計を合わせ直しても動かない
/// （壁の時計で書くと、時計が1時間戻っただけで1時間待つことになる）。
/// </summary>
public sealed record BoothGateState
{
    /// <summary>最後の問い合わせが終わった刻み。まだ誰も問い合わせていなければ null。</summary>
    public long? EndedAt { get; init; }

    /// <summary>
    /// 送っている最中か。**終われば必ず下ろすので、読んだ人がこれを見るのは、送ったまま落ちたアプリが居たときだけ。**
    /// そのときは、いつ終わったかが分からないので「今終わった」とみなして1回分の間を空ける。
    /// </summary>
    public bool InFlight { get; init; }

    /// <summary>最後に問い合わせたアプリの、そのときの間隔（429 で広げていれば広げた値）。</summary>
    public int IntervalMs { get; init; }

    /// <summary>BOOTH に「この刻みまでは来るな」と言われていれば、その刻み。</summary>
    public long? QuietUntil { get; init; }

    /// <summary>最後に問い合わせたのは誰か（<see cref="BoothClient"/> 1つごとの印）。</summary>
    public string? Sender { get; init; }

    /// <summary>順番を待っている別のアプリ。最後に問い合わせた側は、これを見て1回譲る。</summary>
    public string? Waiter { get; init; }

    /// <summary>待っているアプリの間隔。長ければ、最後に問い合わせた側もそこまで待つ。</summary>
    public int WaiterIntervalMs { get; init; }

    public static BoothGateState Empty { get; } = new();

    /// <summary>
    /// 読めなかったときの中身。**「送っている最中に落ちた」と同じに扱う**——いつ終わったかが分からないので、
    /// 間を空けずに出る側ではなく、1回分の間を空ける側に倒す。
    /// </summary>
    public static BoothGateState Unknown { get; } = new() { InFlight = true };

    /// <summary>最後の行。これが無ければ書きかけ（書いている途中で落ちた）とみなす。</summary>
    private const string EndLine = "end";

    public string ToText()
    {
        var text = new StringBuilder();
        Append(text, "endedAt", EndedAt?.ToString(CultureInfo.InvariantCulture));
        Append(text, "inFlight", InFlight ? "1" : null);
        Append(text, "intervalMs", IntervalMs > 0 ? IntervalMs.ToString(CultureInfo.InvariantCulture) : null);
        Append(text, "quietUntil", QuietUntil?.ToString(CultureInfo.InvariantCulture));
        Append(text, "sender", Sender);
        Append(text, "waiter", Waiter);
        Append(text, "waiterIntervalMs", WaiterIntervalMs > 0 ? WaiterIntervalMs.ToString(CultureInfo.InvariantCulture) : null);
        text.Append(EndLine).Append('\n');
        return text.ToString();
    }

    private static void Append(StringBuilder text, string key, string? value)
    {
        if (value is not null)
        {
            text.Append(key).Append('=').Append(value).Append('\n');
        }
    }

    public static BoothGateState Parse(string text)
    {
        // まだ誰も書いていない（できたばかりの）ファイル
        if (text.Length == 0)
        {
            return Empty;
        }

        var state = Empty;
        foreach (var line in text.Split('\n'))
        {
            if (line == EndLine)
            {
                return state;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                return Unknown;
            }

            var value = line[(separator + 1)..];
            switch (line[..separator])
            {
                case "endedAt" when TryLong(value, out var endedAt):
                    state = state with { EndedAt = endedAt };
                    break;
                case "inFlight" when value == "1":
                    state = state with { InFlight = true };
                    break;
                case "intervalMs" when TryInt(value, out var interval):
                    state = state with { IntervalMs = interval };
                    break;
                case "quietUntil" when TryLong(value, out var quietUntil):
                    state = state with { QuietUntil = quietUntil };
                    break;
                case "sender":
                    state = state with { Sender = value };
                    break;
                case "waiter":
                    state = state with { Waiter = value };
                    break;
                case "waiterIntervalMs" when TryInt(value, out var waiterInterval):
                    state = state with { WaiterIntervalMs = waiterInterval };
                    break;
                default:
                    return Unknown;
            }
        }

        return Unknown;
    }

    private static bool TryLong(string value, out long result)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static bool TryInt(string value, out int result)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
}

/// <summary>
/// BOOTH への問い合わせの、**PC で1つの門**（絶対に破らない決め事1。ユーザ判断 2026-09-30）。
///
/// <see cref="BoothClient"/> の中の門（<see cref="PriorityGate"/>）はアプリ1本ごとで、二重起動の錠は保存先ごとなので、
/// 保存先の違うアプリを2本開くと（普段使いと確かめ用の写し・アプリと評価台・並べて走らせた道具）、それぞれが1.5秒ごとに
/// 問い合わせて、合わせると決め事を破っていた。ここは同じ Windows のユーザで動く全部のプロセスが通る。
///
/// **仕組みは、ファイル1つと、その上の錠（範囲の錠）。**
/// - 握っている間は、ほかの誰も握れない。握るのは、問い合わせを送ってから終わるまでと、中身を読み書きする一瞬だけ。
///   **間隔を待っている間は放す**——握ったまま待つと、裏の作業を続けているアプリが握り続けて、
///   もう1本の人が押した問い合わせが入れない。
/// - 落ちたら OS がハンドルを閉じて錠も外すので、錠が残って問い合わせが止まったままにならない
///   （<see cref="Storage.SingleInstanceLock"/> と同じ理由。名前付きセマフォは落ちると残る。
///   名前付きミューテックスは落ちても残らないが、取ったスレッドでしか放せず、await をまたぐ通信を挟めない）。
/// - 最後の問い合わせの刻みも同じファイルに書く。錠と中身が1つなので、「握ったが中身は別の所」の食い違いが起きない。
/// - **ファイルは開いたままにして、握るたびに開け閉めしない。**握るたびに開け閉めする形で測ると、1回の問い合わせに
///   4.2ms 上乗せされた（書いて閉じるたびにウイルス対策の検査が走る。2026-09-30・<c>experiments/BoothGateProbe bench</c>）。
///
/// **開けないとき（書けない場所・壊れた権限）は、門なしで進む**（ログに1回残す）。
/// 止めると BOOTH から何も取れないアプリになる。アプリ1本の中の決め事は <see cref="BoothClient"/> が今までどおり守る。
/// </summary>
public sealed class BoothMachineGate : IDisposable
{
    /// <summary>
    /// 握られていたときに握り直すまでの間合い。握られているのは相手の問い合わせ1本の間（手元の実測で 0.16〜0.3 秒：
    /// 1件 1.66〜1.80 秒から間隔の 1.5 秒を引いた分。<c>experiments/FetchTimingProbe</c>）で、
    /// その後に 1.5 秒の間隔が続くので、50ms の遅れは待ちに隠れる。
    /// これより短くしても早くは出られず、握り直しの回数だけが増える
    /// </summary>
    private static readonly TimeSpan RetryEvery = TimeSpan.FromMilliseconds(50);

    /// <summary>ERROR_SHARING_VIOLATION・ERROR_LOCK_VIOLATION。ほかの誰かが握っている。</summary>
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    /// <summary>
    /// 錠を掛ける範囲（先頭からこの長さ）。中身は 150 バイトほどで、これより長ければこのアプリの書いた物ではない。
    /// 範囲はファイルの終わりを越えてよいので、空のファイルにも掛かる
    /// </summary>
    private const int LockedBytes = 1024;

    private readonly string _file;
    private readonly Func<TimeSpan, CancellationToken, Task> _retryDelay;

    /// <summary>
    /// このプロセスの中で同じ門を使う人どうしの順番。範囲の錠は別のハンドルとの間の物なので、
    /// 同じハンドルを分け合う人（1つのプロセスに <see cref="BoothClient"/> が2つ）はここで1人ずつにする
    /// </summary>
    private readonly SemaphoreSlim _inProcess = new(1, 1);

    private FileStream? _stream;
    private int _failureLogged;
    private bool _disposed;

    /// <summary>
    /// 置き場。**保存先の中には置かない**——保存先の違うアプリ同士で共有できないと意味が無い。
    /// 既定の保存先（<c>%LOCALAPPDATA%\Chmonos</c>）とも分ける。そこは引越しで中身を丸ごと運ぶ・消す場所で、
    /// 開いたままのファイルが混ざると運べない。一時フォルダにもしない（場所が環境変数で決まるので、
    /// 道具から起動したアプリと人が開いたアプリで別の場所を見ると、黙って門が2つになる）。
    /// </summary>
    public static string DefaultFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Chmonos.Shared",
        "booth-gate.txt");

    /// <summary>この Windows のユーザで1つの門。</summary>
    public static BoothMachineGate ForThisUser { get; } = new(DefaultFile);

    /// <param name="file">握るファイル。試験と確かめの道具は自分の場所を渡す（本物の門に触らないため）。</param>
    /// <param name="retryDelay">握られていたときの待ち。試験では実際に待たない物を渡す。</param>
    public BoothMachineGate(string file, Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        _file = file;
        _retryDelay = retryDelay ?? ((duration, token) => Task.Delay(duration, token));
    }

    /// <summary>
    /// 握る。握れるまで待つ（中断は効く）。
    /// </summary>
    /// <returns>握った物。門が使えないときは null（呼んだ側は門なしで進む）。</returns>
    public async Task<Turn?> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _inProcess.WaitAsync(cancellationToken);
        var entered = false;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var stream = _stream ??= Open();
                    LockRange(stream);
                    entered = true;
                    return new Turn(this, stream);
                }
                catch (IOException exception) when ((exception.HResult & 0xFFFF) is SharingViolation or LockViolation)
                {
                    // 誰かが握っている。放すのを待つ
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                      or NotSupportedException or ArgumentException
                                                      or ObjectDisposedException)
                {
                    // 次に握るときは開け直す（置き場が後から使えるようになれば、そこから門に戻る）
                    Close();
                    LogOnce("BOOTH への問い合わせの門（PC で1つ）を握る", exception);
                    return null;
                }

                await _retryDelay(RetryEvery, cancellationToken);
            }
        }
        finally
        {
            if (!entered)
            {
                _inProcess.Release();
            }
        }
    }

    private FileStream Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Path.GetDirectoryName(_file) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        // ほかのプロセスも同じファイルを開いたままにするので、読み書きは分け合う（1人ずつにするのは範囲の錠）。
        // 緩衝は持たない。書いた物は、次に握る人にすぐ見えてほしい
        return new FileStream(
            _file,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite,
            bufferSize: 1,
            FileOptions.None);
    }

    /// <summary>
    /// 範囲の錠を掛ける。macOS の .NET には無い（アプリは Windows だけで動く。ほかでは「門が使えない」側に倒れる）。
    /// </summary>
    private static void LockRange(FileStream stream)
    {
        if (OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException();
        }

        stream.Lock(0, LockedBytes);
    }

    private void Close()
    {
        try
        {
            _stream?.Dispose();
        }
        catch (IOException)
        {
        }

        _stream = null;
    }

    private void Leave(FileStream stream)
    {
        try
        {
            // 握れたのは錠を掛けられた所だけなので、ここへ来るのは錠のある所だけ
            if (!OperatingSystem.IsMacOS())
            {
                stream.Unlock(0, LockedBytes);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // 錠が外せないまま持ち続けると、ほかのアプリが出られない。ハンドルごと閉じれば OS が外す
            Close();
            LogOnce("BOOTH への問い合わせの門（PC で1つ）を放す", exception);
        }

        _inProcess.Release();
    }

    /// <summary>同じ失敗を問い合わせのたびに書かない（1.5秒ごとにログが1行ずつ増える）。</summary>
    private void LogOnce(string context, Exception exception)
    {
        if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
        {
            Diagnostics.AppLog.Error(context, exception);
        }
    }

    /// <summary>
    /// ファイルを閉じる。アプリは閉じない（終わるまで使い、終われば OS が閉じる）。
    /// 試験が、自分の一時フォルダを消す前に呼ぶ。
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        Close();
    }

    /// <summary>門を握っている間。<see cref="Dispose"/> で放す。</summary>
    public sealed class Turn : IDisposable
    {
        private readonly BoothMachineGate _owner;
        private FileStream? _stream;

        internal Turn(BoothMachineGate owner, FileStream stream)
        {
            _owner = owner;
            _stream = stream;
            State = Read(stream);
        }

        /// <summary>今の中身。握ったときに読み、<see cref="Write"/> で書いたらその値になる。</summary>
        public BoothGateState State { get; private set; }

        private BoothGateState Read(FileStream stream)
        {
            try
            {
                var length = stream.Length;
                if (length > LockedBytes)
                {
                    return BoothGateState.Unknown;
                }

                var buffer = new byte[(int)length];
                stream.Position = 0;
                stream.ReadExactly(buffer);
                return BoothGateState.Parse(Encoding.UTF8.GetString(buffer));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _owner.LogOnce("BOOTH への問い合わせの門（PC で1つ）を読む", exception);
                return BoothGateState.Unknown;
            }
        }

        /// <summary>
        /// 中身を書き換える。**書けなくても投げない**——問い合わせの前後（送る直前・終わった直後）で呼ぶので、
        /// 投げると問い合わせそのものが失敗に見える。書けなかった分は、次に読む人が古い中身を見るだけ。
        /// ディスクへの書き出しは待たない（守りたいのはプロセスの間で見えることで、電源が落ちた後に残ることではない）。
        /// </summary>
        public void Write(BoothGateState state)
        {
            State = state;
            if (_stream is not { } stream)
            {
                return;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(state.ToText());
                stream.Position = 0;
                stream.Write(bytes);

                // 前の中身の方が長いと、後ろに古い行が残る。最後の行（end）より後ろは読まないが、切っておく
                if (stream.Length != bytes.Length)
                {
                    stream.SetLength(bytes.Length);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _owner.LogOnce("BOOTH への問い合わせの門（PC で1つ）へ書く", exception);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _stream, null) is { } stream)
            {
                _owner.Leave(stream);
            }
        }
    }
}
