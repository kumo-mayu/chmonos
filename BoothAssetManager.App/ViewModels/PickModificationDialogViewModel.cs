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
    public string Detail => Record.Members.Count == 0
        ? $"{AvatarText}　まだ何も入っていません"
        : $"{AvatarText}　{Record.Members.Count} 件入っています";
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
        string packageName,
        string projectText,
        IReadOnlyList<PickModificationRowViewModel> rows,
        IReadOnlyList<string> avatarNames,
        Func<string, string?> resolveAvatar)
    {
        PackageName = packageName;
        ProjectText = projectText;
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

    public string PackageName { get; }

    /// <summary>送り先のUnity。押す前に見えている必要がある。</summary>
    public string ProjectText { get; }

    public ObservableCollection<PickModificationRowViewModel> Rows { get; }

    public bool HasRows => Rows.Count > 0;

    public IReadOnlyList<string> AvatarNames { get; }

    /// <summary>アバターの名前から商品IDを引く。呼ぶ側が登録簿を持っている。</summary>
    private Func<string, string?> ResolveAvatar { get; }

    public RelayCommand PickCommand { get; }

    public RelayCommand PickAvatarCommand { get; }

    public string HeadingText => $"「{PackageName}」を送って、改変に足します。";

    /// <summary>候補が無いときに出す文。次にやることを書く。</summary>
    public string EmptyText =>
        "このプロジェクトに紐付いた改変はまだありません。新しく作って、そこに足せます。";

    public PickModificationRowViewModel? Picked
    {
        get => _picked;
        private set
        {
            if (SetField(ref _picked, value))
            {
                OnPropertyChanged(nameof(CanCommit));
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
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewAvatarText
    {
        get => _newAvatarText;
        private set => SetField(ref _newAvatarText, value);
    }

    public string NewAvatarLabel => NewAvatarItemId is null
        ? "アバターをまだ選んでいません"
        : $"アバター： {NewAvatarText}";

    /// <summary>押せるか。**選んでいないのに押せると、何が起きるか分からない。**</summary>
    public bool CanCommit => MakingNew
        ? NewName.Trim().Length > 0 && NewAvatarItemId is not null
        : Picked is not null;

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
            NewAvatarItemId = itemId;
            NewAvatarText = trimmed;
            OnPropertyChanged(nameof(NewAvatarLabel));
        }
    }
}
