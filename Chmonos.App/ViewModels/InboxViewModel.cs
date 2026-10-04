using System.Collections.ObjectModel;
using System.IO;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>要確認1件。既読は消さずに残すので、状態も持つ。</summary>
public sealed class NotificationRow : ViewModelBase
{
    private bool _isRead;

    public required NotificationRecord Record { get; init; }

    public required string KindText { get; init; }

    public string Title => Record.Title;

    public string Detail => Record.Detail;

    public string? ItemId => Record.ItemId;

    public bool HasItem => !string.IsNullOrEmpty(Record.ItemId);

    /// <summary>
    /// 変わったところ1つ。**前の値も出す**（ユーザ指示 2026-09-18：価格・文言・バリエーション数は
    /// 「変化」なので、後の値だけでは何が起きたか分からない）。前が無いもの（足された見出しなど）は後だけ
    /// </summary>
    /// <remarks>
    /// 説明文の見出しの変更は、変わった行（<see cref="Lines"/>）を持つときはその行を出し、頭の抜き出し（<see cref="Text"/>）は出さない（メモ13-②）。
    /// 抜き出しは前後とも頭の70字なので、見出しの後ろの方が変わると「同じ → 同じ」に見えていた
    /// </remarks>
    public sealed record DiffRow(string Field, string Text)
    {
        public IReadOnlyList<ChangedLineRow> Lines { get; init; } = [];

        /// <summary>札に入り切らなかった行の数（「ほか n 行」）。無ければ空。</summary>
        public string MoreText { get; init; } = string.Empty;
    }

    /// <summary>
    /// 行の中身。**どの種別も同じ札で出す**（ユーザ指示 2026-09-18：商品ページの変更だけが札で、
    /// ほかの束は薄い1行だった）。変化を持つ知らせは変わったところを1つずつ、
    /// 持たない知らせは「見出し：中身」を1枚の札にする
    /// </summary>
    /// <remarks>
    /// 一度作ったら控える。記録は作った後に変わらない（init のみ）のに、読まれるたびに作り直していて、
    /// 札の数を見る <see cref="HasCards"/> と一覧の結び付けで1行につき2回以上組み立てていた
    /// </remarks>
    public IReadOnlyList<DiffRow> Cards => _cards ??= BuildCards();

    private IReadOnlyList<DiffRow>? _cards;

    private IReadOnlyList<DiffRow> BuildCards()
    {
        if (Record.Diffs.Count > 0)
        {
            return Record.Diffs.Select(ToCard).ToList();
        }

        if (Detail.Length == 0)
        {
            return [];
        }

        // 「消えたバリエーション：支援版（旧）」のように、見出しと中身に分けて書いてある
        var separator = Detail.IndexOf('：');
        return separator > 0
            ? [new DiffRow(Detail[..separator], Detail[(separator + 1)..])]
            : [new DiffRow(string.Empty, Detail)];
    }

    private static DiffRow ToCard(NotificationDiff diff)
    {
        var lines = ChangedLines.From(diff);
        if (lines.HasAny)
        {
            var (rows, more) = lines.ForCard();
            return new DiffRow(diff.Field, string.Empty) { Lines = rows, MoreText = more };
        }

        // 価格はバリエーションごとの値段があれば、商品ページの行と同じ「名前 ¥前 → ¥今」を1行ずつ（メモ27-⑤）。
        // 商品の価格の文字は一番安い値段だけで、高い方だけが変わると「¥ 500~ → ¥ 500~」になる
        if (diff.Prices is { Count: > 0 } prices)
        {
            return new DiffRow(diff.Field, string.Join("\n", prices.Select(Core.Services.BoothChanges.PriceChangeText)));
        }

        return new DiffRow(
            diff.Field,
            diff.Before is { Length: > 0 } before
                ? $"{before} → {diff.After ?? "（無し）"}"
                : diff.After ?? string.Empty);
    }

    public bool HasCards => Cards.Count > 0;

    /// <summary>更新履歴の変化など、注目度の高いもの。見落とすと困る側。</summary>
    public bool IsStrong => Record.IsStrong;

    /// <summary>知らせた状況がもう無いもの。用は済んでいるが、何が起きていたかは残す。</summary>
    public bool IsResolved => Record.IsResolved;

    /// <summary>
    /// この通知を片付けるための操作（ユーザ指示 2026-09-18）。
    /// 「確認した」では何も直らないので、種類ごとに直しに行ける道を1つ足す
    /// </summary>
    public string ActionText { get; init; } = string.Empty;

    /// <summary>押す前に、何がどうなるかを言う（取り返しが付くかも書く）。</summary>
    public string ActionTip { get; init; } = string.Empty;

    public bool HasAction => ActionText.Length > 0;

    public RelayCommand? ActionCommand { get; set; }

    /// <summary>
    /// 受信箱なので、いつ起きたかは「どれくらい前か」で読む（ユーザ指示 2026-09-18）。重ねた知らせは最後に変わった日時で言う。
    /// 正確な日時はツールチップ（<see cref="CreatedTip"/>）に置く
    /// </summary>
    public string CreatedText
    {
        get
        {
            var span = DateTimeOffset.Now - Record.LastChangedAt;
            return span switch
            {
                { TotalMinutes: < 1 } => "たった今",
                { TotalHours: < 1 } => $"{(int)span.TotalMinutes}分前",
                { TotalDays: < 1 } => $"{(int)span.TotalHours}時間前",
                { TotalDays: < 7 } => $"{(int)span.TotalDays}日前",
                _ => Record.LastChangedAt.ToString("yyyy-MM-dd"),
            };
        }
    }

    /// <summary>未読のうちに変化を重ねた知らせは、最初と最後の日時を並べる（何日にわたって変わったかが読めるように）。</summary>
    public string CreatedTip => Record.UpdatedAt is { } updated
        ? $"{Record.CreatedAt:yyyy-MM-dd HH:mm} 〜 {updated:yyyy-MM-dd HH:mm}"
        : Record.CreatedAt.ToString("yyyy-MM-dd HH:mm");

    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (SetField(ref _isRead, value))
            {
                OnPropertyChanged(nameof(ReadButtonText));
                OnPropertyChanged(nameof(ReadButtonName));
                ReadChanged?.Invoke(this);
            }
        }
    }

    public string ReadButtonText => IsRead ? "未読に戻す" : "既読にする";

    /// <summary>
    /// 既読の丸の、読み上げ・自動操作の名前。丸は行ごとに並ぶので、どの行の物かを入れる。
    /// 今押すと起きることを言う（丸は切り替えのボタンで、読み上げは状態を「オン・オフ」としか言わない）。
    /// 語は画面の「すべて既読にする」「未読のみ」に合わせる
    /// </summary>
    public string ReadButtonName => ReadNameFor(Title, IsRead);

    internal static string ReadNameFor(string title, bool isRead) => isRead ? $"{title}を未読に戻す" : $"{title}を既読にする";

    public event Action<NotificationRow>? ReadChanged;

    public RelayCommand? ToggleReadCommand { get; set; }

    public RelayCommand? OpenItemCommand { get; set; }
}

/// <summary>種別ごとの束。何が起きた話なのかで分けて読む。</summary>
public sealed class NotificationGroup : ViewModelBase
{
    /// <summary>種別ごとに畳んだかを覚える。読み直しや「未読のみ」の切り替えで束を作り直すので、行の側には置けない。</summary>
    private static readonly Dictionary<NotificationKind, bool> Collapsed = [];

    public required NotificationKind Kind { get; init; }

    /// <summary>
    /// 束を畳めるようにする（ユーザ指示 2026-09-18）。種類が8つあり、1種類が何十件にもなるので、
    /// 開いたままだと他の種類が画面の外に出る
    /// </summary>
    public bool IsExpanded
    {
        get => !Collapsed.GetValueOrDefault(Kind);
        set
        {
            if (IsExpanded == value)
            {
                return;
            }

            Collapsed[Kind] = !value;
            OnPropertyChanged();
        }
    }

    public required string KindText { get; init; }

    public required string Description { get; init; }

    /// <summary>束の行。組み直しでは差分だけを出し入れする（<see cref="CollectionSync"/>）。</summary>
    public ObservableCollection<NotificationRow> Rows { get; } = [];

    public int UnreadCount => Rows.Count(row => !row.IsRead && !row.IsResolved);

    /// <summary>
    /// 束の件数は、行に出る札と同じ色・同じ言葉で出す（ユーザ指示 2026-09-18）。
    /// 「4 件（未読 4）」の一続きの小さな文字では、何件あって何を先に読むのかが掴めなかった
    /// </summary>
    public string TotalText => $"{Rows.Count} 件";

    public bool HasUnread => UnreadCount > 0;

    public string UnreadText => $"未読 {UnreadCount}";

    /// <summary>まだ読んでいない重要（行の「重要」の札の数）。</summary>
    public int StrongCount => Rows.Count(row => row.IsStrong && !row.IsRead);

    public bool HasStrong => StrongCount > 0;

    public string StrongText => $"重要 {StrongCount}";

    public int ResolvedCount => Rows.Count(row => row.IsResolved);

    public bool HasResolved => ResolvedCount > 0;

    public string ResolvedText => $"解消済み {ResolvedCount}";

    /// <summary>この束だけをまとめて既読にする（ユーザ判断 2026-09-18：束ごとにあれば十分）。</summary>
    public RelayCommand? MarkGroupReadCommand { get; set; }

    /// <summary>
    /// 既読にしても行は消さない方針なので、束の側の件数は自分で数え直す必要がある。
    /// ここが黙って古いままだと「未読 1」と出たまま未読が無い、という嘘になる。
    /// </summary>
    public void RefreshCount()
    {
        foreach (var name in new[]
        {
            nameof(UnreadCount), nameof(HasUnread), nameof(UnreadText),
            nameof(StrongCount), nameof(HasStrong), nameof(StrongText),
            nameof(ResolvedCount), nameof(HasResolved), nameof(ResolvedText),

            // 束は組み直しで使い回すので、行の数も変わり得る
            nameof(TotalText),
        })
        {
            OnPropertyChanged(name);
        }
    }
}

/// <summary>
/// 要確認の平らな一覧の1行（仮想化の単位）。
///
/// 束の中に行を入れ子にした一覧は、見えていない行まで全部作る（知らせ1000件で開くのに約7秒固まった。2026-09-24）。
/// 束の見出しと行を1本に並べて仮想化し、束の枠（角丸・左右の線・束の間の余白）は行ごとに描き分ける。
/// <see cref="IsLast"/> は束の最後の行か（畳んだ束では見出し）で、そこだけ枠の下を閉じる。
/// </summary>
public abstract class InboxLine : ViewModelBase
{
    private bool _isLast;

    public bool IsLast
    {
        get => _isLast;
        set => SetField(ref _isLast, value);
    }
}

/// <summary>束の見出しの行。見た目の結び先は束そのもの。</summary>
public sealed class InboxHeadLine(NotificationGroup group) : InboxLine
{
    public NotificationGroup Group { get; } = group;
}

/// <summary>知らせ1件の行。見た目の結び先は知らせの行そのもの。</summary>
public sealed class InboxRowLine(NotificationRow row) : InboxLine
{
    public NotificationRow Row { get; } = row;
}

/// <summary>
/// 要確認画面。「今すぐ困らないが知っておきたいこと」の受信箱。
///
/// 未確定や編集が「残っている作業量」なのに対し、こちらは「新しく起きたこと」。
/// 確認しても消さずに既読にするのは、見たことと無かったことを分けるため。
/// </summary>
public sealed class InboxViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    /// <summary>「取り込み中に n 件増えました」の1行を出すために見る。</summary>
    public MainViewModel Main => _main;

    private List<NotificationRow> _all = [];
    private bool _unreadOnly;
    private string _statusText = string.Empty;

    /// <summary>開いたら送る先の商品。送ったら忘れる（読み直すたびに同じ所へ戻されないように）。</summary>
    private string? _focusItemId;

    private InboxLine? _focusLine;

    /// <summary>
    /// 画面がそこまで送る行。一覧は仮想化していて、行の部品は見えている分しか無いので、
    /// 送るのは一覧を持つ画面（View）が行う。送ったら View が null に戻す
    /// </summary>
    public InboxLine? FocusLine
    {
        get => _focusLine;
        set => SetField(ref _focusLine, value);
    }

    public InboxViewModel(AppServiceContainer services, MainViewModel main, string? focusItemId = null)
    {
        _services = services;
        _main = main;
        _focusItemId = focusItemId;

        _unreadOnly = services.UiState.InboxUnreadOnly;

        MarkAllReadCommand = new RelayCommand(() => MarkAllReadAsync().Forget(), () => UnreadCount > 0);
        RefreshCommand = new RelayCommand(() => ReloadAsync().Forget());

        ReloadAsync().Forget();
    }

    public ObservableCollection<NotificationGroup> Groups { get; } = [];

    /// <summary>
    /// 画面に並べる平らな一覧（先頭は状況の1行を出すこの画面そのもの、続いて束の見出しと行）。
    /// 束と行の出し入れのたびに差分で寄せる（<see cref="CollectionSync"/>）。仮想化しているので、
    /// 作られるのは見えている行だけ
    /// </summary>
    public ObservableCollection<object> Lines { get; } = [];

    private readonly Dictionary<NotificationGroup, InboxHeadLine> _headLines = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NotificationRow, InboxRowLine> _rowLines = new(ReferenceEqualityComparer.Instance);

    public RelayCommand MarkAllReadCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public int TotalCount => _all.Count;

    /// <summary>ナビのバッジと数え方を揃える（解消済みは一覧に出ないので数えない）。</summary>
    public int UnreadCount => _all.Count(row => !row.IsRead && !row.IsResolved);

    public string HeaderText => UnreadCount > 0
        ? $"未読 {UnreadCount} 件 / 全 {TotalCount} 件"
        : $"全 {TotalCount} 件・未読なし";

    public bool IsEmpty => Groups.Count == 0;

    public string EmptyText => _all.Count == 0
        ? "要確認はありません"
        : "未読はありません";

    /// <summary>既定は未読のみ。溜まった既読に埋もれると「新しく起きたこと」が読めない。</summary>
    public bool UnreadOnly
    {
        get => _unreadOnly;
        set
        {
            if (SetField(ref _unreadOnly, value))
            {
                Rebuild();

                // 開き直すたびに既定へ戻るのが面倒だったので覚える（ユーザ判断 2026-09-18）
                var unreadOnly = value;
                _main.SaveUiStateAsync(state => state with { InboxUnreadOnly = unreadOnly }).Forget();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetField(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => StatusText.Length > 0;

    /// <summary>
    /// 開くたびに、マスタに無い分類を参照しているitemを探し直してから読む。
    /// マスタのJSONは手で書き換えられるので、その食い違いはここでしか気付けない。
    /// </summary>
    public async Task ReloadAsync()
    {
        var detected = 0;
        try
        {
            // 見つけた物は知らせとして書くので、書き込みの道（保存先を運ぶ間の門）を通す
            if (await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.DetectOrphanReferences())
                is Core.Commands.CommandResult.Counted counted)
            {
                detected = counted.Count;
            }
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            // 検出に失敗しても、既にある通知は読めるようにする
        }

        // 知らせのファイル（上限2000件で約1MB）は裏で読む。前は画面のスレッドで同期で読んでいた。
        // 開き直しが重なったら、最後に頼んだ読みだけを当てる（先に頼んだ古い一覧で上書きしない）
        var turn = ++_loadTurn;
        var records = await Task.Run(() => _services.Notifications.Load());

        RunOnUiThread(() =>
        {
            if (turn != _loadTurn)
            {
                return;
            }

            Load(records);
            FocusRequestedItem();

            // 検出で通知が増えることがあるので、ナビの件数も数え直す
            _main.RefreshBadges();

            if (detected > 0)
            {
                StatusText = $"一覧に無いユーザータグを参照している商品を {detected} 件見つけました。";
            }
        });
    }

    /// <summary>知らせの読み込みの番号。画面のスレッドだけが触る。</summary>
    private int _loadTurn;

    private void Load(IReadOnlyList<NotificationRecord> records)
    {
        foreach (var row in _all)
        {
            row.ReadChanged -= OnRowReadChanged;
        }

        _all = records
            .OrderByDescending(record => record.LastChangedAt)
            .Select(CreateRow)
            .ToList();

        Rebuild();
    }

    /// <summary>
    /// 頼まれた商品の「商品の更新」の知らせの束を開き、その行を画面に送らせる。
    /// 未読を先に選ぶ（ショップの「更新あり」は未読の知らせから出している）
    /// </summary>
    private void FocusRequestedItem()
    {
        if (_focusItemId is not { } itemId)
        {
            return;
        }

        _focusItemId = null;
        var candidates = Groups
            .Where(group => group.Kind == NotificationKind.ItemUpdated)
            .SelectMany(group => group.Rows.Select(row => (group, row)))
            .Where(pair => pair.row.ItemId == itemId)
            .OrderBy(pair => pair.row.IsRead)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var (group, row) = candidates[0];
        group.IsExpanded = true;
        if (_rowLines.TryGetValue(row, out var line))
        {
            FocusLine = line;
        }
    }

    private NotificationRow CreateRow(NotificationRecord record)
    {
        var row = new NotificationRow
        {
            Record = record,
            KindText = KindLabel(record.Kind),
            IsRead = record.IsRead,
            ActionText = ActionLabel(record),
            ActionTip = ActionTip(record),
        };

        row.ReadChanged += OnRowReadChanged;
        row.ToggleReadCommand = new RelayCommand(() => row.IsRead = !row.IsRead);
        row.OpenItemCommand = new RelayCommand(() => OpenItemAsync(row.ItemId).Forget(), () => row.HasItem);
        row.ActionCommand = new RelayCommand(() => ActAsync(row).Forget(), () => row.HasAction);

        return row;
    }

    /// <summary>種類ごとの「ここを直す」。直す道が無い種類（商品ページの変更など）は空。</summary>
    private static string ActionLabel(NotificationRecord record) => record.Kind switch
    {
        NotificationKind.OrphanTag => "タグの管理を開く",
        // 「展開フォルダの登録を外す」では押すまで何が起きるか想像が付かなかった（ユーザ指摘 2026-09-18）。
        // 押した後どうなるか（zipの方でこの商品を数える）を名乗る
        NotificationKind.ArchiveFoundForFolder => "zipで登録しなおす",
        NotificationKind.ItemBackOnBooth or NotificationKind.OrphanVariationLink
            or NotificationKind.VariationBackOnBooth => "商品情報を取り直す",
        _ => string.Empty,
    };

    private static string ActionTip(NotificationRecord record) => record.Kind switch
    {
        NotificationKind.OrphanTag => "タグの管理を開きます。消えたユーザータグを作り直すか、商品から外せます。",
        NotificationKind.ArchiveFoundForFolder =>
            "展開したフォルダの代わりにzipで数えます。ファイルは削除しません。",
        NotificationKind.ItemBackOnBooth or NotificationKind.OrphanVariationLink
            or NotificationKind.VariationBackOnBooth =>
            "BOOTHから商品情報を取り直します。"
            + "\n商品のファイルはダウンロードしません。",
        _ => string.Empty,
    };

    private async Task ActAsync(NotificationRow row)
    {
        switch (row.Record.Kind)
        {
            case NotificationKind.OrphanTag:
                _main.ShowTagManage();
                return;

            case NotificationKind.ArchiveFoundForFolder:
                // 通知のIDに、外したいフォルダの場所が入っている（archive-found:{パス}）
                if (row.ItemId is { } itemId && row.Record.Id.Split(':', 2) is [_, { Length: > 0 } path] && !_isRefreshing)
                {
                    // zipを付けるところまでやる（ユーザ判断 2026-09-18）。大きいzipはハッシュに数秒かかるので、
                    // 待つ間の2度押しで同じzipを2回読ませない（「商品情報を取り直す」と同じ守り・40b3863）
                    _isRefreshing = true;
                    StatusText = "zipを読んで登録しています…";
                    try
                    {
                        await SwapFolderForArchiveAsync(itemId, path);
                    }
                    catch (Exception exception)
                    {
                        // 受けないと「zipを読んで登録しています…」のまま残り、止まったように見えた
                        Core.Diagnostics.AppLog.Error("要確認からのzipでの登録しなおし", exception);
                        StatusText = $"登録しなおせませんでした。{Core.Services.FailureText.Cause(exception)}";
                    }
                    finally
                    {
                        _isRefreshing = false;
                    }
                }

                return;

            case NotificationKind.ItemBackOnBooth:
            case NotificationKind.OrphanVariationLink:
            case NotificationKind.VariationBackOnBooth:
                if (row.ItemId is { } target && !_isRefreshing)
                {
                    // 取り直しは1.5秒の間隔を空けて並ぶので数秒かかる。待つ間の2度押しで同じ商品を2回取りに行かせない
                    _isRefreshing = true;
                    StatusText = "商品情報を取り直しています…";
                    try
                    {
                        var result = await _services.Commands.ExecuteAsync(new UiCommand.RefreshItem(target));

                        // 前は結果を見ずに「取り直しました」と出していたので、商品ページが消えていても成功に見えた
                        StatusText = result is CommandResult.Failed failed
                            ? failed.Message
                            : "BOOTHの商品ページから情報を取り直しました。";
                        await NoteItemChangedAsync(target);
                        await ReloadAsync();
                    }
                    catch (Exception exception)
                    {
                        // 受けないと「取り直しています…」のまま残り、止まったように見えた
                        Core.Diagnostics.AppLog.Error("要確認からの商品情報の取り直し", exception);
                        StatusText = $"取り直せませんでした。{Core.Services.FailureText.Cause(exception)}";
                    }
                    finally
                    {
                        _isRefreshing = false;
                    }
                }

                return;
        }
    }

    /// <summary>
    /// 書き換えた商品を読み直して検索へ渡す。検索の一覧は読み込んだ写しを持っているので、知らせないと
    /// 全件の読み直しまでカードと所持・容量の絞り込みが古いままだった（点検 2026-09-29）。全件は読み直さない（2000件で数秒かかる）
    /// </summary>
    private async Task NoteItemChangedAsync(string itemId)
    {
        if (await _services.Store.Items.LoadAsync(itemId) is { } item)
        {
            _main.Search.NoteItemChanged(item);
        }
    }

    private async Task SwapFolderForArchiveAsync(string itemId, string path)
    {
        var outcome = await _services.Commands.ExecuteAsync(new UiCommand.SwapFolderForArchive(itemId, path));

        StatusText = outcome is CommandResult.ArchiveSwapped { Outcome: { } swapped }
            ? swapped.Result switch
            {
                Core.Services.ArchiveSwapResult.Registered =>
                    $"「{swapped.ArchiveName}」で登録し直しました。展開したフォルダのファイルは削除していません。",
                Core.Services.ArchiveSwapResult.AlreadyRegistered =>
                    $"「{swapped.ArchiveName}」は登録済みなので、展開したフォルダの登録だけ外しました。"
                    + "ファイルは削除していません。",
                Core.Services.ArchiveSwapResult.ArchiveMissing =>
                    "隣にzipが見つかりませんでした。移動したか、外付けを外している可能性があります。"
                    + "登録はそのままにしてあります。",
                Core.Services.ArchiveSwapResult.ArchiveUnreadable =>
                    "zipを読めませんでした。ほかのアプリが開いている可能性があります。登録はそのままです。",
                _ => "この商品は見つかりませんでした。",
            }
            : "登録しなおせませんでした。";

        await NoteItemChangedAsync(itemId);
        await ReloadAsync();
    }

    /// <summary>「商品情報を取り直す」か「zipで登録しなおす」が走っているか。どちらも数秒かかるので、終わるまで次を受けない</summary>
    private bool _isRefreshing;

    /// <summary>
    /// 行に「既読」の印を付けるが、**行からの知らせは受け取らない**（ユーザ判断 2026-09-21・P16）。
    ///
    /// まとめて既読は保存を1回で済ませてあるので、行ごとの知らせで1件ずつ書き直させない。
    /// 「外して・立てて・付け直す」を2か所に書いていたので、作法をここ1つに寄せた。
    /// </summary>
    private void MarkReadWithoutEcho(NotificationRow row)
    {
        row.ReadChanged -= OnRowReadChanged;
        row.IsRead = true;
        row.ReadChanged += OnRowReadChanged;
    }

    private void OnRowReadChanged(NotificationRow row)
    {
        // **保存を待ってから数え直す。**待たずに数えると、ナビのバッジだけ1つ古い数が残った
        // （ユーザ指摘 2026-09-18：バッジ12・画面11）
        SaveReadAsync(row).Forget();

        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HeaderText));
        RelayCommand.RaiseCanExecuteChanged();

        // 未読のみ表示のときは、読んだものがその場で消えると気持ちよくないので、
        // 一覧の組み直しはしない（次に開いたときに整理される）。
        // 代わりに、束の見出しの件数だけ数え直す。
        foreach (var group in Groups)
        {
            group.RefreshCount();
        }
    }

    private async Task SaveReadAsync(NotificationRow row)
    {
        await _services.Commands.ExecuteAsync(new UiCommand.SetNotificationRead(row.Record.Id, row.IsRead));
        _main.RefreshBadges();
    }

    /// <summary>束の中の未読だけを既読にする。全部既読にするより、読んだ範囲を素直に言える</summary>
    private async Task MarkGroupReadAsync(NotificationGroup group)
    {
        var ids = group.Rows.Where(row => !row.IsRead).Select(row => row.Record.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.MarkNotificationsRead(ids));

        foreach (var row in group.Rows.Where(row => !row.IsRead))
        {
            MarkReadWithoutEcho(row);
        }

        group.RefreshCount();
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HeaderText));
        _main.RefreshBadges();
        RelayCommand.RaiseCanExecuteChanged();
    }

    private async Task MarkAllReadAsync()
    {
        await _services.Commands.ExecuteAsync(new UiCommand.MarkAllNotificationsRead());

        foreach (var row in _all.Where(row => !row.IsRead))
        {
            MarkReadWithoutEcho(row);
        }

        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HeaderText));
        _main.RefreshBadges();
        Rebuild();
    }

    private async Task OpenItemAsync(string? itemId)
    {
        if (itemId is null)
        {
            return;
        }

        var record = await _services.Store.Items.LoadAsync(itemId);
        if (record is not null)
        {
            _main.ShowItem(record);
            return;
        }

        // 商品IDを付け替えたり商品を消しても通知は書き換えないので、宛先が無いことがある。
        // 黙って何も起きないと壊れたように見えるので言う（ユーザ判断 2026-09-18）。
        // 宛先が無い通知は、もう手当てのしようがないので解消済みにする
        StatusText = "この商品は見つかりませんでした。商品IDを変えたか、管理対象から除外した可能性があります。この知らせは解消済みにしました。";

        // 知らせのファイルは画面のスレッドで読まない
        var ids = await Task.Run(() => _services.Notifications.Load()
            .Where(notification => notification.ItemId == itemId && !notification.IsResolved)
            .Select(notification => notification.Id)
            .ToList());

        await _services.Commands.ExecuteAsync(new UiCommand.ResolveNotifications(ids));
        await ReloadAsync();
    }

    private void Rebuild()
    {
        // 解消済みは用が済んでいるので、未読のみの表示には出さない
        var rows = _unreadOnly
            ? _all.Where(row => !row.IsRead && !row.IsResolved).ToList()
            : _all;

        // 束と行は差分だけを出し入れする。この一覧は仮想化していない（束の枠が行をまたいで1枚の札になっている）ので、
        // 丸ごと作り直すと「未読のみ」を切り替えるたび・まとめて既読にするたびに全部の行の部品を作り直していた
        var target = new List<NotificationGroup>();
        foreach (var group in rows
            // アプリ全体の話（取得できる情報の形式の変化）は、商品1件ごとの話と並べない。
            // ナビの「設定」の上の帯で知らせる（ユーザ判断 2026-09-18）
            .Where(row => row.Record.Kind != NotificationKind.PageStructureChanged)
            .GroupBy(row => row.Record.Kind)
            // 重要が混ざっている種類を先に、その次は新しい知らせがある種類から（ユーザ判断 2026-09-18）。
            // 種類の宣言順では、何から読めばよいかが伝わらなかった
            .OrderByDescending(group => group.Any(row => row.IsStrong && !row.IsRead))
            .ThenByDescending(group => group.Max(row => row.Record.LastChangedAt))
            .ThenBy(group => group.Key))
        {
            var built = Groups.FirstOrDefault(existing => existing.Kind == group.Key);
            if (built is null)
            {
                var created = new NotificationGroup
                {
                    Kind = group.Key,
                    KindText = KindLabel(group.Key),
                    Description = KindDescription(group.Key),
                };

                created.MarkGroupReadCommand = new RelayCommand(
                    () => MarkGroupReadAsync(created).Forget(),
                    () => created.UnreadCount > 0);

                // 畳む・開くで、平らな一覧の行を出し入れする
                created.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(NotificationGroup.IsExpanded))
                    {
                        RebuildLines();
                    }
                };
                built = created;
            }

            CollectionSync.Apply(built.Rows, group.ToList());
            built.RefreshCount();
            target.Add(built);
        }

        CollectionSync.Apply(Groups, target);
        RebuildLines();

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 束の並びと開き具合から、平らな一覧を組み直す。行の器は使い回すので、同じ束・同じ知らせには同じ行を渡す
    /// （差し替えると、見えている行の部品が作り直される）。
    /// </summary>
    private void RebuildLines()
    {
        var target = new List<object> { this };
        var usedHeads = new HashSet<NotificationGroup>(ReferenceEqualityComparer.Instance);
        var usedRows = new HashSet<NotificationRow>(ReferenceEqualityComparer.Instance);

        foreach (var group in Groups)
        {
            if (!_headLines.TryGetValue(group, out var head))
            {
                head = new InboxHeadLine(group);
                _headLines[group] = head;
            }

            usedHeads.Add(group);
            target.Add(head);
            InboxLine last = head;

            if (group.IsExpanded)
            {
                foreach (var row in group.Rows)
                {
                    if (!_rowLines.TryGetValue(row, out var line))
                    {
                        line = new InboxRowLine(row);
                        _rowLines[row] = line;
                    }

                    usedRows.Add(row);
                    line.IsLast = false;
                    target.Add(line);
                    last = line;
                }
            }

            head.IsLast = ReferenceEquals(last, head);
            last.IsLast = true;
        }

        // 消えた束・知らせの行は持ち続けない（読み直しのたびに溜まる）
        foreach (var gone in _headLines.Keys.Where(group => !usedHeads.Contains(group)).ToList())
        {
            _headLines.Remove(gone);
        }

        foreach (var gone in _rowLines.Keys.Where(row => !usedRows.Contains(row)).ToList())
        {
            _rowLines.Remove(gone);
        }

        CollectionSync.Apply(Lines, target);
    }

    private static string KindLabel(NotificationKind kind) => kind switch
    {
        // 見出しはユーザ指定（2026-09-18）。何が起きたかを名詞で言い切る
        NotificationKind.ItemUpdated => "商品ページの変更",
        NotificationKind.OrphanTag => "タグの参照切れ",
        NotificationKind.OrphanVariationLink => "消えたバリエーション",
        NotificationKind.VariationBackOnBooth => "復活したバリエーション",
        NotificationKind.PageStructureChanged => "取得できる情報の形式の変化",
        NotificationKind.ArchiveFoundForFolder => "zipを入手した",
        NotificationKind.ItemBackOnBooth => "非公開商品の復活",
        NotificationKind.UnpackedFilesImported => "展開先のファイルを取り込んだ",
        NotificationKind.HandEditMismatch => "手で直したJSONの食い違い",
        _ => "その他",
    };

    private static string KindDescription(NotificationKind kind) => kind switch
    {
        NotificationKind.ItemUpdated => "取得し直したときに内容が変わっていたものです。",
        NotificationKind.OrphanTag => "タグの管理・属性の管理から消えたか名前が変わったものを、商品がまだ参照しています。",
        NotificationKind.OrphanVariationLink => "手元のファイルや購入の記録が指すバリエーションが、BOOTH側から消えました。",
        NotificationKind.VariationBackOnBooth => "消えていたバリエーションが、BOOTHにまた出てきました。",
        NotificationKind.PageStructureChanged => "BOOTHの情報の形式が変わったかもしれません。対応アバターの検出と検索に使える情報が減ります。",
        NotificationKind.ArchiveFoundForFolder => "展開したフォルダとzipの両方を登録しています。片方の登録を外すまで、同じ中身を二重に数えます。",
        NotificationKind.ItemBackOnBooth => "非公開と見なしていた商品が、BOOTHでまた見えるようになりました。",
        NotificationKind.UnpackedFilesImported => "自動で始めた取り込みで、zipを展開したフォルダの中のファイルを取り込みました。元のzipの方で持ち直せます。",
        NotificationKind.HandEditMismatch => "手で直したJSONに、同じ名前の重複や、ファイル名と商品IDの食い違いがあります。JSONを開いて直してください。",
        _ => string.Empty,
    };
}
