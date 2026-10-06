using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using Chmonos.App.Services;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

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

    /// <summary>
    /// 導入の順。1から数える（人が読む番号）。
    /// **逆の順で見せているときも入れた順の番号のまま**（メモ26-①）：番号は「何番目に入れたか」で、
    /// 送る順と同じ数を指していないと読み違える
    /// </summary>
    public string OrderText => $"{Index + 1}";

    /// <summary>
    /// 使ったファイルの状態（<see cref="ModificationRowBuilder.FileTextOf"/>。改変の画面の行と同じ）。
    /// **空欄の意味を言い分ける。推定で埋めない。**
    /// </summary>
    public required string SourceText { get; init; }

    public bool HasFile => Member.HasFile;

    /// <summary>
    /// 入れた順の逆に見せているか（メモ26-①）。矢印は見えている向きで動かすので、逆のときは記録の上で反対へ動く。
    /// </summary>
    public bool Reversed { get; init; }

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

    // **端では矢印を押せなくする。**押せるのに何も起きないボタンは嘘になる。
    // 前・後ろは見えている並びの向き（逆の順なら、見えている前＝記録の後ろ）
    public bool CanMoveBack => Reversed ? Index < Total - 1 : Index > 0;

    public bool CanMoveForward => Reversed ? Index > 0 : Index < Total - 1;

    /// <summary>見せる向きだけを変えた写し（カードは使い回す）。</summary>
    internal ModificationMemberRowViewModel WithReversed(bool reversed) => new()
    {
        Index = Index,
        Total = Total,
        Member = Member,
        Name = Name,
        SourceText = SourceText,
        IsMissing = IsMissing,
        VariationText = VariationText,
        Card = Card,
        Reversed = reversed,
    };

    /// <summary>見えている前へ動かすとき、記録の並びの上で動かす向き。</summary>
    public int BackDelta => Reversed ? 1 : -1;

    // 押せない理由も出す（`ui-rules.md`・E11）。同じ並びの中で、理由の出るボタンと出ないボタンが混ざっていた
    public string MoveBackHint => CanMoveBack
        ? Reversed ? "前へ移動します。後に送られます。" : "前へ移動します。先に送られます。"
        : "いちばん前にあります。";

    public string MoveForwardHint => CanMoveForward
        ? Reversed ? "後ろへ移動します。先に送られます。" : "後ろへ移動します。後に送られます。"
        : "いちばん後ろにあります。";
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

    /// <summary>バージョン。置き場所（<see cref="Folder"/>）とは別に出す——パスは等幅の英字の字体で出さないと、区切りの「\」が「¥」に見える。</summary>
    public string VersionText => Candidate.Version ?? "バージョンが読めません";

    /// <summary>置き場所。同名のプロジェクトを見分けるために出す。</summary>
    public string Folder => Candidate.Folder;

    public bool HasFolder => Candidate.Folder.Length > 0;

    /// <summary>いま開いているものは目印を付ける。紐付けたいのは大抵これ。</summary>
    public bool IsOpen => Candidate.IsOpen;

    public bool IsMissing => !Candidate.Exists;

    /// <summary>これが今の紐付け先か。二重に押させないために出す。</summary>
    public required bool IsCurrent { get; init; }
}

/// <summary>紐付けたプロジェクトに入っている、手元の商品1件（#72）。</summary>
public sealed class ProjectCandidateRowViewModel : ViewModelBase
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

    public string? ThumbnailPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    /// <summary>使ったものの行と同じ。出すのは34DIPの枠だけなので頭の絵の大きさで、裏で読み、届いたら描き直す。</summary>
    public BitmapSource? Thumbnail => ThumbnailPath is { } path
        ? Thumbnails?.PeekForIcon(path, () => OnPropertyChanged(nameof(Thumbnail)))
        : null;

    /// <summary>吹き出しの大きめの絵。**吹き出しが開いたときに初めて読む**（行ごとに全部読むとメモリを食う）。</summary>
    public BitmapSource? HoverImage => ThumbnailPath is { } path
        ? Thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(HoverImage)))
        : null;

    public bool HasHoverImage => ThumbnailPath is not null;

    public string Initial => AvatarText.InitialOf(Name);
}

/// <summary>
/// 改変の詳細。
///
/// 構成物・写真・Unityプロジェクト・メモが入るので、商品ページと同じ格の画面にした
/// （アバター詳細の中で展開すると縦に伸び続ける）。決めた理由は
/// <c>docs/history/modifications.md</c>。
/// </summary>
public sealed class ModificationViewModel : ViewModelBase, IGalleryHost, IItemCardHost, IPendingWrites, ILeavingScreen, IItemImagesListener
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly ThumbnailLoader _thumbnails;

    /// <summary>
    /// 離れたら、全商品を読む読み込み（足す商品の候補・プロジェクトに入っている商品を探す）を取り消す（既知 P8）。
    /// この画面は開くたびに作り直すので、離れた後の結果は誰も見ない。探す方は unitypackage を全部解くので重い。
    /// 書き込み（名前・メモ・使ったもの）は止めない
    /// </summary>
    private readonly CancellationTokenSource _leaving = new();

    public void OnLeaving() => _leaving.Cancel();

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
        _saveName = new Debounced(TimeSpan.FromMilliseconds(800), SaveNameAsync);
        _saveMemo = new Debounced(TimeSpan.FromMilliseconds(800), SaveMemoAsync);
        _saveBlueprint = new Debounced(TimeSpan.FromMilliseconds(800), SaveBlueprintAsync);
        ChangeAvatarCommand = new RelayCommand(() => ChangeAvatarAsync().Forget());

        // 使ったものは検索と同じカード・リストで出す（ユーザ指示 2026-09-14）。どちらで出すかと列の幅は、この画面で覚える
        ListColumns = new ItemListColumns(services.PaneWidths, "modification", hasSelect: false, shopHeader: "使ったファイル");
        _isListMode = ItemListMode.IsList(services, "modification");
        _isReversed = services.UiState.ModificationMembersReversed;
        ShowInsertOrderCommand = new RelayCommand(() => SetReversed(false));
        ShowReverseOrderCommand = new RelayCommand(() => SetReversed(true));
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
            parameter => MoveMemberAsync(parameter as ModificationMemberRowViewModel, back: true).Forget(),
            parameter => parameter is ModificationMemberRowViewModel);
        MoveMemberForwardCommand = new RelayCommand(
            parameter => MoveMemberAsync(parameter as ModificationMemberRowViewModel, back: false).Forget(),
            parameter => parameter is ModificationMemberRowViewModel);
        OpenItemCommand = new RelayCommand(
            parameter => OpenItemAsync(parameter as ModificationMemberRowViewModel).Forget(),
            parameter => parameter is ModificationMemberRowViewModel);
        AddMemberCommand = new RelayCommand(parameter => AddMemberAsync(parameter as string).Forget());
        OpenAvatarCommand = new RelayCommand(() => _main.ShowAvatar(AvatarItemId));
        OpenProjectCommand = new RelayCommand(() => OpenProjectAsync().Forget(), () => HasProject);
        LinkProjectCommand = new RelayCommand(
            parameter => LinkProjectAsync(parameter as UnityProjectRowViewModel).Forget(),
            parameter => parameter is UnityProjectRowViewModel);
        UnlinkProjectCommand = new RelayCommand(() => LinkProjectAsync(null).Forget(), () => HasProject);
        RefreshProjectsCommand = new RelayCommand(() => LoadProjectsAsync().Forget());
        OpenProjectFolderCommand = new RelayCommand(
            () => ExplorerReveal.RevealAsync(Record.UnityProject).Forget(), () => HasProject);
        SendAllToUnityCommand = new RelayCommand(
            // 外した行は送らない（今は使っていない物）。**入れた順に送る**——逆の順で見せていても、見えている順では送らない（メモ26-①）
            () => SendToUnityAsync(RowsToSendAll(), "使ったものを順にUnityへ送る").Forget(),
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
        DuplicateCommand = new RelayCommand(() => DuplicateRequested?.Invoke(Record.Id));

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

    /// <summary>戻るを出すか（V2）。組み込みのときと、戻る先が無いときは出さない。</summary>
    public bool ShowsBack => !IsEmbedded && _main.CanGoBack;

    /// <summary>
    /// 本文の幅の下限。これより狭い入れ物では、本文が横に送れる。
    /// 下限は、覚えた左の幅ではなく**左の最小**から出す。覚えた幅は窓が狭いと頭打ちになる（PaneGrid）ので、
    /// 覚えた幅で下限を決めると、縮められる左を縮めずに横へ送らせることになる（点検 2026-09-23）
    /// </summary>
    public double BodyMinWidth => MinBodyWidth(IsEmbedded, LeftPane.MinPixels);

    /// <summary>
    /// 組み込んだときの下限は、左の最小＋右の最小（320）＋本文の左右の余白（20×2）ちょうど。
    /// 前は 0（下限なし）にしていたので、列の最小の合計より狭い入れ物では、横に送れないまま右が切れた
    /// （幅 900 の窓では「使ったもの」の右端が切れ、右の列——名前・Unityプロジェクト——には届かなかった。2026-09-30）。
    /// 単独の画面の 400 は前からの値（右の最小と余白に、40 のゆとり）。
    /// </summary>
    internal static double MinBodyWidth(bool embedded, double leftMin) => leftMin + (embedded ? 320 + 40 : 400);

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

    public RelayCommand DuplicateCommand { get; }

    /// <summary>消し終えた。組み込んだ側が一覧を読み直して右側を空ける。</summary>
    public event Action? Deleted;

    /// <summary>「複製」を押した。複製は左の一覧と右の開き方に関わるので、作るのは改変の画面（<see cref="ModificationHubViewModel"/>）で、ここは頼むだけ</summary>
    public event Action<string>? DuplicateRequested;

    /// <summary>複製の直後に開いたとき true。名前の欄へフォーカスを置く（名前を変えるのが次の一手）。画面が読んだら下ろす</summary>
    public bool WantsNameFocus { get; set; }

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
        var token = _leaving.Token;
        try
        {
            var loaded = await _services.Store.Items.LoadAllAsync(cancellationToken: token);
            var items = loaded.Items.Where(item => item.HasOwnedFiles).ToList();
            IProgress<int> progress = new Progress<int>(done =>
                ProjectFindText = $"手元の商品の中身を読んでいます…（{done}/{items.Count}）");

            // unitypackage を解くのは重い（大きな物は1件0.2秒ほど）ので裏で読む。
            // 共有の部品（lilToon など）を見分けるため、使ったものに入っている商品も含めて全部読む
            var matches = await Task.Run(() =>
            {
                var paths = new Dictionary<string, IReadOnlyList<UnityPackageAsset>>(StringComparer.Ordinal);
                var done = 0;
                // 同じ zip の包みは控えを1回だけ読んで配る（前は包みごとに控えを丸ごと読んでいた）
                var reads = new UnityPackageReads();
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    paths[item.Id] = UnityImportQueue.PackagesOf(item).SelectMany(reads.ReadAssets).ToList();
                    progress.Report(++done);
                }

                return UnityProjectMatcher.Match(project, paths);
            }, token);

            // 1枚目の場所はフォルダを見るので、画面のスレッドの外で引く（結果に出る商品だけ）
            var shown = matches.Select(match => match.ItemId).ToHashSet(StringComparer.Ordinal);
            var thumbnailPaths = await Task.Run(() => ModificationRowBuilder.ThumbnailPathsOf(
                _services, _thumbnails, items.Where(item => shown.Contains(item.Id))), token);
            var members = Record.UsedMembers.Select(member => member.ItemId).ToHashSet(StringComparer.Ordinal);
            // 同じ ID が2件あると ToDictionary が投げ、照合の結果が黙って出なくなる（改変の一覧・アバターの画面と同じ備え。点検 2026-09-28）
            var names = items.GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.Ordinal);
            foreach (var match in matches.Where(match => !members.Contains(match.ItemId)))
            {
                FoundInProject.Add(new ProjectCandidateRowViewModel
                {
                    ItemId = match.ItemId,
                    Name = names[match.ItemId],
                    Present = match.Present,
                    Total = match.Total,
                    ThumbnailPath = thumbnailPaths.GetValueOrDefault(match.ItemId),
                    Thumbnails = _thumbnails,
                });
            }

            ProjectFindText = FoundInProject.Count > 0
                ? $"このプロジェクトに入っている手元の商品が {FoundInProject.Count} 件ありました。この改変に使ったものなら「追加」を押してください。"
                : matches.Count > 0
                    ? "このプロジェクトに入っている手元の商品は、すべて「使ったもの」に入っています。"
                    : "このプロジェクトの中に、手元の商品のファイルは見つかりませんでした。数えられるのはzipの中にunitypackageがある商品だけです。使ったものは上の欄から商品名で追加できます。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 画面を離れた（出ない画面なので何も言わない）
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
            AddNotice.Set(failed.Message, true);
            return;
        }

        FoundInProject.Remove(row);
        AddNotice.Set($"「{row.Name}」を追加しました。", false);
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

    /// <summary>送るのをやめる（E7）。送信は1本ずつなので、どの画面から押しても同じ物が止まる。</summary>
    public RelayCommand StopUnityCommand => _stopUnity ??= new RelayCommand(Services.UnityImportQueue.Stop);

    private RelayCommand? _stopUnity;

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
        var steps = new List<(int Index, ModificationMember Member, string ItemId, IReadOnlyList<UnityPackageEntry> Fixed, PackageChoiceSection? Choice)>();
        var nothing = new List<string>();

        foreach (var row in rows)
        {
            var item = await _services.Store.Items.LoadAsync(row.Member.ItemId);

            // 包みの一覧は item の要約から引くが、要約が無い・欠けている商品は zip を開いて数える。
            // 1GB 級の zip が HDD にあると止まって見えるので、画面のスレッドでは読まない
            var (packages, choice) = item is null
                ? ([], null)
                : await Task.Run(() =>
                {
                    var found = PackagesFor(item, row.Member);
                    return (found, row.Member.FileHash is null && found.Count > 1 ? PackageChoiceSection.Build(item) : null);
                });
            if (packages.Count == 0)
            {
                nothing.Add(row.Name);
                continue;
            }

            steps.Add((row.Index, row.Member, row.Member.ItemId, choice is null ? packages : [], choice));
        }

        if (steps.Count == 0)
        {
            Services.Notice.Show(
                "使ったものの中に、Unityへ送れるもの（手元のzipやフォルダの中の .unitypackage）がありませんでした。",
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
                + $"unitypackage {fixedCount} 件を入れた順に送ります。"
                + "Unityの取り込み画面で「Import」か「Cancel」を押すと、次の1件が表示されます。"
                + (nothing.Count > 0 ? $"\n\n手元に送れるものが無い {nothing.Count} 件は飛ばします。" : string.Empty),
                title,
                System.Windows.MessageBoxButton.OKCancel,
                // 人が押した操作の確認はどれも Question（`ui-rules.md`・D9）。
                // 紐付いていない別のプロジェクトへ送るときは、既定のボタンをキャンセル側に倒して止める
                System.Windows.MessageBoxImage.Question,
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
                new UiCommand.RecordModificationMemberFiles(Record.Id, step.Member, step.Choice!.CheckedMembers));
            if (result is CommandResult.Failed failed)
            {
                MembersNotice.Set(failed.Message, true);
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

            // 送れなかった物の zip が無ければ記録へ（商品ページの1件の送り方と同じ）
            await FilePresenceNotes.NoteFailedSendsAsync(
                _services, queue.Select(entry => (entry.ItemId, entry.Package)), outcomes, _main.Search.NoteItemChanged);

            UnityQueueText = UnityQueueOutcome.Describe(outcomes);
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
        => PlacesFor(item, member).Select(place => place.Entry).ToList();

    /// <summary><see cref="PackagesFor"/> に、item に書いてある入る先を添えた物（書いていなければ null）。</summary>
    internal static IReadOnlyList<UnityPackagePlace> PlacesFor(ItemRecord item, ModificationMember member)
    {
        if (member.FileHash is { } hash && member.Package is { } package
            && item.Local.OwnedFiles.FirstOrDefault(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)) is { } owner
            && owner.Paths.FirstOrDefault(File.Exists) is { } zip)
        {
            // 要約は zip のハッシュについて書いた物なので、記録した包みの分だけ引けば足りる（全部そろっているかは問わない）
            var roots = owner.UnityPackages?.FirstOrDefault(summary => string.Equals(summary.Entry, package, StringComparison.Ordinal))?.Roots;
            return [new UnityPackagePlace(new UnityPackageEntry(zip, package, 0) { ZipHash = hash }, roots)];
        }

        // 登録したフォルダの中の物を送った記録（ハッシュ無しで場所だけ。メモ65-③）。今もフォルダに在れば、その1つを送る
        if (member.FileHash is null && member.Package is { } folderPackage
            && item.Local.LocalFolders
                .SelectMany(UnityHandoff.PlacesOf)
                .FirstOrDefault(place => string.Equals(place.Entry.EntryPath, folderPackage, StringComparison.Ordinal)) is { } inFolder)
        {
            return [inFolder];
        }

        return UnityImportQueue.PlacesOf(item);
    }

    /// <summary>使ったもの1件を Unity のプロジェクトタブで示す（改変の画面の「Unityで選択」と同じ道・<see cref="UnityMemberSelect"/>）。</summary>
    private async Task SelectMemberInUnityAsync(ModificationMemberRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var item = await _services.Store.Items.LoadAsync(row.Member.ItemId);
        if (await UnityMemberSelect.RunAsync(_services, Record, row.Member, row.Name, row.SourceText, item, Notices.LineOrWindow("Unityで選択", text => MembersNotice.Set(text, false)), _main.Search.NoteItemChanged))
        {
            await ReloadAsync();
        }
    }

    /// <summary>使ったもの。**見せている順**（入れた順か、その逆。メモ26-①）。記録の並びは <see cref="RowsInRecordOrder"/>。</summary>
    public ObservableCollection<ModificationMemberRowViewModel> Members { get; } = [];

    /// <summary>入れた順（記録の並び）の行。送る・数えるのはこちら——見せている順で送ると、依存物より先に本体が入る。</summary>
    internal IReadOnlyList<ModificationMemberRowViewModel> RowsInRecordOrder() => [.. Members.OrderBy(row => row.Index)];

    /// <summary>「使ったものを順にUnityへ送る」で送る行（入れた順・外した行を除く）。</summary>
    internal IReadOnlyList<ModificationMemberRowViewModel> RowsToSendAll() => [.. RowsInRecordOrder().Where(row => row.IsUsed)];

    // ---- 使ったものを見せる順（メモ26-①・ユーザ判断 2026-10-04） ----
    //
    // 依存される物（シェーダー・ライブラリ）が上に来て、人が「使った」と思っている衣装が下に沈むので、逆の順でも見せる。
    // **見せる順だけ**を変え、記録の並び・番号・「順にUnityへ送る」の順は入れた順のまま。どちらで見せるかはアプリを閉じても覚える（全部の改変で同じ）

    private bool _isReversed;

    public bool IsReversed => _isReversed;

    public bool IsInsertOrder => !_isReversed;

    public RelayCommand ShowInsertOrderCommand { get; }

    public RelayCommand ShowReverseOrderCommand { get; }

    /// <summary>並びの上の説明。逆の順のときは、送る順が見えている順と違うことを言う。</summary>
    public string MembersOrderText => _isReversed
        ? "入れた順の逆に並べています。Unityへは入れた順に送ります。"
        : "上から順に入れた記録です。依存するものが先に来るように並べ替えられます。";

    private void SetReversed(bool reversed)
    {
        if (_isReversed == reversed)
        {
            return;
        }

        _isReversed = reversed;
        OnPropertyChanged(nameof(IsReversed));
        OnPropertyChanged(nameof(IsInsertOrder));
        OnPropertyChanged(nameof(MembersOrderText));
        _services.Commands.ExecuteAsync(new UiCommand.ChangeUiState(state => state with { ModificationMembersReversed = reversed })).Forget();

        // 並べ直すだけなので読み直さない（読み直すとプロジェクトの候補まで探し直す）。矢印の向きが変わるので行は作り直す
        var rows = RowsInRecordOrder().Select(row => row.WithReversed(reversed)).ToList();
        Members.Clear();
        foreach (var row in reversed ? Enumerable.Reverse(rows) : rows)
        {
            Members.Add(row);
        }
    }

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

    /// <summary>裏の取得がこの商品の画像を置いた。使ったものの行のカードを描き直す（作ったときに一度だけ絵を探すので）。</summary>
    void IItemImagesListener.NoteItemImagesSaved(string itemId)
    {
        foreach (var member in Members)
        {
            if (member.Card is { } card && string.Equals(card.Item.Id, itemId, StringComparison.Ordinal))
            {
                card.RefreshImages();
            }
        }
    }

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



    /// <summary>右クリックの「改変に追加…」。選びはこの画面に無いので、押した1件だけ（検索の画面と同じ命令）。</summary>


    public RelayCommand CardAddToModificationCommand => _main.Search.CardAddToModificationCommand;


    public RelayCommand CardSelectInUnityCommand => _main.Search.CardSelectInUnityCommand;

    public RelayCommand HideItemCommand => _main.Search.HideItemCommand;

    public bool HasMembers => Members.Count > 0;

    /// <summary>
    /// 構成物が空のときに出す文。
    ///
    /// **次にやることを書く。**空欄だけだと、どうやって足すのか分からない。
    /// </summary>
    public string MembersEmptyText =>
        "まだ追加していません。下の欄から探して追加するか、商品ページの「改変に追加して送る」でUnityへ送ると自動で入ります。";

    public ObservableCollection<ModificationImageViewModel> Images { get; } = [];

    public bool HasImages => Images.Count > 0;

    /// <summary>
    /// 写真が1枚も無いとき、大きい絵の所に出す（ユーザ指示 2026-09-14：0枚なら写真が無いことをギャラリーで分かるようにする）。
    /// 前は大きい絵の所が空で、下に案内が出ていた。**次にやることを書く**（空欄だけだと足し方が分からない）
    /// </summary>
    public string GalleryEmptyText => _gallery.Count > 0
        ? string.Empty
        : "この改変の写真はまだありません。\n「＋」でまとめて選ぶか、ここへドロップするか、Ctrl+Vで貼ってください。";

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

    /// <summary>
    /// 大きく出す1枚。保存された大きさで、裏で読む（キャッシュに乗る）。読み終わるまでは一覧の小さな絵を出しておく
    /// （商品の写真の欄と同じ。前は画面のスレッドでその場で読み、送るたびに1枚ぶん止まっていた）
    /// </summary>
    public BitmapSource? SelectedImage
    {
        get
        {
            if (_gallery.Count == 0)
            {
                return null;
            }

            var selected = _gallery[_selectedIndex];
            return _thumbnails.PeekFull(selected.Path, () =>
            {
                // 送った後に届いた前の絵で描き直させない
                if (_gallery.Count > _selectedIndex && ReferenceEquals(_gallery[_selectedIndex], selected))
                {
                    OnPropertyChanged(nameof(SelectedImage));
                }
            }) ?? selected.Image;
        }
    }

    /// <summary>何枚目か。**2枚以上のときだけ出す**（商品の写真の欄と同じ・U11 と同じ決まり）。</summary>
    public string GalleryCounter => _gallery.Count <= 1 ? string.Empty : $"{_selectedIndex + 1} / {_gallery.Count}";

    public bool CanGoPreviousImage => _gallery.Count > 1;

    public bool CanGoNextImage => _gallery.Count > 1;

    /// <summary>改変の写真は全部自分で貼ったもの。並べ替えと削除は、いま出ている1枚があれば出す。</summary>
    public bool CurrentIsUserAdded => _gallery.Count > 0;

    /// <summary>いま出ている1枚があるか。画面の「この画像を削除」ボタンを、写真が無い改変には出さない。</summary>
    public bool HasCurrentImage => _gallery.Count > 0;

    public bool CurrentIsPinned => false;

    public bool ShowsPinThumbnail => false;

    public bool ShowsUnpinThumbnail => false;

    public bool ShowsImageRoles => false;

    // 右クリックの項目は出したまま押せなくして理由を言う（ユーザ判断 2026-10-04）
    public string PinDisabledTip => "改変の写真には使えません";

    public string UnpinDisabledTip => "改変の写真には使えません";

    public string RemoveImageButtonTip => CurrentIsUserAdded
        ? "ファイルごと消します。元に戻せません。"
        : "自分で追加した画像だけ消せます";

    public string AddImageTip => "この改変に写真を追加します。ドロップやCtrl+Vでも追加できます。";

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
            var tile = new GalleryImage
            {
                Path = path,
                FileName = image.FileName,
                Number = _gallery.Count + 1,
            };

            // 小さな絵は裏で読む（商品の写真の欄と同じ）。無いファイルを頼むと「読めない」と覚えてしまうので、在るものだけ
            if (File.Exists(path))
            {
                tile.LoadTile(_thumbnails);
            }

            _gallery.Add(tile);
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
            nameof(CurrentIsUserAdded), nameof(HasCurrentImage), nameof(RemoveImageButtonTip),
        })
        {
            OnPropertyChanged(name);
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>足す商品の候補。手元にある商品の名前。</summary>
    public ObservableCollection<string> ItemSuggestions { get; } = [];

    /// <summary>候補の表示名 → 商品ID。同じ表示名の別の商品を、名前の先頭一致で取り違えないための引き表。</summary>
    private Dictionary<string, string> _suggestionIds = new(StringComparer.CurrentCultureIgnoreCase);

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

    /// <summary>待っている名前・メモ・blueprint ID を今書く（画面を離れる前・閉じる前）。</summary>
    public Task FlushPendingWritesAsync()
        => Task.WhenAll(_saveName.RunNowAsync(), _saveMemo.RunNowAsync(), _saveBlueprint.RunNowAsync());

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
        ? "「avtr_」で始まるIDの形ではありません。VRChatのアバターの詳細（WebのアバターのページのURLなど）からコピーしてください。"
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
        if (await VrcOsc.SendAvatarChangeAsync(id) is { } problem)
        {
            BlueprintNotice.Set(problem, true);
            return;
        }

        BlueprintNotice.Set(
            "VRChatに着替えを送りました。着替わらないときは、VRChatでOSCが有効か、"
            + "このアバターを着られるかを確かめてください。",
            false);
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
        "Unityプロジェクトを紐付けると、ここから開けます。作業中のプロジェクトがあれば下に表示されます。";

    /// <summary>
    /// Unity Hub・VCC・ALCOM が手元にあるか。候補と一緒に読み直す（入れて戻ってきて「読み直す」を押したときに合わせる）。
    /// 前は入っているかを見ずに「Unity Hubと、VCCかALCOMの一覧を見ましたが」と言い、入っていない物まで見たかのように言っていた
    /// </summary>
    private UnityTools _tools = UnityTools.Unknown;

    /// <summary>候補が1つも無いときに出す文。改変の画面の Unityプロジェクトの見方が空のときと同じことを言う。</summary>
    public string ProjectCandidatesEmptyText => UnityToolsText.ProjectsEmpty(_tools, _services.Settings.ProjectManager);

    /// <summary>「紐付ける先」の「読み直す」の吹き出し。</summary>
    public string RefreshProjectsHint => UnityToolsText.RefreshHint(_tools, _services.Settings.ProjectManager);

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

    // ---- 押した欄・ボタンの下の知らせ（2026-10-04。上の帯の Status に全部出していて、幅420で切れていた） ----
    // 出す場所の決まりは docs/feedback/notice-placement-2026-10-03.md。行の外す・戻す・削除と写真の削除は
    // 押した所ごと消えるので、その一覧の見出しの近く（MembersNotice・GalleryNotice）に出す

    /// <summary>名前の欄の下。保存できなかったときだけ。</summary>
    public AreaNotice NameNotice { get; } = new();

    /// <summary>メモの欄の下。保存できなかったときだけ。</summary>
    public AreaNotice MemoNotice { get; } = new();

    /// <summary>blueprint ID の欄と「VRChatで着替える」の下。</summary>
    public AreaNotice BlueprintNotice { get; } = new();

    /// <summary>Unityプロジェクトの紐付け・開くの下。</summary>
    public AreaNotice ProjectNotice { get; } = new();

    /// <summary>使ったものを足す欄（名前で足す・プロジェクトの候補）の下。</summary>
    public AreaNotice AddNotice { get; } = new();

    /// <summary>使ったものの一覧の見出しの近く（外す・戻す・削除・Unityで選択・Unityへ送る）。</summary>
    public AreaNotice MembersNotice { get; } = new();

    /// <summary>写真のギャラリーの近く（追加・貼り付け・削除）。</summary>
    public AreaNotice GalleryNotice { get; } = new();

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

        var rows = new List<ModificationMemberRowViewModel>();
        for (var index = 0; index < Record.Members.Count; index++)
        {
            var member = Record.Members[index];
            var item = await _services.Store.Items.LoadAsync(member.ItemId);

            rows.Add(new ModificationMemberRowViewModel
            {
                Index = index,
                Total = Record.Members.Count,
                Member = member,
                Name = item?.DisplayName ?? member.ItemId,
                SourceText = ModificationRowBuilder.FileTextOf(member, item),
                Reversed = IsReversed,

                // 手元に無くても記録は残す。そのとき使ったのは事実
                IsMissing = item is null || !item.IsOwned,

                VariationText = VariationLabel(member, item),

                Card = item is null ? null : _main.Search.CreateCard(item),
            });
        }

        Members.Clear();
        foreach (var row in IsReversed ? Enumerable.Reverse(rows) : rows)
        {
            Members.Add(row);
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
        // レジストリとファイルを見るので、どちらも画面のスレッドの外で調べる
        var projects = Task.Run(() => _services.DiscoverUnityProjects());
        var tools = Task.Run(() => _services.DetectUnityTools());
        var found = await projects;
        _tools = await tools;
        OnPropertyChanged(nameof(ProjectCandidatesEmptyText));
        OnPropertyChanged(nameof(RefreshProjectsHint));

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
        if (!ConfirmReplaceProject(row))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(
            new UiCommand.SetModificationProject(Record.Id, row?.Candidate.Path));

        ProjectNotice.Set(
            row is null
                ? "Unityプロジェクトの紐付けを外しました。"
                : $"「{row.Name}」を紐付けました。",
            false);

        await ReloadAsync();
    }

    /// <summary>
    /// 既に紐付いている先を差し替える・外す前に聞く（動線の点検 B3）。
    ///
    /// 改変の記録は紐付け先を1つしか持たないので、差し替えると前の紐付け先はどこにも残らない。
    /// 前は確かめずに差し替わり、何を指していたか分からなくなった。前の名前と場所を出し、戻し方を書く。
    /// **戻し方は前の紐付け先が候補の一覧に載っているかで変わる**——候補は Unity Hub と VCC の一覧から作るので、
    /// 載っていなければこの画面からは付け直せない。そのときは既定のボタンをキャンセルに倒す（ui-dialogs.md：取り返しのつかない操作）。
    /// まだ何も紐付いていないときと、同じ先を選び直したときは聞かない（失う物が無い）。
    /// </summary>
    private bool ConfirmReplaceProject(UnityProjectRowViewModel? row)
    {
        if (Record.UnityProject is not { } oldPath
            || (row is not null && ModificationService.SamePath(row.Candidate.Path, oldPath)))
        {
            return true;
        }

        var oldName = ProjectName;
        var canRelink = ProjectCandidates.Any(candidate => ModificationService.SamePath(candidate.Candidate.Path, oldPath));
        var how = canRelink
            ? $"戻すときは、下の「紐付ける先」から「{oldName}」をもう一度選んでください。"
            : $"「{oldName}」に紐付け直すには、{UnityToolsText.AddProjectFirst(_tools, _services.Settings.ProjectManager)}";
        var what = row is null
            ? $"Unityプロジェクト「{oldName}」の紐付けを外します。"
            : $"Unityプロジェクトの紐付けを「{oldName}」から「{row.Name}」に替えます。";

        var answer = Services.Notice.Show(
            what + "\n\n"
            + $"今の紐付け先：{oldPath}\n"
                        + how + "\n\n"
            + "プロジェクトのフォルダと、改変の使ったもの・写真はそのままです。",
            row is null ? "紐付けを外す" : "紐付けを替える",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            canRelink ? System.Windows.MessageBoxResult.OK : System.Windows.MessageBoxResult.Cancel);

        return answer == System.Windows.MessageBoxResult.OK;
    }

    /// <summary>
    /// 紐付けたプロジェクトを開く。
    ///
    /// **3通りに言い分ける。**開いていたら手前に出るだけなので、
    /// 何も起きなかったように見えないように結果を出す。
    /// </summary>
    private async Task OpenProjectAsync()
    {
        var name = ProjectName;

        // 文は改変の画面の「Unityを開く」と同じ。フォルダが無いときだけ、ここでは指し直せることを言う
        var result = UnityLaunch.OpenProject(Record.UnityProject);
        var text = result == UnityOpenResult.Missing
            ? $"「{name}」が見つかりません。移したのなら、下の一覧から指し直せます。"
            : await UnityOpenText.ForAsync(_services, result, name);

        // 開けなかった結果は、指し直す・入れる・別のやり方で開くが要るので警告の色で出す
        ProjectNotice.Set(
            text,
            result is UnityOpenResult.NoEditorNoHub or UnityOpenResult.Missing or UnityOpenResult.Failed);
    }

    /// <summary>記録に残した種類の番号を、人が読める名前に直す。</summary>
    internal static string VariationLabel(ModificationMember member, ItemRecord? item)
    {
        if (member.VariationId is not { } id)
        {
            return string.Empty;
        }

        var variation = item?.Booth.Variations.FirstOrDefault(candidate => candidate.Id == id);
        return variation is null ? "バリエーションの名前が分かりません" : DisplayText.VariationName(variation.Name);
    }

    /// <summary>
    /// 足す商品の候補。**手元にあるものだけ**を出す。
    /// 持っていない商品を改変に足せても、再現には使えない。
    /// </summary>
    private async Task LoadSuggestionsAsync()
    {
        ItemSuggestions.Clear();

        // 候補なので検索の写しで足りる（開くたびに全件のJSONを読み直していた）
        IReadOnlyList<ItemRecord> loaded;
        try
        {
            loaded = await _main.Search.ItemsAsync(_leaving.Token);
        }
        catch (OperationCanceledException) when (_leaving.IsCancellationRequested)
        {
            // 画面を離れた。投げ直さないのは、書き込みの後に読み直していた呼び手を失敗に見せないため
            return;
        }

        // 足した商品とアバター自身は出さない（足せても意味が無く、足した物を選び直させるだけになる）
        var excluded = Record.Members.Select(member => member.ItemId).Append(Record.AvatarItemId)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = loaded
            .Where(item => item.IsOwned && !excluded.Contains(item.Id))
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture)
            .ToList();

        // 同じ表示名の商品が複数あるときは、ショップ名（それでも重なれば商品ID）で見分ける
        var duplicated = candidates.GroupBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToHashSet();
        var ids = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        var labels = new List<string>();
        foreach (var item in candidates)
        {
            var label = item.DisplayName;
            if (duplicated.Contains(item))
            {
                label = item.Booth.Shop?.Name is { Length: > 0 } shop ? $"{label}（{shop.Trim()}）" : label;
                if (ids.ContainsKey(label))
                {
                    label = $"{label}（{item.Id}）";
                }
            }

            if (ids.TryAdd(label, item.Id))
            {
                labels.Add(label);
            }
        }

        _suggestionIds = ids;
        foreach (var label in labels)
        {
            ItemSuggestions.Add(label);
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

        // 自動で保存する欄は、保存できたときは何も言わない（出すのは保存できなかったときだけ）
        NameNotice.Set(result is CommandResult.Failed failed ? failed.Message : string.Empty, true);
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

        MemoNotice.Set(result is CommandResult.Failed failed ? failed.Message : string.Empty, true);
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

        BlueprintNotice.Set(result is CommandResult.Failed failed ? failed.Message : string.Empty, true);
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
            System.Windows.MessageBoxImage.Question,
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
    /// **どのファイルを使ったかは窓で人が選ぶ**（メモ26-②・ユーザ判断 2026-10-04）。選ばなければ空のまま。
    /// 選べるファイルが無い商品は聞かずに足す（選ぶ物が無い窓を出さない）。**窓を出さずに黙って入れることはしない**（推定で埋めない）
    /// </summary>
    private async Task AddMemberAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // 名前は候補から選ぶので、引くのも候補の引き表から。商品名だけで探すと、同じ名前の別の商品を足し得る。
        // 候補に無い名前（足した物・アバター自身・手元に無い物）は足さない
        var item = _suggestionIds.TryGetValue(name.Trim(), out var itemId)
            ? await _services.Store.Items.LoadAsync(itemId)
            : null;

        if (item is null)
        {
            AddNotice.Set($"「{name.Trim()}」という商品が手元に見つかりません。", true);
            return;
        }

        var files = new MemberFilePickViewModel([item]);
        if (files.HasChoices && !MemberFilePickViewModel.Ask(files, "使ったものを追加", $"「{item.DisplayName}」をこの改変に追加します。"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(
            Record.Id,
            files.MemberFor(item.Id, DateTimeOffset.Now)));

        AddNotice.Set(
            result is CommandResult.Failed failed ? failed.Message : $"「{item.DisplayName}」を追加しました。",
            result is CommandResult.Failed);

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
            new UiCommand.SetModificationMemberDetached(Record.Id, row.Member, detached));

        MembersNotice.Set(
            detached
                ? $"「{row.Name}」を外しました。「戻す」で元に戻せます。"
                : $"「{row.Name}」を戻しました。",
            false);
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
            $"「{row.Name}」をこの改変から完全に削除します。\n\n"
            + (row.HasFile
                ? $"使ったファイル（{row.SourceText}）の記録も消えます。\n\n"
                : string.Empty)
            + "この操作は元に戻せません。",
            "使ったものを削除",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(
            new UiCommand.RemoveModificationMember(Record.Id, row.Member));

        MembersNotice.Set($"「{row.Name}」を削除しました。", false);
        await ReloadAsync();
    }

    /// <param name="back">見えている並びの前へ動かすか。逆の順で見せているときは、記録の上では後ろへ動く。</param>
    private async Task MoveMemberAsync(ModificationMemberRowViewModel? row, bool back)
    {
        if (row is null)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(
            new UiCommand.MoveModificationMember(Record.Id, row.Member, back ? row.BackDelta : -row.BackDelta));

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

        MembersNotice.Set($"「{row.Name}」は手元にありません。記録は残っています。", true);
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
                GalleryNotice.Set($"{Path.GetFileName(path)} を読めませんでした。", true);
                continue;
            }

            if (await AddImageBytesAsync(bytes))
            {
                added++;
            }
            else
            {
                GalleryNotice.Set($"{Path.GetFileName(path)} は画像として読めませんでした。", true);
            }
        }

        if (added > 0)
        {
            GalleryNotice.Set($"写真を {added} 枚貼りました。", false);
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
        var added = await AddImageBytesAsync(bytes);
        GalleryNotice.Set(added ? "写真を1枚貼りました。" : "画像として読めませんでした。", !added);

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
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(
            new UiCommand.RemoveModificationImage(Record.Id, image.FileName));

        GalleryNotice.Set("写真を消しました。", false);
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
