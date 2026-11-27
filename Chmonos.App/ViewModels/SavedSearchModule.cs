using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 条件「保存した検索」（ユーザ判断 2026-11-27・`docs/spec/search-saved.md`「条件にする・フォルダ」）。
/// 照らす中身は Core の <see cref="SavedSearchCondition"/>（API と同じ）。ここは名前と「検索文字列も使う」を持ち、元の状態（無い・輪）を言うだけ。
/// 名前はメニューで選んで足すときに決まり、欄では替えない（別の保存した検索にするときは足し直す）
/// </summary>
public sealed class SavedSearchModule : SearchModule
{
    private readonly Func<IReadOnlyList<SearchHistoryEntry>> _saved;
    private string _name = string.Empty;
    private bool _useText = true;

    public SavedSearchModule(Func<IReadOnlyList<SearchHistoryEntry>> saved)
        : base(SearchModuleKind.SavedSearch)
        => _saved = saved;

    public string Name => _name;

    /// <summary>欄の1行目（どの保存した検索か）。</summary>
    public string Description => $"「{_name}」に当てはまる商品";

    /// <summary>
    /// 元の検索文字列も照らすか（チェック「検索文字列も使う」・ユーザ判断 2026-11-27 案2）。既定は入で、呼び出した結果と同じになる
    /// </summary>
    public bool UseText
    {
        get => _useText;
        set
        {
            if (SetField(ref _useText, value))
            {
                OnPropertyChanged(nameof(SummaryText));
                NotifyChanged();
            }
        }
    }

    /// <summary>元の保存した検索に文字があるときだけ、チェックを出す（無ければ入でも外でも同じ）。</summary>
    public bool ShowsUseText => Problem == SavedSearchProblem.None
        && SavedSearches.Find(_saved(), _name) is { } entry && entry.Text.Trim().Length > 0;

    public SavedSearchProblem Problem => SavedSearchReferences.ProblemOf(_saved(), _name);

    /// <summary>当てられない理由（欄に出す）。無ければ null。</summary>
    public string? ProblemText => Problem switch
    {
        SavedSearchProblem.Missing => "この保存した検索はありません",
        SavedSearchProblem.Cycle => "保存した検索が輪になっています",
        _ => null,
    };

    public bool HasProblem => ProblemText is not null;

    public override bool SupportsExclude => true;

    protected override bool HasCondition => _name.Trim().Length > 0;

    protected override ItemCondition BuildCondition() => new SavedSearchCondition(_name, _useText);

    protected override string SummaryBody => _name;

    /// <summary>保存した検索が変わった（足した・消した・名前を変えた・中身を上書きした）。出し分けを知らせ直す。</summary>
    public void RefreshSource()
    {
        Unprepare();
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(ProblemText));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(ShowsUseText));
    }

    /// <summary>名前の変更に付いていく（絞り直しは呼ぶ側）。指していなければ何もしない</summary>
    public bool FollowRename(string oldName, string newName)
    {
        if (!SavedSearches.SameName(_name, oldName))
        {
            return false;
        }

        _name = newName.Trim();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CollapsedSummary));
        RefreshSource();
        return true;
    }

    /// <summary>名前は消さない（どれを使うかは人が選んだ物）。チェックだけ既定に戻す</summary>
    public override void Clear()
    {
        _useText = true;
        OnPropertyChanged(nameof(UseText));
        Unprepare();
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with { Items = [_name], IgnoreText = !_useText };

    protected override void Read(SearchModuleState state)
    {
        _name = state.Items.FirstOrDefault()?.Trim() ?? string.Empty;
        _useText = !state.IgnoreText;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(UseText));
        RefreshSource();
    }
}
