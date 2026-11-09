using Chmonos.Core.Commands;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品ページの上の帯：使っている商品に新しい版が出たら促す（<c>docs/spec/used-updates.md</c>・ユーザ判断 2026-11-06）。
/// 出すのは、中身の更新の控えがあり（確認していない・新しいファイルを取り込んでいない）、改変か Unity のプロジェクトで使っている商品だけ。
/// 使っているかは見張り（<see cref="Services.UsedItemsWatch"/>）が裏で調べるので、答えが変わったら出し直す。
/// </summary>
public sealed partial class ItemViewModel
{
    private UsedBy _usedBy = UsedBy.None;
    private bool _listensToUsedItems;

    /// <summary>帯を出すか。</summary>
    public bool ShowsUsedUpdateBar => Item.Local.HasPendingContentUpdate && _usedBy.IsUsed;

    /// <summary>帯の文。どこで使っているかを先に言う（促す理由そのもの）。</summary>
    public string UsedUpdateText
    {
        get
        {
            var places = new List<string>(2);
            if (_usedBy.Modifications > 0)
            {
                places.Add($"改変 {_usedBy.Modifications} 件");
            }

            if (_usedBy.Projects > 0)
            {
                places.Add($"Unityプロジェクト {_usedBy.Projects} 件");
            }

            return $"{string.Join("・", places)}で使っています。新しい版が出ています。";
        }
    }

    /// <summary>説明文の更新履歴の見出しまで流す。見出しが無い商品（説明文だけの物）では出さない。</summary>
    public RelayCommand ShowUpdateHistoryCommand => _showUpdateHistory ??= new RelayCommand(
        () =>
        {
            if (UpdateHistorySection() is { } section)
            {
                GoToChange(new ChangeTarget(section.Heading, ChangeTone.Changed, ChangePlace.Section, section));
            }
        },
        () => UpdateHistorySection() is not null);

    private RelayCommand? _showUpdateHistory;

    public bool HasUpdateHistorySection => UpdateHistorySection() is not null;

    /// <summary>「確認した」：この商品では促しを消す。改変の行の札は改変ごとに確認する（ユーザ判断 2026-11-09）。</summary>
    public RelayCommand CheckContentUpdateCommand => _checkContentUpdate ??= new RelayCommand(
        () => CheckContentUpdateAsync().Forget(),
        () => ShowsUsedUpdateBar && !IsEditLocked);

    private RelayCommand? _checkContentUpdate;

    private async Task CheckContentUpdateAsync()
    {
        await RunItemCommandAsync(new UiCommand.CheckContentUpdate(Item.Id));
        RaiseUsedUpdate();
    }

    private SectionRow? UpdateHistorySection()
        => _pageSections.FirstOrDefault(section => Core.Booth.H2SectionExtractor.IsUpdateHistoryHeading(section.Key));

    /// <summary>開いたときに今の答えを当て、見張りの答えが変わるたびに出し直す。このページを離れたら聞くのをやめる。</summary>
    private void ListenToUsedItems()
    {
        _usedBy = _main.UsedItems.Current.Of(Item.Id);
        if (!_listensToUsedItems)
        {
            _listensToUsedItems = true;
            _main.UsedItems.Changed += OnUsedItemsChanged;
        }
    }

    private void OnUsedItemsChanged(object? sender, EventArgs e)
        => RunOnUiThread(() =>
        {
            // 履歴に残った古いページが聞き続けないように、見えていなければやめる（見張りはアプリが閉じるまで生きる）
            if (!ReferenceEquals(_main.CurrentViewModel, this) && !IsEmbedded)
            {
                _main.UsedItems.Changed -= OnUsedItemsChanged;
                _listensToUsedItems = false;
                return;
            }

            var next = _main.UsedItems.Current.Of(Item.Id);
            if (next != _usedBy)
            {
                _usedBy = next;
                RaiseUsedUpdate();
            }
        });

    private void RaiseUsedUpdate()
    {
        OnPropertyChanged(nameof(ShowsUsedUpdateBar));
        OnPropertyChanged(nameof(UsedUpdateText));
        RelayCommand.RaiseCanExecuteChanged();
    }
}
