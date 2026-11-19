namespace Chmonos.App.ViewModels;

/// <summary>
/// ファイルを持たない「BOOTHに無い商品」を名前だけで作るときに、名前を聞く小窓（2026-11-20 のメモ「ファイルの無いlocalItemが作成できない気がする」）。
/// 新しく名付ける欄なので候補は付けない（screen-and-wording の例外）。商品は同じ名前を許すので、同じ名前を断らない
/// </summary>
public sealed class LocalItemNameDialogViewModel : ViewModelBase
{
    // 長さの上限は置かない。未確定の「BOOTHに無い商品として登録する」の名前の欄も置いておらず、入口によって切り方を変えないため
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanCommit));
            }
        }
    }

    public bool CanCommit => _name.Trim().Length > 0;

    /// <summary>試験の差し替え口。窓を出すと答える人がいないので止まる（<see cref="ModificationNameDialogViewModel.Intercept"/> と同じ）</summary>
    internal static Func<LocalItemNameDialogViewModel, bool>? Intercept { get; set; }

    /// <summary>窓を出して聞く。確定したら true（名前は <see cref="Name"/>）。</summary>
    public static bool Ask(LocalItemNameDialogViewModel model)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.LocalItemNameDialog(model).ShowDialog() == true;
}
