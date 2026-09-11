using System.Collections.ObjectModel;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 改変の一覧の1行。
///
/// **同じ名前を許してあるので、日付が見分けの手掛かり**（`設計詳細_改変の記録.md` Q27）。
/// </summary>
public sealed class ModificationRowViewModel(ModificationRecord record)
{
    public ModificationRecord Record { get; } = record;

    public string Name { get; } = record.Name;

    public string CreatedText { get; } = record.CreatedAt.ToString("yyyy-MM-dd");

    /// <summary>使ったものの件数。0件でも「まだ足していない」と分かるように出す。</summary>
    public string MemberText { get; } = record.Members.Count == 0
        ? "まだ足していません"
        : $"{record.Members.Count} 件";

    public bool HasUnityProject { get; } = record.HasUnityProject;

    /// <summary>紐付けたプロジェクトのフォルダ名。フルパスは詳細で出す。</summary>
    public string ProjectText { get; } = record.UnityProject is { } path
        ? System.IO.Path.GetFileName(path.TrimEnd('\\', '/'))
        : string.Empty;
}

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

    /// <summary>手で「アバターとして扱わない」にしたもの。一覧には残すが、そうと分かるようにする。</summary>
    public bool IsExcluded => Summary.Entry.AvatarOverride == false;

    public string BaseText => Summary.Entry.BaseName ?? string.Empty;

    public bool HasBase => !string.IsNullOrWhiteSpace(Summary.Entry.BaseName);

    /// <summary>直接対応と素体経由は分けて出す。素体経由は推定なので同じ顔で並べない。</summary>
    public string CountText => Summary.ViaBaseCount > 0
        ? $"{Summary.DirectCount} + 素体経由 {Summary.ViaBaseCount}"
        : $"{Summary.DirectCount}";

    /// <summary>飾り記号を飛ばした頭文字。そのままだと「【」ばかり並ぶ</summary>
    public string Initial => AvatarText.InitialOf(Name);

    /// <summary>BOOTHの正式名。短い表示名だけでは分からないときのために持ち回る</summary>
    public string BoothName => Summary.Entry.BoothName ?? string.Empty;

    /// <summary>
    /// この行を引ける語。表示名・商品ID・BOOTHの正式名・別名。
    ///
    /// 表示名だけで引けると思うと**引けない場面がある**。表示名は短くしてあるので、
    /// BOOTHの正式名の一部で探すと当たらない。VRChatでは商品IDで探す習慣もある。
    /// 別名（誤記や略称の受け皿）も入れておく。
    /// </summary>
    public bool Matches(string query)
        => Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || ItemId.Contains(query, StringComparison.Ordinal)
            || BoothName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Summary.Entry.Aliases.Any(alias => alias.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase));
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

    private string _itemIdInput = string.Empty;

    /// <summary>
    /// 配布されている素体の商品ID。BOOTHの商品URLを貼っても読む。
    /// 結んでおくと、その素体のページへ行けるようになり、所有の判定にも使える。
    /// </summary>
    public string ItemIdInput
    {
        get => _itemIdInput;
        set => SetField(ref _itemIdInput, value);
    }

    public RelayCommand? ToggleInferCommand { get; set; }

    public RelayCommand? RenameCommand { get; set; }

    public RelayCommand? DeleteCommand { get; set; }

    public RelayCommand? SetItemIdCommand { get; set; }
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
    /// <summary>名前の候補を出す数。並べすぎると選べない。</summary>
    private const int MaxNameSuggestions = 5;

    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    private AvatarRowViewModel? _selected;
    private bool _isLoading = true;
    private bool _isDetecting;
    private string _status = string.Empty;
    private string _query = string.Empty;
    private string _baseInput = string.Empty;
    private string _aliasInput = string.Empty;
    private string _nameInput = string.Empty;
    private List<AvatarRowViewModel> _all = [];

    public AvatarsViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        DetectCommand = new RelayCommand(() => _ = DetectAsync(), () => !IsDetecting);
        SetBaseCommand = new RelayCommand(() => _ = SetBaseAsync());
        ClearBaseCommand = new RelayCommand(() => _ = ClearBaseAsync());
        AddAliasCommand = new RelayCommand(() => _ = AddAliasAsync());
        SaveMemoCommand = new RelayCommand(() => _ = SaveMemoAsync());
        RenameCommand = new RelayCommand(() => _ = RenameAsync());
        UseNameSuggestionCommand = new RelayCommand(
            parameter => { if (parameter is string name) { NameInput = name; } },
            parameter => parameter is string);
        RemoveAliasCommand = new RelayCommand(parameter => _ = RemoveAliasAsync(parameter as string));
        ToggleOwnedCommand = new RelayCommand(() => _ = ToggleOwnedAsync());
        RecheckCommand = new RelayCommand(() => _ = RecheckAsync());
        TreatAsAvatarCommand = new RelayCommand(parameter => _ = SetOverrideAsync(parameter as string));
        OpenBoothCommand = new RelayCommand(OpenBooth);
        ShowItemsCommand = new RelayCommand(ShowItems);
        CreateModificationCommand = new RelayCommand(
            () => _ = CreateModificationAsync(),
            () => Selected is not null && ModificationNameInput.Trim().Length > 0);
        OpenModificationCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is ModificationRowViewModel row)
                {
                    // 戻り先をアバターの管理にしておく。改変からは必ずここへ帰る
                    _main.ShowModification(row.Record, ("アバターの管理", _main.ShowAvatars));
                }
            },
            parameter => parameter is ModificationRowViewModel);
        DeleteModificationCommand = new RelayCommand(
            parameter => _ = DeleteModificationAsync(parameter as ModificationRowViewModel),
            parameter => parameter is ModificationRowViewModel);

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

    public RelayCommand SaveMemoCommand { get; }

    public RelayCommand RenameCommand { get; }

    public RelayCommand RemoveAliasCommand { get; }

    public RelayCommand ToggleOwnedCommand { get; }

    public RelayCommand RecheckCommand { get; }

    public RelayCommand TreatAsAvatarCommand { get; }

    public RelayCommand OpenBoothCommand { get; }

    public RelayCommand ShowItemsCommand { get; }

    // ---- 改変の記録 ----

    /// <summary>選んでいるアバターの改変。新しく作った順。</summary>
    public ObservableCollection<ModificationRowViewModel> Modifications { get; } = [];

    public bool HasModifications => Modifications.Count > 0;

    public RelayCommand CreateModificationCommand { get; }

    /// <summary>改変の詳細を開く。商品ページと同じ格の画面へ差し替える</summary>
    public RelayCommand OpenModificationCommand { get; }

    public RelayCommand DeleteModificationCommand { get; }

    private string _modificationNameInput = string.Empty;

    /// <summary>作る改変の名前。「普段着」「制服」のような呼び分け。</summary>
    public string ModificationNameInput
    {
        get => _modificationNameInput;
        set
        {
            if (SetField(ref _modificationNameInput, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 改変が無いときに出す文。
    ///
    /// **何のための場所かを書く。**空欄だけだと、使い方が分からないまま放置される
    /// （`usedOn` が15件中0件だったのと同じ道を避ける）。
    /// </summary>
    public string ModificationEmptyText =>
        "まだありません。着せ替えごとに1つ作ると、使った衣装やギミックをまとめて残せます。";

    private async Task LoadModificationsAsync()
    {
        Modifications.Clear();

        if (Selected is { } row)
        {
            foreach (var record in await _services.Modifications.LoadForAvatarAsync(row.ItemId))
            {
                Modifications.Add(new ModificationRowViewModel(record));
            }
        }

        OnPropertyChanged(nameof(HasModifications));
    }

    private async Task CreateModificationAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var name = ModificationNameInput.Trim();

        // **同じ名前を許すが、黙って2つ並べない。**
        // 作り直したいのか、間違えて2つ目を作ろうとしているのかは人にしか分からない
        if (await _services.Modifications.HasSameNameAsync(row.ItemId, name))
        {
            var answer = System.Windows.MessageBox.Show(
                $"「{name}」という改変が既にあります。\n\n"
                + "同じ名前で作れます（作り直したいときのため）。\n"
                + "一覧では作った日付で見分けられます。",
                "同じ名前の改変があります",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.Cancel);

            if (answer != System.Windows.MessageBoxResult.OK)
            {
                return;
            }
        }

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.CreateModification(row.ItemId, name));

        if (result is CommandResult.Failed failed)
        {
            Status = failed.Message;
            return;
        }

        ModificationNameInput = string.Empty;
        Status = $"改変「{name}」を作りました。";
        await LoadModificationsAsync();
    }

    private async Task DeleteModificationAsync(ModificationRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        // **取り返しがつかないので、何が消えるかを数で書く**
        var images = row.Record.Images.Count;
        var answer = System.Windows.MessageBox.Show(
            $"改変「{row.Name}」を消します。\n\n"
            + (images > 0 ? $"貼った画像 {images} 枚も一緒に消えます。\n" : string.Empty)
            + "元には戻せません。使った商品そのものは消えません。",
            "改変を消す",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.DeleteModification(row.Record.Id));
        Status = result is CommandResult.Failed failed ? failed.Message : $"改変「{row.Name}」を消しました。";
        await LoadModificationsAsync();
    }

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

    /// <summary>表示名の編集欄。BOOTHの正式名は別に残す。</summary>
    public string NameInput
    {
        get => _nameInput;
        set => SetField(ref _nameInput, value);
    }

    private string _memoInput = string.Empty;

    /// <summary>
    /// このアバターについての覚え書き。
    /// 「素体は同じだが肩幅が違う」のような、検出では拾えない事情を残す場所。
    /// </summary>
    public string MemoInput
    {
        get => _memoInput;
        set => SetField(ref _memoInput, value);
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
                NameInput = value?.Name ?? string.Empty;
                MemoInput = value?.Summary.Entry.Memo ?? string.Empty;

                foreach (var name in new[]
                {
                    nameof(HasSelection), nameof(SelectedName), nameof(SelectedIdText),
                    nameof(SelectedCategoryText), nameof(SelectedCountText), nameof(Aliases),
                    nameof(OwnedButtonText), nameof(SelectedOwnedText), nameof(SelectedSeenAsText),
                    nameof(SelectedCheckedText), nameof(SelectedBaseNote), nameof(HasSelectedBaseNote),
                    nameof(SelectedBoothName), nameof(HasSelectedBoothName),
                    nameof(NeedsName), nameof(NameSuggestions), nameof(HasNameSuggestions),
                    nameof(ReferencedByText), nameof(HasReferencedBy),
                    nameof(HasModifications),
                })
                {
                    OnPropertyChanged(name);
                }

                // 選んだアバターの改変を読み直す。待たせないので投げっぱなしにする
                ModificationNameInput = string.Empty;
                _ = LoadModificationsAsync();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    /// <summary>
    /// 名前がまだ無いか、商品IDのままの項目か。
    /// BOOTHが404を返す項目は名前を引けないので、手元の材料から候補を出す。
    /// </summary>
    public bool NeedsName => Selected is not null
        && (string.IsNullOrWhiteSpace(Selected.Summary.Entry.DisplayName)
            || Selected.Summary.Entry.DisplayName == Selected.ItemId);

    /// <summary>
    /// 名前の候補。**別名から取る。**
    ///
    /// 別名（<c>nameHints</c>）は <c>IsAvatar</c> の判定を通さずに溜まるので、
    /// 404の項目にも「くうた」「くうた対応」のような呼び名が残っている。
    ///
    /// **絞った1つを先頭に出し、残りも並べる。**絞る根拠が弱いから——
    /// 「対応アバター」節由来は1件ずつしか溜まらないことが多く、回数が並ぶと選べない。
    /// 1つに絞って外していたらユーザは打ち直すことになる。
    ///
    /// <c>ShortenName</c> による切り出しは**別名が1件も無いときの最後の手段**。
    /// 参照商品の名前をそのまま入れるのは明確に誤り——それは*衣装*の名前で、
    /// アバターの名前ではない。
    /// </summary>
    public IReadOnlyList<string> NameSuggestions
    {
        get
        {
            if (Selected is null)
            {
                return [];
            }

            var entry = Selected.Summary.Entry;

            var fromAliases = entry.Aliases
                .Where(alias => !alias.Rejected && alias.Text.Trim().Length >= 2)
                .OrderByDescending(alias => alias.Count)
                .ThenBy(alias => alias.Text.Length)
                .Select(alias => alias.Text.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(MaxNameSuggestions)
                .ToList();

            if (fromAliases.Count > 0)
            {
                return fromAliases;
            }

            var shortened = Core.Services.AvatarText.ShortenName(entry.BoothName);
            return string.IsNullOrWhiteSpace(shortened) || shortened == entry.ItemId
                ? []
                : [shortened];
        }
    }

    public bool HasNameSuggestions => NameSuggestions.Count > 0;

    /// <summary>
    /// このアバターを対応先として挙げている所持商品。
    /// **IDと件数だけでは数字で判断させることになる**ので、商品名を並べる。
    /// </summary>
    public string ReferencedByText
    {
        get
        {
            var names = Selected?.Summary.ReferencedBy ?? [];
            if (names.Count == 0)
            {
                return string.Empty;
            }

            var total = Selected!.Summary.DirectCount;
            var rest = total - 1;

            return rest > 0
                ? $"「{names[0]}」ほか {rest} 件が対応先として挙げています"
                : $"「{names[0]}」が対応先として挙げています";
        }
    }

    public bool HasReferencedBy => ReferencedByText.Length > 0;

    public RelayCommand UseNameSuggestionCommand { get; }

    public string SelectedName => Selected?.Name ?? string.Empty;

    public string SelectedIdText => Selected is null ? string.Empty : $"ID {Selected.ItemId}";

    /// <summary>BOOTHの正式名。表示名を短くしている分、元の名前も読めるようにする。</summary>
    public string SelectedBoothName => Selected?.BoothName ?? string.Empty;

    public bool HasSelectedBoothName => SelectedBoothName.Length > 0 && SelectedBoothName != SelectedName;

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
                return $"「{name}」は衣装の互換を広げない設定です。他のアバター向けの衣装は、素体経由として出ません。";
            }

            var siblings = group.Summary.MemberCount - 1;
            return siblings <= 0
                ? $"「{name}」に属しているのはこのアバターだけです。"
                : $"「{name}」の他の {siblings} 体向けの衣装も、素体経由として一緒に出ます。";
        }
    }

    public bool HasSelectedBaseNote => SelectedBaseNote.Length > 0;

    /// <summary>
    /// 照合に使っている別名。**消したものは出さない。**
    /// 行はデータに残っているが（消したという事実を次の検出まで持ち越すため）、
    /// 一覧に出すと「消したのに残っている」と読まれる。
    /// </summary>
    public IReadOnlyList<string> Aliases => Selected?.Summary.Entry.Aliases
        .Where(alias => !alias.Rejected)
        .OrderByDescending(alias => alias.Count)
        .Select(alias => alias.Count > 0 ? $"{alias.Text}（{alias.Count}）" : alias.Text)
        .ToList() ?? [];

    private static string Label(string source) => source switch
    {
        "SupportSection" => "対応アバター節",
        "Tag" => "タグ",
        "Variation" => "種類の名前",
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
                var baseRow = new AvatarBaseRowViewModel
                {
                    Summary = summary,
                    ItemIdInput = summary.Group.ItemId ?? string.Empty,
                    ToggleInferCommand = new RelayCommand(() => _ = ToggleInferAsync(name, !summary.Group.InferClothing)),
                    RenameCommand = new RelayCommand(() => RenameBase(name)),
                    DeleteCommand = new RelayCommand(() => _ = ConfirmDeleteBaseAsync(name)),
                };
                baseRow.SetItemIdCommand = new RelayCommand(() => _ = SetBaseItemIdAsync(name, baseRow.ItemIdInput));
                Bases.Add(baseRow);
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
            .Where(row => _query.Length == 0 || row.Matches(_query))
            .ToList();

        var ownedCount = matched.Count(row => row.IsOwned && !row.IsExcluded);

        // 未所有も必ず出す。手持ちの衣装の対応先が未所有アバターなのは普通で、
        // 隠すと一覧がほぼ空になる（実データでも主力の対応先が未所有だった）
        foreach (var row in matched)
        {
            row.GroupName = row.IsExcluded
                ? "アバターとして扱わないもの"
                : row.IsOwned
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

            // **UiCommand を通す。**直接呼ぶと CommandHandler の優先度の包みの外に
            // 出てしまい、既定の Metadata（取り込みの①②と同じ順位）で順番待ちする。
            // 押した人は画面の前で結果を待っているので User に乗せたい。
            // 取り込みの中の③は内側で Detection を指定しているので、そのまま待てる側に残る
            var outcome = await _services.Commands.ExecuteAsync(new UiCommand.DetectAvatars(progress));
            if (outcome is CommandResult.Failed detectFailed)
            {
                Status = detectFailed.Message;
                return;
            }

            if (outcome is not CommandResult.AvatarsDetected detected)
            {
                return;
            }

            var result = detected.Result;

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
            // 検出は途中まで進んでいることがあり、もう一度押せば続きから走る
            Status = $"検出の途中で止まりました：{exception.Message}　もう一度押すと続きから試します。";
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

    /// <summary>
    /// 素体に配布商品を結ぶ。空にすると外れる。
    /// 数字でもBOOTHの商品URLでも受ける（ブラウザから来るのは普通URLの方）。
    /// </summary>
    private async Task SetBaseItemIdAsync(string name, string input)
    {
        var trimmed = input.Trim();

        if (trimmed.Length == 0)
        {
            await _services.Avatars.SetBaseItemIdAsync(name, null);
            Status = $"「{name}」の配布商品との結び付きを外しました。";
            await LoadAsync();
            return;
        }

        var itemId = Core.Services.BoothItemId.Parse(trimmed);
        if (itemId is null)
        {
            Status = "商品IDが読み取れませんでした。数字か、BOOTHの商品ページのURLを入れてください。";
            return;
        }

        await _services.Avatars.SetBaseItemIdAsync(name, itemId);
        Status = $"「{name}」を商品 {itemId} に結び付けました。";
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

    /// <summary>
    /// 素体グループを消す。アバターまわりで唯一、取り消せない操作なので、
    /// 押す前に何件書き換わるかを数えて出す。
    /// </summary>
    private async Task ConfirmDeleteBaseAsync(string name)
    {
        var members = Bases.FirstOrDefault(row => row.Name == name)?.Summary.MemberCount ?? 0;
        var items = await Task.Run(() => _services.Avatars.CountItemsUsingBaseAsync(name));

        var answer = System.Windows.MessageBox.Show(
            $"共通素体「{name}」を消します。\n\n"
            + $"アバター {members} 体が所属無しに戻り、商品 {items} 件から素体の宣言が消えます。\n"
            + "素体経由で出ていた対応も出なくなります。\n\n"
            + "この操作は元に戻せません。同じ名前で作り直しても、所属と宣言は戻りません。",
            "共通素体を消す",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.Cancel);

        if (answer == System.Windows.MessageBoxResult.OK)
        {
            await DeleteBaseAsync(name);
        }
    }

    private async Task DeleteBaseAsync(string name)
    {
        var updated = await _services.Avatars.DeleteBaseAsync(name);
        Status = $"「{name}」を消しました（商品 {updated} 件を書き換え）。";
        await LoadAsync();
    }

    private async Task RenameAsync()
    {
        if (Selected is null || NameInput.Trim().Length == 0)
        {
            return;
        }

        await _services.Avatars.SetDisplayNameAsync(Selected.ItemId, NameInput);
        await LoadAsync();
    }

    private async Task SaveMemoAsync()
    {
        if (Selected is null)
        {
            return;
        }

        await _services.Avatars.SetMemoAsync(Selected.ItemId, MemoInput);
        Status = MemoInput.Trim().Length == 0 ? "メモを消しました。" : "メモを保存しました。";
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
        // 原因は特定できないので、見当だけ並べて判断はユーザに残す
        Status = ok
            ? "確認し直しました。"
            : "BOOTHに確認できませんでした。通信が失敗したか、取得の設定が入っていないことがあります。";
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
