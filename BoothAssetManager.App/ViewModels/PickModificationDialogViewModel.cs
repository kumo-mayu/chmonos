using System.Collections.ObjectModel;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>選べる改変1件。</summary>
public sealed class PickModificationRowViewModel
{
    public required ModificationRecord Record { get; init; }

    public required string AvatarText { get; init; }

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
        Rows = new ObservableCollection<PickModificationRowViewModel>(rows);
        AvatarNames = avatarNames;
        ResolveAvatar = resolveAvatar;

        PickCommand = new RelayCommand(
            parameter => Pick(parameter as PickModificationRowViewModel),
            parameter => parameter is PickModificationRowViewModel);
        PickAvatarCommand = new RelayCommand(parameter => PickAvatar(parameter as string));

        // 候補が無いときは、作る話から始める（選ぶものが無い画面を見せない）
        _makingNew = rows.Count == 0;
    }

    /// <summary>窓の題。送るときと足すだけのときで言い分ける。</summary>
    public string Title { get; }

    public string HeadingText { get; }

    /// <summary>送り先などの前提。**押す前に見えている必要がある。**空なら出さない。</summary>
    public string ContextText { get; }

    public bool HasContext => ContextText.Length > 0;

    public ObservableCollection<PickModificationRowViewModel> Rows { get; }

    public bool HasRows => Rows.Count > 0;

    public IReadOnlyList<string> AvatarNames { get; }

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
    public string NewAvatarLabel => "候補から選んでください（まだ選んでいません）";

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
            : "足す先の改変を選んでください。";

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
