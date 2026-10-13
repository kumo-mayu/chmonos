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

        _afterStart.Clear();
        Started?.Invoke(_work);

        try
        {
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
            Finished?.Invoke();
        }
    }

    /// <summary>知らせをその場で渡す。間引きと画面のスレッドへの受け渡しは、受ける側が行う。</summary>
    private sealed class ForwardProgress(Action<ImportProgress> report) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => report(value);
    }
}
