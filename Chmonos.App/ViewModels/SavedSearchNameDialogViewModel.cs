namespace Chmonos.App.ViewModels;

/// <summary>
/// 保存した検索の名前を決める小窓（今の検索を保存・名前を変更）。
/// 名前は新しく名付ける物なので候補は付けず、今ある名前と同じなら欄の下に「既にあります」と出して保存を押せなくする
/// （`.claude/rules/screen-and-wording.md`「新しく名付けて作る欄には付けない」）
/// </summary>
public sealed class SavedSearchNameDialogViewModel : ViewModelBase
{
    /// <summary>名前の長さの上限。節の行は1行で切るので、これより長い名前は読み切れない</summary>
    public const int MaxNameLength = 100;

    private readonly IReadOnlyList<string> _taken;
    private string _name;

    public SavedSearchNameDialogViewModel(Purpose purpose, string initialName, IReadOnlyList<string> takenNames)
    {
        For = purpose;
        _name = initialName;
        _taken = takenNames;
    }

    public enum Purpose
    {
        Save,
        Rename,
    }

    public Purpose For { get; }

    public string Title => For == Purpose.Save ? "今の検索を保存" : "名前を変更";

    public string CommitText => For == Purpose.Save ? "保存" : "変更";

    /// <summary>何が起きるか（保存）・取り返しがつくか（名前の変更・D4）。</summary>
    public string Note => For == Purpose.Save
        ? "条件、検索文字列、表示順などが保存されます。"
        : "名前は同じ手順で戻せます。";

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsTaken));
                OnPropertyChanged(nameof(CanCommit));
            }
        }
    }

    public bool IsTaken => _taken.Any(other => Core.Services.SavedSearches.SameName(other, _name));

    public bool CanCommit => _name.Trim().Length > 0 && !IsTaken;
}
