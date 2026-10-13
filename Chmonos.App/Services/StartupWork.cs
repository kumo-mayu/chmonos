using System.IO;

namespace Chmonos.App.Services;

/// <summary>
/// 起動したときの裏の作業（③の検出し直し・通信しない確かめ・⑤残りの画像・持っていないアバターの画像・⑦期限・見つからない物の見回り・控えの片付け）。
/// **画面に依存しない**：画面が無い起動（<c>--headless</c>）でも同じ作業を同じ順で回すため、主画面の ViewModel から外した
/// （docs/history/headless-core-2026-10-13.md 段1a。中身と順は主画面にあったときと同じ）。
/// 画面へ返すのは合図だけ（進み具合・読み直し・件数の数え直し・見回りで書いた商品）。画面が居なければ誰も受けない。
/// 合図は裏のスレッドから来る。受ける側が画面のスレッドへ渡す。
///
/// ③ 対応アバターの検出し直し。前に全体を検出してから設定の日数が過ぎていたときだけ（AvatarService.IsRedetectDue）。
///
/// ⑤ 前の取り込みで取り切れなかった画像を取り直す。
///    対象は手元のJSONだけで決まる（<c>Booth.Images</c> の件数とディスクの差）ので、
///    フォルダの走査は起きない。**起動時に黙ってドライブを舐めに行くのとは質が違う。**
///
/// ⑦ 期限の来た商品を取り直す。梯子のいちばん下で、急ぐ理由が無い唯一の段。
///
/// **⑤を先にするのは、見た目の穴の方が先に目に入るから。**
/// どちらも取り込みが始まれば優先順位で自然に譲るので、待たせる必要はない。
///
/// 失敗は画面に出さない。ユーザが頼んだ作業ではないので、
/// 邪魔をしてまで知らせる価値がない（どちらも次の起動でまた試す）。**ログには残す。**
/// **段ごとに受け止める。**前は1つの try でつないでいて、⑤で落ちると⑦まで何も言わずに止まっていた（技術的負債 2-3）。
/// </summary>
public sealed class StartupWork(AppServiceContainer services)
{
    private CancellationTokenSource? _backlog;

    /// <summary>走っている裏の作業。終わるまで次を始めない（同じ段を2本走らせると、同じ画像を2回取りに行く）。</summary>
    private Task? _backlogRun;

    /// <summary>進み具合（「何を n/N」）。常設の1行に出す（ユーザ指示）。前は通信の様子（間隔を空けています）しか出ず、何をしているのか読めなかった。</summary>
    public event Action<string, int, int>? Progress;

    /// <summary>1段が終わった（進み具合の1行を下ろす）。</summary>
    public event Action? StageEnded;

    /// <summary>商品をまとめて書き換えた。検索の絞り込みやカードを組み直す。</summary>
    public event Action? LibraryChanged;

    /// <summary>要確認などの件数が変わり得る。</summary>
    public event Action? CountsChanged;

    /// <summary>見回りが見つからなくなった日時を書いた商品。</summary>
    public event Action<IReadOnlyList<string>>? ItemsSwept;

    /// <summary>
    /// 作業が1つ終わった（段・見回り・アバターの画像。成否に関わらず）。画面の無い起動が、静かになってから片付けを走らせる合図にする
    /// </summary>
    public event Action? WorkEnded;

    /// <summary>
    /// 起動したときの作業を始める。設定「起動したとき、裏で取得を始める」が止めるのは BOOTH へ行く段だけ（background-and-network.md）。
    /// 前は設定を切ると丸ごと戻り、通知の上限・ページの作り・手で直した JSON の確認まで、設定を切った人には一度も走らなかった
    /// </summary>
    /// <returns>
    /// 始めたか。**プロセスで1回だけ**始める：画面の無い起動の後に窓を開くと主画面がもう一度呼ぶが、起動したときの作業は済んでいる
    /// （呼んだ側は、起動時だけの作業〔自動の取り込み〕もしない）
    /// </returns>
    public bool Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return false;
        }

        StartChecksAndPrunes();
        CheckUnreadableItems();
        ResumePendingOperations();
        return true;
    }

    private int _started;

    private void StartChecksAndPrunes()
    {
        if (services.Settings.ResumeFetchInBackground)
        {
            StartBacklog(force: false);
        }
        else
        {
            var token = Token();
            Task.Run(async () =>
            {
                try
                {
                    await RunLocalChecksAsync(token);
                }
                catch (OperationCanceledException)
                {
                    // 閉じたときに止めた
                }
            }, token).Forget();
        }

        PruneVideoTitles();
        PruneRecent();
    }

    /// <summary>
    /// 取り込み画面の「足りない情報を取得」から、起動時と同じ裏の作業を今始める（ユーザ判断 2026-09-28）。
    /// 設定で裏の取得を切っていても始める——人が頼んだので。
    /// 優先度は起動時と同じ低い段のまま：量が多い取得で、取り込みや商品ページで押した取り直しを先に通したいため
    /// （CLAUDE.md の「UiCommand を通さない例外」にこのボタンも入る）。
    /// </summary>
    /// <returns>始めたか。既に走っていれば始めない。</returns>
    public bool StartNow() => StartBacklog(force: true);

    /// <summary>閉じるときに裏の作業を止める。</summary>
    public void Stop() => _backlog?.Cancel();

    private CancellationToken Token() => (_backlog ??= new CancellationTokenSource()).Token;

    private bool StartBacklog(bool force)
    {
        if (!force && !services.Settings.ResumeFetchInBackground)
        {
            return false;
        }

        if (_backlogRun is { IsCompleted: false })
        {
            return false;
        }

        _backlog = new CancellationTokenSource();
        var token = _backlog.Token;

        IProgress<(int Done, int Total)> ReportAs(string label)
            => new SyncProgress<(int Done, int Total)>(report => Progress?.Invoke(label, report.Done, report.Total));

        var images = ReportAs("画像を取得中");
        var avatars = ReportAs("アバターの画像を取得中");
        var due = ReportAs("商品の更新を確認中");
        var detection = new SyncProgress<Core.Services.AvatarDetectProgress>(
            report => Progress?.Invoke("対応アバターを検出中", report.Done, report.Total));

        _backlogRun = Task.Run(async () =>
        {
            try
            {
                // 対応アバターの検出し直し。梯子では③なので画像より先。手元の照合がほとんどで、問い合わせは知らないIDの分だけ
                if (services.Avatars.IsRedetectDue())
                {
                    await RunStageAsync("対応アバターの検出し直し", async () =>
                    {
                        using var priority = Core.Booth.BoothClient.Prioritize(Core.Booth.BoothPriority.Detection);
                        var result = await services.Avatars.DetectAsync(detection, token);

                        // 検索の絞り込みやカードは読み込んだ時の対応アバターで組んである。書き換えた商品があれば組み直す
                        if (result.ItemsUpdated > 0)
                        {
                            LibraryChanged?.Invoke();
                        }
                    });
                }

                await RunLocalChecksAsync(token);

                await RunStageAsync("前の取り込みで残った画像", () => services.Backlog.ResumeAsync(images, token));

                // 持っていないアバターの1枚目（U18）。商品の画像の穴の方が先に目に入るので⑤の後
                await RunStageAsync("持っていないアバターの画像", () => services.AvatarImages.SyncAsync(avatars, token));

                await RunStageAsync("期限の来た商品の取り直し", () => services.Due.RunAsync(due, token));

                // ⑦で商品ページが変わっていれば要確認が増える。件数を出し直す
                CountsChanged?.Invoke();
            }
            catch (OperationCanceledException)
            {
                // 閉じたときに止めた
            }
        }, token);
        _backlogRun.Forget();
        return true;
    }

    /// <summary>起動時の裏の作業のうち、通信しない確かめ。設定「起動したとき、裏で取得を始める」に関わらず走る。</summary>
    private async Task RunLocalChecksAsync(CancellationToken token)
    {
        // 足すときは上限を見ていないので、ここで1回だけ落とす（ユーザ判断 2026-09-18）。
        // どちらも要確認の一覧を**書く**ので、他の段と同じく門と受け止めを通す——
        // 直に await していたため、ここで転ぶと残りの段（残った画像・アバターの画像・期限）が丸ごと走らなかった
        await RunStageAsync("通知の整理", () => services.Notifications.PruneAsync(token));

        // BOOTH側の作りが変わっていないか（説明文の見出しが読めているか）を見て、ナビの帯を出し入れする
        await RunStageAsync("ページの作りの確認", () => services.Notifications.DetectPageStructureAsync(token));

        // 手で直した JSON の食い違い（同じ名前が2つ・商品IDとファイル名が違う）。
        // 通信はしないので、裏の取得を切っていても見る（J2・L6）
        await RunStageAsync("手で直したJSONの確認", () => services.Notifications.DetectHandEditIssuesAsync(token));

        CountsChanged?.Invoke();
    }

    /// <summary>
    /// 起動したときに裏で一度、記録しているファイルとフォルダの場所を全部見て、見つからなくなった日時を付け外しする（ユーザ判断 2026-10-05）。
    /// **窓を出した後に呼ぶ**（全件を読み、ディスクを見るので、窓が出るまでの待ちに乗せない）。
    /// </summary>
    /// <remarks>
    /// 取り込みと使おうとした画面でしか書いていなかったので、取り込まずに使っている間は、手で消した zip が
    /// カードの印・検索の条件「見つからないファイル」・統計に出なかった。決まりは取り込みの見回りと同じ（<see cref="Core.Services.MissingMarksSweep"/>）。
    /// BOOTH に問い合わせないので、設定「起動したとき、裏で取得を始める」を切っていても見る（通知の整理・手で直した JSON の確認と同じ）。
    /// 取り込みが同時に走っても、見回りは1本ずつ回り、錠の中で今の値と同じなら書かない。
    /// </remarks>
    public void StartMissingMarksSweep()
    {
        var token = Token();
        Task.Run(async () =>
        {
            try
            {
                // 裏の作業は UiCommand を通らないので、保存先を運ぶ間の門はここで待つ（RunStageAsync と同じ）
                await Core.Storage.StoreWriteGate.WaitAsync(token);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var written = await services.MissingMarks.SweepAsync(token);
                Core.Services.UiTrace.Write("速さ", $"起動時の見回り：見つからなくなった日時を {written.Count} 件の商品に書いた（{watch.ElapsedMilliseconds} ms）");
                if (written.Count > 0)
                {
                    ItemsSwept?.Invoke(written);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 見られなくても起動は妨げない。次の起動か取り込みでまた見る
                Core.Diagnostics.AppLog.Error("起動時の裏の作業：見つからないファイルの見回り", exception);
            }
            finally
            {
                WorkEnded?.Invoke();
            }
        }, token).Forget();
    }

    /// <summary>
    /// 取り込みの後、新しく見つかったアバターの1枚目を続けて取る（ユーザ判断 2026-09-12）。
    /// 以前は次の起動の裏の取得まで待っていたので、取り込んだ直後の一覧や候補は頭文字のままだった。
    /// 検出のときにJSONを取っているので1枚目のURLは分かっており、ここで増えるのは画像の取得だけ。
    /// 起動時の裏の取得と重なっても、<see cref="Core.Services.AvatarImageSync"/> が1本ずつ回すので二重には取らない。
    /// </summary>
    public void StartAvatarImageSync()
    {
        var token = Token();
        IProgress<(int Done, int Total)> progress = new SyncProgress<(int Done, int Total)>(
            report => Progress?.Invoke("アバターの画像を取得中", report.Done, report.Total));

        Task.Run(async () =>
        {
            try
            {
                // 登録簿を書くので、保存先を運んでいる間は待つ（他の裏の段と同じ門）
                await Core.Storage.StoreWriteGate.WaitAsync(token);
                await services.AvatarImages.SyncAsync(progress, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 取れなくても次の起動でまた試す。頼まれた作業ではないので邪魔をしない
                Core.Diagnostics.AppLog.Error("取り込みの後のアバターの画像", exception);
            }
            finally
            {
                StageEnded?.Invoke();
                WorkEnded?.Invoke();
            }
        }, token).Forget();
    }

    /// <summary>
    /// 30日を過ぎた動画のタイトルの控えを消す（YouTube の開発者ポリシー：API で取ったデータは30日を過ぎたら取り直すか消す）。
    /// 動画の欄を開いたときの取り直しだけでは、開かれないまま残る控えが30日を越える。
    /// 通信しないので、裏の取得を設定で切っていても行う。
    /// </summary>
    private void PruneVideoTitles()
        => Task.Run(async () =>
        {
            try
            {
                await services.Commands.ExecuteAsync(new Core.Commands.UiCommand.PruneVideoTitles());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Core.Diagnostics.AppLog.Error("起動時の裏の作業：動画のタイトルの控えの整理", exception);
            }
        }).Forget();

    /// <summary>
    /// 起動したときに、読めない商品の記録を数えて通知に出す（ユーザ判断 2026-10-05）。
    ///
    /// 読めない記録は全件の読み込みが飛ばすので、その商品は黙って検索から消えていた。
    /// BOOTH に問い合わせないので、設定「起動したとき、裏で取得を始める」を切っていても見る
    /// （その設定で止まる裏の作業の段には入れない）。全件の読み込みは写しを使うので、検索の読み込みと重ねても軽い。
    /// </summary>
    private void CheckUnreadableItems()
        => Task.Run(async () =>
        {
            try
            {
                var result = await services.Commands.ExecuteAsync(new Core.Commands.UiCommand.DetectUnreadableItems());
                if (result is Core.Commands.CommandResult.Counted { Count: > 0 })
                {
                    CountsChanged?.Invoke();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Core.Diagnostics.AppLog.Error("起動時の裏の作業：読めない商品の記録の確認", exception);
            }
        }).Forget();

    /// <summary>
    /// 起動したときに、前の起動で途中で止まった操作（IDの変更・タグや属性の名前の変更）の続きを済ませる（ユーザ判断 2026-10-06「A」）。
    ///
    /// 途中で止まると、一部の商品や参照だけが新しい名前（ID）を指したまま残る。人が押した操作の続きで、BOOTHへは問い合わせないので、
    /// 設定「起動したとき、裏で取得を始める」を切っていても行う（その設定で止まる裏の作業の段には入れない）。
    /// 何か済ませたら、検索などが読み込んだ時の名前で組んであるので組み直す。続けられなかった物は通知に出る。
    /// </summary>
    private void ResumePendingOperations()
        => Task.Run(async () =>
        {
            try
            {
                var result = await services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ResumePendingOperations());
                if (result is Core.Commands.CommandResult.Counted { Count: > 0 })
                {
                    CountsChanged?.Invoke();
                    LibraryChanged?.Invoke();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Core.Diagnostics.AppLog.Error("起動時の裏の作業：途中で止まった操作の続き", exception);
            }
        }).Forget();

    /// <summary>手元に無くなった商品の「最近」の足跡を落とす（<see cref="RecentTracker.PruneMissingItemsAsync"/>）。通信しない。</summary>
    private void PruneRecent()
        => Task.Run(async () =>
        {
            try
            {
                var dropped = await services.Recent.PruneMissingItemsAsync();
                Core.Services.UiTrace.Write("速さ", $"起動時の片付け：無い商品の足跡 {dropped} 行を消した");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Core.Diagnostics.AppLog.Error("起動時の裏の作業：最近の足跡の整理", exception);
            }
        }).Forget();

    /// <summary>裏の作業の1段。落ちてもログに残して次の段へ進む。止まるのは閉じたとき（取り消し）だけ。</summary>
    private async Task RunStageAsync(string name, Func<Task> stage)
    {
        try
        {
            // 裏の作業は `UiCommand` を通らないので、門はここで通す（E8）。
            // 保存先を運んでいる間に書き込むと、コピー済みへ書いた分が元を消すときに失われる
            await Core.Storage.StoreWriteGate.WaitAsync(_backlog?.Token ?? CancellationToken.None);
            await stage();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Core.Diagnostics.AppLog.Error($"起動時の裏の作業：{name}", exception);
        }
        finally
        {
            StageEnded?.Invoke();
            WorkEnded?.Invoke();
        }
    }

    /// <summary>
    /// 知らせをその場で渡す <see cref="IProgress{T}"/>。<see cref="Progress{T}"/> は作ったスレッドの文脈へ投げるので、
    /// 画面の無い起動では受け先が無い。画面のスレッドへ渡すのは受ける側（常設の1行は自分で渡す）
    /// </summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
