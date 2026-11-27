namespace Chmonos.App.ViewModels;

/// <summary>
/// 保存した検索の小窓（今の検索を保存・条件を上書き・名前を変更・フォルダ）。
/// 名前は新しく名付ける物なので候補は付けず、今ある名前と同じなら欄の下に「既にあります」と出して保存を押せなくする
/// （`.claude/rules/screen-and-wording.md`「新しく名付けて作る欄には付けない」）。
/// 保存と上書きでは、検索文字列・表示順・カードかリストかを含めるかをチェックで選ぶ（ユーザ判断 2026-11-27・案1）
/// </summary>
public sealed class SavedSearchNameDialogViewModel : ViewModelBase
{
    /// <summary>名前の長さの上限。節の行は1行で切るので、これより長い名前は読み切れない</summary>
    public const int MaxNameLength = 100;

    private readonly IReadOnlyList<string> _taken;
    private string _name;
    private bool _includeText = true;
    private bool _includeSort = true;
    private bool _includeView = true;

    public SavedSearchNameDialogViewModel(Purpose purpose, string initialName, IReadOnlyList<string> takenNames, bool hasText = false)
    {
        For = purpose;
        _name = initialName;
        _taken = takenNames;
        HasText = hasText;
    }

    public enum Purpose
    {
        Save,
        Rename,

        /// <summary>新しいフォルダを名付けて、そこへ移す（2026-11-27）。今あるフォルダの名前は「既にあります」（今あるフォルダはメニューから選ぶ）</summary>
        NewFolder,

        /// <summary>フォルダの名前を変更（中身をまとめて移す）。</summary>
        RenameFolder,

        /// <summary>条件を上書き（名前は変えない。含める物を選んで、前の内容を消して書く・2026-11-27）。</summary>
        Overwrite,
    }

    public Purpose For { get; }

    public string Title => For switch
    {
        Purpose.Save => "今の検索を保存",
        Purpose.NewFolder => "新しいフォルダ",
        Purpose.RenameFolder => "フォルダの名前を変更",
        Purpose.Overwrite => "条件を上書き",
        _ => "名前を変更",
    };

    public string CommitText => For switch
    {
        Purpose.Save => "保存",
        Purpose.NewFolder => "移動",
        Purpose.Overwrite => "上書き",
        _ => "変更",
    };

    /// <summary>名前の欄を出すか（上書きは名前を変えない）。</summary>
    public bool ShowsName => For != Purpose.Overwrite;

    /// <summary>上書きの窓の本文（何が起きるか・取り返しがつかないこと・D4）。</summary>
    public string? Message => For == Purpose.Overwrite ? $"「{_name}」の条件を上書きします。上書きすると、前の内容には戻せません。" : null;

    public bool HasMessage => Message is not null;

    /// <summary>
    /// 取り返しのつかない上書きは、Enter で押さない（既定をキャンセル側に倒す・D4）。ほかは Enter で決める
    /// </summary>
    public bool CommitIsDefault => For != Purpose.Overwrite;

    /// <summary>何が起きるか（保存・新しいフォルダ）・取り返しがつくか（名前の変更・D4）。</summary>
    public string Note => For switch
    {
        Purpose.Save or Purpose.Overwrite => "条件と「対象」「オプション」は、いつも保存されます。",
        Purpose.NewFolder => "フォルダを作って、この保存した検索を移します。",
        _ => "名前は同じ手順で戻せます。",
    };

    // ---- 含める物（保存・上書き） ----

    /// <summary>含める物のチェックを出すか。</summary>
    public bool ShowsParts => For is Purpose.Save or Purpose.Overwrite;

    /// <summary>今、検索欄に文字を打っているか。打っている時だけ「検索文字列」を出す（無ければ入れても外しても同じ）。</summary>
    public bool HasText { get; }

    public bool ShowsTextPart => ShowsParts && HasText;

    public bool IncludeText
    {
        get => _includeText;
        set => SetField(ref _includeText, value);
    }

    public bool IncludeSort
    {
        get => _includeSort;
        set => SetField(ref _includeSort, value);
    }

    public bool IncludeView
    {
        get => _includeView;
        set => SetField(ref _includeView, value);
    }

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

    public bool IsTaken => ShowsName && _taken.Any(other => Core.Services.SavedSearches.SameName(other, _name));

    public bool CanCommit => !ShowsName || (_name.Trim().Length > 0 && !IsTaken);

    /// <summary>
    /// 上書きの窓の初めの状態を、元の行に合わせる（外して保存した物は外・文字の無い行は文字も外）
    /// </summary>
    public void StartFrom(Core.Models.SearchHistoryEntry entry)
    {
        _includeText = entry.Text.Trim().Length > 0;
        _includeSort = !entry.KeepSort;
        _includeView = !entry.KeepView;
    }

    /// <summary>選んだ物だけを残す。文字は打っていなければ含めても空</summary>
    public Core.Models.SearchHistoryEntry Apply(Core.Models.SearchHistoryEntry entry)
        => Core.Services.SavedSearches.WithParts(entry, text: _includeText, sort: _includeSort, view: _includeView);
}

/// <summary>
/// 保存した検索の小窓を出す口。試験は <see cref="Intercept"/> で答える（窓を出すと答える人がいないので止まる。<see cref="RenameDialogAsk"/> と同じ）
/// </summary>
internal static class SavedSearchDialogAsk
{
    /// <summary>窓を出す代わりに答える口。**アプリでは null のまま**。</summary>
    internal static Func<SavedSearchNameDialogViewModel, bool>? Intercept { get; set; }

    public static bool Ask(SavedSearchNameDialogViewModel model)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.SavedSearchNameDialog(model).ShowDialog() == true;
}
