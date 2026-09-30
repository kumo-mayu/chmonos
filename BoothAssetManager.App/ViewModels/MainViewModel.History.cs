using System.IO;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>主画面：画面の履歴（U23）（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class MainViewModel
{
    // ---- 画面の履歴（U23） ----
    //
    // 「戻る」は常に直前の画面へ（ブラウザと同じ・ユーザ判断）。以前は開くときに戻り先を1つ渡す作りで、
    // 渡さない入口（要確認・説明のリンク・落とす／貼る・ファイルを外した後など）は検索へ落ち、
    // 商品→ショップ→商品のように往復すると2段目から先を失っていた。
    // ナビで移っても履歴は切らない（ユーザ判断：「一瞬の確認の可能性もあります」）

    /// <summary>
    /// 覚えておく画面の数。一日中開いたままでも伸び続けないように。
    /// 100（ユーザ指示 2026-09-12）。編集画面で商品を移るたびに1つ積むようにしたので、50では編集の数十件で押し出される。
    /// 1つは開き直す手順だけ（画面は持たない）なので、100でも軽い
    /// </summary>
    private const int MaxHistory = 100;

    /// <summary>
    /// 戻るの文言に載せる名前の長さ。長い商品名が上部バーを占領して隣の情報を押し出さないように
    /// （以前の商品ページの決め事と同じ30字。手元の15件で名前は中央28字・最長48字）
    /// </summary>
    private const int MaxBackLabel = 30;

    /// <param name="IsEdit">編集画面の中で商品を移った分か。指定して入った編集を終えるとき、ここを飛ばして入る前の画面へ戻す</param>
    private sealed record HistoryEntry(string Label, Action Restore, bool IsEdit = false);

    private enum Navigation
    {
        Push,
        Replace,
        Back,

        /// <summary>「進む」で開き直す分。履歴には積むが、進む先の控えは捨てない。</summary>
        Forward,
    }

    private readonly List<HistoryEntry> _history = [];

    /// <summary>
    /// 戻った先から**また進む**ための控え（ユーザ指示 2026-09-20・M7：ブラウザと同じく進むも要る）。
    /// 戻るたびに、離れる画面をここへ積む。**新しい画面へ移ったら捨てる**——ブラウザと同じで、
    /// 枝分かれした後の「進む」は行き先が決まらない
    /// </summary>
    private readonly List<HistoryEntry> _forward = [];

    private Navigation _nextNavigation = Navigation.Push;

    public bool CanGoBack => _history.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    /// <summary>戻るボタンの文言。行き先の名前を出す（どこへ戻るのか分からないと押せない）。</summary>
    public string BackButtonText => _history.Count > 0 ? $"← {_history[^1].Label}に戻る" : "← 検索に戻る";

    /// <summary>左上の戻る・進む（ブラウザと同じ形）。名前は出せないので、行き先はツールチップで言う。</summary>
    public string BackTip => _history.Count > 0 ? $"「{_history[^1].Label}」へ戻る（Alt+←）" : "戻る先がありません";

    public string ForwardTip => _forward.Count > 0 ? $"「{_forward[^1].Label}」へ進む（Alt+→）" : "進む先がありません";

    /// <summary>
    /// <see cref="LeaveEdit"/> で戻る先の名前（編集の中で移った分は飛ばす）。
    /// 編集のボタンが行き先と違う名前を名乗っていた（B1：商品ページから入っても「検索へ戻る」と出ていた）。
    /// </summary>
    public string LeaveEditLabel
    {
        get
        {
            for (var i = _history.Count - 1; i >= 0; i--)
            {
                if (!_history[i].IsEdit)
                {
                    return _history[i].Label;
                }
            }

            return "検索";
        }
    }

    /// <summary>直前の画面へ戻る。履歴が無ければ検索へ。</summary>
    /// <remarks>
    /// **待ちの要る開き直し（商品・ショップ・改変）の最中に押された戻る・進むは受けない。**
    /// 素早く2回押すと、1回目の待ちの間はまだ画面が替わっていないので、今の画面をもう一度「進む」に積み、
    /// 待ちの控え（<see cref="_pendingBack"/>）も2回目で上書きして、1回目の行き先が履歴から消えていた。
    /// 済んだ方の開き直しが勝つと、もう一方は「人が移った」と見て捨てられる（戻る・進むのどの組でも同じ）。
    /// 2回目を1回目の先から戻す形も考えたが、1回目の待ちを取り消す手立てが要り、
    /// 消えた先を飛ばす処理と絡んで筋が増える。待ちは手元の JSON を1つ読む程度（ほぼ一瞬）なので、受けない方を選んだ。
    /// ナビで移るのは今までどおり受ける（開き直しをやめ、控えを履歴へ戻す）。
    /// </remarks>
    public void GoBack()
    {
        if (_asyncRestores > 0)
        {
            return;
        }

        GoBack(rememberForward: true);
    }

    /// <summary>
    /// 待ちの要る開き直しが走っている数。0 でないうちは、人の戻る・進むを受けない（<see cref="GoBack()"/>）。
    /// 数にしたのは、消えた先を飛ばすとき、待ちの中から次の開き直しが始まって重なるため。
    /// </summary>
    private int _asyncRestores;

    /// <summary>
    /// 待ちの要る開き直しを走らせる。**終わり方に依らず数を戻す**（読めずに投げても、戻るが押せないままにならないように）。
    /// </summary>
    private async Task RunAsyncRestore(Func<Task> restore)
    {
        _asyncRestores++;
        try
        {
            await restore();
        }
        finally
        {
            _asyncRestores--;
        }
    }

    /// <param name="rememberForward">
    /// 戻る前の画面を「進む」に積むか。**消えた先を飛ばしてもう1つ戻るときは積まない**——
    /// 画面はまだ差し替わっていないので、同じ画面が「進む」に2つ並び、進むを2回押すと同じ所へ2回来る。
    /// </param>
    private void GoBack(bool rememberForward)
    {
        if (_history.Count == 0)
        {
            // 戻り先が無いので、今の画面を積み直さずに検索へ出す
            // （積むと、戻ったはずなのに戻るがまた光って、今出てきた画面を指す）。
            // 消えた先を飛ばしてここへ来たときは、待っていた控えも捨てる（戻すと消えた先が履歴に返ってくる）
            _pendingBack = null;
            _nextNavigation = Navigation.Replace;
            ShowSearch();
            return;
        }

        // 戻る前の画面を「進む」に積む（積めない画面＝控えの作れない物は積まない）
        HistoryEntry? pushedForward = null;
        if (rememberForward && _currentViewModel is not null && EntryFor(_currentViewModel) is { } leaving)
        {
            _forward.Add(leaving);
            pushedForward = leaving;
        }

        var entry = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        _nextNavigation = Navigation.Back;
        _backInFlight = new PendingBack(entry, pushedForward);
        try
        {
            entry.Restore();
        }
        finally
        {
            _backInFlight = null;
        }
    }

    /// <param name="Entry">履歴から除いた、開き直している控え。</param>
    /// <param name="Forward">戻る前の画面として「進む」に積んだ控え（積んでいなければ null）。</param>
    private sealed record PendingBack(HistoryEntry Entry, HistoryEntry? Forward);

    /// <summary><see cref="GoBack(bool)"/> が控えの手順を呼んでいる間だけ立つ。</summary>
    private PendingBack? _backInFlight;

    /// <summary>
    /// 待ちの要る戻る（商品・ショップ・改変）で、開き直しを待っている控え。
    ///
    /// **待つ間に人が別の画面へ移ったら、除いた控えを履歴へ戻す**（開き直しはやめる）。
    /// 前は開き直しを待つ前に履歴から1件除いていたので、待つ間にナビを押すと、
    /// やめた開き直しの行き先がそのまま履歴から消えていた（戻るが1つ飛ぶ）。
    /// 手順がすぐに画面を差し替える控え（検索・一覧の画面）は待たないので、ここには入らない。
    /// </summary>
    private PendingBack? _pendingBack;

    /// <summary>画面が差し替わる所から呼ぶ。戻る待ちを片付ける（戻るで着いたなら捨て、別の移動なら控えを戻す）。</summary>
    private void SettlePendingBack(Navigation navigation, object? next)
    {
        if (_pendingBack is not { } pending || ReferenceEquals(_currentViewModel, next))
        {
            return;
        }

        _pendingBack = null;
        if (navigation == Navigation.Back)
        {
            return;
        }

        // この後で今の画面が積まれる。戻っていなかった形（控え → 今の画面）に並ぶよう、先に戻す
        _history.Add(pending.Entry);
        if (pending.Forward is { } forward)
        {
            _forward.Remove(forward);
        }
    }

    /// <summary>戻った先から進む。進む先が無ければ何もしない。待ちの要る開き直しの最中は受けない（<see cref="GoBack()"/>）。</summary>
    public void GoForward()
    {
        if (_asyncRestores > 0)
        {
            return;
        }

        GoForwardCore();
    }

    /// <summary>進む本体。消えた先を飛ばすときは開き直しの最中から呼ぶので、待ちの数を見ない。</summary>
    private void GoForwardCore()
    {
        if (_forward.Count == 0)
        {
            return;
        }

        var entry = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);

        // 進むときは、今の画面を履歴へ積む（戻れば元の場所に返れる）
        _nextNavigation = Navigation.Forward;
        entry.Restore();
    }

    private void ClearForward()
    {
        if (_forward.Count > 0)
        {
            _forward.Clear();
        }
    }

    /// <summary>
    /// 商品を指定して入った編集を終えたとき、入る前の画面へ戻す（動線の点検 D4）。
    /// 商品ページの「この商品を編集」から1件だけ直しに入っても、以前は検索へ飛ばされていた。
    /// 編集の中で商品を移った分の履歴は飛ばす（そのまま戻ると、前に開いていた商品の編集に戻ってしまう）
    /// </summary>
    public void LeaveEdit()
    {
        // 足跡を捨ててから戻るが受けられないと、足跡だけ消えて編集に残る。受けないなら先に止める
        if (_asyncRestores > 0)
        {
            return;
        }

        DropEditSteps();
        GoBack();
    }

    /// <summary>
    /// ナビから入った編集（未編集の順番）を終えたとき。**編集の足跡を捨ててから**検索へ戻す。
    ///
    /// 前は普通の移動で検索へ戻していたので、編集の足跡が履歴に残ったまま編集画面まで積まれていた。
    /// 戻るを押すと、順番の記録はもう消してあるので、開いていた商品ではなく
    /// 未編集の先頭から編集が始まり直していた（押した回数ぶん掘り返す）。
    /// </summary>
    public void LeaveEditToSearch()
    {
        DropEditSteps();
        _nextNavigation = Navigation.Replace;
        ShowSearch();
    }

    private void DropEditSteps()
    {
        while (_history.Count > 0 && _history[^1].IsEdit)
        {
            _history.RemoveAt(_history.Count - 1);
        }
    }

    private void Remember(object leaving)
    {
        if (EntryFor(leaving) is not { } entry)
        {
            return;
        }

        _history.Add(entry);
        if (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
        }
    }

    /// <summary>
    /// 画面を戻すための控え。**画面そのものは持たず、開き直す手順を持つ。**
    /// 画面を抱えると、戻るまでその画面の画像や一覧を握ったままになる（#71でメモリを押し上げた型）。
    /// 検索と取り込みの画面は1つを持ち回しているので、絞り込みやスクロール位置もそのまま戻る
    /// </summary>
    /// <remarks>
    /// **手順が捕まえるのは ID や選択の値だけにする。**前は <c>() => RestoreItemAsync(item.Item.Id)</c> のように
    /// 画面そのものを捕まえていたので、履歴100件分の商品ページが絵やカードごと生き残っていた。
    /// 値は控えを作る時点で取り出し、その値だけを手順に渡す（<see cref="ItemEntry"/> など）。
    /// </remarks>
    private HistoryEntry? EntryFor(object screen) => screen switch
    {
        // 検索は1つを持ち回すので、**そのときの条件も控える**（P4）。
        // 控えないと、戻っても「この商品だけ出す」で全消しされた後の条件のままだった
        SearchViewModel search => new HistoryEntry("検索", RestoreSearch(search.CaptureFilters())),
        // 商品ページは、流した位置も控える（ユーザ判断 2026-09-30。ショップと同じ決まり）。
        // 前は控えず、使い回された画面に残った位置で出ていた（別の商品で流して戻ると、その商品の位置で出た）
        ItemViewModel item => ItemEntry(Shorten(item.Name), item.Item.Id, item.CaptureScrollOffset()),
        // ショップの一覧とショップの中は、見ていた位置（と中の絞り）も控える（ユーザ判断 2026-09-28）。
        // 開き直すと一覧が先頭に戻り、商品を1件見て戻るたびに探し直していた
        ShopViewModel shop => ShopEntry(shop.Shop, shop.CaptureState()),
        ModificationViewModel modification => ModificationEntry(Shorten(modification.Record.Name), modification.Record.Id),
        AvatarsViewModel avatars => new HistoryEntry("アバターの管理", RestoreAvatars(avatars.Selected?.ItemId)),
        ModificationHubViewModel hub => HubEntry(hub.Level, hub.Selection),
        ShopsViewModel shops => ShopsEntry(shops.CaptureListAnchor()),
        FolderViewModel folders => FolderEntry(folders.SelectedKey),
        StatsViewModel => new HistoryEntry("統計", ShowStats),
        ImportViewModel => new HistoryEntry("取り込み", ShowImport),
        ResolveViewModel => new HistoryEntry("未確定", ShowResolve),
        InboxViewModel => new HistoryEntry("要確認", ShowInbox),
        TagManageViewModel => new HistoryEntry("タグの管理", ShowTagManage),
        AttributeManageViewModel => new HistoryEntry("属性の管理", ShowAttributeManage),
        SettingsViewModel => new HistoryEntry("設定", ShowSettings),
        EditViewModel edit => EditEntry(edit),
        _ => null,
    };

    // 以下は引数だけを捕まえる（画面を捕まえないように、ラムダを画面の変数と同じ所で書かない）
    private HistoryEntry ItemEntry(string label, string itemId, double scrollOffset)
        => new(label, () => RunAsyncRestore(() => RestoreItemAsync(itemId, scrollOffset)).Forget());

    private HistoryEntry ShopEntry(Core.Services.ShopSummary shop, ShopViewState state)
        => new(Shorten(shop.Name), () => RunAsyncRestore(() => RestoreShopAsync(shop, state)).Forget());

    private HistoryEntry ShopsEntry(ListAnchor? anchor) => new("ショップ一覧", () =>
    {
        ShowShops();

        // 作った直後は一覧を裏で読んでいる最中。位置は View が一覧を組み終えてから当てる
        if (anchor is not null && CurrentViewModel is ShopsViewModel shops)
        {
            shops.RestoreListAnchor(anchor);
        }
    });

    private HistoryEntry ModificationEntry(string label, string modificationId)
        => new(label, () => RunAsyncRestore(() => RestoreModificationAsync(modificationId)).Forget());

    private HistoryEntry HubEntry(ModificationHubLevel level, ModificationHubSelection? selection)
        => new("改変", () => ShowModifications(level, selection));

    private HistoryEntry FolderEntry(string? key) => new("フォルダ", () => ShowFolders(key));

    /// <summary>
    /// 編集画面の中で商品を移るとき、今の商品を履歴に積む（ユーザ指示 2026-09-12：
    /// Alt＋← で、編集画面で前に開いていた商品へ戻れるように）。
    /// </summary>
    public void RememberEditStep(EditViewModel edit)
    {
        Remember(edit);

        // 次の商品へ移ったら枝分かれしたので「進む」は捨てる（画面を移るときと同じ。ブラウザと同じ）。
        // ここだけ Remember を直に呼んでいて、捨てるのも知らせるのも抜けていた
        ClearForward();
        NotifyHistoryChanged();
    }

    /// <summary>戻る・進むの見た目（押せるか・行き先の名前）を出し直す。</summary>
    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(BackButtonText));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(BackTip));
        OnPropertyChanged(nameof(ForwardTip));
    }

    /// <summary>
    /// 編集画面の控え。**どの商品を開いていたか（位置）まで預ける。**
    /// 指定して入った編集は順番そのもの（<see cref="EditRun"/>）も預け、未編集の順番は edit-session.json から開き直す（ユーザ判断）
    /// </summary>
    private HistoryEntry EditEntry(EditViewModel edit)
    {
        var run = edit.Run;
        var index = edit.Index;

        // 位置ではなく商品で覚える。編集画面に入り直すと保存した商品が外れて順番が詰まるので、位置はずれる
        var itemId = edit.CurrentItemId;
        var label = edit.HasItem ? $"編集（{Shorten(edit.Name)}）" : "編集";
        return new HistoryEntry(label, () => RestoreEditAsync(run, itemId, index).Forget(), IsEdit: true);
    }

    private async Task RestoreEditAsync(EditRun? run, string? itemId, int index)
    {
        // 同じ順番の編集を開いている間は、画面はそのままで位置だけ戻す。
        // 作り直すと、店名の候補を作るために全件を読み直す（#71。2000件で重い）
        if (CurrentViewModel is EditViewModel current && ReferenceEquals(current.Run, run))
        {
            // **進むで来たときは、今の商品を履歴に積む。**画面の差し替えが起きないので、差し替えの所で積む仕組みを通らない。
            // 積まないと、進んだ先から戻るが今の商品を飛ばして1つ前へ行っていた。
            // 「進む」の控えは捨てない（RememberEditStep は捨てるので使わない）
            if (_nextNavigation == Navigation.Forward && current.HasItem)
            {
                Remember(current);
            }

            // 画面の差し替えが起きないので、戻るの印をここで下ろす（残すと次の画面移動が履歴に積まれない）
            _nextNavigation = Navigation.Push;
            NotifyHistoryChanged();
            await current.ShowStepAsync(itemId, index);
            return;
        }

        var edit = new EditViewModel(_services, this, Thumbnails);
        CurrentViewModel = edit;

        if (run is not null)
        {
            await edit.ResumeRunAsync(run, itemId);
        }
        else
        {
            await edit.ResumeAsync(itemId);
        }
    }

    /// <summary>
    /// 改変も開き直した時点の中身で出す（商品ページと同じ）。
    /// **覚えた時の記録をそのまま抱えて渡していたので、外で消した改変がそのまま出ていた。**
    /// 消えていれば飛ばして、もう1つ戻る
    /// </summary>
    private async Task RestoreModificationAsync(string id)
    {
        var forward = _nextNavigation == Navigation.Forward;
        var restoring = BeginAsyncRestore();

        if (await _services.Modifications.LoadAsync(id) is { } record)
        {
            if (!ResumeAsyncRestore(restoring, forward))
            {
                return;
            }

            ShowModification(record);
            return;
        }

        if (!ResumeAsyncRestore(restoring, forward))
        {
            return;
        }

        _nextNavigation = Navigation.Push;
        if (forward)
        {
            GoForwardCore();
        }
        else
        {
            GoBack(rememberForward: false);
        }
    }

    /// <summary>
    /// ショップも開き直した時点で数え直す。覚えた時の集計をそのまま出していたので、
    /// その後に外した商品が並んだままになっていた。無くなっていれば飛ばす
    /// </summary>
    private async Task RestoreShopAsync(Core.Services.ShopSummary remembered, ShopViewState state)
    {
        var forward = _nextNavigation == Navigation.Forward;
        var restoring = BeginAsyncRestore();

        // ショップ一覧と同じく、全商品のJSONは読み直さずに検索画面の写しから数える
        var items = Search.SnapshotItems();
        var shops = await Task.Run(() => _services.Shops.Summarize(items));
        var fresh = shops.FirstOrDefault(entry =>
            string.Equals(entry.Subdomain, remembered.Subdomain, StringComparison.OrdinalIgnoreCase));

        if (!ResumeAsyncRestore(restoring, forward))
        {
            return;
        }

        if (fresh is not null)
        {
            ShowShop(fresh);

            // 作った直後は商品を裏で読んでいる最中なので、絞りは読み終えた所の組み立てで効く
            if (CurrentViewModel is ShopViewModel shop)
            {
                shop.RestoreState(state);
            }

            return;
        }

        _nextNavigation = Navigation.Push;
        if (forward)
        {
            GoForwardCore();
        }
        else
        {
            GoBack(rememberForward: false);
        }
    }

    /// <summary>
    /// 開き直すのに待ちが要る控え（商品・ショップ・改変）の入口（ユーザ判断 2026-09-21・P17）。
    ///
    /// **待っている間は「戻る」の印を下ろしておく。**印は一度きりなので、
    /// 待っている間に人がナビを押すと、その移動が「戻る」扱いになって履歴に積まれず、
    /// 「進む」も消えないまま1つずれていた。
    /// </summary>
    /// <returns>待ち始めた時点の画面。戻ってきたときに、人が動かしていないかを見るのに使う。</returns>
    private object? BeginAsyncRestore()
    {
        _nextNavigation = Navigation.Push;

        // 戻るの途中なら、待つ間に人が移ったときに控えを戻せるように預かる。
        // 消えた先を飛ばしてもう1つ戻る途中なら、最初の戻るで「進む」に積んだ分を引き継ぐ
        if (_backInFlight is { } inFlight)
        {
            _pendingBack = inFlight with { Forward = inFlight.Forward ?? _pendingBack?.Forward };
        }

        return _currentViewModel;
    }

    /// <summary>
    /// 待ちから戻ってきた。**人が別の画面へ移っていたら、開き直しをやめる**
    /// （押した移動を勝手に上書きしない）。移っていなければ、差し替える直前に向きを立て直す。
    /// </summary>
    private bool ResumeAsyncRestore(object? restoring, bool forward)
    {
        if (!ReferenceEquals(_currentViewModel, restoring))
        {
            return false;
        }

        _nextNavigation = forward ? Navigation.Forward : Navigation.Back;
        return true;
    }

    /// <summary>検索は、離れたときの条件に戻してから出す（P4）。</summary>
    private Action RestoreSearch(Core.Models.SearchHistoryEntry filters) => () =>
    {
        Search.RestoreFilters(filters);
        ShowSearch();
    };

    /// <summary>アバター画面は、選んでいたアバターを選んだ状態で戻す。</summary>
    private Action RestoreAvatars(string? selectedId)
        => selectedId is null ? ShowAvatars : () => ShowAvatar(selectedId);

    /// <summary>
    /// 商品ページは開き直した時点の中身で出す（覚えた時の中身は、その後の編集で古くなっている）。
    /// 消えた商品（IDを変えた・登録を外した）は飛ばして、もう1つ前へ戻る
    /// </summary>
    /// <param name="scrollOffset">離れたときの流した位置。開き直した画面へ渡す（先頭にいたなら 0）。</param>
    private async Task RestoreItemAsync(string itemId, double scrollOffset)
    {
        // どちら向きに動いていたか。進んでいる最中に消えた商品へ当たったのに戻していたので、
        // 「進む」を押すと1つ戻っていた（押した先が読めない）
        var forward = _nextNavigation == Navigation.Forward;
        var restoring = BeginAsyncRestore();

        if (await _services.Store.Items.LoadAsync(itemId) is { } item)
        {
            if (!ResumeAsyncRestore(restoring, forward))
            {
                return;
            }

            ShowItem(item, scrollOffset);
            return;
        }

        if (!ResumeAsyncRestore(restoring, forward))
        {
            return;
        }

        _nextNavigation = Navigation.Push;
        if (forward)
        {
            GoForwardCore();
        }
        else
        {
            GoBack(rememberForward: false);
        }
    }

    private static string Shorten(string label)
        => label.Length <= MaxBackLabel ? label : label[..MaxBackLabel] + "…";

    public bool IsSearchActive => CurrentViewModel is SearchViewModel;

    public bool IsImportActive => CurrentViewModel is ImportViewModel;

    public bool IsEditActive => CurrentViewModel is EditViewModel;
}
