namespace Chmonos.App.ViewModels;

/// <summary>
/// 改変を作る・複製するときに名前を聞く小窓（feedback 2026-11-18・ユーザ判断「上に『新しい改変』のボタンだけを置き、名前はウィンドウで聞く。
/// 複製もウィンドウで、複製元の名前を入れて開く」）。
/// 前は右のアバターの枠の下端に名前の欄があり、改変が多いと流した先になり、欄の Enter でも作れなかった。複製は名前を聞かずに「〇〇のコピー」で作っていた。
/// 改変は同じアバターに同じ名前を許すので、同じ名前でも押せるままにし、欄の下で知らせるだけにする（前は作る時に別の窓で聞き直していた）
/// </summary>
public sealed class ModificationNameDialogViewModel : ViewModelBase
{
    /// <summary>名前の長さの上限。左の一覧の行は1行で切るので、これより長い名前は読み切れない（アバターの管理の欄と同じ 60）</summary>
    public const int MaxNameLength = 60;

    private readonly IReadOnlyList<string> _taken;
    private string _name;

    public ModificationNameDialogViewModel(Purpose purpose, string avatarName, string initialName, IReadOnlyList<string> takenNames)
    {
        For = purpose;
        AvatarName = avatarName;
        _name = initialName;
        _taken = takenNames;
    }

    public enum Purpose
    {
        Create,
        Duplicate,
    }

    public Purpose For { get; }

    public string AvatarName { get; }

    public string Title => For == Purpose.Create ? "新しい改変" : "改変を複製";

    public string CommitText => For == Purpose.Create ? "作る" : "複製";

    /// <summary>どのアバターの改変か。改変はアバター1体に属する</summary>
    public string AvatarText => $"{AvatarName}の改変";

    /// <summary>何ができるか（作る）・何がコピーされるか（複製）。</summary>
    public string Note => For == Purpose.Create
        ? "「普段着」「制服」のように、着せ替え1つにつき1つ作ります。"
        : "使ったもの・紐付けたプロジェクト・メモをコピーします。写真はコピーしません。";

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

    /// <summary>同じアバターに同じ名前の改変があるか。比べ方は Core の <c>HasSameNameAsync</c> と同じ（前後の空白を除き、大文字小文字を区別しない）</summary>
    public bool IsTaken => _name.Trim() is { Length: > 0 } trimmed
        && _taken.Any(other => string.Equals(other, trimmed, StringComparison.CurrentCultureIgnoreCase));

    public bool CanCommit => _name.Trim().Length > 0;

    /// <summary>
    /// 試験の差し替え口。窓を出すと答える人がいないので止まる（<see cref="Services.Notice.Intercept"/> と同じ）
    /// </summary>
    internal static Func<ModificationNameDialogViewModel, bool>? Intercept { get; set; }

    /// <summary>窓を出して聞く。確定したら true（名前は <see cref="Name"/>）。</summary>
    public static bool Ask(ModificationNameDialogViewModel model)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.ModificationNameDialog(model).ShowDialog() == true;
}
