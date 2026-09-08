using System.Collections.ObjectModel;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>一覧の1行。</summary>
public sealed class AvatarRowViewModel : ViewModelBase
{
    public required AvatarSummary Summary { get; init; }

    /// <summary>一覧のグループ見出し。所有しているものを先に固めて出す。</summary>
    public string GroupName { get; set; } = string.Empty;

    public string ItemId => Summary.Entry.ItemId;

    public string Name => string.IsNullOrWhiteSpace(Summary.Entry.DisplayName)
        ? Summary.Entry.ItemId
        : Summary.Entry.DisplayName!;

    public bool IsOwned => Summary.IsOwned;

    public string BaseText => Summary.Entry.BaseName ?? string.Empty;

    public bool HasBase => !string.IsNullOrWhiteSpace(Summary.Entry.BaseName);

    /// <summary>直接対応と素体経由は分けて出す。素体経由は推定なので同じ顔で並べない。</summary>
    public string CountText => Summary.ViaBaseCount > 0
        ? $"{Summary.DirectCount} + 素体経由 {Summary.ViaBaseCount}"
        : $"{Summary.DirectCount}";

    public string Initial => Name.Length == 0 ? "?" : Name[..1];
}

/// <summary>素体グループの1行。</summary>
public sealed class AvatarBaseRowViewModel : ViewModelBase
{
    public required AvatarBaseSummary Summary { get; init; }

    public string Name => Summary.Group.Name;

    public string MemberText => $"アバター {Summary.MemberCount}（所有 {Summary.OwnedMemberCount}）";

    public string ItemText => $"名指し {Summary.ItemCount} 件";

    public bool InferClothing => Summary.Group.InferClothing;

    public bool HasItemId => !string.IsNullOrWhiteSpace(Summary.Group.ItemId);

    public string ItemIdText => HasItemId ? $"配布あり（{Summary.Group.ItemId}）" : "素体単体の配布なし";

    public RelayCommand? ToggleInferCommand { get; set; }

    public RelayCommand? RenameCommand { get; set; }

    public RelayCommand? DeleteCommand { get; set; }
}

/// <summary>
/// アバターの管理。
///
/// 一覧は所有しているアバターを先に出す。検出が育つと未所有のアバターが何百と並ぶので、
/// 素体でグループ化した一覧にすると「素体の指定なし」に大半が落ちて読めなくなる
/// （実データでは独自素体が大半）。素体の管理は別の欄に分ける。
/// </summary>
public sealed class AvatarsViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    private AvatarRowViewModel? _selected;
    private bool _isLoading = true;
    private bool _isDetecting;
    private string _status = string.Empty;
    private string _query = string.Empty;
    private string _baseInput = string.Empty;
    private string _aliasInput = string.Empty;
    private List<AvatarRowViewModel> _all = [];

    public AvatarsViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        DetectCommand = new RelayCommand(() => _ = DetectAsync(), () => !IsDetecting);
        SetBaseCommand = new RelayCommand(() => _ = SetBaseAsync());
        ClearBaseCommand = new RelayCommand(() => _ = ClearBaseAsync());
        AddAliasCommand = new RelayCommand(() => _ = AddAliasAsync());
        RemoveAliasCommand = new RelayCommand(parameter => _ = RemoveAliasAsync(parameter as string));
        ToggleOwnedCommand = new RelayCommand(() => _ = ToggleOwnedAsync());
        RecheckCommand = new RelayCommand(() => _ = RecheckAsync());
        TreatAsAvatarCommand = new RelayCommand(parameter => _ = SetOverrideAsync(parameter as string));
        OpenBoothCommand = new RelayCommand(OpenBooth);
        ShowItemsCommand = new RelayCommand(ShowItems);

        // 既定のビューに見出しを付ける。ListBoxはこのビューを通して並べる
        System.Windows.Data.CollectionViewSource.GetDefaultView(Rows).GroupDescriptions.Add(
            new System.Windows.Data.PropertyGroupDescription(nameof(AvatarRowViewModel.GroupName)));

        _ = LoadAsync();
    }

    /// <summary>
    /// 一覧は1つにまとめ、所有/未所有はグループ見出しで分ける。
    /// ListBoxを2つ縦に積むと、ScrollViewerの中で高さが無限になって描画が壊れる。
    /// </summary>
    public ObservableCollection<AvatarRowViewModel> Rows { get; } = [];

    public ObservableCollection<AvatarBaseRowViewModel> Bases { get; } = [];

    public ObservableCollection<string> BaseNames { get; } = [];

    public RelayCommand DetectCommand { get; }

    public RelayCommand SetBaseCommand { get; }

    public RelayCommand ClearBaseCommand { get; }

    public RelayCommand AddAliasCommand { get; }

    public RelayCommand RemoveAliasCommand { get; }

    public RelayCommand ToggleOwnedCommand { get; }

    public RelayCommand RecheckCommand { get; }

    public RelayCommand TreatAsAvatarCommand { get; }

    public RelayCommand OpenBoothCommand { get; }

    public RelayCommand ShowItemsCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool IsDetecting
    {
        get => _isDetecting;
        private set
        {
            if (SetField(ref _isDetecting, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(DetectButtonText));
            }
        }
    }

    public string DetectButtonText => IsDetecting ? "検出しています…" : "対応アバターを検出する";

    public string Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    /// <summary>1体も見つかっていないとき。数字ではなく次にやることを出す。</summary>
    public bool IsEmpty => !IsLoading && Rows.Count == 0;

    public bool HasBases => Bases.Count > 0;

    public string Query
    {
        get => _query;
        set
        {
            if (SetField(ref _query, value))
            {
                Rebuild();
            }
        }
    }

    public string BaseInput
    {
        get => _baseInput;
        set => SetField(ref _baseInput, value);
    }

    public string AliasInput
    {
        get => _aliasInput;
        set => SetField(ref _aliasInput, value);
    }

    public AvatarRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                BaseInput = value?.Summary.Entry.BaseName ?? string.Empty;
                AliasInput = string.Empty;

                foreach (var name in new[]
                {
                    nameof(HasSelection), nameof(SelectedName), nameof(SelectedIdText),
                    nameof(SelectedCategoryText), nameof(SelectedCountText), nameof(Aliases),
                    nameof(OwnedButtonText), nameof(SelectedOwnedText), nameof(SelectedSeenAsText),
                    nameof(SelectedCheckedText), nameof(SelectedBaseNote), nameof(HasSelectedBaseNote),
                })
                {
                    OnPropertyChanged(name);
                }
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public string SelectedName => Selected?.Name ?? string.Empty;

    public string SelectedIdText => Selected is null ? string.Empty : $"ID {Selected.ItemId}";

    /// <summary>BOOTHのcategoryはそのまま出す。判定の根拠が読めるようにするため。</summary>
    public string SelectedCategoryText => Selected?.Summary.Entry.Category ?? "（販売終了などで確認できていません）";

    public string SelectedCountText => Selected is null
        ? string.Empty
        : $"直接対応 {Selected.Summary.DirectCount} 件 / 素体経由 {Selected.Summary.ViaBaseCount} 件";

    public string SelectedOwnedText => Selected?.Summary.IsOwned == true ? "所有している" : "所有していない";

    public string OwnedButtonText => Selected?.Summary.Entry.IsOwnedManually == true
        ? "手動の所有指定を外す"
        : "所有しているものとして扱う";

    /// <summary>どの文脈で候補に挙がったか。判定の根拠なので隠さない。</summary>
    public string SelectedSeenAsText
    {
        get
        {
            if (Selected is null || Selected.Summary.Entry.SeenAs.Count == 0)
            {
                return string.Empty;
            }

            var parts = Selected.Summary.Entry.SeenAs
                .OrderByDescending(pair => pair.Value)
                .Select(pair => $"{Label(pair.Key)} {pair.Value} 件");

            return "見つかった経路：" + string.Join(" / ", parts);
        }
    }

    public string SelectedCheckedText => Selected?.Summary.Entry.CheckedAt is { } at
        ? $"最終確認 {at:yyyy-MM-dd}"
        : string.Empty;

    /// <summary>素体を指定したときに何が起きるかをその場に書く。推定が広がる操作なので。</summary>
    public string SelectedBaseNote
    {
        get
        {
            if (Selected?.Summary.Entry.BaseName is not { } name)
            {
                return string.Empty;
            }

            var group = Bases.FirstOrDefault(row =>
                string.Equals(row.Name, name, StringComparison.CurrentCultureIgnoreCase));

            if (group is null)
            {
                return string.Empty;
            }

            if (!group.InferClothing)
            {
                return $"「{name}」は衣装の互換を広げない設定です（頭部などの部位規格）。";
            }

            var siblings = group.Summary.MemberCount - 1;
            return siblings <= 0
                ? $"「{name}」に属しているのはこのアバターだけです。"
                : $"「{name}」の他の {siblings} 体向けの衣装も、素体経由として一緒に出ます。";
        }
    }

    public bool HasSelectedBaseNote => SelectedBaseNote.Length > 0;

    public IReadOnlyList<string> Aliases => Selected?.Summary.Entry.Aliases
        .OrderByDescending(alias => alias.Count)
        .Select(alias => alias.Count > 0 ? $"{alias.Text}（{alias.Count}）" : alias.Text)
        .ToList() ?? [];

    private static string Label(string source) => source switch
    {
        "SupportSection" => "対応アバター節",
        "Tag" => "タグ",
        "Variation" => "バリエーション名",
        "H2Link" => "説明文のリンク",
        "Manual" => "手入力",
        _ => source,
    };

    private async Task LoadAsync()
    {
        var avatars = await Task.Run(() => _services.Avatars.LoadAsync());
        var bases = await Task.Run(() => _services.Avatars.LoadBasesAsync());

        RunOnUiThread(() =>
        {
            _all = avatars.Select(summary => new AvatarRowViewModel { Summary = summary }).ToList();

            Bases.Clear();
            BaseNames.Clear();

            foreach (var summary in bases)
            {
                var name = summary.Group.Name;
                Bases.Add(new AvatarBaseRowViewModel
                {
                    Summary = summary,
                    ToggleInferCommand = new RelayCommand(() => _ = ToggleInferAsync(name, !summary.Group.InferClothing)),
                    RenameCommand = new RelayCommand(() => RenameBase(name)),
                    DeleteCommand = new RelayCommand(() => DeleteBase(name)),
                });
                BaseNames.Add(name);
            }

            IsLoading = false;
            Rebuild();
        });
    }

    private void Rebuild()
    {
        var selectedId = Selected?.ItemId;

        var matched = _all
            .Where(row => _query.Length == 0
                || row.Name.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                || row.ItemId.Contains(_query, StringComparison.Ordinal))
            .ToList();

        var ownedCount = matched.Count(row => row.IsOwned);

        // 未所有も必ず出す。手持ちの衣装の対応先が未所有アバターなのは普通で、
        // 隠すと一覧がほぼ空になる（実データでも主力の対応先が未所有だった）
        foreach (var row in matched)
        {
            row.GroupName = row.IsOwned
                ? $"所有しているアバター（{ownedCount}）"
                : $"対応表記で見かけたアバター（{matched.Count - ownedCount}）";
        }

        Rows.Clear();
        foreach (var row in matched)
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasBases));
        OnPropertyChanged(nameof(IsEmpty));

        Selected = matched.FirstOrDefault(row => row.ItemId == selectedId) ?? matched.FirstOrDefault();
    }

    private async Task DetectAsync()
    {
        IsDetecting = true;
        Status = "手元の説明文とタグを読んでいます…";

        try
        {
            var progress = new Progress<AvatarDetectProgress>(report =>
                Status = $"{report.Phase}　{report.Done} / {report.Total}");

            var result = await Task.Run(() => _services.Avatars.DetectAsync(progress));

            var parts = new List<string>
            {
                $"{result.ItemsScanned} 件を見て、{result.ItemsUpdated} 件に対応アバターを書きました",
                $"アバター {result.AvatarsFound} 体",
            };

            if (result.BaseGroupsFound > 0)
            {
                parts.Add($"共通素体 {result.BaseGroupsFound} グループ");
            }

            if (result.Requests > 0)
            {
                parts.Add($"BOOTHへの問い合わせ {result.Requests} 回");
            }

            if (result.Unresolved > 0)
            {
                parts.Add($"通信できず保留 {result.Unresolved} 件（次回もう一度試します）");
            }

            Status = string.Join(" / ", parts);
            await LoadAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = $"検出できませんでした：{exception.Message}";
        }
        finally
        {
            IsDetecting = false;
        }
    }

    private async Task SetBaseAsync()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(BaseInput))
        {
            return;
        }

        await _services.Avatars.SetBaseAsync(Selected.ItemId, BaseInput);
        await LoadAsync();
    }

    private async Task ClearBaseAsync()
    {
        if (Selected is null)
        {
            return;
        }

        await _services.Avatars.SetBaseAsync(Selected.ItemId, null);
        BaseInput = string.Empty;
        await LoadAsync();
    }

    private async Task ToggleInferAsync(string name, bool infer)
    {
        await _services.Avatars.SetInferClothingAsync(name, infer);
        Status = infer
            ? $"「{name}」の一致から衣装の互換を広げます。"
            : $"「{name}」の一致では衣装の互換を広げません。";
        await LoadAsync();
    }

    private void RenameBase(string name)
    {
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            $"「{name}」の新しい名前を入れてください。\n全ての商品の宣言も一緒に書き換えます。",
            "共通素体の名前を変える",
            name);

        if (string.IsNullOrWhiteSpace(input) || input == name)
        {
            return;
        }

        _ = RenameBaseAsync(name, input);
    }

    private async Task RenameBaseAsync(string oldName, string newName)
    {
        var updated = await _services.Avatars.RenameBaseAsync(oldName, newName);
        Status = $"「{oldName}」を「{newName}」に変えました（商品 {updated} 件を書き換え）。";
        await LoadAsync();
    }

    private void DeleteBase(string name)
    {
        var answer = System.Windows.MessageBox.Show(
            $"共通素体「{name}」を消します。\n所属していたアバターは所属無しに戻り、素体経由の対応も出なくなります。",
            "共通素体を消す",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        _ = DeleteBaseAsync(name);
    }

    private async Task DeleteBaseAsync(string name)
    {
        var updated = await _services.Avatars.DeleteBaseAsync(name);
        Status = $"「{name}」を消しました（商品 {updated} 件を書き換え）。";
        await LoadAsync();
    }

    private async Task AddAliasAsync()
    {
        if (Selected is null || AliasInput.Trim().Length < 2)
        {
            return;
        }

        await _services.Avatars.AddAliasAsync(Selected.ItemId, AliasInput);
        AliasInput = string.Empty;
        await LoadAsync();
    }

    private async Task RemoveAliasAsync(string? display)
    {
        if (Selected is null || display is null)
        {
            return;
        }

        // 表示は「くうた（3）」の形なので、括弧より前を名前として扱う
        var text = display.Split('（')[0];
        await _services.Avatars.RemoveAliasAsync(Selected.ItemId, text);
        await LoadAsync();
    }

    private async Task ToggleOwnedAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var next = !Selected.Summary.Entry.IsOwnedManually;
        await _services.Avatars.SetOwnedManuallyAsync(Selected.ItemId, next);
        await LoadAsync();
    }

    private async Task SetOverrideAsync(string? mode)
    {
        if (Selected is null)
        {
            return;
        }

        bool? value = mode switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };

        await _services.Avatars.SetAvatarOverrideAsync(Selected.ItemId, value);
        await LoadAsync();
    }

    private async Task RecheckAsync()
    {
        if (Selected is null)
        {
            return;
        }

        Status = "BOOTHに問い合わせています…";
        var ok = await _services.Avatars.RecheckAsync(Selected.ItemId);
        Status = ok ? "確認し直しました。" : "確認できませんでした（通信の失敗か、取得の設定がありません）。";
        await LoadAsync();
    }

    private void OpenBooth()
    {
        if (Selected is null)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"https://booth.pm/ja/items/{Selected.ItemId}",
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 開けなくてもアプリは動き続ける
        }
    }

    /// <summary>このアバター向けの商品を検索で見る。件数だけ見せても何も判断できない。</summary>
    private void ShowItems()
    {
        if (Selected is null)
        {
            return;
        }

        _main.Search.ShowOnlyAvatar(Selected.ItemId, Selected.Name);
        _main.ShowSearch();
    }
}
