using System.Windows.Data;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// アバターの管理の「変換」（<c>docs/spec/conversion.md</c>「変換扱いの画面」・ユーザ案「未確定のような形で「変換扱い」をリストで積んで順々に設定する」）。
/// 左にプロファイルを持つ商品の一覧（決めていない物を上に群で分ける）、右に商品ページと同じ「変換扱い」の欄（<see cref="ItemConversionEditor"/>）と、
/// 「推した通りに決めて次へ」「プロファイルではない」「後で」（ユーザ判断 2026-11-09）。
/// 一覧は検索の写し（全商品を読んである）から、プロファイルを持つかもしれない商品だけ控えを読んで作る。
/// </summary>
public sealed partial class AvatarsViewModel
{
    internal const string UndecidedGroup = "決めていない";
    internal const string DecidedGroup = "決めた";
    internal const string NotProfileGroup = "扱わない";

    private bool _isConversionMode;
    private List<ConversionItemRowViewModel> _conversionItems = [];
    private ConversionItemRowViewModel? _selectedConversion;
    private ItemConversionEditor? _conversionEditor;
    private int _conversionBuild;

    /// <summary>左の一覧に、変換ツールのプロファイルを持つ商品を並べているか。</summary>
    public bool IsConversionMode
    {
        get => _isConversionMode;
        set
        {
            if (SetField(ref _isConversionMode, value))
            {
                if (value)
                {
                    IsBaseMode = false;
                    SelectedConversion ??= FirstUndecided();
                }

                RaiseModes();
            }
        }
    }

    public string ConversionModeText => $"変換（{_conversionItems.Count}）";

    public RelayCommand ShowConversionModeCommand => _showConversionModeCommand ??= new RelayCommand(() => IsConversionMode = true);

    private RelayCommand? _showConversionModeCommand;

    /// <summary>左の一覧（群は「決めていない」「決めた」「扱わない」の順。並びが群の順になるよう、先に並べてから群に分ける）。</summary>
    public ListCollectionView ConversionItems { get; private set; } = Grouped([]);

    public bool HasConversionItems => _conversionItems.Count > 0;

    /// <summary>変換を見ているのに1件も無いとき。空欄ではなく、何をすれば並ぶかを出す。</summary>
    public bool ShowsNoConversionItems => IsConversionMode && !HasConversionItems;

    public bool ShowsConversionDetail => IsConversionMode && SelectedConversion is not null;

    public ConversionItemRowViewModel? SelectedConversion
    {
        get => _selectedConversion;
        set
        {
            if (SetField(ref _selectedConversion, value))
            {
                ConversionEditor.Show(value?.Item);
                OnPropertyChanged(nameof(ShowsConversionDetail));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>右の「変換扱い」の欄。商品ページと同じ部品で、書いた後は一覧の行と検索の写しも直す。</summary>
    public ItemConversionEditor ConversionEditor => _conversionEditor ??= new ItemConversionEditor(_services, RunConversionCommandAsync, () => false);

    /// <summary>確認待ちのつなぎ先を全部確認済みにして、次の決めていない商品へ（Ctrl+Enter。未確定の「この内容で決める」と同じ流れ）。</summary>
    public RelayCommand DecideSuggestedCommand => _decideSuggestedCommand ??= new RelayCommand(
        () => DecideSuggestedAsync().Forget(),
        () => IsConversionMode && SelectedConversion is not null);

    private RelayCommand? _decideSuggestedCommand;

    /// <summary>「プロファイルではない」：扱わないにして次へ。</summary>
    public RelayCommand MarkNotProfileCommand => _markNotProfileCommand ??= new RelayCommand(
        () => MarkNotProfileAsync().Forget(),
        () => IsConversionMode && SelectedConversion is not null);

    private RelayCommand? _markNotProfileCommand;

    /// <summary>「後で」：何も書かずに次の決めていない商品へ（ユーザ判断 2026-11-09。開き直すとまた上の群に出る）。</summary>
    public RelayCommand LaterCommand => _laterCommand ??= new RelayCommand(
        () => SelectNextUndecided(),
        () => IsConversionMode && SelectedConversion is not null);

    private RelayCommand? _laterCommand;

    public RelayCommand OpenConversionItemCommand => _openConversionItemCommand ??= new RelayCommand(
        () =>
        {
            if (SelectedConversion?.Item is { } item)
            {
                _main.ShowItem(item);
            }
        },
        () => SelectedConversion is not null);

    private RelayCommand? _openConversionItemCommand;

    private async Task DecideSuggestedAsync()
    {
        if (ConversionEditor.UnconfirmedTargets is { Count: > 0 } shown)
        {
            await ConversionEditor.ChangeAsync(ConversionEdits.ConfirmMany(shown));
        }

        SelectNextUndecided();
    }

    private async Task MarkNotProfileAsync()
    {
        await ConversionEditor.ChangeAsync(ConversionEdits.SetMode(ConversionMode.NotProfile));
        SelectNextUndecided();
    }

    /// <summary>
    /// 欄の書き込み。命令を通し、書いた後の記録を読み直して、右の欄・一覧の行・検索の写しを直す
    /// （検索の写しへ知らせないと、衣装の商品ページの「変換経由」が全件の読み直しまで変わらない）。
    /// </summary>
    private async Task RunConversionCommandAsync(UiCommand command)
    {
        if (command is not UiCommand.ChangeConversion change)
        {
            return;
        }

        try
        {
            var result = await _services.Commands.ExecuteAsync(command);
            if (result is CommandResult.Failed failed)
            {
                ConversionEditor.Notice.Warn(failed.Message);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Core.Diagnostics.AppLog.Error("変換扱いの保存", exception);
            ConversionEditor.Notice.Warn($"保存できませんでした。{FailureText.Cause(exception)}");
        }

        if (await _services.Store.Items.LoadAsync(change.ItemId) is not { } reloaded)
        {
            return;
        }

        _main.Search.NoteItemChanged(reloaded);
        var row = _conversionItems.FirstOrDefault(candidate => candidate.ItemId == reloaded.Id);
        if (row is null)
        {
            return;
        }

        row.Update(reloaded, ProfilesOf(reloaded));
        ArrangeConversionItems(keep: SelectedConversion);
        if (ReferenceEquals(SelectedConversion, row))
        {
            ConversionEditor.Show(reloaded);
        }
    }

    /// <summary>
    /// 検索の写しから一覧を作り直す。控えを読むので裏で回し、間に作り直しが重なったら古い方の結果は捨てる。
    /// 対象はプロファイルのファイルを持つ商品と、変換扱いを既に持つ商品（商品ページの欄を出す商品と同じ）。
    /// </summary>
    private void RefreshConversionItems()
    {
        var build = ++_conversionBuild;
        var items = _main.Search.SnapshotItems();
        Task.Run(() => items
                .Where(item => item.Local.Conversion is not null || ConversionScan.MayHoldProfiles(item))
                .Select(item => (Item: item, Profiles: ProfilesOf(item)))
                .Where(pair => pair.Profiles.Count > 0 || pair.Item.Local.Conversion is not null)
                .ToList())
            .ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        Core.Diagnostics.AppLog.Error("変換の一覧を作る", task.Exception!.GetBaseException());
                        return;
                    }

                    RunOnUiThread(() =>
                    {
                        if (build != _conversionBuild)
                        {
                            return;
                        }

                        var keepId = SelectedConversion?.ItemId;
                        _conversionItems = task.Result.Select(pair => ConversionItemRowViewModel.Create(pair.Item, pair.Profiles)).ToList();
                        ArrangeConversionItems(keep: null);
                        _selectedConversion = null;
                        SelectedConversion = _conversionItems.FirstOrDefault(row => row.ItemId == keepId)
                            ?? (IsConversionMode ? FirstUndecided() : null);
                        OnPropertyChanged(nameof(ConversionModeText));
                        OnPropertyChanged(nameof(HasConversionItems));
                        RaiseModes();
                    });
                },
                TaskScheduler.Default)
            .Forget();
    }

    private IReadOnlyList<ProfileSlug> ProfilesOf(ItemRecord item)
        => ConversionScan.MayHoldProfiles(item) ? _services.Conversion.ProfilesOf(item) : [];

    /// <summary>群の順（決めていない → 決めた → 扱わない）と名前で並べ直す。選んでいる行はそのまま選んでおく。</summary>
    private void ArrangeConversionItems(ConversionItemRowViewModel? keep)
    {
        _conversionItems = _conversionItems
            .OrderBy(row => row.GroupRank)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
        ConversionItems = Grouped(_conversionItems);
        OnPropertyChanged(nameof(ConversionItems));
        if (keep is not null)
        {
            // 一覧を差し替えると ListBox が選びを外すので、同じ行を選び直したと知らせる
            _selectedConversion = null;
            SelectedConversion = keep;
        }
    }

    private static ListCollectionView Grouped(List<ConversionItemRowViewModel> rows)
    {
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConversionItemRowViewModel.Group)));
        return view;
    }

    private ConversionItemRowViewModel? FirstUndecided()
        => _conversionItems.FirstOrDefault(row => row.Group == UndecidedGroup) ?? _conversionItems.FirstOrDefault();

    /// <summary>今の行の後ろから、決めていない行を探して選ぶ（無ければ前から。1件も無ければ今の行のまま）。</summary>
    internal void SelectNextUndecided()
    {
        var current = SelectedConversion;
        var index = current is null ? -1 : _conversionItems.IndexOf(current);
        var next = _conversionItems.Skip(index + 1).FirstOrDefault(row => row.Group == UndecidedGroup && !ReferenceEquals(row, current))
            ?? _conversionItems.Take(Math.Max(index, 0)).FirstOrDefault(row => row.Group == UndecidedGroup);
        if (next is not null)
        {
            SelectedConversion = next;
        }
    }
}

/// <summary>「変換」の一覧の1行：プロファイルを持つ商品と、決めた度合い。</summary>
public sealed class ConversionItemRowViewModel : ViewModelBase
{
    public required string ItemId { get; init; }

    public ItemRecord Item { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;

    /// <summary>群の名前（「決めていない」「決めた」「扱わない」）。</summary>
    public string Group { get; private set; } = string.Empty;

    public int GroupRank => Group switch
    {
        AvatarsViewModel.UndecidedGroup => 0,
        AvatarsViewModel.DecidedGroup => 1,
        _ => 2,
    };

    /// <summary>決めていない物の数（「確認待ち 3・未設定 2」）。決めた物は空。</summary>
    public string BadgeText { get; private set; } = string.Empty;

    public bool HasBadge => BadgeText.Length > 0;

    public static ConversionItemRowViewModel Create(ItemRecord item, IReadOnlyList<ProfileSlug> profiles)
    {
        var row = new ConversionItemRowViewModel { ItemId = item.Id };
        row.Update(item, profiles);
        return row;
    }

    /// <summary>書いた後に、読み直した記録で数と群を直す。</summary>
    public void Update(ItemRecord item, IReadOnlyList<ProfileSlug> profiles)
    {
        Item = item;
        Name = item.DisplayName;
        var setting = item.Local.Conversion;
        var targets = (setting?.Targets ?? []).Where(target => !target.Rejected).ToList();
        var unconfirmed = targets.Count(target => !target.Confirmed);
        var unset = profiles.Count(profile => !targets.Any(target => string.Equals(target.Slug, profile.Slug, StringComparison.OrdinalIgnoreCase)));

        Group = setting?.Mode == ConversionMode.NotProfile
            ? AvatarsViewModel.NotProfileGroup
            : unconfirmed + unset > 0 ? AvatarsViewModel.UndecidedGroup : AvatarsViewModel.DecidedGroup;
        BadgeText = Group == AvatarsViewModel.UndecidedGroup
            ? string.Join("・", new[] { unconfirmed > 0 ? $"確認待ち {unconfirmed}" : null, unset > 0 ? $"未設定 {unset}" : null }.OfType<string>())
            : string.Empty;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(HasBadge));
    }
}
