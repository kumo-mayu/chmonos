using System.IO;
using Chmonos.App.Services;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>主画面：起動時の裏の作業と監視フォルダ（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// 使っていない間に進める作業を背景で走らせる。
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
    private void StartBacklogResume()
    {
        if (_services.Settings.ResumeFetchInBackground)
        {
            StartBacklog(force: false);
            return;
        }

        // 設定が止めるのは BOOTH へ行く段だけ（background-and-network.md）。前はここで丸ごと戻り、
        // 通知の上限・ページの作り・手で直した JSON の確認まで、設定を切った人には一度も走らなかった
        var token = (_backlog ??= new CancellationTokenSource()).Token;
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

    /// <summary>
    /// 起動時の裏の作業のうち、通信しない確かめ。設定「起動したとき、裏で取得を始める」に関わらず走る。
    /// </summary>
    private async Task RunLocalChecksAsync(CancellationToken token)
    {
        // 足すときは上限を見ていないので、ここで1回だけ落とす（ユーザ判断 2026-09-18）。
        // どちらも要確認の一覧を**書く**ので、他の段と同じく門と受け止めを通す——
        // 直に await していたため、ここで転ぶと残りの段（残った画像・アバターの画像・期限）が丸ごと走らなかった
        await RunBackgroundStageAsync("通知の整理", () => _services.Notifications.PruneAsync(token));

        // BOOTH側の作りが変わっていないか（説明文の見出しが読めているか）を見て、ナビの帯を出し入れする
        await RunBackgroundStageAsync(
            "ページの作りの確認",
            () => _services.Notifications.DetectPageStructureAsync(token));

        // 手で直した JSON の食い違い（同じ名前が2つ・商品IDとファイル名が違う）。
        // 通信はしないので、裏の取得を切っていても見る（J2・L6）
        await RunBackgroundStageAsync(
            "手で直したJSONの確認",
            () => _services.Notifications.DetectHandEditIssuesAsync(token));

        RunOnUiThread(RefreshCounts);
    }

    /// <summary>走っている裏の作業。終わるまで次を始めない（同じ段を2本走らせると、同じ画像を2回取りに行く）。</summary>
    private Task? _backlogRun;

    /// <summary>
    /// 取り込み画面の「足りない情報を取得」から、起動時と同じ裏の作業を今始める（ユーザ判断 2026-09-28）。
    /// 設定で裏の取得を切っていても始める——人が頼んだので。
    /// 優先度は起動時と同じ低い段のまま：量が多い取得で、取り込みや商品ページで押した取り直しを先に通したいため
    /// （CLAUDE.md の「UiCommand を通さない例外」にこのボタンも入る）。
    /// </summary>
    /// <returns>始めたか。既に走っていれば始めない。</returns>
    public bool StartBacklogNow() => StartBacklog(force: true);

    private bool StartBacklog(bool force)
    {
        if (!force && !_services.Settings.ResumeFetchInBackground)
        {
            return false;
        }

        if (_backlogRun is { IsCompleted: false })
        {
            return false;
        }

        _backlog = new CancellationTokenSource();
        var token = _backlog.Token;

        // 進み具合は常設の1行に「何を n/N」で出す（ユーザ指示）。以前は通信の様子
        // （間隔を空けています）しか出ず、何をしているのか読めなかった。
        // UIスレッドで作っておく（Progress は作ったスレッドへ知らせを戻す）
        IProgress<(int Done, int Total)> ReportAs(string label)
            => new Progress<(int Done, int Total)>(
                report => BoothActivity.ReportWork(WorkSource.Background, label, report.Done, report.Total));

        var images = ReportAs("画像を取得中");
        var avatars = ReportAs("アバターの画像を取得中");
        var due = ReportAs("商品の更新を確認中");
        var detection = new Progress<Core.Services.AvatarDetectProgress>(
            report => BoothActivity.ReportWork(WorkSource.Background, "対応アバターを検出中", report.Done, report.Total));

        _backlogRun = Task.Run(async () =>
        {
            try
            {
                // 対応アバターの検出し直し。梯子では③なので画像より先。手元の照合がほとんどで、問い合わせは知らないIDの分だけ
                if (_services.Avatars.IsRedetectDue())
                {
                    await RunBackgroundStageAsync("対応アバターの検出し直し", async () =>
                    {
                        using var priority = Core.Booth.BoothClient.Prioritize(Core.Booth.BoothPriority.Detection);
                        var result = await _services.Avatars.DetectAsync(detection, token);

                        // 検索の絞り込みやカードは読み込んだ時の対応アバターで組んである。書き換えた商品があれば組み直す
                        if (result.ItemsUpdated > 0)
                        {
                            RunOnUiThread(() => ReloadLibraryAsync().Forget());
                        }
                    });
                }

                await RunLocalChecksAsync(token);

                await RunBackgroundStageAsync("前の取り込みで残った画像", () => _services.Backlog.ResumeAsync(images, token));

                // 持っていないアバターの1枚目（U18）。商品の画像の穴の方が先に目に入るので⑤の後
                await RunBackgroundStageAsync("持っていないアバターの画像", () => _services.AvatarImages.SyncAsync(avatars, token));

                await RunBackgroundStageAsync("期限の来た商品の取り直し", () => _services.Due.RunAsync(due, token));

                // ⑦で商品ページが変わっていれば要確認が増える。件数を出し直す
                RunOnUiThread(RefreshCounts);
            }
            catch (OperationCanceledException)
            {
                // 閉じたときに止めた
            }
        }, token);
        _backlogRun.Forget();
        return true;
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
                await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.PruneVideoTitles());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Core.Diagnostics.AppLog.Error("起動時の裏の作業：動画のタイトルの控えの整理", exception);
            }
        }).Forget();

    /// <summary>
    /// 起動したときに裏で一度、記録しているファイルとフォルダの場所を全部見て、見つからなくなった日時を付け外しする（ユーザ判断 2026-10-05）。
    /// **窓を出した後に呼ぶ**（全件を読み、ディスクを見るので、窓が出るまでの待ちに乗せない。App の起動の処理が呼ぶ）。
    /// </summary>
    /// <remarks>
    /// 取り込みと使おうとした画面でしか書いていなかったので、取り込まずに使っている間は、手で消した zip が
    /// カードの印・検索の条件「見つからないファイル」・統計に出なかった。決まりは取り込みの見回りと同じ（<see cref="Core.Services.MissingMarksSweep"/>）。
    /// BOOTH に問い合わせないので、設定「起動したとき、裏で取得を始める」を切っていても見る（通知の整理・手で直した JSON の確認と同じ）。
    /// 取り込みが同時に走っても、見回りは1本ずつ回り、錠の中で今の値と同じなら書かない。
    /// </remarks>
    public void StartMissingMarksSweep()
    {
        var token = (_backlog ??= new CancellationTokenSource()).Token;
        Task.Run(async () =>
        {
            try
            {
                // 裏の作業は UiCommand を通らないので、保存先を運ぶ間の門はここで待つ（RunBackgroundStageAsync と同じ）
                await Core.Storage.StoreWriteGate.WaitAsync(token);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var written = await _services.MissingMarks.SweepAsync(token);
                Core.Services.UiTrace.Write("速さ", $"起動時の見回り：見つからなくなった日時を {written.Count} 件の商品に書いた（{watch.ElapsedMilliseconds} ms）");
                await NoteSweptItemsAsync(written);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 見られなくても起動は妨げない。次の起動か取り込みでまた見る
                Core.Diagnostics.AppLog.Error("起動時の裏の作業：見つからないファイルの見回り", exception);
            }
        }, token).Forget();
    }

    /// <summary>
    /// 見回りが書いた商品を、検索の写しとカードの印へ知らせる。数が少なければ1件ずつ差し替え、多ければ全件を読み直す
    /// （1件ずつの差し替えは毎回絞り込みをかけ直すので、数百件だと全件の読み直しより重い）。
    /// </summary>
    private async Task NoteSweptItemsAsync(IReadOnlyList<string> written)
    {
        if (written.Count == 0)
        {
            return;
        }

        if (written.Count > SweepNoteOneByOneLimit)
        {
            RunOnUiThread(() => ReloadLibraryAsync().Forget());
            return;
        }

        var items = new List<ItemRecord>(written.Count);
        foreach (var id in written)
        {
            if (await _services.Store.Items.LoadAsync(id) is { } item)
            {
                items.Add(item);
            }
        }

        RunOnUiThread(() =>
        {
            foreach (var item in items)
            {
                Search.NoteItemChanged(item);
            }

            RefreshCounts();
        });
    }

    /// <summary>
    /// 見回りの後、1件ずつ差し替える上限。普段の起動で変わるのは数件（消した・戻した分）で、
    /// 初めて見回る保存先や外付けを付け直した回だけ数百件になる（作り物の5000件の1割を消すと457件）。
    /// </summary>
    internal const int SweepNoteOneByOneLimit = 50;

    /// <summary>手元に無くなった商品の「最近」の足跡を落とす（<see cref="Services.RecentTracker.PruneMissingItemsAsync"/>）。通信しない。</summary>
    private void PruneRecent()
        => Task.Run(async () =>
        {
            try
            {
                var dropped = await _services.Recent.PruneMissingItemsAsync();
                Core.Services.UiTrace.Write("速さ", $"起動時の片付け：無い商品の足跡 {dropped} 行を消した");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Core.Diagnostics.AppLog.Error("起動時の裏の作業：最近の足跡の整理", exception);
            }
        }).Forget();

    /// <summary>裏の作業の1段。落ちてもログに残して次の段へ進む。止まるのは閉じたとき（取り消し）だけ。</summary>
    private async Task RunBackgroundStageAsync(string name, Func<Task> stage)
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
            BoothActivity.EndWork(WorkSource.Background);
        }
    }

    /// <summary>
    /// 取り込みの後、新しく見つかったアバターの1枚目を続けて取る（ユーザ判断 2026-09-12）。
    /// 以前は次の起動の裏の取得まで待っていたので、取り込んだ直後の一覧や候補は頭文字のままだった。
    /// 検出のときにJSONを取っているので1枚目のURLは分かっており、ここで増えるのは画像の取得だけ。
    /// 起動時の裏の取得と重なっても、<see cref="Core.Services.AvatarImageSync"/> が1本ずつ回すので二重には取らない。
    /// </summary>
    public void StartAvatarImageSync()
    {
        var token = (_backlog ??= new CancellationTokenSource()).Token;
        IProgress<(int Done, int Total)> progress = new Progress<(int Done, int Total)>(
            report => BoothActivity.ReportWork(WorkSource.Background, "アバターの画像を取得中", report.Done, report.Total));

        Task.Run(async () =>
        {
            try
            {
                // 登録簿を書くので、保存先を運んでいる間は待つ（他の裏の段と同じ門）
                await Core.Storage.StoreWriteGate.WaitAsync(token);
                await _services.AvatarImages.SyncAsync(progress, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 取れなくても次の起動でまた試す。頼まれた作業ではないので邪魔をしない
                Core.Diagnostics.AppLog.Error("取り込みの後のアバターの画像", exception);
            }
            finally
            {
                BoothActivity.EndWork(WorkSource.Background);
            }
        }, token).Forget();
    }

    /// <summary>閉じるときに背景の取得を止める。</summary>
    public void StopBackgroundWork()
    {
        _backlog?.Cancel();
        _watch?.Cancel();
    }

    private CancellationTokenSource? _watch;

    /// <summary>
    /// 監視対象フォルダに新しいファイルが無いかを、起動時に見る。
    ///
    /// **走査してよいのは、ユーザが「ここを見ておいて」と指示したフォルダだけ。**
    /// 監視に入っていないフォルダは今まで通り、押されるまで見に行かない。
    ///
    /// 見つけても**既定では取り込みを始めない**。走査は手元のディスクを読むだけだが、
    /// 取り込みはBOOTHへの通信で1件あたり十数秒かかる。起動した瞬間に黙って始めると、
    /// ユーザがこれからやろうとしていた操作と行列を取り合う。件数を出して押させる。
    ///
    /// 設定「起動時に自動で取り込む」を入れた人だけ、そのまま始める（#38）。対象は監視フォルダの新着と、
    /// 前回途中で止まった取り込みの続き（ユーザ判断 2026-09-29。<see cref="Core.Scanning.LaunchImportTargets"/>）。
    /// そのときも画面は切り替えない——起動した直後に画面が飛ぶと、しようとしていた操作の邪魔になる。
    /// 進み具合は常設の1行に出る。
    /// </summary>
    private void StartWatchScan()
    {
        var watched = _services.Settings.WatchedFolders;
        var importsOnLaunch = _services.Settings.StartImportOnLaunch;

        // 監視フォルダが無くても、自動で取り込む人には続きを見る（続きは監視フォルダと関係なく残る）
        if (watched.Count == 0 && !importsOnLaunch)
        {
            return;
        }

        _watch = new CancellationTokenSource();
        var token = _watch.Token;

        Task.Run(async () =>
        {
            try
            {
                var result = await _services.Watch.FindNewAsync(watched, token);

                // ドライブは在るのに見つからない監視フォルダ（名前を変えた・移した）は、外付けを外しているのと違い
                // 待っても戻らないので取り込み画面で言う（見つからない・移動の点検 9・2026-10-05。前は外付けと同じに黙っていた）
                RunOnUiThread(() => WatchedMissingFolders = result.MissingFolders);

                if (importsOnLaunch)
                {
                    // **対象は監視フォルダの新着と、前回の続きだけ**（ユーザ判断 2026-09-21・G1、2026-09-29）。
                    // 取り込み画面が起動時に履歴を全部「対象」に積んでいたため、
                    // 新着を足して走らせると履歴も丸ごと舐め直していた（設定の説明と真逆）。
                    // 前は新着だけを見ていて、①の途中で閉じた回は走査の控えに載っているので新着に数えられず、
                    // 続きが残っていても何も始まらなかった
                    var targets = Core.Scanning.LaunchImportTargets.Collect(result.NewFiles, LoadImportStateOrNull());

                    // 外付けを外している・消した物は積まない。積むと、取り込み画面の「見つかりません。もう一度ドロップして」が
                    // 起動しただけで出る（ドロップしていないのに）。在るかは画面のスレッドの外で見る
                    var present = targets
                        .Where(path => Core.Services.DiskCheck.FileExists(path) || Core.Services.DiskCheck.FolderExists(path))
                        .ToList();
                    if (present.Count == 0)
                    {
                        return;
                    }

                    // 押してもいないので、展開先のファイルがあっても窓で尋ねない（G2）。
                    // 続きの対象には監視していないフォルダもあるが、起動時に「監視しますか」とも聞かない
                    RunOnUiThread(() => Import.AddDroppedPaths(
                        present, startImmediately: true, offerWatch: false, askAboutUnpacked: false));
                    return;
                }

                if (!result.HasNew)
                {
                    return;
                }

                // **前に「知らせなくてよい」と言われた顔ぶれのままなら黙る**（ユーザ判断 2026-09-21・G16）。
                // 取り込むつもりの無いファイルが監視フォルダに居座ると、起動のたびに同じ件数を
                // 知らされ続けていた（消す以外に黙らせる手が無かった）
                if (WatchNewDigest(result.NewFiles) == _services.UiState.DismissedWatchNew)
                {
                    return;
                }

                RunOnUiThread(() =>
                {
                    WatchedNewFiles = result.NewFiles;
                    OnPropertyChanged(nameof(WatchedNewCount));
                    OnPropertyChanged(nameof(HasWatchedNew));
                    OnPropertyChanged(nameof(WatchedNewText));
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 見に行けなくても起動は妨げない。次の起動でまた見る
                Core.Diagnostics.AppLog.Error("監視フォルダの新着を見る", exception);
            }
        }, token).Forget();
    }

    /// <summary>取り込みの続きの記録。読めなければ「続きは無い」と同じに扱う（下の帯と同じ。起動は妨げない）。</summary>
    private Core.Scanning.ImportState? LoadImportStateOrNull()
    {
        try
        {
            return _services.Store.ImportState.Load();
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            Core.Diagnostics.AppLog.Error("起動時の取り込み：続きの記録を読む", exception);
            return null;
        }
    }

    /// <summary>
    /// 新着の顔つき（G16）。**並べた順に依らない**ように並べ替えてから作る。
    /// 中身は覚えない——覚えるのはこの1文字列だけで、監視フォルダが大きくても増えない。
    /// </summary>
    private static string WatchNewDigest(IReadOnlyList<string> files)
    {
        var joined = string.Join("\n", files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// 今出ている新着を「もう知らせなくてよい」にする（G16）。
    /// **同じ顔ぶれの間は黙り、何か増えたらまた言う。**取り込むつもりが変わったら、押し直さなくても出てくる。
    /// </summary>
    public void DismissWatchedNew()
    {
        var digest = WatchNewDigest(WatchedNewFiles);
        WatchedNewFiles = [];
        OnPropertyChanged(nameof(WatchedNewCount));
        OnPropertyChanged(nameof(HasWatchedNew));
        OnPropertyChanged(nameof(WatchedNewText));

        SaveUiStateAsync(state => state with { DismissedWatchNew = digest }).Forget();
    }

    private RelayCommand? _dismissWatchedNew;

    public RelayCommand DismissWatchedNewCommand => _dismissWatchedNew ??= new RelayCommand(DismissWatchedNew);

    private IReadOnlyList<string> _watchedMissingFolders = [];

    /// <summary>起動時に見たとき、ドライブは在るのに見つからなかった監視フォルダ。</summary>
    public IReadOnlyList<string> WatchedMissingFolders
    {
        get => _watchedMissingFolders;
        private set
        {
            _watchedMissingFolders = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasWatchedMissing));
            OnPropertyChanged(nameof(WatchedMissingText));
        }
    }

    public bool HasWatchedMissing => WatchedMissingFolders.Count > 0;

    public string WatchedMissingText => WatchedMissingSummary(WatchedMissingFolders);

    /// <summary>見つからない監視フォルダの文。1つなら名前を出す（どれかが分かれば、外すか直すかを決められる）。</summary>
    internal static string WatchedMissingSummary(IReadOnlyList<string> folders) => folders.Count switch
    {
        0 => string.Empty,
        1 => $"監視フォルダ「{FolderName(folders[0])}」が見つかりません。",
        _ => $"監視フォルダが {folders.Count} 個見つかりません。",
    };

    private static string FolderName(string path)
        => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    /// <summary>監視対象で見つかった、まだ見ていないファイル。</summary>
    public IReadOnlyList<string> WatchedNewFiles { get; private set; } = [];

    public int WatchedNewCount => WatchedNewFiles.Count;

    public bool HasWatchedNew => WatchedNewCount > 0;

    public string WatchedNewText => $"監視対象に新しいファイルが {WatchedNewCount} 件あります。";

    /// <summary>見つかったぶんを取り込み対象に積む。押されて初めて通信が始まる。</summary>
    public void TakeWatchedNew()
    {
        if (!HasWatchedNew)
        {
            return;
        }

        var files = WatchedNewFiles;
        WatchedNewFiles = [];
        OnPropertyChanged(nameof(WatchedNewCount));
        OnPropertyChanged(nameof(HasWatchedNew));
        OnPropertyChanged(nameof(WatchedNewText));

        Import.AddDroppedPaths(files);
        ShowImport();
    }
}
