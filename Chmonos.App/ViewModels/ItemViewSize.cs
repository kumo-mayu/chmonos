using Chmonos.App.Services;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 一覧の右下のスライダーが変える大きさ（ユーザ判断 2026-09-29）。カードで出しているときはカードの幅、
/// リストで出しているときは行の高さ。
///
/// **既定は画面ごとに覚える**（ユーザ指示 2026-11-18「画面毎に保持した方が直感的かもしれない。デフォは独立」）。
/// 設定で切ると、前の形（値はアプリに1つで、どの画面で動かしても全部が同じ大きさ）になる。設定の <c>cardWidth</c>・<c>listRowHeight</c> が
/// その1つの値で、画面ごとの値が無い画面もこれで開く（<c>itemViewSizes</c>）。
///
/// 開いている画面は常に1つなので、値の置き場（<see cref="CardMetrics"/> とこのインスタンス）は1つのまま、
/// **画面が替わる時に、その画面の値を置き場へ当てる**（<see cref="UseScreen"/>）。見た目は今まで通りここを x:Static で読み
/// （弱い参照の知らせなので、使い捨ての画面が残っても離れる）、列の割りは <see cref="CardMetrics"/> を読む。
/// インスタンスは起動の後に差し替えない（x:Static の束縛と列の購読が、このインスタンスに付いている）。
///
/// カードとリストで値を分けて持つのは、同じ目盛で両方を動かすと、カードに合わせた大きさでリストの行が大きくなりすぎるため。
/// </summary>
public sealed class ItemViewSize : ViewModelBase
{
    /// <summary>
    /// 行の高さの範囲。下は絵が見分けられる小ささ（絵28）、上は前に絵の列を最大（120）まで広げたときの絵の大きさ（104）＋余白。
    /// 絵はカードと同じ大きさ（240DIP 前後）で読むので、この範囲ならぼやけない
    /// </summary>
    public const double MinListRowHeight = 40;

    public const double MaxListRowHeight = 116;

    public const double DefaultListRowHeight = 52;

    /// <summary>行の上下の余白（5＋5）と区切りの線（1）に少し足した分。行の高さからこれを引いた正方形が絵になる。</summary>
    private const double RowChrome = 12;

    /// <summary>絵の列の左右の余白。前は「列の幅 − 16 が絵」だったので同じにする（列の幅は絵に合わせて決まる）。</summary>
    private const double IconColumnPadding = 16;

    /// <summary>
    /// 覚えるまでの待ち。ドラッグの間は値が1秒に数十回変わる。見た目はすぐ変え、書くのは止まってから1回
    /// （画面の幅の境目と同じ 0.4 秒）
    /// </summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly AppServiceContainer? _services;
    private readonly Debounced? _save;
    private double _listRowHeight;

    /// <summary>
    /// 画面ごとの値。動かした時点でここへ入れ、書くのは止まってから。**画面の鍵は動かした時点で決める**——
    /// 書くまでの 0.4 秒の間に別の画面へ移っても、前の画面の値が次の画面の鍵に入らない
    /// </summary>
    private readonly Dictionary<string, ItemViewSizeEntry> _sizes;

    /// <summary>1つの値（設定の <c>cardWidth</c>・<c>listRowHeight</c>）。画面ごとの値が無い画面もこれで開く。</summary>
    private int _sharedCardWidth;

    private int _sharedRowHeight;

    private bool _perScreen;

    /// <summary>今の値が誰の物か（スライダーのある画面の鍵）。スライダーの無い画面へ移っても替えない。</summary>
    private string? _screen;

    private ItemViewSize(AppServiceContainer? services, AppSettings settings)
    {
        _services = services;
        _listRowHeight = ClampRow(settings.ListRowHeight);
        _sharedCardWidth = (int)CardMetrics.Clamp(settings.CardWidth);
        _sharedRowHeight = (int)_listRowHeight;
        _perScreen = settings.ItemViewSizePerScreen;
        _sizes = new Dictionary<string, ItemViewSizeEntry>(settings.ItemViewSizes ?? new Dictionary<string, ItemViewSizeEntry>(), StringComparer.Ordinal);

        // 設定を持たない既定の値（Current の初期化子）はカードの大きさに触れない。静的な初期化子は、Initialize で
        // Current へ代入する瞬間に遅れて走ることがあり、ここで当てると読んだ設定の幅を既定の228で上書きしていた
        // （起動し直すとスライダーの大きさを忘れた。2026-09-29 に画面で確かめて見つけた）。CardMetrics の既定も同じ228
        if (services is not null)
        {
            CardMetrics.Apply(settings.CardWidth);
            CardMetrics.ApplyShowAttributes(settings.ShowCardAttributes);
            _save = new Debounced(SaveDelay, SaveAsync);
        }
    }

    /// <summary>
    /// 今の値。起動のときに <see cref="Initialize"/> で設定から作り直す（それまでは既定の大きさ）。
    /// XAML が x:Static で読むので、画面を作る前に作っておく
    /// </summary>
    public static ItemViewSize Current { get; private set; } = new(null, new AppSettings());

    /// <summary>設定から作る。画面のスレッドで、画面を作る前に1回（覚えるための時計を画面のスレッドに置くため）。</summary>
    public static void Initialize(AppServiceContainer services) => Current = new ItemViewSize(services, services.Settings);

    public static double MinCardWidth => CardMetrics.MinWidth;

    public static double MaxCardWidth => CardMetrics.MaxWidth;

    /// <summary>
    /// 画面の鍵（カードかリストかを覚える鍵 <c>ItemListMode</c> と同じ名前）。大きさのスライダーを持たない画面は null で、移っても値を替えない。
    /// 改変は一覧の画面と1つの改変の画面で同じ鍵（右の欄に1つの改変を組み込むので、行き来で大きさが変わらないように）
    /// </summary>
    public static string? ScreenKeyOf(object? screen) => screen switch
    {
        SearchViewModel => "search",
        ShopViewModel => "shop",
        FolderViewModel => "folder",
        ModificationHubViewModel or ModificationViewModel => "modification",
        TagManageViewModel => "tag",
        AttributeManageViewModel => "attribute",
        AvatarsViewModel => "avatar",
        SettingsViewModel => "settings-hidden",
        _ => null,
    };

    /// <summary>
    /// 画面が替わる時に呼ぶ（主画面が、新しい画面を組む前に）。画面ごとに覚えているなら、その画面の値を当てる。
    /// 1回で正しい大きさに組まれるよう、画面を差し替える前に呼ぶ
    /// </summary>
    public void UseScreen(object? screen)
    {
        if (ScreenKeyOf(screen) is not { } key || key == _screen)
        {
            return;
        }

        _screen = key;
        if (_perScreen)
        {
            var entry = _sizes.GetValueOrDefault(key);
            Show(entry?.CardWidth ?? _sharedCardWidth, entry?.ListRowHeight ?? _sharedRowHeight);
        }
    }

    /// <summary>
    /// 画面ごとに覚えるか（設定の画面のチェック）。
    ///
    /// 入れた時は画面ごとの値を空にし、今の1つの値を全部の画面の最初の値にする（ユーザ判断 2026-11-19）。
    /// 切った時は、前から持っている1つの値に戻す（画面ごとに動かした値は1つの値へ写さない。どの画面の値を写すかは決めていない）。
    /// 切り替えと値は1回で書く（同じファイル・同じ錠。2回に分けると、間に閉じた時に片方だけ残る）
    /// </summary>
    public bool PerScreen
    {
        get => _perScreen;
        set
        {
            if (_perScreen == value)
            {
                return;
            }

            _perScreen = value;
            OnPropertyChanged();
            _save?.Cancel();
            if (value)
            {
                _sizes.Clear();
            }

            Show(_sharedCardWidth, _sharedRowHeight);
            SaveAsync().Forget();
        }
    }

    /// <summary>
    /// 設定の「既定に戻す」。画面ごとに覚えた大きさも消し、切り替えも既定（画面ごと）に戻す。
    /// 1つの値だけ戻すと、画面ごとの値が残った画面は戻らず「戻したのに大きいまま」になる
    /// </summary>
    public void ResetToDefaults(AppSettings defaults)
    {
        _save?.Cancel();
        _sizes.Clear();
        _sharedCardWidth = (int)CardMetrics.Clamp(defaults.CardWidth);
        _sharedRowHeight = (int)ClampRow(defaults.ListRowHeight);
        if (_perScreen != defaults.ItemViewSizePerScreen)
        {
            _perScreen = defaults.ItemViewSizePerScreen;
            OnPropertyChanged(nameof(PerScreen));
        }

        Show(_sharedCardWidth, _sharedRowHeight);
        SaveAsync().Forget();
    }

    /// <summary>カードの幅。変えるとその場で全部のカードが大きさを変え、列数が変わる画面は割り直す。</summary>
    public double CardWidth
    {
        get => CardMetrics.Width;
        set
        {
            var before = CardMetrics.Width;
            CardMetrics.Apply(value);
            if (CardMetrics.Width != before)
            {
                OnPropertyChanged();
                Remember();
            }
        }
    }

    /// <summary>リストの1行の高さ。</summary>
    public double ListRowHeight
    {
        get => _listRowHeight;
        set
        {
            if (SetField(ref _listRowHeight, ClampRow(value)))
            {
                OnPropertyChanged(nameof(ListIconSize));
                OnPropertyChanged(nameof(ListIconColumnWidth));
                Remember();
            }
        }
    }

    /// <summary>行の頭の絵の一辺。行の高さに合わせて変わる。</summary>
    public double ListIconSize => _listRowHeight - RowChrome;

    /// <summary>
    /// 絵の列の幅。絵に合わせて決まる。見出しの境目をドラッグして広げたときは、絵と行の高さを変える
    /// （ユーザ指示 2026-09-14「絵の欄を大きくしたら縦の幅も変える」をそのまま残す。スライダーも一緒に動く）
    /// </summary>
    public double ListIconColumnWidth
    {
        get => ListIconSize + IconColumnPadding;
        set
        {
            if (double.IsNaN(value) || value <= 0)
            {
                return;
            }

            var before = _listRowHeight;
            ListRowHeight = value - IconColumnPadding + RowChrome;

            // 範囲の外まで引かれて値が変わらなかったときも、列を範囲の端へ戻して見せる
            if (_listRowHeight == before)
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>待っている保存を今書く（閉じる前に呼ぶ。ドラッグを止めてすぐ閉じると 0.4 秒の待ちごと捨てられる）。</summary>
    public Task FlushAsync() => _save?.RunNowAsync() ?? Task.CompletedTask;

    private static double ClampRow(double height)
        => double.IsNaN(height) ? DefaultListRowHeight : Math.Clamp(Math.Round(height), MinListRowHeight, MaxListRowHeight);

    /// <summary>人が動かした値を、今の画面（画面ごとのとき）か1つの値に入れ、止まってから書く。</summary>
    private void Remember()
    {
        var cardWidth = (int)CardMetrics.Width;
        var rowHeight = (int)_listRowHeight;
        if (_perScreen && _screen is { } key)
        {
            _sizes[key] = new ItemViewSizeEntry { CardWidth = cardWidth, ListRowHeight = rowHeight };
        }
        else
        {
            _sharedCardWidth = cardWidth;
            _sharedRowHeight = rowHeight;
        }

        _save?.Request();
    }

    /// <summary>覚えている値を置き場へ当てる（書かない。画面を替えた・設定を切り替えただけで、人は動かしていない）。</summary>
    private void Show(int cardWidth, int rowHeight)
    {
        var before = CardMetrics.Width;
        CardMetrics.Apply(cardWidth);
        if (CardMetrics.Width != before)
        {
            OnPropertyChanged(nameof(CardWidth));
        }

        if (SetField(ref _listRowHeight, ClampRow(rowHeight), nameof(ListRowHeight)))
        {
            OnPropertyChanged(nameof(ListIconSize));
            OnPropertyChanged(nameof(ListIconColumnWidth));
        }
    }

    private async Task SaveAsync()
    {
        if (_services is null)
        {
            return;
        }

        // 画面のスレッドで値を写してから渡す（当てるのは錠の中で、別のスレッドのことがある）。
        // この3つの欄はここだけが書くので、覚えている値をそのまま書く
        var perScreen = _perScreen;
        var cardWidth = _sharedCardWidth;
        var rowHeight = _sharedRowHeight;
        IReadOnlyDictionary<string, ItemViewSizeEntry>? sizes = _sizes.Count == 0
            ? null
            : new Dictionary<string, ItemViewSizeEntry>(_sizes, StringComparer.Ordinal);
        await _services.Commands.ExecuteAsync(new UiCommand.ChangeSettings(settings => settings with
        {
            ItemViewSizePerScreen = perScreen,
            ItemViewSizes = sizes,
            CardWidth = cardWidth,
            ListRowHeight = rowHeight,
        }));
    }
}
