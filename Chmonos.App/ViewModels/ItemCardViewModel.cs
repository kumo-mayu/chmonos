using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using Chmonos.App.Services;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>サムネイル下端に並べる区切り。今どの画像を見ているかを示す。</summary>
public sealed class ThumbnailSegment : ViewModelBase
{
    private bool _isActive;

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }
}

/// <summary>
/// 検索結果に並ぶ1件。
///
/// サムネイルはマウスの横位置で切り替える（BOOTHのサイトと同じ操作感）。
/// BOOTHはカードあたり4枚程度に絞って先読みしているが、
/// こちらはギャラリー画像を全てローカルに持っているので全部使える。
/// 復号は最初にマウスが乗ったときだけ行う。
/// </summary>
public sealed class ItemCardViewModel : ViewModelBase
{
    /// <summary>
    /// 1区切りに最低これだけの幅を確保する。
    /// 画像が20枚あるようなitemでカード幅を等分すると1区切り10px程度になり、
    /// 狙って止められず画像がちらつくだけになるため。
    /// </summary>
    private const double MinimumSegmentWidth = 28;

    private readonly ThumbnailLoader _thumbnails;
    private readonly string _imageDirectory;
    private IReadOnlyList<string>? _imageFiles;
    private string? _activePath;
    private int _activeIndex;
    private int _stepCount;

    public ItemCardViewModel(
        ItemRecord item,
        ThumbnailLoader thumbnails,
        string imageDirectory,
        ThumbnailRole thumbnailRole = ThumbnailRole.Default)
    {
        Item = item;
        _isFavorite = item.Local.IsFavorite;
        _thumbnails = thumbnails;
        _imageDirectory = imageDirectory;
        ThumbnailRole = thumbnailRole;
    }

    public ItemRecord Item { get; }

    /// <summary>
    /// 右クリックの「BOOTHで開く」「リンクをコピー」を押せるか。**BOOTHに無い商品として登録した物には開く先が無い**。
    /// 前は押せて、黙って何も起きなかった（商品ページ側は同じ条件で塞いでいた。`docs/spec/ui-rules.md`：効かない選択肢は出さない）
    /// </summary>
    public bool HasBoothPage => !Item.IsLocalOnly;

    public string OpenBoothTip => Item.IsLocalOnly
        ? "BOOTHに無い商品なので開けません。"
        : "BOOTHの商品ページをブラウザで開きます。アプリの外へ出ます。";

    public string CopyLinkTip => Item.IsLocalOnly
        ? "BOOTHに無い商品なので、リンクがありません。"
        : "BOOTHの商品ページのリンクをコピーします。";

    /// <summary>ショップの画面（アプリの中）へ行けるか。BOOTHの店が分からない商品では行き先が無い。</summary>
    public bool HasShop => !string.IsNullOrWhiteSpace(Item.Booth.Shop?.Subdomain);

    public string OpenShopTip => HasShop
        ? "このショップの商品を、アプリの中のショップ画面で開きます。"
        : "ショップが分からない商品なので開けません。";

    private bool _isFavorite;

    /// <summary>
    /// お気に入りの星（#70）。**押した瞬間に変える**——保存を待ってから変えると、押したのに反応しないように見える。
    /// 書けなかったら検索画面が元に戻す。
    /// </summary>
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

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public string FavoriteTip => IsFavorite ? "お気に入りから外す" : "お気に入りに入れる";

    /// <summary>
    /// どの役割の画像をサムネイルに出すか。設定から来る。
    ///
    /// カードを組むときに渡す。設定を変えたら一覧を組み直すので、
    /// カード側で見張る必要はない。
    /// </summary>
    public ThumbnailRole ThumbnailRole { get; }

    private bool _isSelected;
    private bool _isSelectionMode;

    /// <summary>まとめて編集へ送るための選択。カードは持ち回すので状態も残る。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                SelectionChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// 1件以上選ばれている状態か。
    /// このときカード全体が選択の的になり、商品ページへは専用のボタンから移る。
    /// 選ぶ操作の最中に、少しずれただけで別画面へ飛ばされるのを防ぐため。
    /// </summary>
    public bool IsSelectionMode
    {
        get => _isSelectionMode;
        set => SetField(ref _isSelectionMode, value);
    }

    /// <summary>
    /// 選ぶ箱を出すか。選んだカードをまとめて送る先の無い画面（アバターの管理）では出さない
    /// （メモ9-⑤ 2026-10-02：アバターのカードに乗せると選ぶ箱が出て、何枚でも印が付けられたが、選んでも何もできなかった）
    /// </summary>
    public bool CanSelect { get; set; } = true;

    /// <summary>選んだか外したか。選んでいるカードの「更新あり」の札が変わったときにも知らせる（まとめ操作の出し分けが変わる）。</summary>
    public event Action? SelectionChanged;

    public required string Name { get; init; }

    public string ShopName { get; init; } = string.Empty;

    public string SizeText { get; init; } = string.Empty;

    public bool IsOwned { get; init; }

    public bool NeedsEdit { get; init; }

    /// <summary>取り込みの③（対応アバターの検出）がまだ。「未編集」の代わりに「取り込み中」と出す（U8・U10）。</summary>
    public bool IsAwaitingDetection { get; init; }

    private bool _hasUpdate;
    private RelayCommand? _markUpdateRead;

    /// <summary>
    /// 要確認に未読の「商品の更新」がある。入れるのはショップの画面（ユーザ判断 2026-09-27：
    /// ショップの一覧に「更新のあった商品が1件」と出ても、ショップの中でどれか分からなかった）と、
    /// 検索の画面・フォルダビューの右の欄（ユーザ判断 2026-10-02「検索にも出しましょう」）。ほかの画面のカードには入れない。
    /// 既読にすると下ろす（カードは作り直さずに使い回すので、変えられるようにしてある）
    /// </summary>
    public bool HasUpdate
    {
        get => _hasUpdate;
        set
        {
            if (SetField(ref _hasUpdate, value))
            {
                OnPropertyChanged(nameof(CanMarkUpdateRead));

                // 選んだ物の中に未読の更新があるかで、まとめ操作の「既読にする」を出し分ける。
                // 選んだカードの札が変わったら（ほかの画面で既読にした・知らせが届いた）、選んだ物が変わったのと同じに数え直させる
                if (_isSelected)
                {
                    SelectionChanged?.Invoke();
                }
            }
        }
    }

    /// <summary>「更新あり」を押したとき。<see cref="HasUpdate"/> を入れる画面だけが渡す</summary>
    public RelayCommand? ShowUpdateCommand
    {
        get => _showUpdate;
        set => SetField(ref _showUpdate, value);
    }

    private RelayCommand? _showUpdate;

    /// <summary>
    /// 右クリックの「既読にする」（ユーザ指示 2026-10-02）。この商品の未読の更新の知らせを既読にする（商品ページの「既読にする」と同じ命令）。
    /// <see cref="HasUpdate"/> を入れる画面だけが渡す
    /// </summary>
    public RelayCommand? MarkUpdateReadCommand
    {
        get => _markUpdateRead;
        set
        {
            if (SetField(ref _markUpdateRead, value))
            {
                OnPropertyChanged(nameof(CanMarkUpdateRead));
            }
        }
    }

    /// <summary>右クリックに「既読にする」を出すか（未読の更新があり、既読にする道を渡されている）。</summary>
    public bool CanMarkUpdateRead => _hasUpdate && _markUpdateRead is not null;

    private bool _isImagePending;

    /// <summary>
    /// 取り込みの途中で、絵がまだ1枚も無い。枠に「画像を取得中」と出す（U8）。
    /// 絵が届いたら <see cref="RefreshImages"/> で下ろす。
    /// 絵を読んでいる途中（裏で復号中）かどうかでは決めない——それだと読むたびに文字がちらつく
    /// </summary>
    public bool IsImagePending
    {
        get => _isImagePending;
        init => _isImagePending = value;
    }

    public bool HasMissingFile { get; init; }

    public string UserTagText { get; init; } = string.Empty;

    /// <summary>
    /// カードの下に出すユーザタグの1行。小分類を出すときは「大分類：小分類・小分類」にする（ユーザ判断 2026-09-16）——
    /// 小分類だけだと、どの大分類の下の物かがカードから分からない。
    /// </summary>
    public static string UserTagLine(IEnumerable<UserTagAssignment> tags, bool withSubs)
        => string.Join(" / ", tags.Select(tag => withSubs && tag.Subs.Count > 0
            ? $"{tag.Top}：{string.Join("・", tag.Subs)}"
            : tag.Top));

    public bool HasUserTag => UserTagText.Length > 0;

    /// <summary>
    /// 表示中の画像。値を持たず、読むたびにキャッシュへ問い合わせる。
    ///
    /// カード自身が復号結果を抱えると、画面外へスクロールしても解放されず、
    /// キャッシュ側で上限を設けた意味が無くなるため。
    /// 実際に読むのは仮想化で実体化されたカードだけになる。
    /// </summary>
    public BitmapSource? Thumbnail => _activePath is null
        ? FirstImage()
        : _thumbnails.LoadForCard(_activePath);

    /// <summary>
    /// 静止しているときに出す1枚目。
    ///
    /// 保存名は元URLのハッシュなので、ディレクトリ順に取るとBOOTHの1枚目とは限らない
    /// （実データでは13件中9件が違っていた）。BOOTHの並びを正にする。
    ///
    /// 覚え込まないのは、画像が後から届く場合があるため
    /// （届いた時点で出せるようにしておく）。
    /// </summary>
    /// <summary>止まっているときに出す1枚の場所。指名があればそれ、無ければ並びの1枚目（役割の指定があればそちらが勝つ）。</summary>
    private string? RestingImagePath()
    {
        // 一度見つかったら覚える。描き直すたびにフォルダの時刻を見て並べ直していた（Thumbnail は描き直し・知らせのたびに読まれる）。
        // 見つからない間は覚えない——絵が後から届いたときに出せるように。届いた・並びが変わったときは
        // RefreshImages（裏の取得の知らせ）か、記録が変わってカードごと作り直されるので、覚えたままにはならない
        if (_restingPath is { } remembered)
        {
            return remembered;
        }

        _restingPath = FindRestingImagePath();
        return _restingPath;
    }

    private string? _restingPath;

    private string? FindRestingImagePath()
    {
        var ordered = Chmonos.Core.Images.ItemImageOrder.Arrange(
            _imageDirectory,
            Item.Booth.Images,
            _thumbnails.ListFiles(_imageDirectory),
            Item.Local.UserImages);

        return Chmonos.Core.Images.ItemImageOrder
            .Thumbnail(ordered, Item.Local.ThumbnailImage, ThumbnailRole, Item.Local.ImageRoles);
    }

    /// <summary>リストで、フォルダの行と商品の行を見分ける（フォルダビューの右側は両方を1つの一覧に並べる）。</summary>
    public bool IsFolder => false;

    private BitmapSource? FirstImage()
    {
        var path = RestingImagePath();

        // 手元に無ければ裏で読み、その間は枠の薄い灰色のまま描く（U12）。
        // 画面のスレッドで読むと、速いスクロールで新しい行が出るたびに止まった。
        // なぞって送るとき（_activePath）は今までどおりその場で読む——裏へ回すと、なぞるたびに灰色がちらつく
        if (path is null)
        {
            return null;
        }

        // 速く流している間に頼んだ絵は小さい。止まったら正規の大きさで頼み直す（U27）
        _peekedWhileFast |= _thumbnails.IsFastScrolling;
        return _thumbnails.PeekForCard(path, () => OnPropertyChanged(nameof(Thumbnail)));
    }

    private bool _peekedWhileFast;

    /// <summary>
    /// 絵を読む大きさが変わった（一覧の右下のスライダーで、カードの大きさが読む大きさの刻みを越えた）。頼み直させる。
    /// 画面に出ていないカードは誰も聞いていないので、呼んでも読みには行かない
    /// </summary>
    public void NoteCardEdgeChanged() => OnPropertyChanged(nameof(Thumbnail));

    /// <summary>
    /// スクロールが止まった。速く流している間に小さく読んだカードだけ、正規の大きさで頼み直させる。
    /// 読み終わるまでは小さい絵をそのまま出しておくので、灰色には戻らない
    /// </summary>
    public void NoteScrollSettled()
    {
        if (_peekedWhileFast)
        {
            _peekedWhileFast = false;
            OnPropertyChanged(nameof(Thumbnail));
        }
    }

    /// <summary>今どの画像を見ているかの目印。画像が2枚以上あるときだけ出す。</summary>
    public ObservableCollection<ThumbnailSegment> Segments { get; } = [];

    public bool HasMultipleImages => Segments.Count > 1;

    /// <summary>ギャラリー画像の総数。マウスの区切り数より多いことがある。</summary>
    public int ImageCount { get; private set; }

    public int CurrentImageNumber { get; private set; } = 1;

    /// <summary>「3 / 12」のような表示。区切りを絞っているときに全体像が分かるようにする。</summary>
    public string CounterText => ImageCount > 1 ? $"{CurrentImageNumber} / {ImageCount}" : string.Empty;

    private bool _isHovering;

    /// <summary>マウスが乗っている間だけ枚数表示を出す。</summary>
    public bool IsHovering
    {
        get => _isHovering;
        private set => SetField(ref _isHovering, value);
    }

    /// <summary>
    /// カード上のマウス位置（0.0〜1.0）に対応する画像へ切り替える。
    /// 画像の一覧と復号は、最初に呼ばれたときにまとめて用意する。
    /// </summary>
    /// <param name="widthPixels">サムネイル領域の幅。区切りをいくつに分けられるかの判断に使う。</param>
    public void ShowImageAt(double ratio, double widthPixels)
    {
        EnsureImagesLoaded(widthPixels);
        if (_imageFiles is null || _stepCount <= 1)
        {
            return;
        }

        var step = (int)(Math.Clamp(ratio, 0, 0.9999) * _stepCount);

        // 段数を絞っている場合は、ギャラリー全体から等間隔で拾う
        var index = _stepCount == _imageFiles.Count
            ? step
            : (int)((long)step * _imageFiles.Count / _stepCount);

        SetIndex(step, Math.Clamp(index, 0, _imageFiles.Count - 1));
    }

    /// <summary>
    /// マウスが離れたら、**止まっているときの1枚**に戻す。
    ///
    /// 先頭に戻すのではない。サムネイルに指名した絵があればそれが
    /// 「止まっているときの姿」なので、そこへ戻さないと
    /// 一度なぞっただけで指名が無かったことになる。
    /// </summary>
    public void ResetImage()
    {
        // 早抜けの前に下ろす。画像が1枚の商品はここで抜けるので、「乗っている」が残ったまま
        // 選択のチェックと中身の無い枚数の札（黒い丸）が出続けていた（U11）
        IsHovering = false;

        if (_imageFiles is null || _stepCount <= 1)
        {
            return;
        }

        SetIndex(0, 0);

        // 指名があるなら、なぞる前の姿は指名した1枚。
        // _activePath を空にすると FirstImage() が選び直す
        if (!string.IsNullOrEmpty(Item.Local.ThumbnailImage))
        {
            _activePath = null;
            OnPropertyChanged(nameof(Thumbnail));
        }
    }

    /// <summary>
    /// 画像が後から届いたときに、描き直させる。
    ///
    /// なぞるための一覧（<see cref="_imageFiles"/>）は最初に乗ったときに作って持っているので、
    /// 捨てないと増えた分をなぞれない。止まっている1枚は <see cref="Thumbnail"/> が毎回選び直す。
    /// </summary>
    public void RefreshImages()
    {
        _imageFiles = null;
        _restingPath = null;
        _activePath = null;
        _activeIndex = 0;
        _stepCount = 0;
        Segments.Clear();
        ImageCount = 0;
        CurrentImageNumber = 1;
        _isImagePending = false;

        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(IsImagePending));
        OnPropertyChanged(nameof(HasMultipleImages));
        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(CounterText));
    }

    private void EnsureImagesLoaded(double widthPixels)
    {
        IsHovering = true;

        if (_imageFiles is not null)
        {
            return;
        }

        // 保存名はURLのハッシュなので、名前順に読むと1枚目が乱数で決まる。
        // BOOTHの並びを正にして、サムネイルが商品ページと一致するようにする
        _imageFiles = Chmonos.Core.Images.ItemImageOrder.Paths(
            _imageDirectory,
            Item.Booth.Images,
            _thumbnails.ListFiles(_imageDirectory),
            Item.Local.UserImages);
        ImageCount = _imageFiles.Count;

        var maxSteps = Math.Max(1, (int)(widthPixels / MinimumSegmentWidth));
        _stepCount = Math.Min(_imageFiles.Count, maxSteps);

        if (_stepCount > 1)
        {
            for (var i = 0; i < _stepCount; i++)
            {
                Segments.Add(new ThumbnailSegment { IsActive = i == 0 });
            }

            OnPropertyChanged(nameof(HasMultipleImages));
        }

        OnPropertyChanged(nameof(ImageCount));
        OnPropertyChanged(nameof(CounterText));
    }

    private void SetIndex(int step, int imageIndex)
    {
        if (step == _activeIndex || _imageFiles is null || step < 0 || step >= Segments.Count)
        {
            return;
        }

        var path = _imageFiles[imageIndex];
        if (_thumbnails.LoadForCard(path) is null)
        {
            return;
        }

        Segments[_activeIndex].IsActive = false;
        _activeIndex = step;
        Segments[_activeIndex].IsActive = true;
        _activePath = path;
        CurrentImageNumber = imageIndex + 1;
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(CounterText));
    }
}
