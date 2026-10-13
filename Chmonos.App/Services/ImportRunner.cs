using Chmonos.Core.Commands;
using Chmonos.Core.Scanning;

namespace Chmonos.App.Services;

/// <summary>取り込みの頼み。対象は始める直前に決める（展開先のファイルを元の zip に差し替えるか、を尋ねるのは頼んだ側）。</summary>
/// <param name="ResolveTargets">今回の対象を決める。立ち上げの途中に来た頼みは、立ち上がった後に呼ぶ。</param>
/// <param name="History">取り込み元の履歴へ足すパス（差し替える前の、頼まれた物）。</param>
public sealed record ImportRequest(Func<List<string>> ResolveTargets, IReadOnlyList<string> History);

/// <summary>取り込みの終わり方。</summary>
public abstract record ImportOutcome
{
    private ImportOutcome()
    {
    }

    public sealed record Imported(ImportSummary Summary) : ImportOutcome;

    public sealed record Failed(string Message) : ImportOutcome;

    public sealed record Canceled : ImportOutcome;

    /// <summary>取り込みの結果でも失敗でもない答え（命令の取り違え。画面は何も出さない）。</summary>
    public sealed record Other : ImportOutcome;
}

/// <summary>
/// 取り込みを走らせる係。**画面に依存しない**：画面が無い起動（自動の取り込み・後の API）でも、画面からの取り込みと同じ道を通す
/// （docs/history/headless-core-2026-10-13.md 段1b。ユーザ判断「経路は統一する」）。取り込み画面はこの係を映すだけ。
///
/// **1本だけ走らせる。**走っていなければ始め、走っていれば**今の取り込みに積む**。
/// 2本目を起こさないのは、取得の順序（画像より先にJSON）が2本では保てず、どちらの進捗を出すのかも決められなくなるため。
///
/// 知らせ（始まった・進んだ・終わった）は裏のスレッドから来ることがある。受ける側が画面のスレッドへ渡す。
/// </summary>
public sealed class ImportRunner(AppServiceContainer services)
{
    private CancellationTokenSource? _cancellation;

    /// <summary>実行中の作業集合。走らせている最中に足せるので、ここを握っておく。</summary>
    private ImportWorkSet? _work;

    /// <summary>
    /// 立ち上げの途中（<see cref="_work"/> が入るまで）。2本目を止めるのに要る——
    /// その間に確認の窓と設定の保存を挟むので、待っている間に画面のメッセージが回る。
    /// </summary>
    private bool _starting;

    /// <summary>
    /// 立ち上げの途中に来た頼み。立ち上がったら積む。前は黙って弾いていたので、この間に落とした物は
    /// 一覧には載るのに今回の取り込みに入らず、帯は「終わりました」と言っていた
    /// </summary>
    private readonly List<ImportRequest> _afterStart = [];

    private readonly List<Func<ImportOutcome, Task>> _ending = [];

    /// <summary>走っている取り込み（無ければ null）。</summary>
    public ImportWorkSet? Running => _work;

    /// <summary>始まった（作業集合ができた）。この後に進み具合が来る。</summary>
    public event Action<ImportWorkSet>? Started;

    /// <summary>進み具合。走査はファイル1つごとに来るので、受ける側が間引く。</summary>
    public event Action<ImportProgress>? Progress;

    /// <summary>取り込み元の履歴を書いた。</summary>
    public event Action? HistorySaved;

    /// <summary>作業集合を手放した（成否に関わらず最後に来る）。</summary>
    public event Action? Finished;

    /// <summary>
    /// 終わり方を受ける。**受ける側が終えるのを待ってから**次へ進む（画面は一覧を読み直し終えてから、アバターの画像を取り始めていた）。
    /// </summary>
    public void OnEnded(Func<ImportOutcome, Task> listener) => _ending.Add(listener);

    /// <summary>
    /// 起動したときに取り込む対象。**監視フォルダの新着と、前回の続きだけ**（ユーザ判断 2026-09-21・G1、2026-09-29。<see cref="LaunchImportTargets"/>）。
    /// 外付けを外している・消した物は積まない（積むと、取り込み画面の「見つかりません。もう一度ドロップして」が起動しただけで出る）。
    /// 在るかは画面のスレッドの外で見る
    /// </summary>
    public Task<IReadOnlyList<string>> LaunchTargetsAsync(IReadOnlyList<string> watchedNewFiles)
        => Task.Run<IReadOnlyList<string>>(() => LaunchImportTargets.Collect(watchedNewFiles, LoadImportStateOrNull())
            .Where(path => Core.Services.DiskCheck.FileExists(path) || Core.Services.DiskCheck.FolderExists(path))
            .ToList());

    /// <summary>取り込みの続きの記録。読めなければ「続きは無い」と同じに扱う（下の帯と同じ。起動は妨げない）。</summary>
    private ImportState? LoadImportStateOrNull()
    {
        try
        {
            return services.Store.ImportState.Load();
        }
        catch (Exception exception) when (exception is System.IO.IOException or System.Text.Json.JsonException)
        {
            Core.Diagnostics.AppLog.Error("起動時の取り込み：続きの記録を読む", exception);
            return null;
        }
    }

    /// <summary>
    /// 人に尋ねずに対象を決める（自動で始めた取り込み。ユーザ判断 2026-09-21・G2）。展開先の中のファイルを指していても、
    /// 尋ねずにそのまま取り込み、**要確認に出して後から差し替えられる**ようにする——起動直後に、押してもいないのに応答待ちの窓が黙って出ていた
    /// </summary>
    public List<string> TargetsWithoutAsking(List<string> targets)
    {
        var origins = UnpackedFileResolver.FindOrigins(targets);
        if (origins.Count > 0)
        {
            NoteUnpackedFoundAsync(origins.Count).Forget();
        }

        return targets;
    }

    /// <summary>
    /// 自動で始めた取り込みが展開先のファイルを指していたことを、要確認に出す（G2）。
    /// 窓で尋ねる代わりなので、**そのまま取り込んだこと**と、**後から差し替えられること**を書く。
    /// </summary>
    private Task NoteUnpackedFoundAsync(int count)
        => services.Commands.ExecuteAsync(new UiCommand.AddNotification(
            new Core.Models.NotificationRecord
            {
                Id = "unpacked-imported",
                Kind = Core.Models.NotificationKind.UnpackedFilesImported,
                Title = "展開したフォルダの中のファイルを取り込みました",
                Detail = $"zipを展開したフォルダの中のファイルを {count} 件取り込みました。"
                    + "元のzipで取り込み直すときは、そのzipを取り込んでから「展開先フォルダの削除」で片付けてください。",
                CreatedAt = DateTimeOffset.Now,
            }));

    /// <summary>走っていれば止める。</summary>
    public void Cancel() => _cancellation?.Cancel();

    /// <summary>
    /// 走っていなければ始めて、終わるまで待つ。走っていれば今の取り込みに積み、積んだ数を返す。
    /// 立ち上げの途中なら、立ち上がった所で積むよう預かり、null を返す（走らせた側が積む）。
    /// </summary>
    public async Task<int?> StartOrStackAsync(ImportRequest request)
    {
        if (_work is { } running)
        {
            var added = running.Add(request.ResolveTargets());
            await SaveHistoryAsync(request.History);
            return added;
        }

        // **立ち上げている最中の2度目を止める。**`_work` が入るのは、展開先の確認の窓と
        // 設定の保存を挟んだ後。その間は画面のメッセージが回るので、2度目の押下が同じ道へ入れた。
        // 通り抜けると取り込みが2本走り、先に終わった方の後始末が中断の口を捨てるので、
        // 走っている方を止められなくなる
        if (_starting)
        {
            _afterStart.Add(request);
            return null;
        }

        _starting = true;
        try
        {
            await RunAsync(request);
        }
        finally
        {
            _starting = false;
        }

        return null;
    }

    /// <summary>
    /// 今回の対象を履歴へ足す（ユーザ判断 2026-09-21・G4）。
    /// 履歴は「何を読んだか」を確かめるための記録なので、ファイルも残す（監視の対象にはしない）。
    /// 足すのは今回の対象だけで、画面の履歴の写しでは書かない（技術的負債 1-1 の再発：
    /// 写しで丸ごと書いていたので、設定画面で足した取り込み元を取り込むたびに消していた）
    /// </summary>
    private async Task SaveHistoryAsync(IReadOnlyList<string> paths)
    {
        await services.Commands.ExecuteAsync(new UiCommand.ChangeSettings(
            settings => Core.Services.FolderListChange.AddImportFolders(settings, paths)));
        HistorySaved?.Invoke();
    }

    private async Task RunAsync(ImportRequest request)
    {
        var targets = request.ResolveTargets();
        await SaveHistoryAsync(request.History);

        _cancellation = new CancellationTokenSource();
        _work = new ImportWorkSet(targets);
        foreach (var later in _afterStart)
        {
            _work.Add(later.ResolveTargets());
        }

        // 立ち上げの途中に預かった頼みの履歴も残す（前は対象には入るが履歴に残らなかった。点検36）
        var laterHistory = _afterStart.SelectMany(later => later.History).ToList();
        _afterStart.Clear();

        // 外から使う API へ「取り込み中」を伝える（その間は古い答えを返し続け、終わってから読み直す）。
        // 前は取り込み画面を通してだけ伝えていて、画面の無い起動の取り込みでは伝わらなかった（点検36・37）
        services.Api.NoteBusy("import", "import");
        Started?.Invoke(_work);

        try
        {
            // 後始末（作業集合を手放す）で覆われた所で書く（書けなくても取り込みを止めたまま残さない）
            if (laterHistory.Count > 0)
            {
                await SaveHistoryAsync(laterHistory);
            }

            ImportOutcome outcome;
            try
            {
                var result = await services.Commands.ExecuteAsync(
                    new UiCommand.ScanFolders(_work),
                    new ForwardProgress(report => Progress?.Invoke(report)),
                    _cancellation.Token);
                outcome = result switch
                {
                    CommandResult.Imported imported => new ImportOutcome.Imported(imported.Summary),
                    CommandResult.Failed failed => new ImportOutcome.Failed(failed.Message),
                    _ => new ImportOutcome.Other(),
                };
            }
            catch (OperationCanceledException)
            {
                outcome = new ImportOutcome.Canceled();
            }

            foreach (var listener in _ending)
            {
                await listener(outcome);
            }

            // 新しく見つかったアバターの1枚目を続けて取る（次の起動まで待たせない）
            if (outcome is ImportOutcome.Imported)
            {
                services.Startup.StartAvatarImageSync();
            }
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            _work = null;
            services.Api.NoteBusy("import", null);
            Finished?.Invoke();
        }
    }

    /// <summary>知らせをその場で渡す。間引きと画面のスレッドへの受け渡しは、受ける側が行う。</summary>
    private sealed class ForwardProgress(Action<ImportProgress> report) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => report(value);
    }
}
