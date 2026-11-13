using Chmonos.App.Services;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 「Unityで選択」で、見つかった画像と unitypackage を木で選ぶ窓（ユーザ判断 2026-11-13：フォルダツリーにする形）。
/// 候補が少ないときは今までの平らな窓（<see cref="ListChoice"/>）のまま。分け方は <see cref="UnityPickTree.IsFlat"/>。
///
/// 木は字下げした平らな一覧で描く（フォルダビューの木と同じ作り）。WPF の TreeView はこのアプリに型が無く暗い表で既定の色のまま残り、
/// 入れ子の一覧は仮想化も効かない（`docs/dev/wpf.md`「一覧と速さ」）。
/// 開け閉め・←→・Enter の動きはここに置き、試験で確かめる
/// </summary>
public sealed class UnityPickDialogViewModel : ViewModelBase
{
    private readonly IReadOnlyList<UnityPickRow> _roots;
    private UnityPickRow? _selected;

    public UnityPickDialogViewModel(string title, IReadOnlyList<UnityImageMatch> images, IReadOnlyList<UnityPackageEntry> packages)
    {
        Title = title;
        _roots = UnityPickTree.Build(images, packages);
        UnityPickTree.ExpandInitially(_roots);
        Rows.ReplaceAll(UnityPickTree.Visible(_roots));
        _selected = UnityPickTree.FirstVisibleLeaf(_roots) ?? Rows.FirstOrDefault();
        ToggleCommand = new RelayCommand(parameter =>
        {
            if (parameter is UnityPickRow row)
            {
                Toggle(row);
            }
        });
    }

    public string Title { get; }

    /// <summary>窓の文。平らな窓と同じ問い。</summary>
    public const string Message = "どれを選択しますか？";

    public const string OkText = "これを選択";

    /// <summary>見えている行（畳んだフォルダの中は入れない）。</summary>
    public RangeObservableCollection<UnityPickRow> Rows { get; } = [];

    public IReadOnlyList<UnityPickRow> Roots => _roots;

    public RelayCommand ToggleCommand { get; }

    public UnityPickRow? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(CanPick));
            }
        }
    }

    /// <summary>決めるボタンを押せるか。フォルダの行では押せない（フォルダは選ぶ物ではない。Enter は開け閉めになる）。</summary>
    public bool CanPick => Selected?.IsLeaf == true;

    /// <summary>決めた行。葉のときだけ。</summary>
    public UnityPickRow? Picked => CanPick ? Selected : null;

    /// <summary>フォルダを開く・畳む。畳んだ中の行を選んでいたら、そのフォルダを選び直す（見えない行を選んだまま残さない）。</summary>
    public void Toggle(UnityPickRow row)
    {
        if (!row.CanExpand)
        {
            return;
        }

        row.IsExpanded = !row.IsExpanded;
        if (!row.IsExpanded && Selected is { } selected && IsInside(selected, row))
        {
            Selected = row;
        }

        Rows.ReplaceAll(UnityPickTree.Visible(_roots));
    }

    /// <summary>
    /// → の動き（エクスプローラの木と同じ）：畳んだフォルダは開く、開いたフォルダは最初の子へ移る、葉では何もしない。
    /// 返すのは次に選ぶ行（変わらなければ今の行）
    /// </summary>
    public UnityPickRow? Right()
    {
        if (Selected is not { CanExpand: true } row)
        {
            return Selected;
        }

        if (!row.IsExpanded)
        {
            Toggle(row);
            return row;
        }

        return Selected = row.Children.FirstOrDefault() ?? row;
    }

    /// <summary>← の動き：開いたフォルダは畳む、ほか（葉・畳んだフォルダ）は親のフォルダへ移る。根では何もしない。</summary>
    public UnityPickRow? Left()
    {
        if (Selected is not { } row)
        {
            return null;
        }

        if (row.CanExpand && row.IsExpanded)
        {
            Toggle(row);
            return row;
        }

        return Selected = row.Parent ?? row;
    }

    /// <summary>Enter・ダブルクリック：葉なら決める（true）、フォルダなら開け閉めする（false）。</summary>
    public bool Activate()
    {
        if (Selected is not { } row)
        {
            return false;
        }

        if (row.IsLeaf)
        {
            return true;
        }

        Toggle(row);
        return false;
    }

    private static bool IsInside(UnityPickRow row, UnityPickRow folder)
    {
        for (var at = row.Parent; at is not null; at = at.Parent)
        {
            if (at == folder)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 窓を出す代わりに答える口。**アプリでは null のまま**（窓を出す）。試験が入れる
    /// （窓を出すと答える人がいないので止まる。<see cref="ResetSettingsDialogViewModel.Intercept"/> と同じ）
    /// </summary>
    internal static Func<UnityPickDialogViewModel, UnityPickRow?>? Intercept { get; set; }

    /// <summary>窓で聞く。決めた葉を返す。やめたら null。</summary>
    public static UnityPickRow? Ask(string title, IReadOnlyList<UnityImageMatch> images, IReadOnlyList<UnityPackageEntry> packages)
    {
        var model = new UnityPickDialogViewModel(title, images, packages);
        var picked = Intercept is { } intercept
            ? intercept(model)
            : new Views.UnityPickDialog(model).ShowDialog() == true ? model.Picked : null;
        if (UiTrace.IsOn)
        {
            var chose = picked is null ? "やめた" : $"「{picked.ToolTip.Split('\n')[0]}」";
            UiTrace.Write("選ぶ", $"「{title}」{Message}｜木・画像 {images.Count} 件・unitypackage {packages.Count} 件 → {chose}");
        }

        return picked;
    }
}
