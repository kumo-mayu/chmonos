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
    public required string KindText { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<NotificationRow> Rows { get; init; }

    public int UnreadCount => Rows.Count(row => !row.IsRead);

    public string CountText => UnreadCount > 0
        ? $"{Rows.Count} 件（未読 {UnreadCount}）"
        : $"{Rows.Count} 件";

    /// <summary>
    /// 既読にしても行は消さない方針なので、束の側の件数は自分で数え直す必要がある。
    /// ここが黙って古いままだと「未読 1」と出たまま未読が無い、という嘘になる。
    /// </summary>
    public void RefreshCount()
    {
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(CountText));
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

        MarkAllReadCommand = new RelayCommand(() => _ = MarkAllReadAsync(), () => UnreadCount > 0);
        RefreshCommand = new RelayCommand(() => _ = ReloadAsync());

        _ = ReloadAsync();
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
        };

        row.ReadChanged += OnRowReadChanged;
        row.ToggleReadCommand = new RelayCommand(() => row.IsRead = !row.IsRead);
        row.OpenItemCommand = new RelayCommand(() => _ = OpenItemAsync(row.ItemId), () => row.HasItem);

        return row;
    }

    private void OnRowReadChanged(NotificationRow row)
    {
        _ = _services.Commands.ExecuteAsync(new UiCommand.SetNotificationRead(row.Record.Id, row.IsRead));

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
        }
    }

    private void Rebuild()
    {
        var rows = _unreadOnly ? _all.Where(row => !row.IsRead).ToList() : _all;

        Groups.Clear();
        foreach (var group in rows
            .GroupBy(row => row.Record.Kind)
            .OrderBy(group => group.Key))
        {
            Groups.Add(new NotificationGroup
            {
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
        NotificationKind.ItemUpdated => "商品ページが変わった",
        NotificationKind.AvatarNeedsCheck => "対応アバターの確認",
        NotificationKind.DuplicateFile => "同じ中身のファイル",
        NotificationKind.OrphanTag => "一覧に無い分類",
        NotificationKind.OrphanVariationLink => "消えたバリエーション",
        NotificationKind.PageStructureChanged => "BOOTHの構造変化",
        NotificationKind.ArchiveFoundForFolder => "zipが手元に入った",
        NotificationKind.ItemBackOnBooth => "BOOTHに戻ってきた",
        _ => "その他",
    };

    private static string KindDescription(NotificationKind kind) => kind switch
    {
        NotificationKind.ItemUpdated => "取得し直したときに内容が変わっていたものです。",
        NotificationKind.AvatarNeedsCheck => "推定した対応アバターの確認待ちです。",
        NotificationKind.DuplicateFile => "同じ中身が複数の場所にありました。容量は1回だけ数えています。",
        NotificationKind.OrphanTag => "一覧から消えたか名前が変わった分類を、商品がまだ参照しています。",
        NotificationKind.OrphanVariationLink => "紐付けていたバリエーションがBOOTH側から消えました。",
        NotificationKind.PageStructureChanged => "説明文の読み取りが効かなくなっている可能性があります。",
        NotificationKind.ArchiveFoundForFolder => "フォルダ登録が役目を終えています。解除しないと容量が二重に数えられます。",
        NotificationKind.ItemBackOnBooth => "非公開と見なしていた商品が、BOOTHでまた見えるようになりました。",
        _ => string.Empty,
    };
}
