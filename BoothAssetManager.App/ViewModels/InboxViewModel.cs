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

    public IReadOnlyList<NotificationDiff> Diffs => Record.Diffs;

    public bool HasDiffs => Record.Diffs.Count > 0;

    /// <summary>更新履歴の変化など、注目度の高いもの。見落とすと困る側。</summary>
    public bool IsStrong => Record.IsStrong;

    /// <summary>知らせた状況がもう無いもの。用は済んでいるが、何が起きていたかは残す。</summary>
    public bool IsResolved => Record.IsResolved;

    /// <summary>
    /// この通知を片付けるための操作（ユーザ指示 2026-09-18）。
    /// 「確認した」では何も直らないので、種類ごとに直しに行ける道を1つ足す
    /// </summary>
    public string ActionText { get; init; } = string.Empty;

    public bool HasAction => ActionText.Length > 0;

    public RelayCommand? ActionCommand { get; set; }

    public string CreatedText => Record.CreatedAt.ToString("yyyy-MM-dd HH:mm");

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

    public int UnreadCount => Rows.Count(row => !row.IsRead);

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
    private bool _unreadOnly = true;
    private string _statusText = string.Empty;

    public InboxViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        MarkAllReadCommand = new RelayCommand(() => MarkAllReadAsync().Forget(), () => UnreadCount > 0);
        RefreshCommand = new RelayCommand(() => ReloadAsync().Forget());

        ReloadAsync().Forget();
    }

    public ObservableCollection<NotificationGroup> Groups { get; } = [];

    public RelayCommand MarkAllReadCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public int TotalCount => _all.Count;

    public int UnreadCount => _all.Count(row => !row.IsRead);

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
        NotificationKind.ArchiveFoundForFolder => "フォルダ登録を解除する",
        NotificationKind.ItemBackOnBooth or NotificationKind.OrphanVariationLink
            or NotificationKind.VariationBackOnBooth => "BOOTHから取り直す",
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
                // 通知のIDに、解除したいフォルダの場所が入っている（archive-found:{パス}）
                if (row.ItemId is { } itemId && row.Record.Id.Split(':', 2) is [_, { Length: > 0 } path])
                {
                    await _services.Commands.ExecuteAsync(new UiCommand.UnregisterFolder(itemId, path));
                    StatusText = "フォルダ登録を解除しました。zipから登録し直せます。";
                    await ReloadAsync();
                }

                return;

            case NotificationKind.ItemBackOnBooth:
            case NotificationKind.OrphanVariationLink:
            case NotificationKind.VariationBackOnBooth:
                if (row.ItemId is { } target)
                {
                    StatusText = "BOOTHから取り直しています…";
                    await _services.Commands.ExecuteAsync(new UiCommand.RefreshItem(target));
                    StatusText = "BOOTHから取り直しました。";
                    await ReloadAsync();
                }

                return;
        }
    }

    private void OnRowReadChanged(NotificationRow row)
    {
        _services.Commands.ExecuteAsync(new UiCommand.SetNotificationRead(row.Record.Id, row.IsRead)).Forget();

        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HeaderText));
        _main.RefreshBadges();
        RelayCommand.RaiseCanExecuteChanged();

        // 未読のみ表示のときは、読んだものがその場で消えると気持ちよくないので、
        // 一覧の組み直しはしない（次に開いたときに整理される）。
        // 代わりに、束の見出しの件数だけ数え直す。
        foreach (var group in Groups)
        {
            group.RefreshCount();
        }
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
            Groups.Add(new NotificationGroup
            {
                Kind = group.Key,
                KindText = KindLabel(group.Key),
                Description = KindDescription(group.Key),
                Rows = group.ToList(),
            });
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
        NotificationKind.OrphanVariationLink => "消えた種類",
        NotificationKind.VariationBackOnBooth => "復活した種類",
        NotificationKind.PageStructureChanged => "取得できる情報の形式の変化",
        NotificationKind.ArchiveFoundForFolder => "zipを入手した",
        NotificationKind.ItemBackOnBooth => "非公開商品の復活",
        _ => "その他",
    };

    private static string KindDescription(NotificationKind kind) => kind switch
    {
        NotificationKind.ItemUpdated => "取得し直したときに内容が変わっていたものです。",
        NotificationKind.OrphanTag => "タグの管理・属性の管理から消えたか名前が変わったものを、商品がまだ参照しています。",
        NotificationKind.OrphanVariationLink => "手元のファイルや購入の記録が指す種類が、BOOTH側から消えました。",
        NotificationKind.VariationBackOnBooth => "消えていた種類が、BOOTHにまた出てきました。",
        NotificationKind.PageStructureChanged => "BOOTHから取得できる情報の形式が変化した可能性があります。アプリの更新が必要かもしれません。",
        NotificationKind.ArchiveFoundForFolder => "フォルダ登録が役目を終えています。解除しないと容量が二重に数えられます。",
        NotificationKind.ItemBackOnBooth => "非公開と見なしていた商品が、BOOTHでまた見えるようになりました。",
        _ => string.Empty,
    };
}
