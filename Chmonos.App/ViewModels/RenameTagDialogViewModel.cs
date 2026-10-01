using System.Collections.ObjectModel;

namespace Chmonos.App.ViewModels;

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

    /// <summary>
    /// 欄に今打ってある文字。
    ///
    /// **既にある名前と同じ綴りになったら、候補から選んだのと同じに統合先にする**（点検 2026-09-23：そのまま打っても
    /// 統合と出ず、候補から選び直さないと気付けなかった）。既にある名前は表記の揺れを生まないので、選ばせる手間を省いてよい。
    /// 綴りは候補の側に揃える（大文字・小文字だけ違う打ち方で新しい名前にしない）。
    /// **無い名前は打っただけでは決めない**——候補の「新規」の行を選ばせる決まり（CLAUDE.md「入力欄には候補を付ける」）のまま。
    /// 決めた後に打ち直して別の文字になったら、決めた物を外す（欄と中身の食い違いを残さない）
    /// </summary>
    public string Input
    {
        get => _input;
        set
        {
            if (!SetField(ref _input, value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(CommitHint));

            var typed = _input.Trim();
            var existing = Candidates.FirstOrDefault(candidate =>
                string.Equals(candidate, typed, StringComparison.CurrentCultureIgnoreCase));
            if (existing is not null)
            {
                Target = existing;
            }
            else if (!string.Equals(typed, Target.Trim(), StringComparison.CurrentCulture))
            {
                Target = string.Empty;
            }
        }
    }

    private string _input = string.Empty;

    public string Target
    {
        get => _target;
        private set
        {
            if (SetField(ref _target, value))
            {
                foreach (var property in new[] { nameof(HasTarget), nameof(TargetText), nameof(ImpactText), nameof(UndoText), nameof(CommitHint), nameof(CommitText), nameof(IsMerge) })
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
            ? $"「{Target.Trim()}」は既にあるため、2つは1つに統合されます。"
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

    /// <summary>押せないときに、何が足りないかを書く（`ui-dialogs.md`・E9）。</summary>
    public string CommitHint => HasTarget
        ? string.Empty
        : IsTypedNewName
            ? $"「{_input.Trim()}」にするには、候補の「新規」の行を選ぶかEnterを押してください。"
            : "今と違う名前を入れると押せます。";

    /// <summary>打ってあるのが、まだ決めていない新しい名前か（押せない理由を言い分けるため）。</summary>
    private bool IsTypedNewName =>
        _input.Trim().Length > 0 && !string.Equals(_input.Trim(), Name, StringComparison.CurrentCulture);
}
