using System.IO;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>主画面：起動時の裏の作業と監視フォルダ（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// 使っていない間に進める2つを背景で走らせる。
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
        if (!_services.Settings.ResumeFetchInBackground)
        {
            return;
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

        Task.Run(async () =>
        {
            try
            {
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
        }, token).Forget();
    }

    /// <summary>裏の作業の1段。落ちてもログに残して次の段へ進む。止まるのは閉じたとき（取り消し）だけ。</summary>
    private async Task RunBackgroundStageAsync(string name, Func<Task> stage)
    {
        try
        {
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
    /// 設定「起動時に監視フォルダの新着を取り込む」を入れた人だけ、そのまま始める（#38）。
    /// そのときも画面は切り替えない——起動した直後に画面が飛ぶと、しようとしていた操作の邪魔になる。
    /// 進み具合は常設の1行に出る。
    /// </summary>
    private void StartWatchScan()
    {
        if (_services.Settings.WatchedFolders.Count == 0)
        {
            return;
        }

        _watch = new CancellationTokenSource();
        var token = _watch.Token;

        Task.Run(async () =>
        {
            try
            {
                var result = await _services.Watch.FindNewAsync(_services.Settings.WatchedFolders, token);
                if (!result.HasNew)
                {
                    return;
                }

                if (_services.Settings.StartImportOnLaunch)
                {
                    RunOnUiThread(() => Import.AddDroppedPaths(result.NewFiles, startImmediately: true));
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
