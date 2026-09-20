using System.Collections.ObjectModel;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 分類の名前を変える窓（ユーザ指示 2026-09-18：行に入力欄を常設せず、ボタンから開く）。
///
/// 既にある名前を選ぶと統合になる。**どちらも商品の書き換えを伴う**ので、
/// 押す前に何件が書き換わるかと、統合になるのかどうかを言う。
/// </summary>
public sealed class RenameTagDialogViewModel : ViewModelBase
{
    private string _target = string.Empty;

    /// <param name="kindText">「大分類」「小分類」「属性」（属性の管理も同じ窓を使う。操作感を揃えるため）。</param>
    /// <param name="itemCount">その分類が付いている商品の数。</param>
    /// <param name="candidates">統合先の候補（自分以外の、同じ階層の名前）。</param>
    public RenameTagDialogViewModel(string kindText, string name, int itemCount, IReadOnlyList<string> candidates)
    {
        KindText = kindText;
        Name = name;
        ItemCount = itemCount;

        foreach (var candidate in candidates)
        {
            Candidates.Add(candidate);
        }

        PickCommand = new RelayCommand(parameter => Target = parameter as string ?? string.Empty);
    }

    public string KindText { get; }

    public string Name { get; }

    public int ItemCount { get; }

    public ObservableCollection<string> Candidates { get; } = [];

    public RelayCommand PickCommand { get; }

    public string Title => $"{KindText}の名前を変更";

    public string HeadingText => ItemCount == 0
        ? $"「{Name}」の名前を変えます。まだどの商品にも付いていません。"
        : $"「{Name}」の名前を変えます。{ItemCount} 件の商品を書き換えます。";

    public string Target
    {
        get => _target;
        private set
        {
            if (SetField(ref _target, value))
            {
                foreach (var property in new[] { nameof(HasTarget), nameof(TargetText), nameof(ImpactText), nameof(UndoText), nameof(CommitText), nameof(IsMerge) })
                {
                    OnPropertyChanged(property);
                }
            }
        }
    }

    public bool HasTarget => Target.Trim().Length > 0 && Target.Trim() != Name;

    public bool IsMerge => Candidates.Any(candidate =>
        string.Equals(candidate, Target.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public string TargetText => HasTarget ? $"{Name} → {Target.Trim()}" : "新しい名前を入れてください";

    public string ImpactText => !HasTarget
        ? string.Empty
        : IsMerge
            ? $"「{Target.Trim()}」は既にあるので、2つは1つに統合されます。"
            : $"新しい名前になります。{KindText}を参照している商品は、まとめて書き換わります。";

    /// <summary>
    /// 取り返しがつくかを、押す前に必ず出す（`ui-rules.md`・D4：属性の統合の窓にだけ出ていた）。
    /// **統合と名前の変更で答えが違う。**名前は同じ手順で戻せるが、統合した2つは分け直せない
    /// （どちらの商品がどちらに付いていたかを残していない）。
    /// </summary>
    public string UndoText => !HasTarget
        ? string.Empty
        : IsMerge
            ? "この操作は元に戻せません。1つになった後は、どちらに付いていたかで分け直せません。"
            : "名前を変えるだけなら、同じ手順で元の名前に戻せます。";

    public string CommitText => IsMerge ? "統合する" : "名前を変える";
}
