using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

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
    public sealed record DiffRow(string Field, string Text);

    /// <summary>
    /// 行の中身。**どの種別も同じ札で出す**（ユーザ指示 2026-09-18：商品ページの変更だけが札で、
    /// ほかの束は薄い1行だった）。変化を持つ知らせは変わったところを1つずつ、
    /// 持たない知らせは「見出し：中身」を1枚の札にする
    /// </summary>
    public IReadOnlyList<DiffRow> Cards
    {
        get
        {
            if (Record.Diffs.Count > 0)
            {
                return Record.Diffs
                    .Select(diff => new DiffRow(
                        diff.Field,
                        diff.Before is { Length: > 0 } before
                            ? $"{before} → {diff.After ?? "（無し）"}"
                            : diff.After ?? string.Empty))
                    .ToList();
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
    /// 受信箱なので、いつ起きたかは「どれくらい前か」で読む（ユーザ指示 2026-09-18）。
    /// 正確な日時はツールチップ（<see cref="CreatedTip"/>）に置く
    /// </summary>
    public string CreatedText
    {
        get
        {
            var span = DateTimeOffset.Now - Record.CreatedAt;
            return span switch
            {
                { TotalMinutes: < 1 } => "たった今",
                { TotalHours: < 1 } => $"{(int)span.TotalMinutes}分前",
                { TotalDays: < 1 } => $"{(int)span.TotalHours}時間前",
                { TotalDays: < 7 } => $"{(int)span.TotalDays}日前",
                _ => Record.CreatedAt.ToString("yyyy-MM-dd"),
            };
        }
    }

    public string CreatedTip => Record.CreatedAt.ToString("yyyy-MM-dd HH:mm");

    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (SetField(ref _isRead, value))
            {
                OnPropertyChanged(nameof(ReadButtonText));
                ReadChanged?.Invoke(this);
            }
        }
    }

    public string ReadButtonText => IsRead ? "未読に戻す" : "確認した";

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

    public required IReadOnlyList<NotificationRow> Rows { get; init; }

    public int UnreadCount => Rows.Count(row => !row.IsRead && !row.IsResolved);

    /// <summary>
    /// 束の件数は、行に出る札と同じ色・同じ言葉で出す（ユーザ指示 2026-09-18）。
    /// 「4 件（未読 4）」の一続きの小さな文字では、何件あって何を先に読むのかが掴めなかった
    /// </summary>
    public string TotalText => $"{Rows.Count}件";

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
        })
        {
            OnPropertyChanged(name);
        }
    }
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

    public InboxViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        _unreadOnly = services.UiState.InboxUnreadOnly;

        MarkAllReadCommand = new RelayCommand(() => MarkAllReadAsync().Forget(), () => UnreadCount > 0);
        RefreshCommand = new RelayCommand(() => ReloadAsync().Forget());

        ReloadAsync().Forget();
    }

    public ObservableCollection<NotificationGroup> Groups { get; } = [];

    public RelayCommand MarkAllReadCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public int TotalCount => _all.Count;

    /// <summary>ナビのバッジと数え方を揃える（解消済みは一覧に出ないので数えない）。</summary>
    public int UnreadCount => _all.Count(row => !row.IsRead && !row.IsResolved);

    public string HeaderText => UnreadCount > 0
        ? $"未読 {UnreadCount} 件 / 全 {TotalCount} 件"
        : $"全 {TotalCount} 件（未読なし）";

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
            detected = await _services.Notifications.DetectOrphanReferencesAsync();
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            // 検出に失敗しても、既にある通知は読めるようにする
        }

        RunOnUiThread(() =>
        {
            Load();

            // 検出で通知が増えることがあるので、ナビの件数も数え直す
            _main.RefreshBadges();

            if (detected > 0)
            {
                StatusText = $"一覧に無い分類を参照している商品を {detected} 件見つけました。";
            }
        });
    }

    private void Load()
    {
        foreach (var row in _all)
        {
            row.ReadChanged -= OnRowReadChanged;
        }

        _all = _services.Notifications.Load()
            .OrderByDescending(record => record.CreatedAt)
            .Select(CreateRow)
            .ToList();

        Rebuild();
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
        NotificationKind.OrphanTag => "タグの管理を開きます。消えたタグを作り直すか、商品から外せます。",
        NotificationKind.ArchiveFoundForFolder =>
            "この商品のファイルを、展開したフォルダではなくzipの方で数えるようにします。"
            + "\nディスクのファイルは消えません。あとから同じフォルダを登録し直せます。",
        NotificationKind.ItemBackOnBooth or NotificationKind.OrphanVariationLink
            or NotificationKind.VariationBackOnBooth =>
            "BOOTHの商品ページに載っている情報（商品名・価格・バリエーション・説明文・画像）を取り直します。"
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
                if (row.ItemId is { } itemId && row.Record.Id.Split(':', 2) is [_, { Length: > 0 } path])
                {
                    // zipを付けるところまでやる（ユーザ判断 2026-09-18）。大きいzipはハッシュに数秒かかる
                    StatusText = "zipを読んで登録しています…";
                    var outcome = await _services.Commands.ExecuteAsync(new UiCommand.SwapFolderForArchive(itemId, path));

                    StatusText = outcome is CommandResult.ArchiveSwapped { Outcome: { } swapped }
                        ? swapped.Result switch
                        {
                            Core.Services.ArchiveSwapResult.Registered =>
                                $"zipで登録しなおしました。これからは {swapped.ArchiveName} でこの商品を数えます。"
                                + "展開したフォルダのファイルは消していません。",
                            Core.Services.ArchiveSwapResult.AlreadyRegistered =>
                                $"{swapped.ArchiveName} は既に登録してあったので、展開フォルダの登録だけ外しました。"
                                + "フォルダのファイルは消していません。",
                            Core.Services.ArchiveSwapResult.ArchiveMissing =>
                                "隣にzipが見つかりませんでした。移動したか、外付けを外している可能性があります。"
                                + "登録はそのままにしてあります。",
                            Core.Services.ArchiveSwapResult.ArchiveUnreadable =>
                                "zipが読めませんでした（ほかのアプリが開いているかもしれません）。登録はそのままにしてあります。",
                            _ => "この商品はもうありません。",
                        }
                        : "登録しなおせませんでした。";

                    await ReloadAsync();
                }

                return;

            case NotificationKind.ItemBackOnBooth:
            case NotificationKind.OrphanVariationLink:
            case NotificationKind.VariationBackOnBooth:
                if (row.ItemId is { } target)
                {
                    StatusText = "商品情報を取り直しています…";
                    await _services.Commands.ExecuteAsync(new UiCommand.RefreshItem(target));
                    StatusText = "BOOTHの商品ページから情報を取り直しました。";
                    await ReloadAsync();
                }

                return;
        }
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
            row.ReadChanged -= OnRowReadChanged;
            row.IsRead = true;
            row.ReadChanged += OnRowReadChanged;
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
            row.ReadChanged -= OnRowReadChanged;
            row.IsRead = true;
            row.ReadChanged += OnRowReadChanged;
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
        StatusText = "この商品はもうありません（商品IDを変えたか、管理から外したようです）。この知らせは解消済みにしました。";

        var ids = _services.Notifications.Load()
            .Where(notification => notification.ItemId == itemId && !notification.IsResolved)
            .Select(notification => notification.Id)
            .ToList();

        await _services.Commands.ExecuteAsync(new UiCommand.ResolveNotifications(ids));
        await ReloadAsync();
    }

    private void Rebuild()
    {
        // 解消済みは用が済んでいるので、未読のみの表示には出さない
        var rows = _unreadOnly
            ? _all.Where(row => !row.IsRead && !row.IsResolved).ToList()
            : _all;

        Groups.Clear();
        foreach (var group in rows
            // アプリ全体の話（取得できる情報の形式の変化）は、商品1件ごとの話と並べない。
            // ナビの「設定」の上の帯で知らせる（ユーザ判断 2026-09-18）
            .Where(row => row.Record.Kind != NotificationKind.PageStructureChanged)
            .GroupBy(row => row.Record.Kind)
            // 重要が混ざっている種類を先に、その次は新しい知らせがある種類から（ユーザ判断 2026-09-18）。
            // 種類の宣言順では、何から読めばよいかが伝わらなかった
            .OrderByDescending(group => group.Any(row => row.IsStrong && !row.IsRead))
            .ThenByDescending(group => group.Max(row => row.Record.CreatedAt))
            .ThenBy(group => group.Key))
        {
            var built = new NotificationGroup
            {
                Kind = group.Key,
                KindText = KindLabel(group.Key),
                Description = KindDescription(group.Key),
                Rows = group.ToList(),
            };

            built.MarkGroupReadCommand = new RelayCommand(
                () => MarkGroupReadAsync(built).Forget(),
                () => built.UnreadCount > 0);

            Groups.Add(built);
        }

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        RelayCommand.RaiseCanExecuteChanged();
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
        _ => "その他",
    };

    private static string KindDescription(NotificationKind kind) => kind switch
    {
        NotificationKind.ItemUpdated => "取得し直したときに内容が変わっていたものです。",
        NotificationKind.OrphanTag => "タグの管理・属性の管理から消えたか名前が変わったものを、商品がまだ参照しています。",
        NotificationKind.OrphanVariationLink => "手元のファイルや購入の記録が指すバリエーションが、BOOTH側から消えました。",
        NotificationKind.VariationBackOnBooth => "消えていたバリエーションが、BOOTHにまた出てきました。",
        NotificationKind.PageStructureChanged => "BOOTHから取得できる情報の形式が変化した可能性があります。アプリの更新が必要かもしれません。",
        NotificationKind.ArchiveFoundForFolder => "展開したフォルダとzipの両方を持っています。登録を外すまで、同じ中身を二重に数えます（ファイルは消えません）。",
        NotificationKind.ItemBackOnBooth => "非公開と見なしていた商品が、BOOTHでまた見えるようになりました。",
        _ => string.Empty,
    };
}
