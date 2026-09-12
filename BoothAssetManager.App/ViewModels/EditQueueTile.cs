using System.Windows.Media.Imaging;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 編集画面の上の帯の1枚。どんな商品が続くかを絵で見せ、押すとその商品へ飛ぶ（ユーザ指示 2026-09-12）。
///
/// 名前も絵も、帯に作られたときに初めて引く。帯は見えている分しか作らないので、
/// 2000件の順番でも引くのは画面に出た数十件だけで済む。
/// </summary>
public sealed class EditQueueTile : ViewModelBase
{
    /// <summary>編集の順番の中の位置。</summary>
    public required int Index { get; init; }

    public Func<string>? NameFactory { get; init; }

    public string Name => NameFactory?.Invoke() ?? string.Empty;

    public bool IsCurrent { get; init; }

    public bool IsPast { get; init; }

    /// <summary>この回で保存した。</summary>
    public bool IsSaved { get; init; }

    /// <summary>保存していない入力が残っている（書きかけ）。左上に印を出す。</summary>
    public bool IsDraft { get; init; }

    /// <summary>通り過ぎたのに保存していない（飛ばした）。薄く出して、保存した物と見分ける。書きかけは薄くしない。</summary>
    public bool IsSkipped => IsPast && !IsSaved && !IsDraft;

    public Func<Action, BitmapSource?>? ImageFactory { get; init; }

    public BitmapSource? Image => ImageFactory?.Invoke(() => OnPropertyChanged(nameof(Image)));

    /// <summary>乗せたときに大きく出す絵（ユーザ指示）。カードの大きさで読む。出したときに初めて読む。</summary>
    public Func<Action, BitmapSource?>? PreviewFactory { get; init; }

    public BitmapSource? Preview => PreviewFactory?.Invoke(() => OnPropertyChanged(nameof(Preview)));

    public string Title => $"{Index + 1} 件目　{Name}";

    public string StateText => IsCurrent
        ? "いま開いている商品"
        : (IsSaved ? "保存済み" : IsDraft ? "編集途中（保存していない入力があります）" : IsPast ? "保存せずに飛ばした" : "これから")
            + "（押すとこの商品へ移ります）";
}
