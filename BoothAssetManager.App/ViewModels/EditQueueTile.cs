using System.Windows.Media.Imaging;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 編集画面の上の帯の1枚。どんな商品が続くかを絵で見せ、押すとその商品へ飛ぶ（ユーザ指示 2026-09-12）。
/// </summary>
public sealed class EditQueueTile : ViewModelBase
{
    /// <summary>編集の順番の中の位置。</summary>
    public required int Index { get; init; }

    public required string Name { get; init; }

    public bool IsCurrent { get; init; }

    public bool IsPast { get; init; }

    /// <summary>この回で保存した。</summary>
    public bool IsSaved { get; init; }

    /// <summary>通り過ぎたのに保存していない（飛ばした）。薄く出して、保存した物と見分ける。</summary>
    public bool IsSkipped => IsPast && !IsSaved;

    /// <summary>絵を読むもの。帯は幅に収まる分しか描かないので、描いた分だけ読む。</summary>
    public Func<Action, BitmapSource?>? ImageFactory { get; init; }

    public BitmapSource? Image => ImageFactory?.Invoke(() => OnPropertyChanged(nameof(Image)));

    public string ToolTip => $"{Index + 1} 件目　{Name}\n" + (IsCurrent
        ? "いま開いている商品"
        : (IsSaved ? "保存済み" : IsPast ? "保存せずに飛ばした" : "これから") + "（押すとこの商品へ移ります）");
}
