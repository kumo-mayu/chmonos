using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

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

    public ItemCardViewModel(ItemRecord item, ThumbnailLoader thumbnails, string imageDirectory)
    {
        Item = item;
        _thumbnails = thumbnails;
        _imageDirectory = imageDirectory;
    }

    public ItemRecord Item { get; }

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

    public event Action? SelectionChanged;

    public required string Name { get; init; }

    public string ShopName { get; init; } = string.Empty;

    public string SizeText { get; init; } = string.Empty;

    public bool IsOwned { get; init; }

    public bool NeedsEdit { get; init; }

    public bool HasMissingFile { get; init; }

    public string UserTagText { get; init; } = string.Empty;

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
        : _thumbnails.Load(_activePath);

    /// <summary>
    /// 静止しているときに出す1枚目。
    ///
    /// 保存名は元URLのハッシュなので、ディレクトリ順に取るとBOOTHの1枚目とは限らない
    /// （実データでは13件中9件が違っていた）。BOOTHの並びを正にする。
    ///
    /// 覚え込まないのは、画像が後から届く場合があるため
    /// （届いた時点で出せるようにしておく）。
    /// </summary>
    private BitmapSource? FirstImage()
    {
        // 指名があればそれをカードに出す。無ければ並びの1枚目
        var ordered = BoothAssetManager.Core.Images.ItemImageOrder.Arrange(
            _imageDirectory,
            Item.Booth.Images,
            _thumbnails.ListFiles(_imageDirectory),
            Item.Local.UserImages);

        var path = BoothAssetManager.Core.Images.ItemImageOrder
            .Thumbnail(ordered, Item.Local.ThumbnailImage);

        return path is null ? null : _thumbnails.Load(path);
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
        if (_imageFiles is null || _stepCount <= 1)
        {
            return;
        }

        SetIndex(0, 0);
        IsHovering = false;

        // 指名があるなら、なぞる前の姿は指名した1枚。
        // _activePath を空にすると FirstImage() が選び直す
        if (!string.IsNullOrEmpty(Item.Local.ThumbnailImage))
        {
            _activePath = null;
            OnPropertyChanged(nameof(Thumbnail));
        }
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
        _imageFiles = BoothAssetManager.Core.Images.ItemImageOrder.Paths(
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
        if (_thumbnails.Load(path) is null)
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
