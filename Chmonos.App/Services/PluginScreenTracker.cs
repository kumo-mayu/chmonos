using System.ComponentModel;
using System.Windows.Threading;
using Chmonos.Api;
using Chmonos.App.ViewModels;
using Chmonos.Query;

namespace Chmonos.App.Services;

/// <summary>
/// 開いている画面をソフトへ知らせる（screenChanged・GET /v1/plugin/current。docs/spec/plugins.md「開いた画面に付いていく」）。
/// 画面の移り変わりを拾うのはここ1か所：主画面が画面を差し替えたら <see cref="NoteScreen"/> を呼び、画面の中の変化
/// （フォルダ・改変の画面の右の中身・右に重ねた商品ページ・編集の今の商品・アバターの選び）は、ここが今の画面の知らせを聞いて拾う
/// （各画面に出す所を書くと、新しい画面や右の欄を足した時に書き忘れる）。
///
/// 流し方：画面を移った時はすぐ流す。同じ画面の中で右の中身が替わった時は <see cref="Delay"/> まとめて最後の物だけ流す
/// （行を続けて選ぶ間に途中の商品を何度も流さない。選択の知らせと同じ）。同じ画面・同じ物は流し直さない
/// （取り直し・API が書いた後の開き直しで流すと、ソフトが受けて書く → 開き直し → また流す、の輪になる）
/// </summary>
internal sealed class PluginScreenTracker
{
    /// <summary>
    /// まとめる間。選択の知らせ（<see cref="PluginSelectionBatcher.Delay"/>）と同じ 300ms：人が行を続けて選ぶ間隔より長く、
    /// ソフトの側から見て遅れが気にならない長さ（spec の決め事）
    /// </summary>
    internal static readonly TimeSpan Delay = PluginSelectionBatcher.Delay;

    /// <summary>同じ画面の中で、開いている物が替わり得る知らせ（ほかの知らせは数が多いので、見ずに捨てる）。</summary>
    private static readonly HashSet<string> InnerProperties = new(StringComparer.Ordinal)
    {
        nameof(FolderViewModel.Detail), nameof(AvatarsViewModel.Selected), nameof(EditViewModel.ItemPage), nameof(EditViewModel.Item),
    };

    private readonly PluginEventHub _hub;
    private readonly Dispatcher _dispatcher;
    private readonly ITimer _timer;

    private object? _screen;
    private INotifyPropertyChanged? _listening;
    private RightPaneItemPage? _listeningRight;

    /// <summary>前に流した画面。誰も購読していない間は忘れる（購読し直した後の最初の変化を、古い値と比べて落とさないため）。</summary>
    private PluginScreen? _published;

    /// <summary>まとめの時間を待っている変化があるか。</summary>
    private bool _pending;

    /// <param name="time">まとめの時間を測る時計（試験は作り物で進める）。時間切れは画面のスレッドへ運んで流す。</param>
    public PluginScreenTracker(PluginEventHub hub, Dispatcher dispatcher, TimeProvider time)
    {
        _hub = hub;
        _dispatcher = dispatcher;
        _timer = time.CreateTimer(_ => OnUiThread(Flush), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        hub.ScreenReader = ReadAsync;
    }

    /// <summary>まとめの時間を待っている変化があるか（試験）。</summary>
    internal bool IsPending => _pending;

    /// <summary>主画面が画面を差し替えた（画面のスレッド）。前の画面の知らせを外して今の画面の知らせを聞き、すぐ流す。</summary>
    public void NoteScreen(object? screen)
    {
        if (_listening is { } before)
        {
            before.PropertyChanged -= OnScreenPropertyChanged;
        }

        if (_listeningRight is { } beforeRight)
        {
            beforeRight.Changed -= OnInnerChanged;
        }

        _screen = screen;
        _listening = screen as INotifyPropertyChanged;
        _listeningRight = (screen as IRightPaneItemHost)?.RightItem;
        if (_listening is { } now)
        {
            now.PropertyChanged += OnScreenPropertyChanged;
        }

        if (_listeningRight is { } nowRight)
        {
            nowRight.Changed += OnInnerChanged;
        }

        // 待っていた前の画面の中の変化は捨てる（検索へ移った後に、古いフォルダの商品が遅れて届かないように）
        Cancel();
        PublishIfChanged();
    }

    private void OnScreenPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && InnerProperties.Contains(name))
        {
            OnInnerChanged();
        }
    }

    /// <summary>同じ画面の中で、開いている物が替わったかもしれない：最後の変化から <see cref="Delay"/> 待って流す。</summary>
    private void OnInnerChanged()
    {
        if (!_hub.Watches(PluginEventHub.ScreenChanged))
        {
            Cancel();
            _published = null;
            return;
        }

        _pending = true;
        _timer.Change(Delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>まとめの時間が切れた。控えた値ではなく今の画面を読み直して流す（待つ間に閉じた・別の物へ替わった分も映す）。</summary>
    internal void Flush()
    {
        if (!_pending)
        {
            return;
        }

        _pending = false;
        PublishIfChanged();
    }

    private void Cancel()
    {
        _pending = false;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void PublishIfChanged()
    {
        if (!_hub.Watches(PluginEventHub.ScreenChanged))
        {
            _published = null;
            return;
        }

        if (StateOf(_screen) is not { } state || state == _published)
        {
            return;
        }

        _published = state;
        _hub.PublishScreen(state);
    }

    private void OnUiThread(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else if (!_dispatcher.HasShutdownStarted)
        {
            _dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>今の画面を画面のスレッドで読む（API の要求のスレッドから来る）。</summary>
    private Task<PluginScreenNow?> ReadAsync(bool withSelection)
    {
        if (_dispatcher.CheckAccess())
        {
            return Task.FromResult(Read(withSelection));
        }

        return _dispatcher.HasShutdownStarted
            ? Task.FromResult<PluginScreenNow?>(null)
            : _dispatcher.InvokeAsync(() => Read(withSelection)).Task;
    }

    private PluginScreenNow? Read(bool withSelection)
    {
        if (StateOf(_screen) is not { } state)
        {
            return null;
        }

        var source = _screen as IPluginSelectionSource ?? (_screen as FolderViewModel)?.Detail as IPluginSelectionSource;
        return new PluginScreenNow(state, withSelection ? source?.SelectedItemIdsForPlugins() : null);
    }

    /// <summary>画面の型 → 英字の名前（docs/spec/plugins.md の一覧）。</summary>
    private static readonly Dictionary<Type, string> ScreenNames = new()
    {
        [typeof(SearchViewModel)] = "search",
        [typeof(ItemViewModel)] = "item",
        [typeof(EditViewModel)] = "edit",
        [typeof(ImportViewModel)] = "import",
        [typeof(ResolveViewModel)] = "resolve",
        [typeof(InboxViewModel)] = "inbox",
        [typeof(ShopsViewModel)] = "shops",
        [typeof(ShopViewModel)] = "shop",
        [typeof(FolderViewModel)] = "folder",
        [typeof(StatsViewModel)] = "stats",
        [typeof(AvatarsViewModel)] = "avatar",
        [typeof(ModificationHubViewModel)] = "modification",
        [typeof(ModificationViewModel)] = "modification",
        [typeof(SettingsViewModel)] = "settings",
        [typeof(TagManageViewModel)] = "tagManage",
        [typeof(AttributeManageViewModel)] = "attributeManage",
    };

    /// <summary>
    /// 画面の型の名前。知らない画面は <c>other</c>（新しい画面を足して書き忘れても、前の画面のままに見せない）。
    /// 主画面が作る型がどれもここか「画面ではない」の一覧に入っていることは、試験が主画面の命令列から型を集めて確かめる
    /// </summary>
    internal static string ScreenNameOf(Type type) => ScreenNames.GetValueOrDefault(type, "other");

    /// <summary>画面の英字の名前と、そこで開いている物（docs/spec/plugins.md の一覧）。画面がまだ無ければ null。</summary>
    internal static PluginScreen? StateOf(object? screen)
    {
        if (screen is null)
        {
            return null;
        }

        var name = ScreenNameOf(screen.GetType());
        return screen switch
        {
            ItemViewModel page => Plain(name, Item(page.Item.Id)),
            EditViewModel edit => Plain(name, (edit.ItemPage?.Item ?? edit.Item) is { } item ? Item(item.Id) : null),
            ShopViewModel shop => Plain(name, new PluginTarget("shop", shop.Shop.Subdomain)),
            FolderViewModel folder => Pane(name, (folder.RightItem.Page ?? folder.Detail as ItemViewModel) is { } page ? Item(page.Item.Id) : null),
            AvatarsViewModel avatars => Pane(name, avatars.RightItem.Page is { } page
                ? Item(page.Item.Id)
                : avatars.Selected is { } row ? new PluginTarget("avatar", row.ItemId) : null),
            ModificationHubViewModel hub => Pane(name, hub.RightItem.Page is { } page ? Item(page.Item.Id) : HubTarget(hub.Detail)),
            ModificationViewModel modification => Plain(name, new PluginTarget("modification", modification.Record.Id)),

            // タグ・属性の管理：右に重ねた商品ページだけ
            IRightPaneItemHost host => Pane(name, host.RightItem.Page is { } page ? Item(page.Item.Id) : null),
            _ => Plain(name, null),
        };
    }

    private static PluginTarget Item(string id) => new("item", id);

    /// <summary>画面そのものが開いている物（商品ページ・編集・ショップ）。</summary>
    private static PluginScreen Plain(string screen, PluginTarget? target) => new(screen, target, false);

    /// <summary>一覧の画面の右の欄に出ている物（行を選ぶたびに替わる）。何も出ていなければ埋め込みでもない。</summary>
    private static PluginScreen Pane(string screen, PluginTarget? target) => new(screen, target, target is not null);

    private static PluginTarget? HubTarget(object? detail) => detail switch
    {
        ModificationViewModel modification => new PluginTarget("modification", modification.Record.Id),
        HubItemDetail item => Item(item.ItemId),
        HubAvatarDetail avatar => new PluginTarget("avatar", avatar.AvatarItemId),
        _ => null,
    };
}
