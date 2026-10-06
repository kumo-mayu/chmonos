using System.Collections.ObjectModel;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>選べる改変1件。</summary>
public sealed class PickModificationRowViewModel : ViewModelBase
{
    public required ModificationRecord Record { get; init; }

    /// <summary>行の頭の絵の場所（改変の写真の1枚目 → アバターの絵。<see cref="Core.Services.ModificationIcon"/>）。無ければ null。</summary>
    public string? IconPath { get; init; }

    /// <summary>絵の読み込み器を返す。絵が無い行では呼ばない（窓ごとの読み込み器を、絵が要るときに初めて作るため）。</summary>
    public Func<Services.ThumbnailLoader>? Thumbnails { get; init; }

    /// <summary>頭の絵。見えた行で初めて読み、読み終えたら知らせる。無ければ空（頭文字が見える）。</summary>
    public System.Windows.Media.Imaging.BitmapSource? Icon => IconPath is { } path
        ? Thumbnails?.Invoke().PeekForIcon(path, () => OnPropertyChanged(nameof(Icon)))
        : null;

    /// <summary>絵の無い行の頭文字（既定の絵。アバターの候補・改変の一覧と同じ出し方）。</summary>
    public string Initial => Core.Services.AvatarText.InitialOf(AvatarText);

    public required string AvatarText { get; init; }

    /// <summary>紐付けた Unity プロジェクトの名前（場所の最後の部分）。紐付けていなければ空。</summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>
    /// 探す欄の語が、名前以外のどこに当たったか（「アバター：〇〇」「プロジェクト：〇〇」）。名前は行に出ているので言わない。
    /// 探していない間・名前だけに当たったときは空。絞り込みのたびに VM が書き直し、行は作り直されるので通知は要らない
    /// </summary>
    public string MatchNote { get; internal set; } = string.Empty;

    public bool HasMatchNote => MatchNote.Length > 0;

    public string Name => Record.Name;

    /// <summary>どのアバターの、何が何件入っているか。同じ名前を見分けるために出す。</summary>
    public string Detail => Record.UsedMembers.Count == 0
        ? $"{AvatarText}　まだ何も入っていません"
        : $"{AvatarText}　{Record.UsedMembers.Count} 件入っています";
}

/// <summary>
/// 「改変に足して送る」で、どの改変に積むかを選ぶ。
///
/// **1件のときも出す**（ユーザ判断）。1件なら黙って積む方が手数は少ないが、
/// 「新しく作る」が答えの可能性がある。要らない操作が1つ増える代わりに、
/// 後から「実は2つ目の改変だった」となるのを防ぐ。
///
/// ダイアログにするのは、**送るという操作の途中で戻ってこられる必要がある**ため。
/// </summary>
public sealed class PickModificationDialogViewModel : ViewModelBase
{
    private PickModificationRowViewModel? _picked;
    private bool _makingNew;
    private string _newName = string.Empty;
    private string? _newAvatarItemId;
    private string _newAvatarText = string.Empty;

    public PickModificationDialogViewModel(
        string title,
        string headingText,
        string contextText,
        IReadOnlyList<PickModificationRowViewModel> rows,
        IReadOnlyList<string> avatarNames,
        Func<string, string?> resolveAvatar)
    {
        Title = title;
        HeadingText = headingText;
        ContextText = contextText;
        _allRows = rows;
        Rows = new ObservableCollection<PickModificationRowViewModel>(rows);
        AvatarNames = avatarNames;
        ResolveAvatar = resolveAvatar;

        PickCommand = new RelayCommand(
            parameter => Pick(parameter as PickModificationRowViewModel),
            parameter => parameter is PickModificationRowViewModel);
        PickAvatarCommand = new RelayCommand(parameter => PickAvatar(parameter as string));
        ClearFilterCommand = new RelayCommand(() => FilterText = string.Empty);

        // 候補が無いときは、作る話から始める（選ぶものが無い画面を見せない）
        _makingNew = rows.Count == 0;
    }

    /// <summary>窓の題。送るときと足すだけのときで言い分ける。</summary>
    public string Title { get; }

    public string HeadingText { get; }

    /// <summary>送り先などの前提。**押す前に見えている必要がある。**空なら出さない。</summary>
    public string ContextText { get; }

    public bool HasContext => ContextText.Length > 0;

    /// <summary>
    /// 手で足すときの「使ったファイル」（メモ26-②）。送って足すとき（送る物で決まる）と、選べるファイルが無いときは null。
    /// 選ばなくても押せる（飛ばせる）ので、押せるかには関わらない
    /// </summary>
    public MemberFilePickViewModel? Files { get; init; }

    public bool HasFiles => Files is { HasChoices: true };

    private readonly IReadOnlyList<PickModificationRowViewModel> _allRows;
    private string _filterText = string.Empty;

    /// <summary>探す欄の中身（今ある改変の一覧を絞る）。</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value ?? string.Empty))
            {
                ApplyFilter();
                OnPropertyChanged(nameof(HasFilterText));
            }
        }
    }

    public bool HasFilterText => FilterText.Length > 0;

    public RelayCommand ClearFilterCommand { get; }

    /// <summary>今見えている改変（探す欄で絞った後）。</summary>
    public ObservableCollection<PickModificationRowViewModel> Rows { get; }

    /// <summary>今ある改変が1件でもあるか。**絞った結果ではない**（探す欄と見出しを出すかの元。絞って0件でも欄は消さない）。</summary>
    public bool HasRows => _allRows.Count > 0;

    /// <summary>探したが1件も当たらなかったか。</summary>
    public bool HasNoMatch => HasFilterText && Rows.Count == 0;

    /// <summary>
    /// 探す語を空白で区切り、**全部の語が**、改変の名前・アバターの名前・Unity プロジェクトの名前のどれかに入っている改変だけを残す。
    /// 大文字小文字・かなの種類・全角半角は区別しない（ほかの画面の探す欄と同じ）。
    /// 選んでいた改変が外れたら選びを解く（見えない物を「追加」できてしまわないように）
    /// </summary>
    private void ApplyFilter()
    {
        var words = ModificationSearchText.Words(FilterText);
        var shown = new List<PickModificationRowViewModel>();
        foreach (var row in _allRows)
        {
            if (Matches(row, words, out var note))
            {
                row.MatchNote = note;
                shown.Add(row);
            }
        }

        Rows.Clear();
        foreach (var row in shown)
        {
            Rows.Add(row);
        }

        if (Picked is not null && !shown.Contains(Picked))
        {
            Picked = null;
        }

        OnPropertyChanged(nameof(HasNoMatch));
    }

    /// <summary>語が全部当たるか。名前以外（アバター・プロジェクト）に当たった物は <paramref name="note"/> に書く（決まりは検索の「改変」と同じ <see cref="ModificationSearchText"/>）。</summary>
    internal static bool Matches(PickModificationRowViewModel row, string[] words, out string note)
        => ModificationSearchText.Matches(row.Name, row.AvatarText, row.ProjectName, words, out note);

    /// <summary>アバターの候補の頭に出す絵。名前から読む（持っていれば商品の1枚目・無ければ控え）。絵が無ければ頭文字が出る。</summary>
    public Func<string, System.Windows.Media.ImageSource?>? AvatarIconSelector { get; init; }

    /// <summary>候補の名前から群と呼び方を引く（メモ58）。無ければ <see cref="OwnedAvatarCount"/> の前後で2群に分ける。</summary>
    public Func<string, Controls.SuggestInfo?>? AvatarInfoSelector { get; init; }

    /// <summary>
    /// 群の見出し。共通素体は改変の持ち主にできない（改変は商品のあるアバターに属する）ので、
    /// この窓の候補に素体の群は出ない。見出しの並びは検索・商品ページと同じ3つで、番号で引く
    /// </summary>
    public IReadOnlyList<string> AvatarGroupHeadings => AvatarSuggestionText.Headings;

    /// <summary>欄に渡す案内。引けない名前は、先頭から <see cref="OwnedAvatarCount"/> 件までを所持、残りを未所持の群にする。</summary>
    public Func<string, Controls.SuggestInfo?> AvatarSuggestInfoSelector => name =>
        AvatarInfoSelector?.Invoke(name)
        ?? new Controls.SuggestInfo(
            AvatarNames.ToList().FindIndex(entry => string.Equals(entry, name, StringComparison.CurrentCultureIgnoreCase)) is >= 0 and var index && index < OwnedAvatarCount
                ? AvatarSuggestionText.OwnedGroup
                : AvatarSuggestionText.OtherGroup,
            []);

    public IReadOnlyList<string> AvatarNames { get; }

    /// <summary>候補の先頭から数えて、持っているアバターの件数。ここまでと残りの間に区切り線を引く（メモ32-②）。init で渡さなければ分けない。</summary>
    public int OwnedAvatarCount { get; init; }

    /// <summary>アバターの名前から商品IDを引く。呼ぶ側が登録簿を持っている。</summary>
    private Func<string, string?> ResolveAvatar { get; }

    public RelayCommand PickCommand { get; }

    public RelayCommand PickAvatarCommand { get; }

    /// <summary>候補が無いときに出す文。次にやることを書く。</summary>
    public required string EmptyText { get; init; }

    /// <summary>既にある改変に足す側の見出し。</summary>
    public required string ExistingLabel { get; init; }

    /// <summary>押して確定するボタンの名前。**何が起きるかを名乗る。**</summary>
    public required string CommitLabel { get; init; }

    public PickModificationRowViewModel? Picked
    {
        get => _picked;
        private set
        {
            if (SetField(ref _picked, value))
            {
                OnPropertyChanged(nameof(CanCommit));
                OnPropertyChanged(nameof(CommitHint));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>新しく作る側を選んでいるか。</summary>
    public bool MakingNew
    {
        get => _makingNew;
        set
        {
            if (SetField(ref _makingNew, value))
            {
                if (value)
                {
                    Picked = null;
                }

                OnPropertyChanged(nameof(PickingExisting));
                OnPropertyChanged(nameof(CanCommit));
                OnPropertyChanged(nameof(CommitHint));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool PickingExisting
    {
        get => !_makingNew;
        set
        {
            if (value)
            {
                MakingNew = false;
            }
        }
    }

    public string NewName
    {
        get => _newName;
        set
        {
            if (SetField(ref _newName, value))
            {
                OnPropertyChanged(nameof(CanCommit));
                OnPropertyChanged(nameof(CommitHint));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>作る先のアバター。改変はアバター1体に属するので、これが要る。</summary>
    public string? NewAvatarItemId
    {
        get => _newAvatarItemId;
        private set
        {
            if (SetField(ref _newAvatarItemId, value))
            {
                OnPropertyChanged(nameof(CanCommit));
                OnPropertyChanged(nameof(CommitHint));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewAvatarText
    {
        get => _newAvatarText;
        private set => SetField(ref _newAvatarText, value);
    }

    private string _newAvatarQuery = string.Empty;

    /// <summary>
    /// アバターの欄の文字。選んだ名前はこの欄にそのまま出す（ユーザ指摘 2026-09-19：欄が空に戻って、下に「アバター：」と
    /// 出るだけでは何を選んだか分かりにくかった）。選んだ後に打ち直したら、選んだアバターは外す（欄と中身を食い違わせない）
    /// </summary>
    public string NewAvatarQuery
    {
        get => _newAvatarQuery;
        set
        {
            if (!SetField(ref _newAvatarQuery, value ?? string.Empty))
            {
                return;
            }

            if (NewAvatarItemId is not null && _newAvatarQuery.Trim() != NewAvatarText)
            {
                NewAvatarItemId = null;
                OnPropertyChanged(nameof(HasNewAvatar));
            }
        }
    }

    public bool HasNewAvatar => NewAvatarItemId is not null;

    /// <summary>選んでいない間だけ、欄の下で何をすればよいかを言う（選んだら欄が名前を見せる）。</summary>
    public string NewAvatarLabel => "候補から選んでください";

    /// <summary>押せるか。**選んでいないのに押せると、何が起きるか分からない。**</summary>
    public bool CanCommit => MakingNew
        ? NewName.Trim().Length > 0 && NewAvatarItemId is not null
        : Picked is not null;

    /// <summary>
    /// 押せないときに、**何が足りないか**を書く（`ui-dialogs.md`・E9）。
    /// 塞がれた理由が出ていないと、押せるようにする道が分からない。
    /// </summary>
    public string CommitHint => CanCommit
        ? string.Empty
        : MakingNew
            ? NewName.Trim().Length == 0
                ? "新しい改変の名前を入れてください。"
                : "どのアバターの改変かを選んでください。"
            : "追加する先の改変を選んでください。";

    private void Pick(PickModificationRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        MakingNew = false;
        Picked = row;
    }

    private void PickAvatar(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        if (ResolveAvatar(trimmed) is { } itemId)
        {
            NewAvatarText = trimmed;
            NewAvatarItemId = itemId;
            NewAvatarQuery = trimmed;
            OnPropertyChanged(nameof(HasNewAvatar));
        }
    }
}
