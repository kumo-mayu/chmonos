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
    public void GoBack()
    {
        if (_history.Count == 0)
        {
            ShowSearch();
            return;
        }

        // 戻る前の画面を「進む」に積む（積めない画面＝控えの作れない物は積まない）
        if (_currentViewModel is not null && EntryFor(_currentViewModel) is { } leaving)
        {
            _forward.Add(leaving);
        }

        var entry = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        _nextNavigation = Navigation.Back;
        entry.Restore();
    }

    /// <summary>戻った先から進む。進む先が無ければ何もしない。</summary>
    public void GoForward()
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
        while (_history.Count > 0 && _history[^1].IsEdit)
        {
            _history.RemoveAt(_history.Count - 1);
        }

        GoBack();
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
    private HistoryEntry? EntryFor(object screen) => screen switch
    {
        SearchViewModel => new HistoryEntry("検索", ShowSearch),
        ItemViewModel item => new HistoryEntry(Shorten(item.Name), () => RestoreItemAsync(item.Item.Id).Forget()),
        ShopViewModel shop => new HistoryEntry(Shorten(shop.Shop.Name), () => ShowShop(shop.Shop)),
        ModificationViewModel modification => new HistoryEntry(
            Shorten(modification.Record.Name), () => ShowModification(modification.Record)),
        AvatarsViewModel avatars => new HistoryEntry("アバターの管理", RestoreAvatars(avatars.Selected?.ItemId)),
        ModificationHubViewModel hub => new HistoryEntry(
            "改変", () => ShowModifications(hub.Level, hub.Selection)),
        ShopsViewModel => new HistoryEntry("ショップ一覧", ShowShops),
        FolderViewModel folders => new HistoryEntry("フォルダ", () => ShowFolders(folders.SelectedKey)),
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

    /// <summary>
    /// 編集画面の中で商品を移るとき、今の商品を履歴に積む（ユーザ指示 2026-09-12：
    /// Alt＋← で、編集画面で前に開いていた商品へ戻れるように）。
    /// </summary>
    public void RememberEditStep(EditViewModel edit)
    {
        Remember(edit);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(BackButtonText));
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
            // 画面の差し替えが起きないので、戻るの印をここで下ろす（残すと次の画面移動が履歴に積まれない）
            _nextNavigation = Navigation.Push;
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(BackButtonText));
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

    /// <summary>アバター画面は、選んでいたアバターを選んだ状態で戻す。</summary>
    private Action RestoreAvatars(string? selectedId)
        => selectedId is null ? ShowAvatars : () => ShowAvatar(selectedId);

    /// <summary>
    /// 商品ページは開き直した時点の中身で出す（覚えた時の中身は、その後の編集で古くなっている）。
    /// 消えた商品（IDを変えた・登録を外した）は飛ばして、もう1つ前へ戻る
    /// </summary>
    private async Task RestoreItemAsync(string itemId)
    {
        if (await _services.Store.Items.LoadAsync(itemId) is { } item)
        {
            ShowItem(item);
            return;
        }

        _nextNavigation = Navigation.Push;
        GoBack();
    }

    private static string Shorten(string label)
        => label.Length <= MaxBackLabel ? label : label[..MaxBackLabel] + "…";

    public bool IsSearchActive => CurrentViewModel is SearchViewModel;

    public bool IsImportActive => CurrentViewModel is ImportViewModel;

    public bool IsEditActive => CurrentViewModel is EditViewModel;
}
