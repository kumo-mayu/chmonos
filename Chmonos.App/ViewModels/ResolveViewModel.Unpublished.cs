using Chmonos.Core.Commands;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 未確定画面：BOOTHで見つからなかった商品IDのまま登録する（ユーザ判断 2026-09-29）。
///
/// 商品IDは分かっている（zipの名前・購入履歴・ショップのページから）のにBOOTHで見つからない商品は、今までは仮ID（BOOTHに無い商品）で
/// 登録するしかなく、再び公開されても情報を取れなかった。確かめた結果が「BOOTHに無い」だったときだけ（一時的に届かないときは出さない）、
/// そのIDのまま「BOOTHで公開されていない」商品として登録する道を出す。
/// ⑦（期限の来た商品の取り直し）で確かめ直し、公開されたら情報を取って通知に出す（Core の <c>AssignUnpublishedItemIdAsync</c>）。
///
/// 2026-10-06（ユーザ）：前は確定の欄の下に「ID … のまま登録する」のボタンが出るだけで、何の道なのか分かりにくかった。
/// BOOTH から何も取れない商品なので、名前と画像を決めるのはここしかない。「商品IDを決める」の欄の中に、説明・名前・画像の枠を出し、
/// 登録は同じ「このIDで登録」（キーも同じ）で行う。名前と画像は「BOOTHに無い商品」と同じ欄の値を使う（どちらの出口でも、
/// 決めるのは同じファイルの名前と画像。2つ持つと、片方で書いた名前がもう片方の登録に乗らない）。
/// </summary>
public sealed partial class ResolveViewModel
{
    private string? _notOnBoothItemId;

    /// <summary>確かめた結果、BOOTHに無かった商品ID。確かめ直す・選び直すと消える。</summary>
    public string? NotOnBoothItemId
    {
        get => _notOnBoothItemId;
        private set
        {
            if (SetField(ref _notOnBoothItemId, value))
            {
                OnPropertyChanged(nameof(HasNotOnBoothItemId));
                RaiseUnpublishedForm();
            }
        }
    }

    public bool HasNotOnBoothItemId => NotOnBoothItemId is not null;

    /// <summary>
    /// 見つからなかったIDのまま登録する形（説明・名前・画像）を出すか。欄の文字がそのIDのときだけ。
    /// 人が欄を書き換えたら、ふつうの「確かめてから登録」に戻す——書き換えたIDはまだ確かめていない。
    /// </summary>
    public bool IsUnpublishedForm => NotOnBoothItemId is { } id && ParseItemIdInput(ItemIdInput) == id;

    /// <summary>
    /// 見つからなかったIDが、このファイルから読み取れた物か（取り込みの手掛かり）。人が打ったIDなら打ち間違いもあり得るので、
    /// 欄の下の「IDが違うか、非公開です」の文を残し、ここでは登録すると何が起きるかだけを言う。
    /// </summary>
    private bool IsNotOnBoothIdFromFile => NotOnBoothItemId is { } id
        && (Selected?.NotOnBoothItemId == id || Selected?.File.CandidateItemIds.Contains(id, StringComparer.Ordinal) == true);

    /// <summary>
    /// 取り込みのときは「無い」と答えられたが、聞き直すと公開されていたID（ユーザ判断 2026-10-06）。
    /// このIDのふつうの登録にだけ、非公開のつもりで添えた画像を持っていく。選び直すと消える
    /// </summary>
    private string? _overturnedItemId;

    /// <summary>説明の1行目。読み取れたIDなら、BOOTHで非公開だったことを言う。人が打ったIDは欄の下の文が言っている。</summary>
    public string UnpublishedLeadText => IsNotOnBoothIdFromFile
        ? "このファイルから読み取れた商品IDは、BOOTHで非公開です。"
        : string.Empty;

    public bool HasUnpublishedLead => UnpublishedLeadText.Length > 0;

    private void RaiseUnpublishedForm()
    {
        OnPropertyChanged(nameof(IsUnpublishedForm));
        OnPropertyChanged(nameof(UnpublishedLeadText));
        OnPropertyChanged(nameof(HasUnpublishedLead));
        OnPropertyChanged(nameof(AssignOutcomeText));
        OnPropertyChanged(nameof(HasAssignOutcome));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private async Task AssignUnpublishedAsync()
    {
        if (Selected is null || !IsUnpublishedForm || NotOnBoothItemId is not { } itemId || string.IsNullOrWhiteSpace(LocalNameInput))
        {
            return;
        }

        // 対象は「このIDで登録」「BOOTHに無い商品として登録」と同じ（チェックがあればその全部＝zipの単位まで広げた物、
        // 無ければ束か1件）。前はここだけ一覧のチェックを見ず、チェックしていても今の行（束）だけを登録していた。
        // 1件目で商品を作り、残りはその商品へ加える（BOOTHへは行かない）
        var fromChecked = HasChecked;
        var (targets, blocked) = RegisterTargets();
        if (blocked is not null)
        {
            StatusText = blocked;
            OnPropertyChanged(nameof(HasStatus));
            return;
        }

        if (targets.Count == 0)
        {
            return;
        }

        // 名前は欄の値（下書きは元zipの名前かファイル名。人が書き換えた名前はそのまま）。添えた画像は押した時点の分を控える
        var name = LocalNameInput.Trim();
        var images = LocalImages.ToList();
        // 確かめの窓はふつうの「このIDで登録」と同じく、一覧でチェックした物をまとめて登録するときだけ出す（ユーザ判断 2026-10-06）。
        // 1件と束は、枠と「登録すると：」の行で何が起きるかを見て押している。窓を挟むと、キーで1件ずつ片付ける流れが止まる
        if (fromChecked)
        {
            var answer = Services.Notice.Show(
                $"{TargetSubject(targets, fromChecked)} を商品ID {itemId} として登録します。\n\n"
                + $"BOOTHで公開されていない商品として、名前「{name}」で登録します。"
                + (images.Count > 0 ? $"選んだ画像 {images.Count} 枚を追加します。" : string.Empty) + "\n\n"
                + "BOOTHで公開されたら情報を取得し、通知に表示します。",
                "このIDのまま登録する",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.Cancel);

            if (answer != System.Windows.MessageBoxResult.OK)
            {
                return;
            }
        }

        IsBusy = true;
        StartRegistering(RegisteringArea.Decision, targets.Count);
        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.AssignUnpublishedItemId(targets[0].File.Hash, itemId, name));
            StepRegistering(1);

            if (result is not CommandResult.ItemSaved)
            {
                StatusText = result is CommandResult.Failed failed ? failed.Message : string.Empty;
                OnPropertyChanged(nameof(HasStatus));
                return;
            }

            // 商品ができてから画像を入れる（BOOTHに無い商品と同じ）。入らなかった分があっても登録は取り消さない（商品ページの「＋」で足し直せる）。
            // 同じIDの商品が先にあった（確かめた後に別の道で作られた）ときも、その商品へ入れる：添えたのはこのファイルの商品の画像
            var imagesFailed = images.Count > 0 ? await AddLocalImagesToAsync(itemId, images) : 0;
            var imagesNote = imagesFailed > 0
                ? $"画像 {imagesFailed} 枚を追加できませんでした。商品ページの「＋」から追加してください。"
                : string.Empty;

            // 確定と同じ扱いで溜める（まとめて編集へ送れる）。検索にもその場で出す（画像を入れた後。カードに画像を出す）
            if (targets.Count == 1)
            {
                await NoteSettledAsync(itemId);
                AfterSettled();
                if (imagesNote.Length > 0)
                {
                    ListNoticeText = imagesNote;
                }

                return;
            }

            var settled = new List<UnresolvedRow> { targets[0] };
            var done = 1;
            foreach (var row in targets.Skip(1))
            {
                var assigned = await _services.Commands.ExecuteAsync(new UiCommand.AssignItemId(row.File.Hash, itemId, RequestsLeftProgress));
                StepRegistering(++done);
                if (assigned is not CommandResult.Failed)
                {
                    settled.Add(row);
                }
            }

            // 残りの中身まで加えた後の商品を写しへ足す
            await NoteSettledAsync(itemId);
            RemoveRows(settled);
            ListNoticeText = (settled.Count == targets.Count
                ? $"{settled.Count} 件を登録しました。"
                : $"{settled.Count} / {targets.Count} 件を登録しました。残りは失敗しました。") + imagesNote;
            HideCoveredContents(settled);
        }
        finally
        {
            EndRegistering();
            IsBusy = false;
        }
    }
}
