using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 改変に使ったもの1件。
///
/// **並びが導入の順**なので、位置（<see cref="Index"/>）が意味を持つ。
/// 位置で指すのは、同じ商品を別バージョンで2回足せるため。
/// </summary>
public sealed class ModificationMemberRowViewModel
{
    public required int Index { get; init; }

    /// <summary>並びの全体の数。端で矢印を消すためだけに持つ。</summary>
    public required int Total { get; init; }

    public required ModificationMember Member { get; init; }

    /// <summary>手元にある商品なら名前。無ければIDのまま。</summary>
    public required string Name { get; init; }

    /// <summary>導入の順。1から数える（人が読む番号）。</summary>
    public string OrderText => $"{Index + 1}";

    /// <summary>
    /// 使ったファイルの状態。
    ///
    /// **空欄の意味を言い分ける。**Unityへ送って足した分はどのzipを使ったかが
    /// 残っているが、手で足した分は分からない。**推定で埋めない。**
    /// </summary>
    public string SourceText => Member.IsFromUnity
        ? Member.Package is { } package ? Path.GetFileName(package) : "Unityへ送った記録あり"
        : "どのファイルを使ったかは分かりません";

    public bool IsFromUnity => Member.IsFromUnity;

    /// <summary>手元にまだあるか。無くても記録は残す（そのとき使ったのは事実）。</summary>
    public required bool IsMissing { get; init; }

    /// <summary>
    /// 使った種類の名前（1種類しかない商品では空）。
    ///
    /// **番号は出さない。**記録には番号で残しているが、人に見せて意味があるのは名前だけ。
    /// 商品が手元から消えたりBOOTHから種類が消えたりすると引けなくなるので、
    /// そのときは名前が分からないと言う（番号を見せても伝わらない）。
    /// </summary>
    public required string VariationText { get; init; }

    public bool HasVariation => VariationText.Length > 0;

    // **端では矢印を押せなくする。**押せるのに何も起きないボタンは嘘になる
    public bool CanMoveBack => Index > 0;

    public bool CanMoveForward => Index < Total - 1;
}

/// <summary>改変に貼った写真1枚。</summary>
public sealed class ModificationImageViewModel
{
    public required string FileName { get; init; }

    public required BitmapSource? Image { get; init; }

    public required int Index { get; init; }

    public required int Total { get; init; }

    public bool CanMoveBack => Index > 0;

    public bool CanMoveForward => Index < Total - 1;
}

/// <summary>紐付けの候補に出すUnityプロジェクト1件。</summary>
public sealed class UnityProjectRowViewModel
{
    public required UnityProjectCandidate Candidate { get; init; }

    public string Name => Candidate.Name;

    /// <summary>置き場所とバージョン。同名のプロジェクトを見分けるために出す。</summary>
    public string Detail
    {
        get
        {
            var version = Candidate.Version ?? "バージョンが読めません";
            return Candidate.Folder.Length > 0 ? $"{version}　{Candidate.Folder}" : version;
        }
    }

    /// <summary>いま開いているものは目印を付ける。紐付けたいのは大抵これ。</summary>
    public bool IsOpen => Candidate.IsOpen;

    public bool IsMissing => !Candidate.Exists;

    /// <summary>これが今の紐付け先か。二重に押させないために出す。</summary>
    public required bool IsCurrent { get; init; }
}

/// <summary>
/// 改変の詳細。
///
/// 構成物・写真・Unityプロジェクト・メモが入るので、商品ページと同じ格の画面にした
/// （アバター詳細の中で展開すると縦に伸び続ける）。決めた理由は
/// <c>設計詳細_改変の記録.md</c>。
/// </summary>
public sealed class ModificationViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly ThumbnailLoader _thumbnails;
    private readonly (string Label, Action Go)? _back;

    private string _status = string.Empty;

    public ModificationViewModel(
        ModificationRecord record,
        AppServiceContainer services,
        MainViewModel main,
        ThumbnailLoader thumbnails,
        (string Label, Action Go)? back = null)
    {
        Record = record;
        _services = services;
        _main = main;
        _thumbnails = thumbnails;
        _back = back;

        _nameInput = record.Name;
        _memoInput = record.Memo ?? string.Empty;

        BackCommand = new RelayCommand(() => (_back?.Go ?? _main.ShowAvatars).Invoke());
        SaveNameCommand = new RelayCommand(() => _ = SaveNameAsync(), () => NameChanged);
        SaveMemoCommand = new RelayCommand(() => _ = SaveMemoAsync(), () => MemoChanged);
        AddImageCommand = new RelayCommand(() => _ = AddImageAsync());
        RemoveImageCommand = new RelayCommand(
            parameter => _ = RemoveImageAsync(parameter as ModificationImageViewModel),
            parameter => parameter is ModificationImageViewModel);
        MoveImageBackCommand = new RelayCommand(
            parameter => _ = MoveImageAsync(parameter as ModificationImageViewModel, -1),
            parameter => parameter is ModificationImageViewModel);
        MoveImageForwardCommand = new RelayCommand(
            parameter => _ = MoveImageAsync(parameter as ModificationImageViewModel, 1),
            parameter => parameter is ModificationImageViewModel);
        RemoveMemberCommand = new RelayCommand(
            parameter => _ = RemoveMemberAsync(parameter as ModificationMemberRowViewModel),
            parameter => parameter is ModificationMemberRowViewModel);
        MoveMemberBackCommand = new RelayCommand(
            parameter => _ = MoveMemberAsync(parameter as ModificationMemberRowViewModel, -1),
            parameter => parameter is ModificationMemberRowViewModel);
        MoveMemberForwardCommand = new RelayCommand(
            parameter => _ = MoveMemberAsync(parameter as ModificationMemberRowViewModel, 1),
            parameter => parameter is ModificationMemberRowViewModel);
        OpenItemCommand = new RelayCommand(
            parameter => _ = OpenItemAsync(parameter as ModificationMemberRowViewModel),
            parameter => parameter is ModificationMemberRowViewModel);
        AddMemberCommand = new RelayCommand(parameter => _ = AddMemberAsync(parameter as string));
        OpenProjectCommand = new RelayCommand(() => OpenProject(), () => HasProject);
        LinkProjectCommand = new RelayCommand(
            parameter => _ = LinkProjectAsync(parameter as UnityProjectRowViewModel),
            parameter => parameter is UnityProjectRowViewModel);
        UnlinkProjectCommand = new RelayCommand(() => _ = LinkProjectAsync(null), () => HasProject);
        RefreshProjectsCommand = new RelayCommand(() => _ = LoadProjectsAsync());
        OpenProjectFolderCommand = new RelayCommand(
            () => Shell.Reveal(Record.UnityProject), () => HasProject);

        _ = ReloadAsync();
    }

    public ModificationRecord Record { get; private set; }

    public RelayCommand BackCommand { get; }

    public string BackText => _back is { } back ? $"← {back.Label}" : "← アバターの管理";

    public RelayCommand SaveNameCommand { get; }

    public RelayCommand SaveMemoCommand { get; }

    public RelayCommand AddImageCommand { get; }

    public RelayCommand RemoveImageCommand { get; }

    public RelayCommand MoveImageBackCommand { get; }

    public RelayCommand MoveImageForwardCommand { get; }

    public RelayCommand RemoveMemberCommand { get; }

    public RelayCommand MoveMemberBackCommand { get; }

    public RelayCommand MoveMemberForwardCommand { get; }

    public RelayCommand OpenItemCommand { get; }

    public RelayCommand AddMemberCommand { get; }

    public RelayCommand OpenProjectCommand { get; }

    public RelayCommand LinkProjectCommand { get; }

    public RelayCommand UnlinkProjectCommand { get; }

    public RelayCommand RefreshProjectsCommand { get; }

    public RelayCommand OpenProjectFolderCommand { get; }

    /// <summary>使ったもの。**並びが導入の順。**</summary>
    public ObservableCollection<ModificationMemberRowViewModel> Members { get; } = [];

    public bool HasMembers => Members.Count > 0;

    /// <summary>
    /// 構成物が空のときに出す文。
    ///
    /// **次にやることを書く。**空欄だけだと、どうやって足すのか分からない。
    /// </summary>
    public string MembersEmptyText =>
        "まだ足していません。下の欄から探して足すか、商品ページの「改変に足して送る」でUnityへ送ると自動で入ります。";

    public ObservableCollection<ModificationImageViewModel> Images { get; } = [];

    public bool HasImages => Images.Count > 0;

    public string ImagesEmptyText =>
        "改変後の姿を貼れます。何枚でも入ります（まとめて選ぶか、ここへ落としてください）。";

    /// <summary>足す商品の候補。手元にある商品の名前。</summary>
    public ObservableCollection<string> ItemSuggestions { get; } = [];

    // ---- アバター ----

    public string AvatarText { get; private set; } = string.Empty;

    public string AvatarItemId => Record.AvatarItemId;

    // ---- 名前 ----

    private string _nameInput;

    public string NameInput
    {
        get => _nameInput;
        set
        {
            if (SetField(ref _nameInput, value))
            {
                OnPropertyChanged(nameof(NameChanged));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool NameChanged => NameInput.Trim().Length > 0 && NameInput.Trim() != Record.Name;

    // ---- メモ ----

    private string _memoInput;

    public string MemoInput
    {
        get => _memoInput;
        set
        {
            if (SetField(ref _memoInput, value))
            {
                OnPropertyChanged(nameof(MemoChanged));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool MemoChanged => MemoInput != (Record.Memo ?? string.Empty);

    // ---- Unityプロジェクト ----

    /// <summary>紐付けの候補。開いているものが先に来る。</summary>
    public ObservableCollection<UnityProjectRowViewModel> ProjectCandidates { get; } = [];

    public bool HasProject => Record.HasUnityProject;

    /// <summary>紐付けたプロジェクトの名前。フォルダ名で出す（パスは長すぎる）。</summary>
    public string ProjectName => Record.UnityProject is { } path
        ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        : string.Empty;

    public string ProjectPath => Record.UnityProject ?? string.Empty;

    /// <summary>
    /// 紐付けたものが消えていないか。
    ///
    /// **黙って外さない。**消したのか移しただけなのかはユーザにしか分からないので、
    /// 出すだけにして指し直す導線を残す。
    /// </summary>
    public bool ProjectMissing => HasProject && !Directory.Exists(Record.UnityProject!);

    public string ProjectEmptyText =>
        "Unityプロジェクトを紐付けると、ここから開けます。作業中のプロジェクトがあれば下に出ます。";

    /// <summary>候補が1つも無いときに出す文。</summary>
    public string ProjectCandidatesEmptyText =>
        "Unity HubとVRChat Creator Companionの一覧を見ましたが、プロジェクトが見つかりませんでした。"
        + "一度Unityで開いたプロジェクトなら出ます。";

    public bool HasProjectCandidates => ProjectCandidates.Count > 0;

    // ---- 状態表示 ----

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

    public string CreatedText => $"作成 {Record.CreatedAt:yyyy-MM-dd}";

    public string UpdatedText => $"更新 {Record.UpdatedAt:yyyy-MM-dd}";

    /// <summary>
    /// 記録を読み直して画面を組み直す。
    ///
    /// 商品名は毎回引く。改変には商品IDしか持たせていないので
    /// （名前を写すと、商品側で名前を変えたときにずれる）。
    /// </summary>
    private async Task ReloadAsync()
    {
        if (await _services.Modifications.LoadAsync(Record.Id) is { } fresh)
        {
            Record = fresh;
        }

        var registry = _services.Store.Avatars.Load();
        var entry = registry.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.ItemId, Record.AvatarItemId, StringComparison.Ordinal));

        AvatarText = entry?.DisplayName
            ?? entry?.BoothName
            ?? Record.AvatarItemId;

        Members.Clear();
        for (var index = 0; index < Record.Members.Count; index++)
        {
            var member = Record.Members[index];
            var item = await _services.Store.Items.LoadAsync(member.ItemId);

            Members.Add(new ModificationMemberRowViewModel
            {
                Index = index,
                Total = Record.Members.Count,
                Member = member,
                Name = item?.DisplayName ?? member.ItemId,

                // 手元に無くても記録は残す。そのとき使ったのは事実
                IsMissing = item is null || !item.IsDownloaded,

                VariationText = VariationLabel(member, item),
            });
        }

        Images.Clear();
        var directory = _services.Paths.ModificationImagesDir(Record.Id);
        for (var index = 0; index < Record.Images.Count; index++)
        {
            var image = Record.Images[index];
            var path = Path.Combine(directory, image.FileName);
            Images.Add(new ModificationImageViewModel
            {
                FileName = image.FileName,
                Image = File.Exists(path) ? _thumbnails.Load(path) : null,
                Index = index,
                Total = Record.Images.Count,
            });
        }

        await LoadSuggestionsAsync();
        await LoadProjectsAsync();

        foreach (var name in new[]
        {
            nameof(Record), nameof(AvatarText), nameof(HasMembers), nameof(HasImages),
            nameof(CreatedText), nameof(UpdatedText), nameof(NameChanged), nameof(MemoChanged),
            nameof(HasProject), nameof(ProjectName), nameof(ProjectPath), nameof(ProjectMissing),
            nameof(HasProjectCandidates),
        })
        {
            OnPropertyChanged(name);
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 紐付けの候補を読み直す。
    ///
    /// **押されたときと開いたときに読む。**一覧は他のアプリが書くもので、
    /// こちらが持っていても古くなる（Unityを開いたら開いている印も変わる）。
    /// </summary>
    private async Task LoadProjectsAsync()
    {
        var found = await Task.Run(() => UnityProjects.Discover());

        ProjectCandidates.Clear();
        foreach (var candidate in found)
        {
            ProjectCandidates.Add(new UnityProjectRowViewModel
            {
                Candidate = candidate,
                IsCurrent = ModificationService.SamePath(candidate.Path, Record.UnityProject),
            });
        }

        // 紐付けたものが一覧に無いことがある（HubにもVCCにも載っていない）。
        // そのときも「今どこを指しているか」は画面に出るので、候補には足さない
        OnPropertyChanged(nameof(HasProjectCandidates));
    }

    /// <summary>紐付けを差し替える。null を渡すと外す。</summary>
    private async Task LinkProjectAsync(UnityProjectRowViewModel? row)
    {
        await _services.Commands.ExecuteAsync(
            new UiCommand.SetModificationProject(Record.Id, row?.Candidate.Path));

        Status = row is null
            ? "Unityプロジェクトの紐付けを外しました。"
            : $"「{row.Name}」を紐付けました。";

        await ReloadAsync();
    }

    /// <summary>
    /// 紐付けたプロジェクトを開く。
    ///
    /// **3通りに言い分ける。**開いていたら手前に出るだけなので、
    /// 何も起きなかったように見えないように結果を出す。
    /// </summary>
    private void OpenProject()
    {
        var name = ProjectName;

        Status = UnityLaunch.OpenProject(Record.UnityProject) switch
        {
            UnityOpenResult.BroughtToFront => $"「{name}」は既に開いています。そのUnityを手前に出しました。",
            UnityOpenResult.Launched => $"「{name}」をUnityで開いています。少し時間がかかります。",
            UnityOpenResult.HandedToHub =>
                $"このプロジェクトのUnityが手元に無いので、Unity Hubに渡しました。Hubが入れるか聞いてくれます。",
            UnityOpenResult.Missing =>
                $"「{name}」が見つかりません。移したのなら、下の一覧から指し直せます。",
            _ => "Unityを開けませんでした。Unity Hubから開いてみてください。",
        };
    }

    /// <summary>記録に残した種類の番号を、人が読める名前に直す。</summary>
    private static string VariationLabel(ModificationMember member, ItemRecord? item)
    {
        if (member.VariationId is not { } id)
        {
            return string.Empty;
        }

        var variation = item?.Booth.Variations.FirstOrDefault(candidate => candidate.Id == id);
        return variation?.Name is { Length: > 0 } name ? name : "種類の名前が分かりません";
    }

    /// <summary>
    /// 足す商品の候補。**手元にあるものだけ**を出す。
    /// 持っていない商品を改変に足せても、再現には使えない。
    /// </summary>
    private async Task LoadSuggestionsAsync()
    {
        ItemSuggestions.Clear();

        var loaded = await _services.Store.Items.LoadAllAsync();
        foreach (var item in loaded.Items
            .Where(item => item.IsDownloaded)
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture))
        {
            ItemSuggestions.Add(item.DisplayName);
        }
    }

    private async Task SaveNameAsync()
    {
        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.RenameModification(Record.Id, NameInput));

        Status = result is CommandResult.Failed failed ? failed.Message : "名前を変えました。";
        await ReloadAsync();
        NameInput = Record.Name;
    }

    private async Task SaveMemoAsync()
    {
        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.SetModificationMemo(Record.Id, MemoInput));

        Status = result is CommandResult.Failed failed ? failed.Message : "メモを保存しました。";
        await ReloadAsync();
    }

    // ---- 構成物 ----

    /// <summary>
    /// 名前から商品を引いて足す。
    ///
    /// **手で足した分は「どのファイルを使ったか」が空のまま。**
    /// 商品ページから送れば埋まるので、そこは推定で埋めない。
    /// </summary>
    private async Task AddMemberAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var loaded = await _services.Store.Items.LoadAllAsync();
        var item = loaded.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.DisplayName, name.Trim(), StringComparison.CurrentCultureIgnoreCase));

        if (item is null)
        {
            Status = $"「{name.Trim()}」という商品が手元に見つかりません。";
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(
            Record.Id,
            new ModificationMember { ItemId = item.Id }));

        Status = result is CommandResult.Failed failed
            ? failed.Message
            : $"「{item.DisplayName}」を足しました。";

        await ReloadAsync();
    }

    private async Task RemoveMemberAsync(ModificationMemberRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(
            new UiCommand.RemoveModificationMember(Record.Id, row.Index));

        Status = $"「{row.Name}」を外しました。";
        await ReloadAsync();
    }

    private async Task MoveMemberAsync(ModificationMemberRowViewModel? row, int delta)
    {
        if (row is null)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(
            new UiCommand.MoveModificationMember(Record.Id, row.Index, delta));

        await ReloadAsync();
    }

    private async Task OpenItemAsync(ModificationMemberRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (await _services.Store.Items.LoadAsync(row.Member.ItemId) is { } item)
        {
            var label = Record.Name;
            var record = Record;
            _main.ShowItem(item, (label, () => _main.ShowModification(record)));
            return;
        }

        Status = $"「{row.Name}」は手元にありません（記録は残しています）。";
    }

    // ---- 写真 ----

    /// <summary>
    /// 写真を選んで足す。**複数まとめて選べる**（ユーザ指示）。
    ///
    /// 1つの改変には正面・背面・表情差分と何枚も撮るので、
    /// 1枚ずつ選ばせるのは手数が合わない。
    /// </summary>
    private async Task AddImageAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "この改変に貼る写真を選ぶ",
            Filter = "画像 (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp",
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await AddImageFilesAsync(dialog.FileNames);
    }

    /// <summary>写真のファイルを足す。落とした場合と選んだ場合で同じ道を通す。</summary>
    public async Task AddImageFilesAsync(IReadOnlyList<string> paths)
    {
        var added = 0;

        foreach (var path in paths)
        {
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Status = $"{Path.GetFileName(path)} を読めませんでした。";
                continue;
            }

            if (await AddImageBytesAsync(bytes))
            {
                added++;
            }
            else
            {
                Status = $"{Path.GetFileName(path)} は画像として読めませんでした。";
            }
        }

        if (added > 0)
        {
            Status = $"写真を {added} 枚貼りました。";
            await ReloadAsync();
        }
    }

    /// <summary>写真の中身を1枚足す。貼り付けもここを通る。</summary>
    public async Task<bool> AddImageBytesAsync(byte[] bytes)
    {
        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.AddModificationImage(Record.Id, bytes));

        return result is not CommandResult.Failed;
    }

    /// <summary>貼り付けたあとに、外から組み直させる。</summary>
    public async Task PasteImageAsync(byte[] bytes)
    {
        Status = await AddImageBytesAsync(bytes)
            ? "写真を1枚貼りました。"
            : "画像として読めませんでした。";

        await ReloadAsync();
    }

    private async Task RemoveImageAsync(ModificationImageViewModel? image)
    {
        if (image is null)
        {
            return;
        }

        // **取り返しがつかない。**ファイルごと消える
        var answer = System.Windows.MessageBox.Show(
            "この写真を消します。\n\n元には戻せません。",
            "写真を消す",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(
            new UiCommand.RemoveModificationImage(Record.Id, image.FileName));

        Status = "写真を消しました。";
        await ReloadAsync();
    }

    private async Task MoveImageAsync(ModificationImageViewModel? image, int delta)
    {
        if (image is null)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(
            new UiCommand.MoveModificationImage(Record.Id, image.FileName, delta));

        await ReloadAsync();
    }
}
