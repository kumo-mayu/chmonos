using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 未確定画面：「このIDで登録」の順番待ちを行ごとに映す（ユーザ判断 2026-10-05 メモ60）。
///
/// 前は登録の進みが画面に1組だけで、どの行を選んでも同じ帯が出て、登録の間は画面全体の操作（ほかの行の登録・除外）が止まっていた。
/// 1件の登録は手元に無い商品なら数分かかり、待つ間に次のファイルを調べて登録したくなる（ユーザ：初めは対応アバターの検討も要る）。
/// 列は主画面が持ち（<see cref="MainViewModel.Registrations"/>）、この画面は行の札と右の欄で映すだけ。
/// **押せなくするのは列にいる行だけ**。ほかの行は確かめも登録も除外もできる。
/// </summary>
public sealed partial class ResolveViewModel
{
    private RegistrationQueue Queue => _main.Registrations;

    /// <summary>今の対象（チェックした物か、選んだ行の単位）のどれかが列にいるか。いれば登録・除外を押せない（2回積まない・登録中に外さない）。</summary>
    public bool IsTargetQueued => (HasChecked ? CheckedRows : ActiveRows).Any(row => Queue.JobFor(row.File.Hash) is not null);

    /// <summary>右の欄に映す列の1件。選んだ行（無ければ今の対象のどれか）が入っている物。</summary>
    private RegistrationJob? TargetJob
        => Selected is null
            ? null
            : Queue.JobFor(Selected.File.Hash)
                ?? ActiveRows.Select(row => Queue.JobFor(row.File.Hash)).FirstOrDefault(job => job is not null);

    public bool IsTargetRunning => TargetJob?.IsRunning == true;

    public bool IsTargetWaiting => TargetJob is { IsRunning: false };

    /// <summary>走っている行の帯の文（前の「登録しています… n / m 件　BOOTHへあと n 件・約 m 分」と同じ）。</summary>
    public string QueueRunningText => TargetJob is { IsRunning: true } job
        ? RegisteringLine(job.Done, job.FileHashes.Count, job.RequestsLeft, _services.Settings.FetchIntervalMs)
        : string.Empty;

    public bool HasQueueTotal => TargetJob is { IsRunning: true } job && job.FileHashes.Count > 1;

    public int QueueTotal => TargetJob?.FileHashes.Count ?? 0;

    public int QueueDone => TargetJob?.Done ?? 0;

    /// <summary>待っている行の右の欄。「ほかの商品を登録しています。開始まで約 n 分、この商品は約 m 分です。」</summary>
    public string QueueWaitingText => TargetJob is { IsRunning: false } job
        ? RegistrationQueue.WaitingText(Queue.SecondsUntilStart(job), Queue.SecondsFor(job))
        : string.Empty;

    /// <summary>待っている登録をやめる（右の欄のボタンと、行の右クリック）。走っている物は止めない（ユーザ判断 2026-10-05「2-A」）。</summary>
    public RelayCommand CancelQueuedCommand => _cancelQueued ??= new RelayCommand(
        () =>
        {
            if (TargetJob is { IsRunning: false } job)
            {
                Queue.Cancel(job);
            }
        },
        () => IsTargetWaiting);

    private RelayCommand? _cancelQueued;

    /// <summary>
    /// 「BOOTHへ問い合わせて商品を作る」登録を列に積む。持っている商品へ足すだけの登録は問い合わせが無く数十msで終わるので、
    /// 積まずにその場で済ませる（呼ぶ側）。積んだ物はチェックを外す（そのまま次のチェックで別の物をまとめられるように）。
    /// </summary>
    private void EnqueueRegistration(IReadOnlyList<UnresolvedRow> targets, ItemPreview preview, bool fromChecked, IReadOnlyList<string> imagePaths)
    {
        Queue.Enqueue(new RegistrationJob
        {
            Record = new QueuedRegistration
            {
                ItemId = preview.Id,
                ItemName = preview.Name,
                FileHashes = [.. targets.Select(row => row.File.Hash)],
                EstimatedRequests = preview.RequestsToRegister,
                UserImagePaths = imagePaths.Count > 0 ? imagePaths : null,
            },
            FromChecked = fromChecked,
        });

        if (fromChecked)
        {
            foreach (var row in targets)
            {
                row.IsSelected = false;
            }
        }

        StatusText = string.Empty;
        OnPropertyChanged(nameof(HasStatus));
    }

    private void OnQueueChanged() => RunOnUiThread(RefreshQueueState);

    /// <summary>行の札と右の欄、押せるかを出し直す。列が変わったとき・一覧を読み直したとき・選び直したときに呼ぶ。</summary>
    private void RefreshQueueState()
    {
        foreach (var row in Files)
        {
            var job = Queue.JobFor(row.File.Hash);
            var failure = job is null ? Queue.FailureFor(row.File.Hash) : null;
            row.SetQueueState(
                job is not null ? RegistrationQueue.BadgeText(Queue.WaitingPosition(job)) : failure is not null ? "登録に失敗" : string.Empty,
                failure ?? string.Empty,
                _main.ResolveSearch.OwnerHashes.Contains(row.File.Hash, StringComparer.OrdinalIgnoreCase),
                job is null ? string.Empty : RegistrationQueue.BadgeTip(job));
        }

        foreach (var name in QueueProperties)
        {
            OnPropertyChanged(name);
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    private static readonly string[] QueueProperties =
    [
        nameof(IsTargetQueued), nameof(IsTargetRunning), nameof(IsTargetWaiting), nameof(QueueRunningText),
        nameof(HasQueueTotal), nameof(QueueTotal), nameof(QueueDone), nameof(QueueWaitingText),
        nameof(IsRegisteringInDecision), nameof(RegisteringText), nameof(HasRegisteringTotal), nameof(RegisteringTotal), nameof(RegisteringDone),
    ];

    /// <summary>選び直したときは、行の札は変わらないので右の欄と押せるかだけ出し直す。</summary>
    private void RaiseQueueProperties()
    {
        foreach (var name in QueueProperties)
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>
    /// 列の1件が終わった。この画面にその行があれば受け止める。
    /// **次の行へ送るのは、終わったときにその行を見ていたときだけ**（ユーザ判断 2026-10-05）。登録は数分かかり、その間に別の行を見ていると、
    /// 前は見ている（登録していない）行を外していた。行は読み直しで作り直されるので、ハッシュで比べる。
    /// </summary>
    private void OnQueueFinished(RegistrationOutcome outcome)
    {
        var job = outcome.Job;
        var rows = Files.Where(row => job.FileHashes.Contains(row.File.Hash, StringComparer.OrdinalIgnoreCase)).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        outcome.Handled = true;
        var settledRows = rows.Where(row => outcome.SettledHashes.Contains(row.File.Hash, StringComparer.OrdinalIgnoreCase)).ToList();
        var viewing = !job.FromChecked && job.FileHashes.Count == 1 && settledRows.Count == 1
            && Selected is { } selected && string.Equals(selected.File.Hash, settledRows[0].File.Hash, StringComparison.OrdinalIgnoreCase);

        OnPropertyChanged(nameof(SettledCount));
        OnPropertyChanged(nameof(HasSettled));
        OnPropertyChanged(nameof(SettledText));

        // 添えた画像が入らなかった分は、BOOTHに無い商品の登録と同じく一覧の見出しの近くに言う
        var imagesNote = outcome.ImagesFailed > 0
            ? $"画像 {outcome.ImagesFailed} 枚を追加できませんでした。商品ページの「＋」から追加してください。"
            : string.Empty;

        if (viewing)
        {
            AfterSettled();
            if (imagesNote.Length > 0)
            {
                ListNoticeText = imagesNote;
            }
        }
        else if (settledRows.Count > 0)
        {
            RemoveFinishedRows(settledRows);
            // 文はその場で済ませる登録と同じ（束・選んだ物は件数で、1件は何を登録したかが分かるよう商品名で）
            ListNoticeText = (outcome.Failure is not null
                ? $"{settledRows.Count} / {job.FileHashes.Count} 件を登録しました。残りは失敗しました。{outcome.Failure}"
                : job.FileHashes.Count > 1
                    ? $"{settledRows.Count} 件を登録しました。"
                    : $"「{job.ItemName}」を登録しました。") + imagesNote;
            HideCoveredContents(settledRows);
        }

        if (outcome.Failure is { } failure)
        {
            // 失敗した行を見ていれば欄の下に理由を出す。見ていなければ一覧の見出しの近くに（行には札「登録に失敗」と吹き出しの理由）
            if (Selected is { } shown && rows.Contains(shown))
            {
                StatusText = failure;
                OnPropertyChanged(nameof(HasStatus));
            }
            else if (settledRows.Count == 0)
            {
                ListNoticeText = $"「{job.ItemName}」を登録できませんでした。";
            }
        }

        RefreshQueueState();
    }

    /// <summary>
    /// 済んだ行を一覧から外す。**見ている行は動かさない**（<see cref="RemoveRows"/> は先頭を選び直す）。
    /// 見ている行が外れたとき（束の登録で、束の中の行を見ていた）だけ、見えている行の中の次を選ぶ。
    /// </summary>
    private void RemoveFinishedRows(IReadOnlyList<UnresolvedRow> rows)
    {
        var visible = FilesView.Cast<UnresolvedRow>().ToList();
        var selectedGone = Selected is { } selected && rows.Contains(selected);
        var index = selectedGone ? visible.IndexOf(Selected!) : -1;

        foreach (var row in rows)
        {
            row.SelectionChanged -= OnCheckedChanged;
            Files.Remove(row);
            visible.Remove(row);
        }

        RememberedSearches.Forget(rows.Select(row => row.File.Hash));

        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(RemainingToolTip));
        OnCheckedChanged();
        _main.RefreshBadges();

        if (selectedGone)
        {
            Selected = visible.Count == 0 ? Files.FirstOrDefault() : visible[Math.Min(Math.Max(index, 0), visible.Count - 1)];
            RequestItemIdFocus(ItemIdFocusReason.Settled);
        }
    }

    private void SubscribeQueue()
    {
        Queue.Changed += OnQueueChanged;
        Queue.Finished += OnQueueFinished;
        _main.ResolveSearch.PropertyChanged += OnSearchStateChanged;
        _main.ResolveSearch.Ended += OnSearchEnded;
    }

    private void UnsubscribeQueue()
    {
        Queue.Changed -= OnQueueChanged;
        Queue.Finished -= OnQueueFinished;
        _main.ResolveSearch.PropertyChanged -= OnSearchStateChanged;
        _main.ResolveSearch.Ended -= OnSearchEnded;
    }
}
