namespace Chmonos.Core.Scanning;

/// <summary>監視フォルダで見つけた、書き終わった新しいファイル1つ。</summary>
/// <param name="Arrived">見張り始めた後に現れた（か、中身が変わった）物か。見張り始める前から在った新着は false。</param>
public sealed record LiveWatchFile(string Path, string Fingerprint, bool Arrived);

/// <summary>
/// 1回の探し直しで渡す新着（全部）と、それを見つけた見張りの世代。
/// 呼び手は画面のスレッドへ預けてから処理するので、その間に張り直すと古い見張りの結果が後から届く。
/// 受け取る時に <see cref="LiveFolderWatch.IsCurrent"/> で照らす（外したフォルダの物で帯・自動の取り込みを始めない。W4）
/// </summary>
public sealed class LiveWatchBatch(IReadOnlyList<LiveWatchFile> files, long generation) : IReadOnlyList<LiveWatchFile>
{
    public long Generation { get; } = generation;

    public int Count => files.Count;

    public LiveWatchFile this[int index] => files[index];

    public IEnumerator<LiveWatchFile> GetEnumerator() => files.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>待ち時間（plan 49）。数字の根拠は各欄に書く。試験は待ちと時計を差し替える。</summary>
public sealed record LiveFolderWatchTiming
{
    /// <summary>
    /// 最後の出来事からこれだけ静かになったら探す（最後の出来事の時刻から数える。待ち始めからではない）。続けて置かれた物（エクスプローラのコピー・zip の展開で数百の出来事が続く）を1回にまとめる。
    /// 5秒：コピーの途中はファイルごとに出来事が来るので数秒の静けさはまず無く、ダウンロードは出来上がった後に Zone.Identifier を付け足す
    /// （その書き込みも同じ静けさの中に収まる）。知らせが5秒遅れても、取り込みは1件に1.5秒以上かかるので気にならない
    /// </summary>
    public TimeSpan Quiet { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 書き終わったかを確かめるため、大きさと更新日時を2回見る間。2秒：ディスクの一覧の大きさはファイルを閉じるまで遅れて反映されることがあり、
    /// 1秒では書き込みの合間を「止まった」と見誤りうる。開けるか（掴まれていないか）も見るので、長くする必要は無い
    /// </summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 壊れた見張り（フォルダが消えた・ドライブが外れた）を張り直してみる間。30秒：つなぎ直すのは人の操作で数十秒はかかり、
    /// 届かない共有は在るかを確かめるだけで最長3秒かかる（FilePresenceProbe の打ち切り）ので、短くしても得が無い
    /// </summary>
    public TimeSpan Rearm { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 書いている途中の物が残ったとき、出来事を待たずに確かめ直す回数の上限。60回（静けさ5秒ごとで約5分）：
    /// それより長いダウンロードは、出来上がると改名の出来事が来るので、そこで確かめ直せる。ずっと掴まれた物で探し続けない
    /// </summary>
    public int MaxSettleRetries { get; init; } = 60;
}

/// <summary>
/// アプリを開いている間、監視フォルダを見張る（plan 49・ユーザ判断 2026-11-13）。設定で入れたときだけ主画面が作る。
///
/// - **出来事は「探し直すきっかけ」だけ。**新しいかは起動時と同じ <see cref="FolderWatch.FindNewAsync"/> で決め直す
///   （走査の控え・外した物・まだ取り込んでいない物。<c>docs/spec/import.md</c>）。あふれ・取りこぼしがあっても、次の探し直しで拾える。
/// - 続けて来た出来事は、最後の出来事から静けさ（<see cref="LiveFolderWatchTiming.Quiet"/>）がたつのを待って1回にまとめる。
/// - 書いている途中の物（ダウンロードの途中・大きさが動く・掴まれている）は渡さず、後で確かめ直す。
/// - <see cref="IsBusy"/>（取り込み中）の間は渡さず、<see cref="Kick"/> で探し直す（今の取り込みに黙って混ぜない）。
/// - あふれたら探し直し、壊れたら張り直す（<see cref="LiveFolderWatchTiming.Rearm"/> ごと）。張り直せたら探し直す。
///
/// 見つけた物は <see cref="Found"/> に渡すだけで、取り込むか・知らせるかは呼び手が決める。BOOTH には問い合わせない（取り込みは呼び手が梯子に積む）。
/// </summary>
public sealed class LiveFolderWatch : IDisposable
{
    private readonly Func<string, IFolderEvents?> _watchFolder;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<WatchResult>> _findNew;
    private readonly IFileSettleProbe _probe;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeProvider _clock;
    private readonly Func<string, bool> _folderPresent;
    private readonly LiveFolderWatchTiming _timing;
    private readonly object _lock = new();

    private Session? _session;

    /// <param name="watchFolder">フォルダの見張りを作る。作れなければ null（後で張り直す）。</param>
    /// <param name="findNew">新着を探す（<see cref="FolderWatch.FindNewAsync"/>）。</param>
    /// <param name="probe">書き終わったかを見る。</param>
    /// <param name="delay">待つ。試験は手で進める物に差し替える。</param>
    /// <param name="clock">
    /// 最後の出来事からの時間を測る時計。待ちと同じ時間で進む物を渡す（待ちだけ作り物にして時計を本物にすると、静けさの残りが減らず空回りする）。
    /// 省ける引数にしないのは、その食い違いを組み立ての時点で止めるため
    /// </param>
    /// <param name="folderPresent">張り直す前に、フォルダが在るかを見る。</param>
    public LiveFolderWatch(
        Func<string, IFolderEvents?> watchFolder,
        Func<IReadOnlyList<string>, CancellationToken, Task<WatchResult>> findNew,
        IFileSettleProbe probe,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeProvider clock,
        Func<string, bool> folderPresent,
        LiveFolderWatchTiming? timing = null)
    {
        _watchFolder = watchFolder;
        _findNew = findNew;
        _probe = probe;
        _delay = delay;
        _clock = clock;
        _folderPresent = folderPresent;
        _timing = timing ?? new LiveFolderWatchTiming();
    }

    /// <summary>書き終わった新着（新着の全部。書いている途中の物は入らない）。裏のスレッドから来る。</summary>
    public Func<LiveWatchBatch, Task>? Found { get; set; }

    /// <summary>
    /// 渡した物が今の見張りの物か（張り直す・止めると、前の見張りの物は今の物でない）。
    /// 画面のスレッドで照らす：張り直し・止めるも画面のスレッドから来るので、照らした後に見張りが替わることは無い
    /// </summary>
    public bool IsCurrent(LiveWatchBatch batch)
    {
        lock (_lock)
        {
            return _session is { } session && session.Generation == batch.Generation;
        }
    }

    /// <summary>取り込み中か。中なら渡さずに待ち、<see cref="Kick"/> で探し直す。</summary>
    public Func<bool> IsBusy { get; set; } = () => false;

    /// <summary>出来事を数えない場所（保存先。保存先が監視フォルダの中にあると、アプリが書くたびに探し直してしまう）。</summary>
    public string? IgnoredRoot { get; set; }

    /// <summary>見張っているか。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _session is not null;
            }
        }
    }

    /// <summary>今見張っているフォルダ（張り直しを待っている物も含む）。</summary>
    public IReadOnlyList<string> Folders
    {
        get
        {
            lock (_lock)
            {
                return _session?.Folders ?? [];
            }
        }
    }

    /// <summary>試験が待つ：今の見張りの回り（止めた後は済んだ物）。</summary>
    /// <summary>試験が見る：探し直しを終えた回数（渡した後まで済んだ数。「探し直しても出さない」を、探し直しが済んでから確かめる）。</summary>
    internal int Passes
    {
        get
        {
            lock (_lock)
            {
                return _session?.Passes ?? 0;
            }
        }
    }

    /// <summary>試験が見る：取り込み中で渡さずにいて、<see cref="Kick"/> を待っているか。</summary>
    internal bool IsDeferred
    {
        get
        {
            lock (_lock)
            {
                return _session?.IsDeferred ?? false;
            }
        }
    }

    /// <summary>試験が待つ：見張り始める前の新着を控え終えた（その後に置いた物が「現れた物」になる）。</summary>
    internal Task BaselineTaken
    {
        get
        {
            lock (_lock)
            {
                return _session?.BaselineTaken ?? Task.CompletedTask;
            }
        }
    }

    internal Task Running
    {
        get
        {
            lock (_lock)
            {
                return _session?.Loop ?? Task.CompletedTask;
            }
        }
    }

    /// <summary>
    /// 見張り始める（見張っていれば張り直す）。フォルダが無ければ止めるだけ。
    /// **画面のスレッドで待たない**：フォルダを開いて見張りを張るのは裏で行う（届かない共有・外れたドライブでは、張るだけで十数秒かかることがある）。
    /// 張り直し（見張っている間に監視フォルダを足し引きした）では、見張り始めの控えと、取り込み中で預けていた物を引き継ぐ
    /// （捨てると、預けた物・書いている途中だった物が新しい控えに入り、「現れた物」でなくなって自動の取り込みから黙って落ちる）。
    /// 控えを取り直すのは、前の見張りの範囲（控えを取り終えたフォルダとその中）に入っていなかったフォルダの分だけ。
    /// 張り直したら、控えを取り終えた後に1回探し直す（取り込み中なら終わってから）。前の見張りが静けさ・書き終わりの確かめ・確かめ直しを
    /// 待っていた物や、画面へ預けて捨てられた結果は、止めると失われ、新しい見張りは次の出来事まで探さないため（ユーザ判断 2026-11-13「A」）。
    /// 最初に張るときは探さない（起動時の確かめと重ねない）
    /// </summary>
    public void Start(IReadOnlyList<string> folders)
    {
        Session? previous;
        lock (_lock)
        {
            previous = _session;
            _session = null;
        }

        previous?.End();
        var rescan = previous is not null;
        var unique = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        long generation;
        List<string> fresh;
        List<string> covered;
        lock (_baselineLock)
        {
            // 世代を進めるのは控えと同じ錠の中：止めた見張りの遅れた控えが、ここより後に混ざらないように（W5）
            generation = ++_generation;
            if (previous is null)
            {
                _baseline.Clear();
                _baselineFolders.Clear();
            }

            // 親を見張っている間に足した子は、もう範囲の中（控えも取ってある）。完全一致で決めると子を新しく扱い、
            // 取り込み中で預けていた子の中の新着を控えに入れて「前からあった物」にしてしまう（W6）
            var before = _baselineFolders.ToList();
            fresh = unique.Where(folder => !before.Any(done => Covers(done, folder))).ToList();

            // 範囲は今も見張っている所だけ残す。外した親を残すと、後で足した子の前からの物まで範囲の中と見て「現れた物」にしてしまう
            covered = [
                .. before.Where(done => unique.Any(folder => Covers(folder, done))),
                .. unique.Where(folder => !fresh.Contains(folder, StringComparer.OrdinalIgnoreCase) && !before.Contains(folder, StringComparer.OrdinalIgnoreCase)),
            ];
            _baselineFolders.Clear();
            _baselineFolders.UnionWith(covered);
        }

        if (unique.Count == 0)
        {
            return;
        }

        var session = new Session(this, unique, fresh, covered, generation, rescan);
        lock (_lock)
        {
            _session = session;
        }

        session.Begin();
    }

    /// <summary>見張りを止める。止めた後に届く出来事は捨てる（見張りごとの中断の印で見分ける）。次に始めるときは控えを取り直す。</summary>
    public void Stop()
    {
        Session? session;
        lock (_lock)
        {
            session = _session;
            _session = null;
        }

        session?.End();
        lock (_baselineLock)
        {
            ++_generation;
            _baseline.Clear();
            _baselineFolders.Clear();
        }
    }

    private readonly object _baselineLock = new();

    /// <summary>見張り始めたときに在った新着の指紋。張り直しをまたいで持つ。</summary>
    private readonly HashSet<string> _baseline = new(StringComparer.Ordinal);

    /// <summary>控えを取り終えたフォルダ（今の見張りの範囲。中の子も含む）。</summary>
    private readonly HashSet<string> _baselineFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>見張りの世代。始める・止めるたびに進む（_baselineLock の中で読み書きする）。</summary>
    private long _generation;

    /// <summary>そのフォルダか、その中か（見張りも走査も中のフォルダまで見る）。</summary>
    private static bool Covers(string folder, string path)
        => Services.PathText.Same(folder, path) || Storage.StoreIds.IsInside(path, folder);

    /// <summary>
    /// 控えを足す。止めた・張り直した前の見張りの読み取りが遅れて終わった物は捨てる
    /// （所有者共通の控えに混ざると、張り直した見張りの新着が「前からあった物」になって自動の取り込みから落ちる。W5）。
    /// 捨てても、張り直した見張りはそのフォルダを控えを取っていない物として取り直す
    /// </summary>
    private void AddBaseline(long generation, IEnumerable<string> folders, IEnumerable<string> fingerprints)
    {
        lock (_baselineLock)
        {
            if (generation != _generation)
            {
                return;
            }

            _baselineFolders.UnionWith(folders);
            _baseline.UnionWith(fingerprints);
        }
    }

    private bool InBaseline(string fingerprint)
    {
        lock (_baselineLock)
        {
            return _baseline.Contains(fingerprint);
        }
    }

    /// <summary>
    /// 取り込みが終わった。取り込み中に見つけて渡さなかった物があれば探し直す
    /// （その間に取り込まれた物は新しくなくなっているので、渡した一覧を使い回さない）。
    /// </summary>
    public void Kick()
    {
        Session? session;
        lock (_lock)
        {
            session = _session;
        }

        session?.KickIfDeferred();
    }

    /// <summary>
    /// 渡した物を呼び手が受け取れなかった（取り込みが始まっていた・自動の取り込みを積めなかった）。取り込み中なら次の <see cref="Kick"/> で、
    /// 中でなければすぐ探し直す
    /// </summary>
    public void Defer()
    {
        Session? session;
        lock (_lock)
        {
            session = _session;
        }

        session?.MarkDeferred();
    }

    public void Dispose() => Stop();

    /// <summary>
    /// 書き終わった物だけを返す（見張りが渡す前と同じ確かめ：大きさと更新日時を <see cref="LiveFolderWatchTiming.Settle"/> を空けて2回見る）。
    /// 帯の［取り込む］は押した時に探し直すので、帯を出した後に置かれ始めた物（コピーの途中）が混ざる。それを取り込みに渡さない（W1）。
    /// 途中の物・消えた物は返さない
    /// </summary>
    public async Task<IReadOnlyList<string>> SettledAsync(IReadOnlyList<string> paths, CancellationToken token = default)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        var (settled, _) = await SplitSettledAsync(paths, token).ConfigureAwait(false);
        return [.. settled.Select(file => file.Path)];
    }

    /// <summary>書き終わった物と、書いている途中の物の数に分ける（消えた物はどちらにも入れない）。</summary>
    private async Task<(List<(string Path, FileSettle Settle)> Settled, int Unsettled)> SplitSettledAsync(IReadOnlyList<string> paths, CancellationToken token)
    {
        var first = paths.Select(path => (Path: path, Settle: _probe.Probe(path))).ToList();
        await _delay(_timing.Settle, token).ConfigureAwait(false);

        var settled = new List<(string Path, FileSettle Settle)>();
        var unsettled = 0;
        foreach (var (path, before) in first)
        {
            var after = _probe.Probe(path);
            if (after is null)
            {
                continue;
            }

            if (before is { } b && b.SizeBytes == after.Value.SizeBytes && b.ModifiedAtUtc == after.Value.ModifiedAtUtc && after.Value.Ready)
            {
                settled.Add((path, after.Value));
            }
            else
            {
                unsettled++;
            }
        }

        return (settled, unsettled);
    }

    internal static string FingerprintOf(string path, FileSettle settle)
        => $"{path.ToUpperInvariant()}|{settle.SizeBytes}|{settle.ModifiedAtUtc.Ticks}";

    /// <summary>1回の見張り（Start から Stop まで）。止めたら捨て、次は作り直す。</summary>
    /// <param name="fresh">見張り始めの控えをまだ取っていないフォルダ（初めて張る・張り直しで足した）。</param>
    /// <param name="covered">前の見張りから引き継いだ範囲（控えを取り終えた所）。足したフォルダの控えから、この中の物は除く。</param>
    /// <param name="generation">この見張りの世代（控えを足す時と、渡した物を画面が受け取る時に照らす）。</param>
    /// <param name="rescan">張り直しか（控えを取った後に1回探し直す。取り込み中なら終わってから）。</param>
    private sealed class Session(LiveFolderWatch owner, List<string> folders, List<string> fresh, List<string> covered, long generation, bool rescan)
    {
        public long Generation => generation;

        private readonly CancellationTokenSource _cancel = new();
        private readonly SemaphoreSlim _signal = new(0);
        private readonly object _gate = new();
        private readonly Dictionary<string, IFolderEvents> _events = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _broken = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>最後に探し直しを頼まれた時刻（<see cref="TimeProvider.GetTimestamp"/>）。静けさはここから数える。</summary>
        private long _lastNudge;
        private bool _deferred;
        private int _settleRetries;
        private Task? _rearm;

        public IReadOnlyList<string> Folders => folders;

        public Task Loop { get; private set; } = Task.CompletedTask;

        private readonly TaskCompletionSource _baselineTaken = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task BaselineTaken => _baselineTaken.Task;

        public void Begin() => Loop = Task.Run(() => RunAsync(_cancel.Token));

        /// <summary>止める。預けていた物は引き継がない（張り直した見張りが、いつも1回探し直す）。</summary>
        public void End()
        {
            _cancel.Cancel();
            lock (_gate)
            {
                foreach (var events in _events.Values)
                {
                    events.Dispose();
                }

                _events.Clear();
                _broken.Clear();
            }
        }

        /// <summary>見張りを張る（裏で）。在るかは打ち切り付きで先に見る（届かない共有で見張りを作ると長く止まる）。無ければ張り直しを待つ。</summary>
        private void ArmAll(CancellationToken token)
        {
            foreach (var folder in folders)
            {
                token.ThrowIfCancellationRequested();
                if (owner._folderPresent(folder))
                {
                    Arm(folder);
                }
                else
                {
                    lock (_gate)
                    {
                        _broken.Add(folder);
                        EnsureRearm();
                    }
                }
            }
        }

        public void KickIfDeferred()
        {
            bool deferred;
            lock (_gate)
            {
                deferred = _deferred;
                _deferred = false;
            }

            if (deferred)
            {
                Nudge();
            }
        }

        private int _passes;

        public int Passes => Volatile.Read(ref _passes);

        public bool IsDeferred
        {
            get
            {
                lock (_gate)
                {
                    return _deferred;
                }
            }
        }

        /// <summary>
        /// 取り込み中なので預ける。印を立てた後に取り込み中かを見直し、終わっていれば探し直しを予約する。
        /// 「取り込み中」と読んでから印を立てるまでに取り込みが終わると、終わりの知らせ（<see cref="Kick"/>）は印の無いうちに来て何もせず、
        /// 次の出来事まで放っておかれる（W7）。取り込みは「終わった」を立ててから知らせるので、見直して中なら知らせはこの後に来る。
        /// 知らせと見直しの両方が来ても、印を下ろす方が1つなので探し直しは1回
        /// </summary>
        public void MarkDeferred()
        {
            lock (_gate)
            {
                _deferred = true;
            }

            if (!owner.IsBusy())
            {
                KickIfDeferred();
            }
        }

        private void Arm(string folder)
        {
            IFolderEvents? events;
            try
            {
                events = owner._watchFolder(folder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                events = null;
            }

            lock (_gate)
            {
                if (_cancel.IsCancellationRequested)
                {
                    events?.Dispose();
                    return;
                }

                if (events is null)
                {
                    _broken.Add(folder);
                    EnsureRearm();
                    return;
                }

                _broken.Remove(folder);
                _events[folder] = events;
            }

            events.Changed += path => OnChanged(path);
            events.Failed += failure => OnFailed(folder, events, failure);
        }

        private void OnChanged(string path)
        {
            if (_cancel.IsCancellationRequested || FileSettleProbe.IsPartialName(path) || IsIgnored(path))
            {
                return;
            }

            // 足跡は置いてから帯までの内訳を見るため（plan 50）。ファイルの名前は書かない（利用者のファイル名をログに残さない）
            Services.UiTrace.Write("監視", "出来事を受けた");
            Nudge();
        }

        private bool IsIgnored(string path)
            => owner.IgnoredRoot is { Length: > 0 } root && (Services.PathText.Same(path, root) || Storage.StoreIds.IsInside(path, root));

        private void OnFailed(string folder, IFolderEvents events, FolderEventsFailure failure)
        {
            if (_cancel.IsCancellationRequested)
            {
                return;
            }

            if (failure == FolderEventsFailure.Broken)
            {
                Diagnostics.AppLog.Warn("監視フォルダの見張り", "見張りが止まったので、張り直しを待つ");
                lock (_gate)
                {
                    if (_events.TryGetValue(folder, out var current) && ReferenceEquals(current, events))
                    {
                        _events.Remove(folder);
                    }

                    _broken.Add(folder);
                    EnsureRearm();
                }

                events.Dispose();
            }

            // あふれた（出来事を落とした）か、止まる前に何かが置かれたかもしれない。どちらも探し直す
            Nudge();
        }

        /// <summary>
        /// 探し直しを頼む。時刻を打ってから起こす（起きた回りが、この頼みの時刻を読めるように）。
        /// 静けさの待ちの中で来たら、この時刻から静けさの分まで待ちが延びる。
        /// 出来事のほか、書き終わりの確かめ直し・取り込みの後・張り直しの後もここを通る（確かめ直しも静けさを空けて回す。
        /// <see cref="LiveFolderWatchTiming.MaxSettleRetries"/> の「5秒ごと」はこれが前提）
        /// </summary>
        private void Nudge()
        {
            // 複数のスレッド（見張りの出来事・張り直し・画面）から来るので、時刻を後ろへ戻さない
            var now = owner._clock.GetTimestamp();
            var seen = Volatile.Read(ref _lastNudge);
            while (now > seen)
            {
                var previous = Interlocked.CompareExchange(ref _lastNudge, now, seen);
                if (previous == seen)
                {
                    break;
                }

                seen = previous;
            }

            try
            {
                _signal.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>張り直しの回りを1本だけ走らせる（_gate の中で呼ぶ）。</summary>
        private void EnsureRearm()
        {
            if (_rearm is { IsCompleted: false } || _cancel.IsCancellationRequested)
            {
                return;
            }

            _rearm = Task.Run(() => RearmAsync(_cancel.Token));
        }

        private async Task RearmAsync(CancellationToken token)
        {
            try
            {
                while (true)
                {
                    await owner._delay(owner._timing.Rearm, token).ConfigureAwait(false);

                    List<string> broken;
                    lock (_gate)
                    {
                        broken = [.. _broken];
                    }

                    if (broken.Count == 0)
                    {
                        return;
                    }

                    foreach (var folder in broken)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!owner._folderPresent(folder))
                        {
                            continue;
                        }

                        Arm(folder);
                        bool armed;
                        lock (_gate)
                        {
                            armed = !_broken.Contains(folder);
                        }

                        if (armed)
                        {
                            // 張っていない間に置かれた物を拾う
                            Nudge();
                        }
                    }

                    lock (_gate)
                    {
                        if (_broken.Count == 0)
                        {
                            _rearm = null;
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                // 張り直したら1回探し直す（前の見張りが預けていた物・確かめを待っていた物を拾う。Start の説明）。
                // 取り込み中なら終わりを待つ。探すのは控えを取った後（印と頼みはここで付けても、回りが待ち始めるのは控えの後）
                if (rescan)
                {
                    MarkDeferred();
                }

                ArmAll(token);
                await TakeBaselineAsync(token).ConfigureAwait(false);

                while (true)
                {
                    await _signal.WaitAsync(token).ConfigureAwait(false);

                    // 最後の頼みから静けさがたつまで待つ。待つ間に出来事が来たら、その出来事からの残りだけ待ち直す。
                    // 「待ち始めから静けさの分」を数え直す形にしない：1つのファイルのコピーでも、作成・書き込み・変更の出来事が
                    // 同じ瞬間に続けて来るので、1つ目で起きた後に届いた残りが待ちを丸ごと延ばし、静けさが実際は倍の10秒になっていた
                    // （plan 50。実機の足跡で、1つ置いて出来事3つ→静けさの終わりまで +10.3s、5つ置いて出来事11→+10.0s）
                    while (true)
                    {
                        var remaining = owner._timing.Quiet - owner._clock.GetElapsedTime(Volatile.Read(ref _lastNudge));
                        if (remaining <= TimeSpan.Zero)
                        {
                            break;
                        }

                        await owner._delay(remaining, token).ConfigureAwait(false);
                    }

                    Services.UiTrace.Write("監視", "静けさが終わった");

                    // 待つ間に溜まった頼みは、これから探す1回に含まれる
                    while (_signal.CurrentCount > 0 && _signal.Wait(0))
                    {
                    }

                    try
                    {
                        await PassAsync(token).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // 探せなくても見張りは続ける。次の出来事でまた探す
                        Diagnostics.AppLog.Error("監視フォルダの新着を探す", exception);
                    }
                    finally
                    {
                        Interlocked.Increment(ref _passes);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _baselineTaken.TrySetResult();
                _signal.Dispose();
            }
        }

        /// <summary>
        /// 見張り始める前から在った新着を控える。呼び手は「見張り始めた後に現れた物」だけを自動で取り込む
        /// （前から置いてあって取り込まずにいた物まで、関係の無いファイルが1つ置かれたのをきっかけに取り込まない）。
        /// 前からの物は起動時の確かめ・取り込み画面の「新しいファイルを取り込む」で今までどおり扱う
        /// </summary>
        private async Task TakeBaselineAsync(CancellationToken token)
        {
            try
            {
                if (fresh.Count == 0)
                {
                    return;
                }

                var result = await owner._findNew(fresh, token).ConfigureAwait(false);
                var fingerprints = new List<string>();
                foreach (var path in result.NewFiles)
                {
                    // 子を見張っている間に親を足した：子の中は前の見張りの控えのまま（預けていた新着を「前からあった物」にしない。W6 の逆の向き）
                    if (covered.Any(done => Covers(done, path)))
                    {
                        continue;
                    }

                    if (owner._probe.Probe(path) is { } settle)
                    {
                        fingerprints.Add(FingerprintOf(path, settle));
                    }
                }

                owner.AddBaseline(generation, fresh, fingerprints);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Diagnostics.AppLog.Error("監視フォルダの見張り：始める前の新着を控える", exception);
            }
            finally
            {
                _baselineTaken.TrySetResult();
            }
        }

        private async Task PassAsync(CancellationToken token)
        {
            if (owner.IsBusy())
            {
                MarkDeferred();
                return;
            }

            var findWatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await owner._findNew(folders, token).ConfigureAwait(false);
            Services.UiTrace.Write("監視", $"新着を探した：{result.NewFiles.Count} 件（{findWatch.ElapsedMilliseconds} ms）");
            if (result.NewFiles.Count == 0)
            {
                _settleRetries = 0;
                await DeliverAsync([]).ConfigureAwait(false);
                return;
            }

            var (done, unsettled) = await owner.SplitSettledAsync(result.NewFiles, token).ConfigureAwait(false);
            var settled = new List<LiveWatchFile>();
            foreach (var (path, settle) in done)
            {
                var fingerprint = FingerprintOf(path, settle);
                settled.Add(new LiveWatchFile(path, fingerprint, Arrived: !owner.InBaseline(fingerprint)));
            }

            Services.UiTrace.Write("監視", $"書き終わりを確かめた：落ち着いた {settled.Count} 件・途中 {unsettled} 件");
            if (unsettled > 0 && _settleRetries < owner._timing.MaxSettleRetries)
            {
                // 書いている途中の物は、出来事が来なくても（一覧の大きさが閉じるまで変わらない書き方がある）確かめ直す
                _settleRetries++;
                Nudge();
            }
            else if (unsettled == 0)
            {
                _settleRetries = 0;
            }

            if (owner.IsBusy())
            {
                MarkDeferred();
                return;
            }

            await DeliverAsync(settled).ConfigureAwait(false);
        }

        private async Task DeliverAsync(IReadOnlyList<LiveWatchFile> files)
        {
            if (_cancel.IsCancellationRequested || owner.Found is not { } found)
            {
                return;
            }

            await found(new LiveWatchBatch(files, generation)).ConfigureAwait(false);
        }
    }
}
