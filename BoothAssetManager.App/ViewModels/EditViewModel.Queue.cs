using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>編集画面：上の帯と、商品の間の移動（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class EditViewModel
{
    // ---- 上の帯：どんな商品が続くか（ユーザ指示 2026-09-12） ----
    //
    // 「n / N 件」とバーだけでは何が続くのか分からないので、右の空きに続く商品を小さな絵で並べる。
    // 済んだ物は5件まで、これからの物は幅に収まるだけ。保存した物には印を付け、押すとその商品へ飛ぶ

    /// <summary>開いたときに見せる済んだ物の数（ユーザ指示）。帯には全部並び、ホイールで遡れる。</summary>
    private const int PastTileCount = 5;

    /// <summary>この回で保存した商品。飛ばした物と見分けるため。</summary>
    private HashSet<string> _saved = new(StringComparer.Ordinal);

    /// <summary>
    /// 帯の絵。1件進むたびに差し替える（1枚ずつ足し引きすると、2000件の順番で2000回の知らせが飛ぶ）。
    /// 帯は見えている分しか作らないので、名前と絵も作られた分しか引かない。
    /// </summary>
    public IReadOnlyList<EditQueueTile> QueueTiles { get; private set; } = [];

    /// <summary>
    /// 「購入した種類」の欄を開いているか。種類の多い商品では膨大になるので畳める（ユーザ指示）。
    /// 編集画面は開くたびに作り直されるので、アプリを閉じるまでここに持つ（次の商品へ進んでも畳んだまま）。
    /// </summary>
    private static bool s_isPurchasesExpanded = true;

    public bool IsPurchasesExpanded
    {
        get => s_isPurchasesExpanded;
        set
        {
            if (s_isPurchasesExpanded != value)
            {
                s_isPurchasesExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>見出しに添える種類の数。畳んでいても何件あるかは分かるように。</summary>
    public string PurchasesCountText => Variations.Count > 0 ? $"（{Variations.Count} 件）" : string.Empty;

    /// <summary>
    /// 買った種類にまだ1つも印が付いていないか。欄を畳んでいても見出しで分かるようにする（ユーザ指示 2026-09-13）。
    /// 種類の一覧が無い商品では出さない
    /// </summary>
    public bool IsPurchaseUnselected => Variations.Count > 0 && !Variations.Any(row => row.IsPurchased);

    /// <summary>上の帯を出すか。要らない人もいるので設定で消せる（ユーザ指示）。</summary>
    public bool ShowsQueueStrip => _services.Settings.ShowEditQueueStrip;

    private RelayCommand? _goFirstCommand;

    /// <summary>1件目へ戻る（ユーザ指示）。いま開いている商品の入力は保存しない（スキップと同じ）。</summary>
    public RelayCommand GoFirstCommand => _goFirstCommand ??= new RelayCommand(
        () => JumpAsync(0).Forget(),
        () => _queue.Count > 0 && _index != 0 && !IsSaving);

    private RelayCommand? _goFirstUnsavedCommand;

    /// <summary>
    /// まだ保存していない物のうち、いちばん前へ飛ぶ（ユーザ指示）。
    /// 飛ばしながら進んだ後で、残した物を頭から片付けるため。
    /// </summary>
    public RelayCommand GoFirstUnsavedCommand => _goFirstUnsavedCommand ??= new RelayCommand(
        () =>
        {
            if (FirstUnsavedIndex() is { } index)
            {
                JumpAsync(index).Forget();
            }
        },
        () => !IsSaving && FirstUnsavedIndex() is { } index && index != _index);

    private int? FirstUnsavedIndex()
    {
        for (var index = 0; index < _queue.Count; index++)
        {
            if (!_saved.Contains(_queue[index]))
            {
                return index;
            }
        }

        return null;
    }

    private RelayCommand? _jumpCommand;

    /// <summary>帯の絵を押すと、その商品へ飛ぶ。いま開いている商品の入力は保存しない（スキップと同じ）。</summary>
    public RelayCommand JumpCommand => _jumpCommand ??= new RelayCommand(
        parameter =>
        {
            if (parameter is EditQueueTile tile)
            {
                JumpAsync(tile.Index).Forget();
            }
        },
        parameter => parameter is EditQueueTile && !IsSaving && !IsMoving);

    private async Task JumpAsync(int index)
    {
        if (index == _index || index < 0 || index >= _queue.Count || IsMoving)
        {
            return;
        }

        RememberStep();
        await MoveToAsync(index);
    }

    /// <summary>画面の履歴から戻ってきたとき。履歴には積まない（戻るで積むと、戻った先から戻れなくなる）。</summary>
    /// <param name="itemId">そのとき開いていた商品。順番の中に無ければ（IDを変えた等）位置で開く。</param>
    public Task ShowStepAsync(string? itemId, int fallbackIndex)
    {
        var at = itemId is null ? -1 : _queue.IndexOf(itemId);
        var target = at >= 0 ? at : Math.Clamp(fallbackIndex, 0, _queue.Count);
        if (target != _index)
        {
            return MoveToAsync(target);
        }

        // 行き先が今と同じでも、**数え上げは止める**。止める処理が MoveToAsync の中にしか無く、
        // たまたま同じ位置へ戻ったときだけ素通りして、数秒後に勝手に検索へ飛んでいた
        StopReturnTimer();
        return Task.CompletedTask;
    }

    private async Task MoveToAsync(int index)
    {
        // 次・前と同じく、動いている間の2度目は受けない。帯で飛ぶのは移る印を立てていなかったので、
        // スキップの直後や連打で読み込みが二重に走り、2本が同じ位置を別々に進めて1件飛び得た
        if (IsMoving)
        {
            return;
        }

        IsMoving = true;
        try
        {
            StopReturnTimer();
            CaptureDraft();
            _index = index;
            await SavePositionAsync();
            await LoadCurrentAsync();
        }
        finally
        {
            IsMoving = false;
        }
    }

    /// <summary>
    /// 別の商品へ移る前に、今の商品を画面の履歴に積む（ユーザ指示 2026-09-12）。
    /// 保存して次へ・スキップ・前へ・帯で飛ぶのどれでも、Alt＋← で直前に開いていた商品へ戻れる
    /// </summary>
    private void RememberStep()
    {
        if (_item is not null)
        {
            _main.RememberEditStep(this);
        }
    }

    private void RebuildQueueTiles()
    {
        // 1件目から最後まで全部並べる（ユーザ判断）。以前は済んだ物を5件で切っていたので、
        // 最後の方の件を開くと帯が数枚になり、送る分が無くてホイールが効かないように見えた。
        // 開いたときに見せる所（済んだ5件＋今の商品が左端）は、画面の側が送って合わせる
        var tiles = new List<EditQueueTile>(_queue.Count);

        for (var index = 0; index < _queue.Count; index++)
        {
            var itemId = _queue[index];

            // 名前も絵も、帯に作られたときに初めて引く。検索が読んである写しから引き、
            // 1件進むたびにJSONを読み直さない（#71 と同じ理由）
            tiles.Add(new EditQueueTile
            {
                Index = index,
                NameFactory = () => _main.Search.FindItem(itemId)?.DisplayName ?? itemId,
                IsCurrent = index == _index,
                IsPast = index < _index,
                IsSaved = _saved.Contains(itemId),
                IsDraft = _main.Drafts.Contains(itemId),
                ImageFactory = onLoaded => TileImage(itemId, onLoaded, preview: false),
                PreviewFactory = onLoaded => TileImage(itemId, onLoaded, preview: true),
                CardFactory = () => _main.Search.CardFor(itemId),
            });
        }

        QueueTiles = tiles;
        OnPropertyChanged(nameof(QueueTiles));
        OnPropertyChanged(nameof(QueueFirstVisibleIndex));
    }

    /// <summary>
    /// 開いたときに帯の左端へ来る絵の位置。済んだ物を5件見せ、その次が今の商品になる（ユーザ指示）。
    /// 最後の方では帯の右端で止まるので、実際にはもっと前から見える。
    /// </summary>
    public int QueueFirstVisibleIndex => Math.Max(0, _index - PastTileCount);

    /// <summary>
    /// 帯の絵。検索のカードと同じ1枚（指名・役割の設定を見る）にそろえる。
    /// 乗せたときに大きく出す方（<paramref name="preview"/>）はカードの大きさで読む。
    /// </summary>
    private BitmapSource? TileImage(string itemId, Action onLoaded, bool preview)
    {
        if (_main.Search.FindItem(itemId) is not { } record)
        {
            return null;
        }

        var directory = _services.Paths.ItemImagesDir(record.Id);
        var ordered = Core.Images.ItemImageOrder.Arrange(
            directory, record.Booth.Images, _thumbnails.ListFiles(directory), record.Local.UserImages);
        var path = Core.Images.ItemImageOrder.Thumbnail(
            ordered, record.Local.ThumbnailImage, _services.Settings.ThumbnailRole, record.Local.ImageRoles);

        if (path is null)
        {
            return null;
        }

        return preview ? _thumbnails.PeekForCard(path, onLoaded) : _thumbnails.PeekForTile(path, onLoaded);
    }

    private async Task<List<string>> BuildDefaultQueueAsync()
    {
        var loaded = await _services.Store.Items.LoadAllAsync();
        var unedited = loaded.Items
            .Where(item => item.Local.UserTags.Count == 0)
            // 取り込みの③がまだの商品は積まない（U8・U10）。③が済めば次に開いたときに入る
            .Where(item => !_main.IsAwaitingDetection(item.Id));

        // 検索の既定・サムネイルを取る順と同じ規則で並べる（ItemOrder.ByAcquired。2026-09-21 ユーザ判断）。
        // 入手日は人が手で入れたときにしか入らないので、取り込んだばかりの商品は全部同じ値になる。
        // 以前はここに同着の決め手が無く、items フォルダの列挙順（＝商品IDの順）がそのまま出ていて、
        // 検索（商品名順）ともサムネイルを取る順（走査した順）とも違う並びになっていた
        return ItemOrder.ByAcquired(unedited, descending: true)
            .Select(item => item.Id)
            .ToList();
    }
}
