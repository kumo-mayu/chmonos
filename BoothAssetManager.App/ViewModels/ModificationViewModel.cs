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
/// <c>設計詳細_改変の記録.md</c>。
/// </summary>
public sealed class ModificationViewModel : ViewModelBase
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

        // 戻るは画面の履歴を遡る（U23）
        BackCommand = new RelayCommand(main.GoBack);
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
        OpenAvatarCommand = new RelayCommand(() => _main.ShowAvatar(AvatarItemId));
        OpenProjectCommand = new RelayCommand(() => OpenProject(), () => HasProject);
        LinkProjectCommand = new RelayCommand(
            parameter => _ = LinkProjectAsync(parameter as UnityProjectRowViewModel),
            parameter => parameter is UnityProjectRowViewModel);
        UnlinkProjectCommand = new RelayCommand(() => _ = LinkProjectAsync(null), () => HasProject);
        RefreshProjectsCommand = new RelayCommand(() => _ = LoadProjectsAsync());
        OpenProjectFolderCommand = new RelayCommand(
            () => Shell.Reveal(Record.UnityProject), () => HasProject);
        SendAllToUnityCommand = new RelayCommand(() => _ = SendAllToUnityAsync(), () => HasMembers && !IsSendingToUnity);
        FindInProjectCommand = new RelayCommand(() => _ = FindInProjectAsync(), () => HasProject && !IsFindingInProject);
        AddCandidateCommand = new RelayCommand(
            parameter => _ = AddCandidateAsync(parameter as ProjectCandidateRowViewModel),
            parameter => parameter is ProjectCandidateRowViewModel);
        DeleteCommand = new RelayCommand(() => _ = DeleteAsync());

        _ = ReloadAsync();
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
    public double BodyMinWidth => IsEmbedded ? 0 : 1060;

    /// <remarks>
    /// 組み込んだときは左（写真・使ったもの）に残りを全部渡す。右は最低幅（320px）で足りるが、
    /// 左の「使ったもの」は名前と操作のボタンが1行に並ぶので、半々だと名前が「【...」まで縮んだ
    /// </remarks>
    public System.Windows.GridLength LeftColumnWidth => IsEmbedded
        ? new System.Windows.GridLength(3, System.Windows.GridUnitType.Star)
        : new System.Windows.GridLength(660);

    /// <summary>改変を消す。組み込んだときだけ出す（単独の画面ではアバターの管理の一覧から消す）。</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>消し終えた。組み込んだ側が一覧を読み直して右側を空ける。</summary>
    public event Action? Deleted;

    /// <summary>記録を読み直した（名前・メモ・使ったもの・紐付けが変わったかもしれない）。組み込んだ側が左の一覧を合わせる。</summary>
    public event Action? Changed;

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
        if (Record.UnityProject is not { } project || !Directory.Exists(project))
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

            var members = Record.Members.Select(member => member.ItemId).ToHashSet(StringComparer.Ordinal);
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
    private async Task SendAllToUnityAsync()
    {
        const string title = "使ったものを順にUnityへ送る";

        // 使ったものの並び（導入の順）に積む。どのファイルか記録の無い行で、送れる物が2つ以上ある商品は、
        // 送り先を決めた後に選ばせる（ユーザ判断 2026-09-13。前は全部送っていて、古い版や別の種類まで入った）
        var steps = new List<(int Index, string ItemId, IReadOnlyList<UnityPackageEntry> Fixed, PackageChoiceSection? Choice)>();
        var nothing = new List<string>();

        foreach (var row in Members)
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
            System.Windows.MessageBox.Show(
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
            System.Windows.MessageBox.Show(UnityImportQueue.BusyMessage, title,
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
            var confirm = System.Windows.MessageBox.Show(
                where + "\n\n"
                + $"unitypackage {fixedCount} 件を、上から順に送ります。1件ずつ取り込み画面が出るので、"
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

            var opened = outcomes.Where(outcome => outcome.Opened).Select(outcome => outcome.Package).ToHashSet();
            // 取り込み画面で Cancel された物は入っていないので、「使った」の足跡を付けない
            var taken = outcomes.Where(outcome => outcome.Opened && !outcome.Cancelled).Select(outcome => outcome.Package).ToHashSet();
            foreach (var itemId in queue.Where(entry => taken.Contains(entry.Package)).Select(entry => entry.ItemId).Distinct())
            {
                _ = _services.Recent.TouchAsync(itemId, RecentKind.Used);
            }

            var failed = outcomes.Where(outcome => !outcome.Opened).ToList();
            var skipped = outcomes.Count(outcome => outcome.Cancelled);
            var shown = skipped == 0
                ? $"{opened.Count} 件の取り込み画面を順に出しました。"
                : $"{opened.Count} 件の取り込み画面を順に出しました（うち {skipped} 件は Cancel されたので入っていません）。";
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

        // 文は改変の画面の「Unityで開く」と同じ。フォルダが無いときだけ、ここでは指し直せることを言う
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

    /// <summary>
    /// 改変を消す（改変の画面の右側から）。**取り返しがつかないので、何が消えるかを数で書く**
    /// （アバターの管理の一覧から消すときと同じ文面）。
    /// </summary>
    private async Task DeleteAsync()
    {
        var images = Record.Images.Count;
        var answer = System.Windows.MessageBox.Show(
            $"改変「{Record.Name}」を消します。\n\n"
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
