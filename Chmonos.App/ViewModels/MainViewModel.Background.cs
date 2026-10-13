using System.IO;
using Chmonos.App.Services;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>主画面：起動時の裏の作業と監視フォルダ（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// 起動時の裏の作業の合図を受ける。作業そのものは画面に依存しない <see cref="StartupWork"/> が回す
    /// （画面の無い起動でも同じ作業を回すため。docs/history/headless-core-2026-10-13.md 段1a）。
    /// 合図は裏のスレッドから来るので、画面のスレッドへ渡す（常設の1行は自分で渡す）
    /// </summary>
    private void ListenToStartupWork()
    {
        var work = _services.Startup;
        work.Progress += (label, done, total) => BoothActivity.ReportWork(WorkSource.Background, label, done, total);
        work.StageEnded += () => BoothActivity.EndWork(WorkSource.Background);
        work.LibraryChanged += () => RunOnUiThread(() => ReloadLibraryAsync().Forget());
        work.CountsChanged += () => RunOnUiThread(RefreshCounts);
        work.ItemsSwept += written => NoteSweptItemsAsync(written).Forget();
    }

    /// <inheritdoc cref="StartupWork.StartNow"/>
    public bool StartBacklogNow() => _services.Startup.StartNow();

    /// <inheritdoc cref="StartupWork.StartMissingMarksSweep"/>
    public void StartMissingMarksSweep() => _services.Startup.StartMissingMarksSweep();

    /// <inheritdoc cref="StartupWork.StartAvatarImageSync"/>
    public void StartAvatarImageSync() => _services.Startup.StartAvatarImageSync();

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
    internal const int DefaultSweepNoteOneByOneLimit = 50;

    /// <summary>
    /// <see cref="DefaultSweepNoteOneByOneLimit"/> の値。アプリは変えない。**試験だけが小さくする**——
    /// 上限を超える側を確かめるのに51件の商品を置いて書き直すと、1件で 約1.2秒かかっていた（2026-10-05 に測った）
    /// </summary>
    internal int SweepNoteOneByOneLimit { get; set; } = DefaultSweepNoteOneByOneLimit;

    /// <summary>閉じるときに背景の取得を止める。</summary>
    public void StopBackgroundWork()
    {
        _services.Startup.Stop();
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
    /// <param name="launched">
    /// プロセスを起動したときか。画面の無い起動の後に窓を開いたときは false で、新着を見て知らせるだけにする
    /// （自動の取り込みは「起動したとき」の設定。窓を開くたびに走らせない）
    /// </param>
    private void StartWatchScan(bool launched)
    {
        var watched = _services.Settings.WatchedFolders;
        var importsOnLaunch = launched && _services.Settings.StartImportOnLaunch;

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
                    // 対象は監視フォルダの新着と前回の続きだけ（決め方は画面の無い起動と同じ。ImportRunner.LaunchTargetsAsync）。
                    // 取り込み画面が起動時に履歴を全部「対象」に積んでいたため、新着を足して走らせると履歴も丸ごと舐め直していた（G1）
                    var present = await _services.Imports.LaunchTargetsAsync(result.NewFiles);
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
