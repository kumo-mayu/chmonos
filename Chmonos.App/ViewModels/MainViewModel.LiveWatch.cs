using System.ComponentModel;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 主画面：アプリを開いている間の監視フォルダの見張り（plan 49・ユーザ判断 2026-11-13
/// 「常時監視自体にオンオフをつけて、そのうえでオンの時は既定で帯を出し、設定から自動取り込みを許可できるようにしよう。
/// 優先順位は普通の取り込みと同じで良いと思う。」）。決め事は <c>docs/spec/folder-watch.md</c>。
///
/// - 設定「開いている間も…探す」が切なら張らない（前と全く同じ。帯も出ない）。
/// - 入なら、見つけた新着を下の帯で知らせ、［取り込む］で取り込み画面の「新しいファイルを取り込む」と同じ道で取り込む。
/// - 「そのまま取り込みを始める」も入なら、見張り始めた後に現れた物を、落としたときと同じ取り込みの道で始める
///   （優先度は人が始めた取り込みと同じ。取り込みの梯子が段を決め、ここでは優先度を張らない）。
/// - 取り込み中に見つけた物は、終わってから知らせる・取り込む（今の取り込みに黙って混ぜない）。
/// - 閉じる・保存先を変える（<see cref="StopBackgroundWork"/>）・設定を切ると止める。
///
/// 画面の無い起動（通知領域だけ）・API・MCP では張らない：張るのは主画面を作ったときだけ。窓を隠して通知領域にいる間は主画面が残るので見張り続ける。
/// </summary>
public sealed partial class MainViewModel
{
    private LiveFolderWatch? _live;
    private Action<AppSettings>? _liveSettingsHandler;
    private Action? _liveImportFinishedHandler;
    private bool _liveNoticeShown;
    private bool _liveNoticeHooked;
    private RelayCommand? _takeLiveWatched;
    private RelayCommand? _dismissLiveWatched;

    /// <summary>
    /// 自動で取り込みに渡した物（場所・大きさ・更新日時の指紋）。同じ物を2度は自動で始めない——BOOTH の不調で取れなかった物や読めなかった物は
    /// 取り込みの後も新着に数えられる（取り直すまで「新しい」。import.md）ので、終わるたびに探し直して始めると、不調の BOOTH へ問い合わせを重ね続ける。
    /// 開いている間だけ持つ（次の起動では起動時の決まりで扱う）
    /// </summary>
    private readonly HashSet<string> _liveAutoTaken = new(StringComparer.Ordinal);

    /// <summary>見張っているか（試験が見る）。</summary>
    internal LiveFolderWatch? LiveWatch => _live;

    /// <summary>下の帯（監視フォルダの新着）を出すか。設定が入で、開いている間に見つけたときだけ。取り込み画面では同じことがその画面に出ているので出さない。</summary>
    public bool HasLiveWatchNotice => _liveNoticeShown && HasWatchedNew && CurrentViewModel != Import;

    public string LiveWatchNoticeText => $"監視フォルダに新しいファイルが {WatchedNewCount} 件あります。";

    /// <summary>帯の［取り込む］。取り込み画面のボタンと同じく、今の新着を探し直して取り込む（画面は移さず、取り込みの帯に進み具合を出す）。</summary>
    public RelayCommand TakeLiveWatchedCommand => _takeLiveWatched ??= new RelayCommand(() => TakeLiveWatchedAsync().Forget());

    /// <summary>帯の ×。「知らせなくてよい」と同じ（同じ顔ぶれの間は出さず、何か増えたらまた出す。G16）。</summary>
    public RelayCommand DismissLiveWatchedCommand => _dismissLiveWatched ??= new RelayCommand(() =>
    {
        _liveNoticeShown = false;
        DismissWatchedNew();
    });

    private void StartLiveWatch()
    {
        _liveSettingsHandler = settings => RunOnUiThread(() => ApplyLiveWatch(settings));
        _services.SettingsStore.Changed += _liveSettingsHandler;

        // 取り込み中に見つけて渡さなかった物を、終わってから探し直す
        _liveImportFinishedHandler = () => _live?.Kick();
        _services.Imports.Finished += _liveImportFinishedHandler;

        ApplyLiveWatch(_services.Settings);
    }

    private void StopLiveWatch()
    {
        if (_liveSettingsHandler is not null)
        {
            _services.SettingsStore.Changed -= _liveSettingsHandler;
            _liveSettingsHandler = null;
        }

        if (_liveImportFinishedHandler is not null)
        {
            _services.Imports.Finished -= _liveImportFinishedHandler;
            _liveImportFinishedHandler = null;
        }

        _live?.Stop();
        _live = null;
    }

    /// <summary>設定に合わせて張る・止める。監視フォルダの足し引きにも合わせる（張り直すと、見張り始める前の新着を控え直す）。</summary>
    private void ApplyLiveWatch(AppSettings settings)
    {
        // 止めた後（閉じる途中）に来た設定の知らせでは張らない
        if (_liveSettingsHandler is null)
        {
            return;
        }

        if (!settings.WatchFoldersWhileOpen || settings.WatchedFolders.Count == 0)
        {
            _live?.Stop();
            SetLiveNotice(false);
            return;
        }

        _live ??= CreateLiveWatch();
        if (!_live.IsRunning || !SameFolders(_live.Folders, settings.WatchedFolders))
        {
            _live.Start(settings.WatchedFolders);
        }
    }

    private static bool SameFolders(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => left.Count == right.Count && left.All(folder => right.Any(other => Core.Services.PathText.Same(folder, other)));

    private LiveFolderWatch CreateLiveWatch()
    {
        var live = _services.CreateLiveFolderWatch(_services.Watch);
        live.IsBusy = () => _services.Imports.IsBusy;

        // 保存先が監視フォルダの中にあると、アプリが書くたびに探し直してしまう
        live.IgnoredRoot = _services.Paths.Root;
        live.Found = files =>
        {
            RunOnUiThread(() => OnLiveFound(live, files));
            return Task.CompletedTask;
        };
        return live;
    }

    private void OnLiveFound(LiveFolderWatch live, LiveWatchBatch files)
    {
        if (!ReferenceEquals(live, _live) || !live.IsRunning || !_services.Settings.WatchFoldersWhileOpen)
        {
            return;
        }

        // 画面のスレッドへ預けた間に監視フォルダを足し引きして張り直していた。前の見張りの物は外したフォルダの物を含みうるので使わない（W4）。
        // 残したフォルダの物も含みうるが、張り直した見張りが控えの後に1回探し直すので、そちらで拾う
        if (!live.IsCurrent(files))
        {
            return;
        }

        // 見張りが見た後に取り込みが始まっていた。今の取り込みに混ぜず、終わってから探し直す
        if (_services.Imports.IsBusy)
        {
            live.Defer();
            return;
        }

        if (_services.Settings.AutoImportWatchedFiles)
        {
            StartLiveAutoImportAsync(live, files).Forget();
            return;
        }

        ShowLiveNotice(files.Select(file => file.Path).ToList());
    }

    /// <summary>
    /// 見張り始めた後に現れた物だけを、自動で取り込む。前から置いてあって取り込まずにいた物（「知らせなくてよい」にした物を含む）は、
    /// 関係の無いファイルが1つ置かれたのをきっかけに取り込まない——それは起動時の確かめと「新しいファイルを取り込む」で今までどおり扱う
    /// </summary>
    private async Task StartLiveAutoImportAsync(LiveFolderWatch live, IReadOnlyList<LiveWatchFile> files)
    {
        var take = files
            .Where(file => file.Arrived && !_liveAutoTaken.Contains(file.Fingerprint))
            .ToList();
        if (take.Count == 0)
        {
            return;
        }

        // 落としたときと同じ取り込みの道（優先度も同じ）。押してもいないので展開先は窓で尋ねず、「監視しますか」も聞かない（起動時の自動の取り込みと同じ・G2）。
        // 在るかを見る間に人が取り込みを始めていたら積まない（W3）
        var started = await Import.StartWatchedAutoAsync(
            take.Select(file => file.Path).ToList(),
            () => NoteImportQueued(started: true));
        if (!started)
        {
            // 「2度は自動で始めない」の指紋には入れない（入れると、終わってから探し直しても始めない）。終わってから探し直す
            if (ReferenceEquals(live, _live))
            {
                live.Defer();
            }

            return;
        }

        foreach (var file in take)
        {
            _liveAutoTaken.Add(file.Fingerprint);
        }
    }

    internal void ShowLiveNotice(IReadOnlyList<string> paths)
    {
        // 「知らせなくてよい」と言った顔ぶれのままなら出さない（G16。起動時と同じ）
        if (paths.Count > 0 && WatchNewDigest(paths) == _services.UiState.DismissedWatchNew)
        {
            return;
        }

        Core.Services.UiTrace.Write("監視", $"帯に出した：{paths.Count} 件");
        HookLiveNotice();
        WatchedNewFiles = paths;
        OnPropertyChanged(nameof(WatchedNewCount));
        OnPropertyChanged(nameof(HasWatchedNew));
        OnPropertyChanged(nameof(WatchedNewText));
        SetLiveNotice(paths.Count > 0);
    }

    private async Task TakeLiveWatchedAsync()
    {
        SetLiveNotice(false);

        // 押した時に探し直した物にも、見張りが帯を出す前と同じ書き終わりの確かめを掛ける（W1）。
        // 途中の物は渡さずに残す。書いている間と書き終わりに出来事が来るので、見張りがいつもの道（静けさ・書き終わりの確かめ・取り込み中なら預ける）で拾い直す。
        // ここで探し直しを頼まない：積んだ取り込みが始まる前に探し直すと、今積んだ物でまた帯を出しうる
        Func<IReadOnlyList<string>, Task<IReadOnlyList<string>>>? keepSettled = _live is { IsRunning: true } live
            ? paths => live.SettledAsync(paths)
            : null;
        var queued = await Import.ImportWatchedNowAsync(keepSettled);
        if (queued > 0)
        {
            NoteImportQueued(started: true);
        }
    }

    private void SetLiveNotice(bool show)
    {
        _liveNoticeShown = show;
        OnPropertyChanged(nameof(HasLiveWatchNotice));
        OnPropertyChanged(nameof(LiveWatchNoticeText));
    }

    // 取り込み画面を開いたら帯は要らない（同じことがその画面に出ている）。離れたらまた出す
    private void HookLiveNotice()
    {
        if (_liveNoticeHooked)
        {
            return;
        }

        _liveNoticeHooked = true;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CurrentViewModel))
            {
                OnPropertyChanged(nameof(HasLiveWatchNotice));
            }
        };
    }
}
