using System.Windows;
using BoothAssetManager.Core.Models;

namespace ViewShot;

/// <summary>
/// 描く物1つ（どの画面か部品を、どの状態で）。<see cref="Scenes.All"/> に登録する。
/// </summary>
/// <param name="Name">コマンドで指す名前（英小文字とハイフン。画像のファイル名にもなる）。</param>
/// <param name="Title">何の場面か。一覧に出す。</param>
/// <param name="Build">作り物のデータを書き、ViewModel を組み、描く部品を返す。</param>
internal sealed record Scene(string Name, string Title, Func<SceneContext, Task<Shot>> Build)
{
    /// <summary>
    /// 既定の幅（DIP）。null は中身に合わせる（小窓・部品）。
    /// 1280 は主の窓の既定（1440）と最小（900）の間で、ナビ・一覧・右の欄が既定の幅のまま並ぶ幅。
    /// </summary>
    public double? Width { get; init; } = 1280;

    /// <summary>既定の高さ（DIP。題の帯は入らない）。null は中身に合わせる。</summary>
    public double? Height { get; init; } = 800;
}

/// <summary>場面が返す物。</summary>
/// <param name="Root">舞台に載せる部品（主の窓の中身・小窓の中身・部品1つ）。</param>
internal sealed record Shot(FrameworkElement Root)
{
    /// <summary>
    /// 見たい所。返した部品の四角で切り出す（窓全体の画像は重く、見たい所が小さくなる）。
    /// 並べ終わってから探すので関数で渡す。null なら全体。<c>--full</c> で切らずに出せる
    /// </summary>
    public Func<FrameworkElement?>? Focus { get; init; }

    /// <summary>切り出す四角の周りに残す余白（DIP）。隣の部品との間が見えるように。</summary>
    public double FocusMargin { get; init; } = 12;

    /// <summary>
    /// 場面が自分で描いたコマ。在れば、落ち着くのを待って描き直さずに、これをそのまま出す（<c>--crop</c> は効く）。
    /// 「最初の配置で何が出るか」のように、待つと消える途中の姿を見る場面が使う（<see cref="SceneContext.PresentFirstFrame"/>）。
    /// 色・幅・倍率の組み合わせは回せない（場面を組んだときの1つだけ）
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? Still { get; init; }
}

/// <summary>1回の実行で描く組み合わせ。</summary>
internal sealed record ShotOptions
{
    public IReadOnlyList<ColorThemeMode> Themes { get; init; } = [ColorThemeMode.Light];

    /// <summary>幅（DIP）。空なら場面の既定。</summary>
    public IReadOnlyList<double> Widths { get; init; } = [];

    /// <summary>今描いている幅。null は場面の既定。</summary>
    public double? Width { get; init; }

    public double? Height { get; init; }

    /// <summary>表示の倍率（Windows の拡大率に当たる。1 = 100%）。</summary>
    public IReadOnlyList<double> Scales { get; init; } = [1.0];

    /// <summary>アプリの設定の「表示の大きさ」（%）。</summary>
    public int ZoomPercent { get; init; } = DisplayZoom.DefaultPercent;

    /// <summary>切り出す四角（DIP）。場面の「見たい所」より優先する。</summary>
    public Rect? Crop { get; init; }

    /// <summary>場面の「見たい所」で切らず、全体を出す。</summary>
    public bool Full { get; init; }

    public required string OutDir { get; init; }

    /// <summary>複数の場面を並べて走らせる数。</summary>
    public int Jobs { get; init; } = 3;
}
