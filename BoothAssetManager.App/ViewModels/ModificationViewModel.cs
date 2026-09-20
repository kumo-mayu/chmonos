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
public sealed class ModificationMemberRowViewModel : IHasItemCard
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

    /// <summary>検索と同じカード（ユーザ指示 2026-09-14）。手元に無い商品は作れないので null。</summary>
    public ItemCardViewModel? Card { get; init; }

    public bool HasCard => Card is not null;

    /// <summary>
    /// 外した行（ユーザ指示 2026-09-19）。行と記録は残し、薄くして「外した」の札を出す。
    /// 「戻す」で戻せ、「削除」で完全に消す（商品の手元のファイルと同じ二段）
    /// </summary>
    public bool IsDetached => Member.Detached;

    public bool IsUsed => !Member.Detached;

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

/// <summary>紐付けたプロジェクトに入っている、手元の商品1件（#72）。</summary>
public sealed class ProjectCandidateRowViewModel
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }

    public required int Present { get; init; }

    public required int Total { get; init; }

    /// <summary>
    /// 何個あったか。**割合が低くても出す。**欲しい物だけを選んで取り込むのは普通の使い方で、
    /// 数個しか無いのは「使っていない」を意味しない。
    /// </summary>
    public string Detail => Present == Total
        ? $"{Total} 個のファイルがすべてプロジェクトにあります"
        : $"{Total} 個のファイルのうち {Present} 個がプロジェクトにあります";
}

/// <summary>
/// 改変の詳細。
///
/// 構成物・写真・Unityプロジェクト・メモが入るので、商品ページと同じ格の画面にした
/// （アバター詳細の中で展開すると縦に伸び続ける）。決めた理由は
/// <c>docs/history/modifications.md</c>。
/// </summary>
public sealed class ModificationViewModel : ViewModelBase, IGalleryHost, IItemCardHost
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly ThumbnailLoader _thumbnails;

    private string _status = string.Empty;

    public ModificationViewModel(
        ModificationRecord record,
        AppServiceContainer services,
        MainViewModel main,
        ThumbnailLoader thumbnails)
    {
        Record = record;
        _services = services;
        _main = main;
        _thumbnails = thumbnails;

        _nameInput = record.Name;
        _memoInput = record.Memo ?? string.Empty;
        _blueprintInput = record.BlueprintId ?? string.Empty;
        _saveName = new Debounced(TimeSpan.FromMilliseconds(800), () => SaveNameAsync().Forget());
        _saveMemo = new Debounced(TimeSpan.FromMilliseconds(800), () => SaveMemoAsync().Forget());
        _saveBlueprint = new Debounced(TimeSpan.FromMilliseconds(800), () => SaveBlueprintAsync().Forget());
        ChangeAvatarCommand = new RelayCommand(() => ChangeAvatarAsync().Forget());

        // 使ったものは検索と同じカード・リストで出す（ユーザ指示 2026-09-14）。どちらで出すかと列の幅は、この画面で覚える
        ListColumns = new ItemListColumns(services.PaneWidths, "modification", hasSelect: false, shopHeader: "使ったファイル");
        _isListMode = ItemListMode.IsList(services, "modification");
        ShowCardsCommand = new RelayCommand(() => SetListMode(false));
        ShowListCommand = new RelayCommand(() => SetListMode(true));

        // 戻るは画面の履歴を遡る（U23）
        BackCommand = new RelayCommand(main.GoBack);
        AddImageCommand = new RelayCommand(() => AddImageAsync().Forget());
        // ギャラリーの右クリックは引数なしで呼ぶ（いま出ている1枚が相手）。商品のギャラリーと同じ
        RemoveImageCommand = new RelayCommand(
            parameter => RemoveImageAsync(ImageFor(parameter)).Forget(),
            parameter => ImageFor(parameter) is not null);
        MoveImageBackCommand = new RelayCommand(
            parameter => MoveImageAsync(ImageFor(parameter), -1).Forget(),
            parameter => ImageFor(parameter) is { CanMoveBack: true });
        MoveImageForwardCommand = new RelayCommand(
            parameter => MoveImageAsync(ImageFor(parameter), 1).Forget(),
            parameter => ImageFor(parameter) is { CanMoveForward: true });
        PreviousImageCommand = new RelayCommand(() => GoToImage(-1), () => CanGoPreviousImage);
        NextImageCommand = new RelayCommand(() => GoToImage(1), () => CanGoNextImage);
        SelectImageCommand = new RelayCommand(SelectImage, parameter => parameter is GalleryImage);
        RemoveMemberCommand = new RelayCommand(
            parameter => SetMemberDetachedAsync(parameter as ModificationMemberRowViewModel, detached: true).Forget(),
            parameter => parameter is ModificationMemberRowViewModel);
        RestoreMemberCommand = new RelayCommand(
            parameter => SetMemberDetachedAsync(parameter as ModificationMemberRowViewModel, detached: false).Forget(),
            parameter => parameter is ModificationMemberRowViewModel);
        DeleteMemberCommand = new RelayCommand(
            parameter => DeleteMemberAsync(parameter as ModificationMemberRowViewModel).Forget(),
            parameter => parameter is ModificationMemberRowViewModel);
        MoveMemberBackCommand = new RelayCommand(
            parameter => MoveMemberAsync(parameter as ModificationMemberRowViewModel, -1).Forget(),
            parameter => parameter is ModificationMemberRowViewModel);
        MoveMemberForwardCommand = new RelayCommand(
            parameter => MoveMemberAsync(parameter as ModificationMemberRowViewModel, 1).Forget(),
            parameter => parameter is ModificationMemberRowViewModel);
        OpenItemCommand = new RelayCommand(
            parameter => OpenItemAsync(parameter as ModificationMemberRowViewModel).Forget(),
            parameter => parameter is ModificationMemberRowViewModel);
        AddMemberCommand = new RelayCommand(parameter => AddMemberAsync(parameter as string).Forget());
        OpenAvatarCommand = new RelayCommand(() => _main.ShowAvatar(AvatarItemId));
        OpenProjectCommand = new RelayCommand(() => OpenProject(), () => HasProject);
        LinkProjectCommand = new RelayCommand(
            parameter => LinkProjectAsync(parameter as UnityProjectRowViewModel).Forget(),
            parameter => parameter is UnityProjectRowViewModel);
        UnlinkProjectCommand = new RelayCommand(() => LinkProjectAsync(null).Forget(), () => HasProject);
        RefreshProjectsCommand = new RelayCommand(() => LoadProjectsAsync().Forget());
        OpenProjectFolderCommand = new RelayCommand(
            () => Shell.Reveal(Record.UnityProject), () => HasProject);
        SendAllToUnityCommand = new RelayCommand(
            // 外した行は送らない（今は使っていない物）
            () => SendToUnityAsync(Members.Where(row => row.IsUsed).ToList(), "使ったものを順にUnityへ送る").Forget(),
            () => HasMembers && !IsSendingToUnity);

        // 1件ごとの「Unity ▾」（ユーザ指示 2026-09-14：「開く」がエクスプローラなのか Unity なのか分かりにくい。インポートと選択の2択にする）
        ImportMemberCommand = new RelayCommand(
            parameter => SendToUnityAsync(
                parameter is ModificationMemberRowViewModel row ? new[] { row } : Array.Empty<ModificationMemberRowViewModel>(),
                "Unityへ送る").Forget(),
            parameter => parameter is ModificationMemberRowViewModel && !IsSendingToUnity);
        SelectMemberInUnityCommand = new RelayCommand(
            parameter => SelectMemberInUnityAsync(parameter as ModificationMemberRowViewModel).Forget(),
            parameter => parameter is ModificationMemberRowViewModel);
        FindInProjectCommand = new RelayCommand(() => FindInProjectAsync().Forget(), () => HasProject && !IsFindingInProject);
        AddCandidateCommand = new RelayCommand(
            parameter => AddCandidateAsync(parameter as ProjectCandidateRowViewModel).Forget(),
            parameter => parameter is ProjectCandidateRowViewModel);
        DeleteCommand = new RelayCommand(() => DeleteAsync().Forget());

        ReloadAsync().Forget();
    }

    public ModificationRecord Record { get; private set; }

    public RelayCommand BackCommand { get; }

    /// <summary>戻るの文言。他の画面と同じ「← {行き先}に戻る」に揃える（以前はここだけ「に戻る」が無かった・U23）。</summary>
    public string BackText => _main.BackButtonText;

    /// <summary>
    /// 改変の画面（<see cref="ModificationHubViewModel"/>）の右側に組み込んだか。
    /// 組み込んだときは戻るを出さず（左の一覧が行き来の役をする）、「この改変を消す」を出す（ユーザ判断 2026-09-13）
    /// </summary>
    public bool IsEmbedded { get; init; }

    public bool ShowsBack => !IsEmbedded;

    /// <summary>
    /// 組み込んだときは左右の列を幅に合わせる。右側は窓より狭いので、単独の画面と同じ固定の 660px では
    /// 横に送るしかなくなる
    /// </summary>
    public double BodyMinWidth => IsEmbedded ? 0 : LeftPane.Pixels + 400;

    /// <remarks>
    /// 組み込んだときは左（写真・使ったもの）に残りを全部渡す。右は最低幅（320px）で足りるが、
    /// 左の「使ったもの」は名前と操作のボタンが1行に並ぶので、半々だと名前が「【...」まで縮んだ
    /// </remarks>
    private PaneColumn? _leftPane;

    /// <summary>
    /// 左の列（ギャラリーと使ったもの）。ドラッグで幅を変えられる（ユーザ判断 2026-09-14）。
    /// 改変の画面に組み込んだときも動かせる（ユーザ指示 2026-09-14）。組み込んだときは別の鍵（既定と範囲が小さい）で覚える
    /// </summary>
    public PaneColumn LeftPane => _leftPane ??= CreateLeftPane();

    private PaneColumn CreateLeftPane()
    {
        var pane = new PaneColumn(_services.PaneWidths, IsEmbedded ? "modifications.modification.left" : "modification.left");
        pane.PropertyChanged += (_, _) => OnPropertyChanged(nameof(BodyMinWidth));
        return pane;
    }

    /// <summary>改変を消す。組み込んだときだけ出す（単独の画面ではアバターの管理の一覧から消す）。</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>消し終えた。組み込んだ側が一覧を読み直して右側を空ける。</summary>
    public event Action? Deleted;

    /// <summary>記録を読み直した（名前・メモ・使ったもの・紐付けが変わったかもしれない）。組み込んだ側が左の一覧を合わせる。</summary>
    public event Action? Changed;



    public RelayCommand AddImageCommand { get; }

    public RelayCommand RemoveImageCommand { get; }

    public RelayCommand MoveImageBackCommand { get; }

    public RelayCommand MoveImageForwardCommand { get; }

    /// <summary>外す（印だけ。行と記録は残り、戻せる）。</summary>
    public RelayCommand RemoveMemberCommand { get; }

    /// <summary>外した行を戻す。</summary>
    public RelayCommand RestoreMemberCommand { get; }

    /// <summary>外した行を完全に消す（聞いてから。元に戻せない）。</summary>
    public RelayCommand DeleteMemberCommand { get; }

    public RelayCommand MoveMemberBackCommand { get; }

    public RelayCommand MoveMemberForwardCommand { get; }

    public RelayCommand OpenItemCommand { get; }

    /// <summary>
    /// 着せているアバターを、アバターの管理で開く（動線の点検 D6）。同じアバターのほかの改変を見に行くのに、
    /// ナビからアバターを選び直していた
    /// </summary>
    public RelayCommand OpenAvatarCommand { get; }

    public RelayCommand AddMemberCommand { get; }

    public RelayCommand OpenProjectCommand { get; }

    public RelayCommand LinkProjectCommand { get; }

    public RelayCommand UnlinkProjectCommand { get; }

    public RelayCommand RefreshProjectsCommand { get; }

    public RelayCommand OpenProjectFolderCommand { get; }

    /// <summary>使ったものを、並びの順に Unity へ送る（#69）。</summary>
    public RelayCommand SendAllToUnityCommand { get; }

    /// <summary>使ったもの1件を Unity へ取り込む（「Unity ▾」の「インポート」）。全件を順に送るのと同じ道で、その1件だけを送る。</summary>
    public RelayCommand ImportMemberCommand { get; }

    /// <summary>使ったもの1件を Unity のプロジェクトタブで示す（「Unity ▾」の「選択」。改変の画面の「Unityで選択」と同じ）。</summary>
    public RelayCommand SelectMemberInUnityCommand { get; }

    // ---- 紐付けたプロジェクトの中から探す（#72） ----

    /// <summary>紐付けたプロジェクトの中を調べ、入っている手元の商品を候補に出す。</summary>
    public RelayCommand FindInProjectCommand { get; }

    /// <summary>候補の1件を「使ったもの」に足す。</summary>
    public RelayCommand AddCandidateCommand { get; }

    /// <summary>紐付けたプロジェクトで見つかった手元の商品。紐付け先を選ぶ候補（<c>ProjectCandidates</c>）とは別物。</summary>
    public ObservableCollection<ProjectCandidateRowViewModel> FoundInProject { get; } = [];

    private bool _isFindingInProject;

    public bool IsFindingInProject
    {
        get => _isFindingInProject;
        private set
        {
            if (SetField(ref _isFindingInProject, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _projectFindText = string.Empty;

    /// <summary>調べている途中の進み具合と、調べ終えた結果の1行。</summary>
    public string ProjectFindText
    {
        get => _projectFindText;
        private set
        {
            if (SetField(ref _projectFindText, value))
            {
                OnPropertyChanged(nameof(HasProjectFindText));
            }
        }
    }

    public bool HasProjectFindText => ProjectFindText.Length > 0;

    /// <summary>
    /// 紐付けたプロジェクトの <c>Assets/</c> と <c>Packages/</c> を見て、手元のどの商品が入っているかを出す（#72・ユーザ案）。
    ///
    /// **画面は読まない。**プロジェクトのファイルと、商品の unitypackage に入っているパスを突き合わせるだけ
    /// （必要のない所に UI Automation を使わない・ユーザ指示）。数え方は <see cref="UnityProjectMatcher"/>。
    ///
    /// **自動では足さない。**プロジェクトには別の改変で入れた物や試しに入れた物も残る。
    /// どれを使ったかは人が決め、「足す」で1件ずつ入れる。
    /// </summary>
    private async Task FindInProjectAsync()
    {
        if (Record.UnityProject is not { } project || !await Core.Services.DiskCheck.FolderExistsAsync(project))
        {
            ProjectFindText = "紐付けたプロジェクトのフォルダが見つかりません。消したか移した場合は、下の「Unityプロジェクト」から紐付け直してください。";
            return;
        }

        IsFindingInProject = true;
        FoundInProject.Clear();
        try
        {
            var loaded = await _services.Store.Items.LoadAllAsync();
            var items = loaded.Items.Where(item => item.IsDownloaded).ToList();
            IProgress<int> progress = new Progress<int>(done =>
                ProjectFindText = $"手元の商品の中身を読んでいます…（{done}/{items.Count}）");

            // unitypackage を解くのは重い（大きな物は1件0.2秒ほど）ので裏で読む。
            // 共有の部品（lilToon など）を見分けるため、使ったものに入っている商品も含めて全部読む
            var matches = await Task.Run(() =>
            {
                var paths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                var done = 0;
                foreach (var item in items)
                {
                    paths[item.Id] = UnityImportQueue.PackagesOf(item).SelectMany(UnityHandoff.ReadAssetPaths).ToList();
                    progress.Report(++done);
                }

                return UnityProjectMatcher.Match(project, paths);
            });

            var members = Record.UsedMembers.Select(member => member.ItemId).ToHashSet(StringComparer.Ordinal);
            var names = items.ToDictionary(item => item.Id, item => item.DisplayName, StringComparer.Ordinal);
            foreach (var match in matches.Where(match => !members.Contains(match.ItemId)))
            {
                FoundInProject.Add(new ProjectCandidateRowViewModel
                {
                    ItemId = match.ItemId,
                    Name = names[match.ItemId],
                    Present = match.Present,
                    Total = match.Total,
                });
            }

            ProjectFindText = FoundInProject.Count > 0
                ? $"このプロジェクトに入っている手元の商品が {FoundInProject.Count} 件ありました。この改変に使ったものなら「足す」を押してください。"
                : matches.Count > 0
                    ? "このプロジェクトに入っている手元の商品は、すべて「使ったもの」に入っています。"
                    : "このプロジェクトの中に、手元の商品のファイルは見つかりませんでした。数えられるのは zip の中に unitypackage がある商品だけです。使ったものは上の欄から商品名で足せます。";
        }
        finally
        {
            IsFindingInProject = false;
        }
    }

    private async Task AddCandidateAsync(ProjectCandidateRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(
            Record.Id,
            new ModificationMember { ItemId = row.ItemId }));

        if (result is CommandResult.Failed failed)
        {
            Status = failed.Message;
            return;
        }

        FoundInProject.Remove(row);
        Status = $"「{row.Name}」を足しました。";
        await ReloadAsync();
    }

    private bool _isSendingToUnity;

    public bool IsSendingToUnity
    {
        get => _isSendingToUnity;
        private set
        {
            if (SetField(ref _isSendingToUnity, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _unityQueueText = string.Empty;

    /// <summary>今どこまで送ったか。取り込み画面は Unity 側に出る。</summary>
    public string UnityQueueText
    {
        get => _unityQueueText;
        private set
        {
            if (SetField(ref _unityQueueText, value))
            {
                OnPropertyChanged(nameof(HasUnityQueueText));
            }
        }
    }

    public bool HasUnityQueueText => UnityQueueText.Length > 0;

    /// <summary>
    /// 使ったものを**並びの順に**（＝導入の順、依存物が先）Unity へ1件ずつ送る（#69・ユーザ判断）。
    /// 改変を作り直すとき、記録した順に入れ直せば同じものが組める。
    ///
    /// 送り先は、紐付けたプロジェクトが開いていればそこ。開いていなければ選ばせ、
    /// 紐付けと違うプロジェクトへ送るときは一度聞く（別のプロジェクトに入れてしまうと剥がすのが手間）。
    /// </summary>
    /// <param name="rows">送る行。全件を順に送るときは全行、「Unity ▾」の「インポート」ではその1行（ユーザ指示 2026-09-14）。</param>
    private async Task SendToUnityAsync(IReadOnlyList<ModificationMemberRowViewModel> rows, string title)
    {
        // 使ったものの並び（導入の順）に積む。どのファイルか記録の無い行で、送れる物が2つ以上ある商品は、
        // 送り先を決めた後に選ばせる（ユーザ判断 2026-09-13。前は全部送っていて、古い版や別の種類まで入った）
        var steps = new List<(int Index, string ItemId, IReadOnlyList<UnityPackageEntry> Fixed, PackageChoiceSection? Choice)>();
        var nothing = new List<string>();

        foreach (var row in rows)
        {
            var item = await _services.Store.Items.LoadAsync(row.Member.ItemId);
            var packages = item is null ? [] : PackagesFor(item, row.Member);
            if (packages.Count == 0)
            {
                nothing.Add(row.Name);
                continue;
            }

            var choice = row.Member.FileHash is null && packages.Count > 1
                ? PackageChoiceSection.Build(item!, row.Index)
                : null;
            steps.Add((row.Index, row.Member.ItemId, choice is null ? packages : [], choice));
        }

        if (steps.Count == 0)
        {
            Services.Notice.Show(
                "使ったものの中に、Unityへ送れるもの（手元の zip の中の .unitypackage）がありませんでした。",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var linkedName = Record.UnityProject is { } project
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(project))
            : null;

        // 送信は1列に限る。Editor.log は全エディタが共有するので、終わりを取り違える（§11-3）
        if (UnityImportQueue.IsRunning)
        {
            Services.Notice.Show(UnityImportQueue.BusyMessage, title,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (UnityTargetPicker.Pick(title, linkedName) is not { } editor)
        {
            return;
        }

        var target = editor.ProjectName ?? "名前の分からないプロジェクト";
        var elsewhere = linkedName is not null
            && !string.Equals(editor.ProjectName, linkedName, StringComparison.OrdinalIgnoreCase);

        var where = elsewhere
            ? $"紐付けたプロジェクト「{linkedName}」は開いていません。代わりに「{target}」へ送ります。"
            : $"Unityの「{target}」へ送ります。";
        var choices = steps.Where(step => step.Choice is not null).Select(step => step.Choice!).ToList();
        var fixedCount = steps.Sum(step => step.Fixed.Count);

        if (choices.Count > 0)
        {
            var model = new PickPackagesDialogViewModel(
                title,
                where + (nothing.Count > 0 ? $"（手元に送れるものが無い {nothing.Count} 件は飛ばします）" : string.Empty),
                choices,
                fixedCount,
                records: true);
            if (!Views.PickPackagesDialog.Ask(model))
            {
                return;
            }
        }
        else
        {
            // 数えているのは unitypackage の数（使ったものの数ではない。1つの商品から2つ送ることがある）
            var confirm = Services.Notice.Show(
                where + "\n\n"
                + $"unitypackage {fixedCount} 件を、上から順に送ります。1件ずつ Unity の取り込み画面が出るので、"
                + "Unity側で「Import」（入れない物は「Cancel」）を押すと次の1件が出ます。"
                + (nothing.Count > 0 ? $"\n\n手元に送れるものが無い {nothing.Count} 件は飛ばします。" : string.Empty),
                title,
                System.Windows.MessageBoxButton.OKCancel,
                elsewhere ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Question,
                elsewhere ? System.Windows.MessageBoxResult.Cancel : System.Windows.MessageBoxResult.OK);

            if (confirm != System.Windows.MessageBoxResult.OK)
            {
                return;
            }
        }

        // **記録してから送る**（商品ページの「改変に足して送る」と同じ。送るのは取り込み画面を待つので長く、途中で閉じられることもある）。
        // 後ろの行から置き換えると、2行に増えても前の行の位置がずれない
        var recorded = false;
        foreach (var step in steps.Where(step => step.Choice is { Checked.Count: > 0 }).OrderByDescending(step => step.Index))
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.RecordModificationMemberFiles(Record.Id, step.Index, step.Choice!.CheckedMembers));
            if (result is CommandResult.Failed failed)
            {
                Status = failed.Message;
            }
            else
            {
                recorded = true;
            }
        }

        var queue = steps
            .SelectMany(step => (step.Choice?.CheckedPackages ?? step.Fixed).Select(package => (step.ItemId, Package: package)))
            .ToList();
        if (queue.Count == 0)
        {
            return;
        }

        IsSendingToUnity = true;
        try
        {
            var progress = new Progress<UnityQueueProgress>(report => UnityQueueText = report.Text);
            var outcomes = await UnityImportQueue.RunAsync(
                editor.ProcessId, queue.Select(entry => entry.Package).ToList(), progress, CancellationToken.None);

            // 取り込み画面で Cancel された物は入っていないので、「使った」の足跡を付けない
            var taken = outcomes.Where(outcome => outcome.Opened && !outcome.Cancelled).Select(outcome => outcome.Package).ToHashSet();
            foreach (var itemId in queue.Where(entry => taken.Contains(entry.Package)).Select(entry => entry.ItemId).Distinct())
            {
                _services.Recent.TouchAsync(itemId, RecentKind.Used).Forget();
            }

            var failed = outcomes.Where(outcome => !outcome.Opened).ToList();
            var shown = UnityQueueOutcome.DescribeShown(outcomes);
            UnityQueueText = failed.Count == 0
                ? shown
                : $"{shown}{failed.Count} 件は送れませんでした（{failed[0].Problem}）。";
        }
        finally
        {
            IsSendingToUnity = false;
        }

        // 記録した行（どのファイルを使ったか）を並びに出す
        if (recorded)
        {
            await ReloadAsync();
        }
    }

    /// <summary>
    /// この構成物で送るもの。**Unityへ送って足した分は、そのとき使った zip と unitypackage をそのまま使う**
    /// ——版まで同じにするため（1年後に組み直すとき v1.01 と v1.06 は別物）。
    /// 手で足した分は、どのファイルを使ったかが分からないので、今ある zip の中身を zip の順に送る。
    /// </summary>
    internal static IReadOnlyList<UnityPackageEntry> PackagesFor(ItemRecord item, ModificationMember member)
    {
        if (member.FileHash is { } hash && member.Package is { } package
            && item.Local.OwnedFiles
                .FirstOrDefault(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase))
                ?.Paths.FirstOrDefault(File.Exists) is { } zip)
        {
            return [new UnityPackageEntry(zip, package, 0) { ZipHash = hash }];
        }

        return UnityImportQueue.PackagesOf(item);
    }

    /// <summary>使ったもの1件を Unity のプロジェクトタブで示す（改変の画面の「Unityで選択」と同じ道・<see cref="UnityMemberSelect"/>）。</summary>
    private async Task SelectMemberInUnityAsync(ModificationMemberRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var item = await _services.Store.Items.LoadAsync(row.Member.ItemId);
        if (await UnityMemberSelect.RunAsync(_services, Record, row.Index, row.Member, row.Name, row.SourceText, item, text => Status = text))
        {
            await ReloadAsync();
        }
    }

    /// <summary>使ったもの。**並びが導入の順。**</summary>
    public ObservableCollection<ModificationMemberRowViewModel> Members { get; } = [];

    // ---- 使ったものの見せ方（検索と同じカード・リスト・ユーザ指示 2026-09-14） ----
    //
    // 前は名前の行だけのリストだった。検索と同じカード（絵・星・押すと商品ページ・右クリック・中クリック）にし、
    // この画面にしか無い物（導入の順・並べ替え・外す・使ったファイル）を足す

    private bool _isListMode;

    /// <summary>リストの列の幅（この画面で覚える）。「ショップ」の列は使ったファイルに使う。</summary>
    public ItemListColumns ListColumns { get; }

    public bool IsListMode => _isListMode;

    public bool IsCardMode => !_isListMode;

    public RelayCommand ShowCardsCommand { get; }

    public RelayCommand ShowListCommand { get; }

    private void SetListMode(bool list)
    {
        if (_isListMode == list)
        {
            return;
        }

        _isListMode = list;
        OnPropertyChanged(nameof(IsListMode));
        OnPropertyChanged(nameof(IsCardMode));
        ItemListMode.Save(_services, "modification", list);
    }

    // カードの操作は検索と同じ（フォルダビューと同じく検索の画面の操作を借りる）
    public void OpenItem(ItemCardViewModel card) => _main.ShowItem(card.Item);

    public void OpenBooth(ItemCardViewModel? card) => _main.Search.OpenBooth(card);

    public Task ToggleFavoriteAsync(ItemCardViewModel card) => _main.Search.ToggleFavoriteAsync(card);

    // カードの右クリックのメニュー（ItemCardResources の CardMenu が Tag から名前で引く）
    public RelayCommand OpenBoothCommand => _main.Search.OpenBoothCommand;

    public RelayCommand OpenShopCommand => _main.Search.OpenShopCommand;

    public RelayCommand CopyLinkCommand => _main.Search.CopyLinkCommand;

    public RelayCommand EditItemCommand => _main.Search.EditItemCommand;

    public RelayCommand RevealCommand => _main.Search.RevealCommand;


    // 右クリックの「開く」「Unity」は検索画面と同じ命令を借りる（ユーザ指示 2026-09-19）

    public RelayCommand CardUnpackCommand => _main.Search.CardUnpackCommand;


    public RelayCommand CardSendToUnityCommand => _main.Search.CardSendToUnityCommand;


    public RelayCommand CardSendToUnityWithRecordCommand => _main.Search.CardSendToUnityWithRecordCommand;


    public RelayCommand CardSelectInUnityCommand => _main.Search.CardSelectInUnityCommand;

    public RelayCommand HideItemCommand => _main.Search.HideItemCommand;

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

    /// <summary>
    /// 写真が1枚も無いとき、大きい絵の所に出す（ユーザ指示 2026-09-14：0枚なら写真が無いことをギャラリーで分かるようにする）。
    /// 前は大きい絵の所が空で、下に案内が出ていた。**次にやることを書く**（空欄だけだと足し方が分からない）
    /// </summary>
    public string GalleryEmptyText => _gallery.Count > 0
        ? string.Empty
        : "この改変の写真はまだありません。\n「＋」でまとめて選ぶか、ここへ落とすか、Ctrl+V で貼ってください。";

    // ---- ギャラリー（商品と同じ部品 ItemGalleryPanel・ユーザ指示 2026-09-13） ----
    //
    // 前は写真を横に並べる独自の欄（「改変後の姿」）だった。ほかの箇所のギャラリーと同じにし、
    // 大きく1枚＋サムネイル一覧＋右クリックの操作（前へ・後ろへ・消す・足す）にする

    private readonly List<GalleryImage> _gallery = [];
    private int _selectedIndex;

    /// <summary>サムネイル一覧。末尾に「＋」（足す枠）を混ぜる（商品のギャラリーと同じ並べ方）。</summary>
    public ObservableCollection<GalleryImage> GalleryTiles { get; } = [];

    public RelayCommand PreviousImageCommand { get; }

    public RelayCommand NextImageCommand { get; }

    public RelayCommand SelectImageCommand { get; }

    /// <summary>大きく出す1枚。保存された大きさで読む（キャッシュに乗る）。</summary>
    public BitmapSource? SelectedImage => _gallery.Count == 0 ? null : _thumbnails.Load(_gallery[_selectedIndex].Path);

    /// <summary>何枚目か。**2枚以上のときだけ出す**（商品の写真の欄と同じ・U11 と同じ決まり）。</summary>
    public string GalleryCounter => _gallery.Count <= 1 ? string.Empty : $"{_selectedIndex + 1} / {_gallery.Count}";

    public bool CanGoPreviousImage => _gallery.Count > 1;

    public bool CanGoNextImage => _gallery.Count > 1;

    /// <summary>改変の写真は全部自分で貼ったもの。並べ替えと削除は、いま出ている1枚があれば出す。</summary>
    public bool CurrentIsUserAdded => _gallery.Count > 0;

    public bool CurrentIsPinned => false;

    public bool ShowsPinThumbnail => false;

    public bool ShowsUnpinThumbnail => false;

    public bool ShowsImageRoles => false;

    public string AddImageTip => "この改変に写真を足す（ここへ落としても、Ctrl+Vで貼っても入ります）";

    // 商品の画像にだけある案内（BOOTH からの取得・削除された画像・自分で足した枚数）は出さない
    public bool HasUserImages => false;

    public bool HasOrphanedImages => false;

    public bool HasMissingImages => false;

    public bool IsFetchingImages => false;

    public bool HasUnavailableImages => false;

    public bool HasImageFetchNotice => false;

    public bool SwitchOnHover => _services.Settings.GallerySwitchOnHover;

    public int HoverDelayMs => Math.Max(0, _services.Settings.GalleryHoverDelayMs);

    public void HoverImage(GalleryImage image)
    {
        if (SwitchOnHover)
        {
            SelectImage(image);
        }
    }

    private void SelectImage(object? parameter)
    {
        if (parameter is GalleryImage image && _gallery.IndexOf(image) is var index and >= 0)
        {
            Select(index);
        }
    }

    /// <summary>端で止めず、最初と最後をつなぐ（商品のギャラリーと同じ）。</summary>
    private void GoToImage(int delta)
    {
        if (_gallery.Count > 1)
        {
            Select(((_selectedIndex + delta) % _gallery.Count + _gallery.Count) % _gallery.Count);
        }
    }

    private void Select(int index)
    {
        if (index == _selectedIndex || index < 0 || index >= _gallery.Count)
        {
            return;
        }

        _gallery[_selectedIndex].IsSelected = false;
        _selectedIndex = index;
        _gallery[_selectedIndex].IsSelected = true;
        NoteGalleryChanged();
    }

    /// <summary>右クリックの相手。引数が無ければ、いま出ている1枚。</summary>
    private ModificationImageViewModel? ImageFor(object? parameter)
        => parameter as ModificationImageViewModel
            ?? (_gallery.Count > _selectedIndex
                ? Images.FirstOrDefault(image => image.FileName == _gallery[_selectedIndex].FileName)
                : null);

    /// <summary>
    /// 写真の一覧を作り直す。**見ていた1枚を選んだままにする**（並べ替えた後に動かした先を目で追えるように。
    /// 消した後は同じ位置の1枚）。
    /// </summary>
    private void RebuildGallery()
    {
        var previous = _gallery.Count > _selectedIndex ? _gallery[_selectedIndex].FileName : null;
        var previousIndex = _selectedIndex;

        _gallery.Clear();
        GalleryTiles.Clear();
        var directory = _services.Paths.ModificationImagesDir(Record.Id);
        foreach (var image in Record.Images)
        {
            var path = Path.Combine(directory, image.FileName);
            _gallery.Add(new GalleryImage
            {
                Path = path,
                FileName = image.FileName,
                Image = File.Exists(path) ? _thumbnails.LoadForTile(path) : null,
            });
        }

        foreach (var tile in _gallery)
        {
            GalleryTiles.Add(tile);
        }

        GalleryTiles.Add(new GalleryImage { Path = string.Empty, FileName = string.Empty, Image = null, IsAddTile = true });

        var found = previous is null ? -1 : _gallery.FindIndex(tile => tile.FileName == previous);
        _selectedIndex = _gallery.Count == 0 ? 0 : found >= 0 ? found : Math.Min(previousIndex, _gallery.Count - 1);
        if (_gallery.Count > 0)
        {
            _gallery[_selectedIndex].IsSelected = true;
        }

        NoteGalleryChanged();
    }

    private void NoteGalleryChanged()
    {
        foreach (var name in new[]
        {
            nameof(SelectedImage), nameof(GalleryCounter), nameof(GalleryEmptyText), nameof(CanGoPreviousImage), nameof(CanGoNextImage),
            nameof(CurrentIsUserAdded),
        })
        {
            OnPropertyChanged(name);
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>足す商品の候補。手元にある商品の名前。</summary>
    public ObservableCollection<string> ItemSuggestions { get; } = [];

    // ---- アバター ----

    public string AvatarText { get; private set; } = string.Empty;

    public string AvatarItemId => Record.AvatarItemId;

    /// <summary>
    /// アバターの絵（アバターの管理と同じ1枚）。右の列の「アバター」の欄に出す（ユーザ指示 2026-09-14：左上ではなく、
    /// 「改変の名前」の上にアバターとして置く）。持っていないアバターは取った1枚目、無ければ出さない
    /// </summary>
    public BitmapSource? AvatarIcon => AvatarImageSync.IconPath(_services.Paths, AvatarItemId, _main.Search.FindItem(AvatarItemId)) is { } path
        ? _thumbnails.PeekForTile(path, () => OnPropertyChanged(nameof(AvatarIcon)))
        : null;

    // ---- 名前 ----

    private string _nameInput;

    // 名前・メモ・blueprint ID は押さずに残す（ユーザ指示 2026-09-19：タグ・属性・アバター・ショップのメモと揃える）。
    // 打ち止めてから 0.8 秒で書く。待ちの間に別の改変へ移っても、この画面の値とこの改変の ID で書くので取り違えない
    private readonly Debounced _saveName;
    private readonly Debounced _saveMemo;
    private readonly Debounced _saveBlueprint;

    public string NameInput
    {
        get => _nameInput;
        set
        {
            if (SetField(ref _nameInput, value))
            {
                OnPropertyChanged(nameof(NameChanged));
                _saveName.Request();
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
                _saveMemo.Request();
            }
        }
    }

    public bool MemoChanged => MemoInput != (Record.Memo ?? string.Empty);

    // ---- VRChat の blueprint ID（ユーザ指示 2026-09-19） ----

    private string _blueprintInput;

    /// <summary>VRChat にアップロードしたアバターの ID（<c>avtr_…</c>）。「VRChatで着替える」で OSC に送る。</summary>
    public string BlueprintInput
    {
        get => _blueprintInput;
        set
        {
            if (SetField(ref _blueprintInput, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(BlueprintHint));
                OnPropertyChanged(nameof(HasBlueprintHint));
                OnPropertyChanged(nameof(CanChangeAvatar));
                _saveBlueprint.Request();
            }
        }
    }

    /// <summary>形が違うときだけ言う（保存は止めない。書き間違いに気付けるように）。</summary>
    public string BlueprintHint => BlueprintInput.Trim().Length > 0 && !VrcOsc.LooksLikeAvatarId(BlueprintInput)
        ? "「avtr_」で始まる ID の形ではありません。VRChat のアバターの詳細（Web のアバターのページの URL など）からコピーしてください。"
        : string.Empty;

    public bool HasBlueprintHint => BlueprintHint.Length > 0;

    public bool CanChangeAvatar => BlueprintInput.Trim().Length > 0;

    /// <summary>VRChat の OSC（手元の 9000 番）へ /avatar/change を送り、この改変のアバターに着替える。</summary>
    public RelayCommand ChangeAvatarCommand { get; }

    private async Task ChangeAvatarAsync()
    {
        var id = BlueprintInput.Trim();
        if (id.Length == 0)
        {
            return;
        }

        // 送りっぱなしの UDP なので、着替えたかはこちらでは分からない。送ったことと、効かないときの確かめ方を言う
        Status = await VrcOsc.SendAvatarChangeAsync(id) is { } problem
            ? $"VRChat へ送れませんでした（{problem}）。"
            : "VRChat に着替えを送りました。着替わらなければ、VRChat の設定で OSC を有効にしているか、"
                + "このアバターを着られるか（自分でアップロードした・お気に入りにしている）を確かめてください。";
    }

    /// <summary>
    /// 改変の記録だけを読み直し、改変の画面の一覧に知らせる。名前・メモを押さずに残すときに使う
    /// （全部を組み直すと、打っている間に使ったものの一覧がちらつく。入力欄は触らない）
    /// </summary>
    private async Task RefreshRecordAsync()
    {
        if (await _services.Modifications.LoadAsync(Record.Id) is { } fresh)
        {
            Record = fresh;
        }

        foreach (var name in new[] { nameof(Record), nameof(UpdatedText), nameof(NameChanged), nameof(MemoChanged) })
        {
            OnPropertyChanged(name);
        }

        Changed?.Invoke();
    }

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
    /// <remarks>在るかは読み込むときに画面のスレッドの外で確かめて覚える（技術的負債 4-2）。前は画面が読むたびにディスクを見ていた。</remarks>
    public bool ProjectMissing { get; private set; }

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

                Card = item is null ? null : _main.Search.CreateCard(item),
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
                // 大きく出すのはギャラリーの方（RebuildGallery）。ここで全部を原寸で読むと、使わない絵でメモリを食う
                Image = null,
                Index = index,
                Total = Record.Images.Count,
            });
        }

        RebuildGallery();

        await LoadSuggestionsAsync();
        await LoadProjectsAsync();
        ProjectMissing = HasProject && !await Core.Services.DiskCheck.FolderExistsAsync(Record.UnityProject);

        foreach (var name in new[]
        {
            nameof(Record), nameof(AvatarText), nameof(AvatarIcon), nameof(HasMembers), nameof(HasImages),
            nameof(CreatedText), nameof(UpdatedText), nameof(NameChanged), nameof(MemoChanged),
            nameof(HasProject), nameof(ProjectName), nameof(ProjectPath), nameof(ProjectMissing),
            nameof(HasProjectCandidates),
        })
        {
            OnPropertyChanged(name);
        }

        RelayCommand.RaiseCanExecuteChanged();
        Changed?.Invoke();
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

        // 文は改変の画面の「Unityを開く」と同じ。フォルダが無いときだけ、ここでは指し直せることを言う
        var result = UnityLaunch.OpenProject(Record.UnityProject);
        Status = result == UnityOpenResult.Missing
            ? $"「{name}」が見つかりません。移したのなら、下の一覧から指し直せます。"
            : UnityOpenText.For(result, name);
    }

    /// <summary>記録に残した種類の番号を、人が読める名前に直す。</summary>
    internal static string VariationLabel(ModificationMember member, ItemRecord? item)
    {
        if (member.VariationId is not { } id)
        {
            return string.Empty;
        }

        var variation = item?.Booth.Variations.FirstOrDefault(candidate => candidate.Id == id);
        return variation?.Name is { Length: > 0 } name ? name : "バリエーションの名前が分かりません";
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

    /// <summary>名前を書く。空のときは書かない（改変の名前は要る。消している途中で空の名前が保存されないように）。</summary>
    private async Task SaveNameAsync()
    {
        if (!NameChanged)
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.RenameModification(Record.Id, NameInput));

        Status = result is CommandResult.Failed failed ? failed.Message : "名前を変えました。";
        await RefreshRecordAsync();
    }

    private async Task SaveMemoAsync()
    {
        if (!MemoChanged)
        {
            return;
        }

        var memo = MemoInput;
        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.SetModificationMemo(Record.Id, memo));

        Status = result is CommandResult.Failed failed
            ? failed.Message
            : memo.Trim().Length == 0 ? "メモを消しました。" : "メモを保存しました。";
        await RefreshRecordAsync();
    }

    private async Task SaveBlueprintAsync()
    {
        var id = BlueprintInput.Trim();
        if (id == (Record.BlueprintId ?? string.Empty))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.SetModificationBlueprintId(Record.Id, id));

        Status = result is CommandResult.Failed failed
            ? failed.Message
            : id.Length == 0 ? "blueprint ID を消しました。" : "blueprint ID を保存しました。";
        await RefreshRecordAsync();
    }

    /// <summary>
    /// 改変を消す（改変の画面の右側から）。**取り返しがつかないので、何が消えるかを数で書く**
    /// （アバターの管理の一覧から消すときと同じ文面）。
    /// </summary>
    private async Task DeleteAsync()
    {
        var images = Record.Images.Count;
        var answer = Services.Notice.Show(
            $"改変「{Record.Name}」を削除します。\n\n"
            + (images > 0 ? $"貼った画像 {images} 枚も一緒に消えます。\n" : string.Empty)
            + "元には戻せません。使った商品そのものは消えません。",
            "改変を削除",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.DeleteModification(Record.Id));
        if (result is CommandResult.Failed failed)
        {
            Status = failed.Message;
            return;
        }

        Deleted?.Invoke();
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

    /// <summary>
    /// 外す・戻す（ユーザ指示 2026-09-19：商品の手元のファイルと同じく戻せるように）。
    /// 前は外すと確かめずに行ごと消え、どのファイル・どの unitypackage を使ったかの記録も戻らなかった（動線の洗い出し B1）。
    /// 印を付けるだけなので確かめない（戻せる）
    /// </summary>
    private async Task SetMemberDetachedAsync(ModificationMemberRowViewModel? row, bool detached)
    {
        if (row is null)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(
            new UiCommand.SetModificationMemberDetached(Record.Id, row.Index, detached));

        Status = detached
            ? $"「{row.Name}」を外しました。行は薄く残るので、「戻す」で戻せます。"
            : $"「{row.Name}」を戻しました。";
        await ReloadAsync();
    }

    /// <summary>
    /// 外した行を完全に消す（ユーザ指示 2026-09-19：改変ではもう一段、紐付けを完全に解く削除を置く）。
    /// **取り返しがつかないので聞く。**使ったファイルの記録も消え、足し直しても Unity へ送るまで戻らない
    /// </summary>
    private async Task DeleteMemberAsync(ModificationMemberRowViewModel? row)
    {
        if (row is null || !row.IsDetached)
        {
            return;
        }

        var answer = Services.Notice.Show(
            $"「{row.Name}」をこの改変から完全に消します。\n\n"
            + (row.IsFromUnity
                ? $"使ったファイル（{row.SourceText}）の記録も消えます。足し直しても、Unity へ送るまで記録は戻りません。\n\n"
                : string.Empty)
            + "この操作は元に戻せません。",
            "使ったものを削除",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(
            new UiCommand.RemoveModificationMember(Record.Id, row.Index));

        Status = $"「{row.Name}」を削除しました。";
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
            _main.ShowItem(item);
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
        var answer = Services.Notice.Show(
            "この写真を削除します。\n\n元には戻せません。",
            "写真を削除",
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
