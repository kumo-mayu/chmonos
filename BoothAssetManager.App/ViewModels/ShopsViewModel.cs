using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>ショップ一覧の並べ替え方。</summary>
public sealed class ShopSortOption
{
    public required string Label { get; init; }

    public required Func<ShopSummary, IComparable> Key { get; init; }

    public bool Descending { get; init; }
}

/// <summary>ショップ一覧の1枚。</summary>
public sealed class ShopCardViewModel : ViewModelBase
{
    private BitmapSource? _icon;
    private Func<Action, BitmapSource?>? _iconFactory;

    public required ShopSummary Shop { get; init; }

    /// <summary>
    /// アイコンの読み方。**見えたカードだけが裏で読む**（検索カードと同じ・U12）。
    /// 以前は一覧を開くときに全店ぶん（友人データで151店）を画面のスレッドで原寸のまま読んでいた。
    /// 裏で取り終えたアイコンは差し替える。
    /// </summary>
    public Func<Action, BitmapSource?>? IconFactory
    {
        get => _iconFactory;
        set
        {
            _iconFactory = value;
            _icon = null;
            RaiseIcon();
        }
    }

    /// <summary>落としてあるアイコン。まだ無いか読み終わっていなければ頭文字のタイルで代える。</summary>
    public BitmapSource? Icon => _icon ??= _iconFactory?.Invoke(RaiseIcon);

    public bool HasIcon => Icon is not null;

    public bool ShowInitial => Icon is null;

    private void RaiseIcon()
    {
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(ShowInitial));
    }

    public string Name => Shop.Name;

    private bool _isFavorite;

    /// <summary>お気に入りのショップか（shops.json）。一覧のカードの星でも付け外しできる（ユーザ指示 2026-09-16）。</summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetField(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteGlyph));
                OnPropertyChanged(nameof(FavoriteTip));
            }
        }
    }

    public string FavoriteGlyph => _isFavorite ? "★" : "☆";

    public string FavoriteTip => _isFavorite ? "お気に入りのショップから外す" : "お気に入りのショップにする";

    public RelayCommand? ToggleFavoriteCommand { get; set; }

    /// <summary>ショップのメモ（shops.json）。一覧の検索で、ショップ名と合わせて探す。</summary>
    public string? Memo { get; init; }

    /// <summary>
    /// ショップのドメイン。**手元だけのショップには付けない**——
    /// BOOTHに無い鍵に .booth.pm を足すと、実在しないURLを名乗ることになる。
    /// </summary>
    public string DomainText => Core.Models.LocalShopKey.IsLocal(Shop.Subdomain)
        ? "BOOTHのショップに紐付いていません"
        : $"{Shop.Subdomain}.booth.pm";

    public string OwnedText => $"{Shop.OwnedCount}";

    public string SpentText => $"¥{Shop.SpentYen:N0}";

    public string LastAcquiredText => Shop.LastAcquiredAt?.ToString("yyyy-MM-dd") ?? "—";

    /// <summary>ファイルの日付で代えた日は、そうと分かるようにする。手入力と同じ顔で出さない。</summary>
    public bool LastAcquiredIsFallback => Shop.LastAcquiredIsFallback;

    public string LastAcquiredTooltip => Shop.LastAcquiredIsFallback
        ? "入手日が未入力なので、ファイルの日付を表示しています。"
        : string.Empty;

    /// <summary>情報はあるが所持していない商品数。0なら出さない。</summary>
    public bool HasUnowned => Shop.KnownCount > Shop.OwnedCount;

    public string UnownedText => $"ほかに情報だけ {Shop.KnownCount - Shop.OwnedCount} 件";

    public bool HasUpdate => Shop.UpdatedCount > 0;

    public string UpdateText => $"更新のあった商品が {Shop.UpdatedCount} 件";

    /// <summary>
    /// アイコンの代わり。ショップのアイコンはBOOTH側のURLしか持っておらず、
    /// ローカルには落としていない（商品画像と違って一覧を出すためだけに取りに行く価値が薄い）。
    /// 頭文字と、サブドメインから決まる色で見分けが付くようにする。
    /// </summary>
    public string Initial => Shop.Name.Length == 0 ? "?" : Shop.Name[..1];

    public System.Windows.Media.Brush InitialBrush => TileBrush(Shop.Subdomain);

    public RelayCommand? OpenCommand { get; set; }

    /// <summary>右クリックから、BOOTHのショップページをブラウザで開く（ユーザ指示 2026-09-20・M2）。URL が分からなければ押せない。</summary>
    public RelayCommand? OpenBoothCommand { get; set; }

    /// <summary>名前から色を決める。同じショップは常に同じ色になる。</summary>
    private static System.Windows.Media.Brush TileBrush(string key)
    {
        var hash = key.Aggregate(17, (current, character) => (current * 31) + character);
        var hue = Math.Abs(hash) % 360;

        var color = System.Windows.Media.ColorConverter.ConvertFromString(
            $"#{FromHue(hue):X6}");

        return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)color!);
    }

    /// <summary>彩度と明度は固定。背景として落ち着く範囲に収める。</summary>
    private static int FromHue(int hue)
    {
        var section = hue / 60;
        var offset = (hue % 60) / 60.0;

        const int Low = 0xC8;
        const int High = 0xE4;
        var rising = Low + (int)((High - Low) * offset);
        var falling = High - (int)((High - Low) * offset);

        return section switch
        {
            0 => (High << 16) | (rising << 8) | Low,
            1 => (falling << 16) | (High << 8) | Low,
            2 => (Low << 16) | (High << 8) | rising,
            3 => (Low << 16) | (falling << 8) | High,
            4 => (rising << 16) | (Low << 8) | High,
            _ => (High << 16) | (Low << 8) | falling,
        };
    }
}

/// <summary>ショップ一覧の1行。行を仮想化の単位にする（検索画面の <see cref="CardRow"/> と同じ作り）。</summary>
public sealed class ShopCardRow
{
    public ObservableCollection<ShopCardViewModel> Cards { get; } = [];
}

/// <summary>
/// ショップ一覧。
///
/// 数え方は決定事項に合わせてある（所持＝ファイルあり、非表示とR-18は件数から除く）。
/// 内部の扱いを隠さないよう、その但し書きは画面にも出す。
///
/// **開くたびに全商品のJSONを読み直さない**（ユーザ指示 2026-09-12：開くのが遅い）。
/// 検索画面が起動時に読んだ写しから数え、並べ方は検索画面で効いた軽量化に揃える
/// （行を単位にした仮想化・見えたカードだけが絵を裏で読む）。
/// </summary>
public sealed class ShopsViewModel : ViewModelBase, ILeavingScreen
{
    /// <summary>カード1枚ぶんの幅（カード304＋間14）。ShopsView.xaml のカードの Width と Margin に合わせる。</summary>
    private const double CardStride = 318;

    /// <summary>一覧の左右の余白（18×2）と縦のスクロールバーのぶん。</summary>
    private const double ListChrome = 36 + 18;

    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly Services.ThumbnailLoader _thumbnails;
    private readonly CancellationTokenSource _iconFetch = new();

    private List<ShopCardViewModel> _all = [];
    private List<ShopCardViewModel> _matches = [];
    private int _columns = 1;
    private string _iconStatus = string.Empty;
    private bool _isLoading;

    // 画面は開くたびに作り直すので、**絞り込みと並べ替えはアプリの側で覚える**
    // （ユーザ判断 2026-09-21・P5）。覚えないと、戻ってきたときに組んでいた絞り込みが消えていた。
    // フォルダの木の開き具合・分類と属性の選択と同じ作法（アプリを閉じるまで保つ）
    private static string s_filterText = string.Empty;
    private static bool s_searchNames = true;
    private static bool s_searchMemos = true;
    private static bool s_favoritesOnly;
    private static string? s_sortLabel;

    private string _filterText = s_filterText;
    private bool _searchNames = s_searchNames;
    private bool _searchMemos = s_searchMemos;
    private bool _favoritesOnly = s_favoritesOnly;
    private ShopSortOption _sort;

    public ShopsViewModel(AppServiceContainer services, MainViewModel main, Services.ThumbnailLoader thumbnails)
    {
        _services = services;
        _main = main;
        _thumbnails = thumbnails;

        SortOptions =
        [
            new ShopSortOption { Label = "所持が多い順", Key = shop => shop.OwnedCount, Descending = true },
            new ShopSortOption { Label = "支出が多い順", Key = shop => shop.SpentYen, Descending = true },
            new ShopSortOption
            {
                Label = "最終購入が新しい順",
                Key = shop => shop.LastAcquiredAt ?? DateOnly.MinValue,
                Descending = true,
            },
            new ShopSortOption { Label = "ショップ名順", Key = shop => shop.Name },
        ];

        // 覚えている並べ替えがあればそれで開く（P5）
        _sort = SortOptions.FirstOrDefault(option => option.Label == s_sortLabel) ?? SortOptions[0];

        // 「読み直す」は全商品を読み直してから数える。普段は写しから数えるので、
        // 手でJSONを直したときなどに最新にする道がここ
        RefreshCommand = new RelayCommand(() => RefreshAsync().Forget());

        ReloadAsync().Forget();
    }

    /// <summary>行に切った一覧。見えている行のカードだけが作られる。</summary>
    public ObservableCollection<ShopCardRow> Rows { get; } = [];

    public IReadOnlyList<ShopSortOption> SortOptions { get; }

    public RelayCommand RefreshCommand { get; }

    public ShopSortOption Sort
    {
        get => _sort;
        set
        {
            if (value is not null && SetField(ref _sort, value))
            {
                Rebuild();
            }
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
            {
                Rebuild();
            }
        }
    }

    /// <summary>
    /// 打った文字をショップ名（とサブドメイン）で探すか（ユーザ指示 2026-09-16：探す対象を選べるように）。
    /// 名前とメモの両方を切ると何も探せないので、最後の1つは外させない。
    /// </summary>
    public bool SearchNames
    {
        get => _searchNames;
        set => SetTarget(ref _searchNames, value, other: _searchMemos);
    }

    /// <summary>打った文字をショップのメモで探すか。</summary>
    public bool SearchMemos
    {
        get => _searchMemos;
        set => SetTarget(ref _searchMemos, value, other: _searchNames);
    }

    /// <summary>星を付けたショップだけを出す（ユーザ指示 2026-09-16）。</summary>
    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (SetField(ref _favoritesOnly, value))
            {
                Rebuild();
            }
        }
    }

    /// <summary>
    /// 今の探す対象を、対象のボタンそのものに書く（ユーザ指示 2026-09-16「対象がどちらかが常にわかるようにする必要がある」）。
    /// メニューを開かないと分からない形だと、メモで探しているつもりで名前だけを探していても気付けない。
    /// </summary>
    public string TargetsText => $"対象：{TargetNames} ▾";

    /// <summary>探す欄の透かし（打つ前にも、何で探すかが読める）。</summary>
    public string SearchPlaceholder => $"{TargetNames}から探す";

    private string TargetNames => string.Join("・", new[]
    {
        _searchNames ? "ショップ名" : null,
        _searchMemos ? "メモ" : null,
    }.OfType<string>());

    private void SetTarget(ref bool field, bool value, bool other)
    {
        if (field != value && (value || other))
        {
            field = value;
            Rebuild();
        }

        // 外せなかったときも、画面の印を今の値に戻す
        OnPropertyChanged(nameof(SearchNames));
        OnPropertyChanged(nameof(SearchMemos));
        OnPropertyChanged(nameof(TargetsText));
        OnPropertyChanged(nameof(SearchPlaceholder));
    }

    /// <summary>一覧のカードの星を切り替える。書くのはショップ画面と同じ命令で、検索の条件にも知らせる。</summary>
    private async Task ToggleFavoriteAsync(ShopCardViewModel card)
    {
        var next = !card.IsFavorite;
        card.IsFavorite = next;

        var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeShopNote(
            card.Shop.Subdomain, card.Shop.Name, card.Shop.Uuid, note => note with { IsFavorite = next }));

        if (result is Core.Commands.CommandResult.ShopNotesChanged changed)
        {
            _main.Search.NoteShopNotesChanged(changed.Notes);
        }

        // 「★だけ」のときに外したら、その場で一覧から消す
        if (_favoritesOnly)
        {
            Rebuild();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string HeaderText => $"{_all.Count} ショップ";

    public bool IsEmpty => !IsLoading && _matches.Count == 0;

    public string EmptyText => _all.Count == 0
        ? "ショップがありません"
        : _favoritesOnly && !_all.Any(card => card.IsFavorite)
            ? "星を付けたショップがまだありません。「★だけ」を外し、カードの☆を押すと付けられます。"
            : "該当するショップがありません。検索語を短くするか、「★だけ」や探す対象を見直してください。";

    /// <summary>一覧の幅から列数を決める（WPFには仮想化するWrapPanelが無いので、行に切って並べる）。</summary>
    public void SetViewportWidth(double width)
    {
        var columns = Math.Max(1, (int)((width - ListChrome) / CardStride));
        if (columns == _columns)
        {
            return;
        }

        _columns = columns;
        FillRows();
    }

    private async Task RefreshAsync()
    {
        await _main.ReloadLibraryAsync();
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            // 写しは画面のスレッドで取り出し、数えるのは裏で（ショップ151店・商品2000件でも画面を止めない）
            var items = _main.Search.SnapshotItems();
            var shops = await Task.Run(() => _services.Shops.Summarize(items));
            var notes = _services.Store.ShopNotes.Load();

            RunOnUiThread(() =>
            {
                _all = shops.Select(shop =>
                {
                    var note = Core.Services.ShopNotes.Of(notes, shop.Subdomain);
                    var card = new ShopCardViewModel { Shop = shop, IsFavorite = note?.IsFavorite == true, Memo = note?.Memo };
                    card.ToggleFavoriteCommand = new RelayCommand(() => ToggleFavoriteAsync(card).Forget());
                    if (shop.IconPath is { } path)
                    {
                        card.IconFactory = onLoaded => _thumbnails.PeekForTile(path, onLoaded);
                    }

                    card.OpenCommand = new RelayCommand(() => _main.ShowShop(shop));
                    card.OpenBoothCommand = new RelayCommand(
                        () => Services.Shell.OpenUrl(shop.Url!), () => !string.IsNullOrEmpty(shop.Url));
                    return card;
                }).ToList();

                Rebuild();
                OnPropertyChanged(nameof(HeaderText));
            });
        }
        finally
        {
            IsLoading = false;
            RunOnUiThread(() => OnPropertyChanged(nameof(IsEmpty)));
        }

        await FetchMissingIconsAsync();
    }

    /// <summary>
    /// まだ持っていないアイコンを裏で順に落とす。
    ///
    /// URLは商品JSONにしか入っていないので、取り込み済みのitemではアイコンだけが抜けている。
    /// 全部揃うまで画面を止めず、届いたものからその場で差し替える。
    /// BOOTHへは1件ずつ間隔を空けて行くので、店数が多いと時間がかかる。
    /// </summary>
    private async Task FetchMissingIconsAsync()
    {
        // 取りに行く店は保存側で選ぶ（手元に無い店）。
        // カードのアイコンを見に行って数えると、見えていないカードまで読み始める
        var needing = _services.Shops.ShopsNeedingIcons(_all.Select(card => card.Shop));
        var missing = needing.Count;
        if (missing == 0)
        {
            return;
        }

        IconStatus = $"ショップのアイコンを取得しています（残り {missing} 件）…";

        try
        {
            var done = 0;

            await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SyncShopIcons(
                needing,
                (subdomain, path) =>
                {
                    done++;
                    RunOnUiThread(() =>
                    {
                        var card = _all.FirstOrDefault(entry =>
                            string.Equals(entry.Shop.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));

                        if (card is not null)
                        {
                            card.IconFactory = onLoaded => _thumbnails.PeekForTile(path, onLoaded);
                        }

                        IconStatus = done >= missing
                            ? string.Empty
                            : $"ショップのアイコンを取得しています（残り {missing - done} 件）…";
                    });

                    return Task.CompletedTask;
                }),
                cancellationToken: _iconFetch.Token);
        }
        catch (OperationCanceledException)
        {
            // 画面を離れたら取りに行くのをやめる
        }
        finally
        {
            RunOnUiThread(() => IconStatus = string.Empty);
        }
    }

    /// <summary>画面を離れるときに呼ぶ。取得を続ける意味がないので止める。</summary>
    public void StopFetching() => _iconFetch.Cancel();

    /// <summary>離れたら、裏で走らせているアイコン取得を止める（結果は誰も見ない）。</summary>
    public void OnLeaving() => StopFetching();

    public string IconStatus
    {
        get => _iconStatus;
        private set
        {
            if (SetField(ref _iconStatus, value))
            {
                OnPropertyChanged(nameof(HasIconStatus));
            }
        }
    }

    public bool HasIconStatus => IconStatus.Length > 0;

    private void Rebuild()
    {
        // 組み直すたびに、今の絞り込みと並べ替えを覚え直す（開き直したときに同じ形で出す・P5）
        s_filterText = _filterText;
        s_searchNames = _searchNames;
        s_searchMemos = _searchMemos;
        s_favoritesOnly = _favoritesOnly;
        s_sortLabel = _sort.Label;

        var filter = _filterText.Trim();

        var matches = _all.Where(card =>
            (!_favoritesOnly || card.IsFavorite)
            && (filter.Length == 0
                || (_searchNames && (card.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                    || card.Shop.Subdomain.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                || (_searchMemos && card.Memo is { } memo && memo.Contains(filter, StringComparison.CurrentCultureIgnoreCase))));

        var sorted = _sort.Descending
            ? matches.OrderByDescending(card => _sort.Key(card.Shop))
            : matches.OrderBy(card => _sort.Key(card.Shop));

        _matches = sorted.ThenBy(card => card.Name, StringComparer.CurrentCulture).ToList();
        FillRows();

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>
    /// 行に切り直す。検索画面ほど詰めず（ずれた所だけ抜き差しする工夫はしない）、丸ごと作り直す。
    /// 店数は数百までで、作り直しても見えている行のカードしか描かれない。
    /// </summary>
    private void FillRows()
    {
        Rows.Clear();
        for (var start = 0; start < _matches.Count; start += _columns)
        {
            var row = new ShopCardRow();
            foreach (var card in _matches.Skip(start).Take(_columns))
            {
                row.Cards.Add(card);
            }

            Rows.Add(row);
        }
    }
}
