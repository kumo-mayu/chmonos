using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 一覧の右下のスライダーが変える大きさ（ユーザ判断 2026-09-29）。カードで出しているときはカードの幅、
/// リストで出しているときは行の高さ。**値はアプリに1つで、カードとリストを切り替える画面すべてが同じ値を見る**
/// （画面ごとに覚えると、ある画面で決めた大きさを別の画面で決め直すことになる）。設定の <c>cardWidth</c>・<c>listRowHeight</c> に覚える。
///
/// カードとリストで値を分けて持つのは、同じ目盛で両方を動かすと、カードに合わせた大きさでリストの行が大きくなりすぎるため。
/// 見た目はここを x:Static で読み（弱い参照の知らせなので、使い捨ての画面が残っても離れる）、列の割りは <see cref="CardMetrics"/> を読む。
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

    private ItemViewSize(AppServiceContainer? services, AppSettings settings)
    {
        _services = services;
        _listRowHeight = ClampRow(settings.ListRowHeight);
        CardMetrics.Apply(settings.CardWidth);
        if (services is not null)
        {
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
                _save?.Request();
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
                _save?.Request();
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

    private async Task SaveAsync()
    {
        if (_services is null)
        {
            return;
        }

        // 画面のスレッドで値を写してから渡す（当てるのは錠の中で、別のスレッドのことがある）
        var cardWidth = (int)CardMetrics.Width;
        var rowHeight = (int)_listRowHeight;
        await _services.Commands.ExecuteAsync(new UiCommand.ChangeSettings(settings => settings with
        {
            CardWidth = cardWidth,
            ListRowHeight = rowHeight,
        }));
    }
}
