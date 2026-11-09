using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 「推した通りにまとめて決める（n 件）」の窓（<c>docs/spec/conversion.md</c>「変換扱いの画面」・ユーザ判断 2026-11-09「押すと一覧の窓を出して外せる」）。
/// 確認したつなぎ先だけを変換経由に使うので、最初は何も出ない（外からの点検）。1件ずつ決める手間を省くが、押す前に対象を全部見せ、
/// 外したい物はチェックを外せる。押すのは人なので、推定を勝手に入れない決まりは守る。
/// </summary>
public sealed class ConfirmConversionsDialogViewModel : ViewModelBase
{
    public ConfirmConversionsDialogViewModel(IReadOnlyList<ConfirmConversionsSection> sections)
    {
        Sections = sections;
        foreach (var row in sections.SelectMany(section => section.Rows))
        {
            row.PropertyChanged += (_, _) => RaiseCount();
        }
    }

    /// <summary>
    /// 試験のときに窓を出さずに答える（窓を出すと答える人がいないので止まる。<see cref="PickPackagesDialogViewModel.Intercept"/> と同じ）。
    /// </summary>
    internal static Func<ConfirmConversionsDialogViewModel, bool>? Intercept { get; set; }

    /// <summary>選ばせる。「確認済みにする」なら true（チェックは各行の <see cref="ConfirmConversionsRow.IsChecked"/> に残る）。</summary>
    public static bool Ask(ConfirmConversionsDialogViewModel model)
        => Intercept is { } intercept ? intercept(model) : new Views.ConfirmConversionsDialog(model).ShowDialog() == true;

    public IReadOnlyList<ConfirmConversionsSection> Sections { get; }

    public int CheckedCount => Sections.Sum(section => section.Rows.Count(row => row.IsChecked));

    public bool CanCommit => CheckedCount > 0;

    public string CommitText => $"確認済みにする（{CheckedCount} 件）";

    /// <summary>押せないときの理由（E9）。押せるときは空。</summary>
    public string CommitHint => CanCommit ? string.Empty : "チェックが1つもありません。";

    private void RaiseCount()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CommitText));
        OnPropertyChanged(nameof(CommitHint));
    }
}

/// <summary>窓の中の商品1つ（見出しは商品の名前）。</summary>
public sealed class ConfirmConversionsSection
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<ConfirmConversionsRow> Rows { get; init; }
}

/// <summary>確認待ちのつなぎ先1つ。最初はチェックを付けておく（まとめて決めるための窓なので、外す物の方が少ない）。</summary>
public sealed class ConfirmConversionsRow : ViewModelBase
{
    private bool _isChecked = true;

    public required ConversionTarget Target { get; init; }

    /// <summary>「fakeaoi（順方向）→ アオイ」の形。</summary>
    public required string Text { get; init; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }
}
