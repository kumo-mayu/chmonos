using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>選べる表示の大きさの1つ（設定画面の一覧の1行）。</summary>
public sealed class ZoomOption
{
    public required int Percent { get; init; }

    public string Label => DisplayZoom.Label(Percent);

    /// <summary>読み上げと自動操作から見える名前。既定だと型名になる</summary>
    public override string ToString() => Label;
}

/// <summary>
/// アプリの中の「表示の大きさ」（ユーザ指示 2026-09-29：違うモニターの大きさ・解像度でも使えるように）。
/// Windows の拡大率とは別に、アプリの中だけを大きく・小さくする。設定の <c>displayZoomPercent</c> に覚える。
///
/// 当て方は窓の中身を丸ごと縮尺する（`LayoutTransform`）。画面ごとの文字の大きさを1つずつ変えると、
/// 決まった幅（ナビ・列の最小・カード）と文字の大きさがずれて崩れるが、丸ごとなら並びは 100% と同じまま大きくなる。
/// 主の窓は <c>MainWindow</c>、小窓は <see cref="DialogFit"/>、メニューと吹き出しは App.xaml の既定の見た目が
/// ここの倍率（<c>AppZoomTransform</c>）を読む。絵は画面の拡大率×この倍率で読む（<see cref="DisplayScale"/>）。
///
/// **窓の最小の大きさは変えない**（DIP のまま・<c>MainWindow.MinimumWidth</c>）。最小は画面の大きさで決まり、
/// 倍率を上げても画面は広くならない。上げた分は中身の側で送る（本文は横に・ナビと一覧は縦に）。
/// </summary>
public sealed class AppZoom : ViewModelBase
{
    /// <summary>覚えるまでの待ち。Ctrl＋＋ を続けて押す間は書かず、止まってから1回（一覧の大きさのスライダーと同じ 0.4 秒）。</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly AppServiceContainer? _services;
    private readonly Debounced? _save;
    private int _percent;

    private AppZoom(AppServiceContainer? services, int percent)
    {
        _services = services;
        _percent = DisplayZoom.Normalize(percent);
        if (services is not null)
        {
            _save = new Debounced(SaveDelay, SaveAsync);
            Apply();
        }
    }

    /// <summary>今の値。起動のときに <see cref="Initialize"/> で設定から作り直す（それまでは 100%）。</summary>
    public static AppZoom Current { get; private set; } = new(null, DisplayZoom.DefaultPercent);

    /// <summary>設定から作る。画面のスレッドで、主の窓を作る前に1回（主の窓は作るときにこの倍率を当てる）。</summary>
    public static void Initialize(AppServiceContainer services)
        => Current = new AppZoom(services, services.Settings.DisplayZoomPercent);

    public static IReadOnlyList<ZoomOption> Options { get; } =
        DisplayZoom.Steps.Select(step => new ZoomOption { Percent = step }).ToList();

    /// <summary>表示の大きさ（%）。変えるとその場で全体の大きさが変わる。</summary>
    public int Percent
    {
        get => _percent;
        set
        {
            if (SetField(ref _percent, DisplayZoom.Normalize(value)))
            {
                Apply();
                _save?.Request();
            }
        }
    }

    public void ZoomIn() => Percent = DisplayZoom.Next(_percent);

    public void ZoomOut() => Percent = DisplayZoom.Previous(_percent);

    public void Reset() => Percent = DisplayZoom.DefaultPercent;

    /// <summary>待っている保存を今書く（閉じる前に呼ぶ）。</summary>
    public Task FlushAsync() => _save?.RunNowAsync() ?? Task.CompletedTask;

    /// <summary>
    /// 倍率を当てる。見た目の側（主の窓・メニュー・吹き出し）は App の資源 <c>AppZoomTransform</c> を読み、
    /// 絵を読む倍率は <see cref="DisplayScale"/> が知らせる（見えているカードが読み直す）
    /// </summary>
    private void Apply()
    {
        var zoom = _percent / 100.0;
        if (System.Windows.Application.Current is { } app)
        {
            var transform = new System.Windows.Media.ScaleTransform(zoom, zoom);
            transform.Freeze();
            app.Resources[TransformResourceKey] = transform;
        }

        DisplayScale.SetZoom(zoom);
    }

    /// <summary>倍率の縮尺を置く App の資源の名前（主の窓の中身・メニュー・吹き出しが DynamicResource で読む）。</summary>
    public const string TransformResourceKey = "AppZoomTransform";

    private async Task SaveAsync()
    {
        if (_services is null)
        {
            return;
        }

        // 画面のスレッドで値を写してから渡す（当てるのは錠の中で、別のスレッドのことがある）
        var percent = _percent;
        await _services.Commands.ExecuteAsync(new UiCommand.ChangeSettings(settings => settings with
        {
            DisplayZoomPercent = percent,
        }));
    }
}
