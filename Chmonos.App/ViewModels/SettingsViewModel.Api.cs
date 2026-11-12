using System.Collections.ObjectModel;
using System.IO;
using Chmonos.App.Services;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 設定の「API」の節（外から使う API の設定。docs/spec/local-api.md・2026-10-12）。
/// 書くのは api-settings.json だけ（<see cref="Core.Commands.UiCommand.ChangeApiSettings"/>）。書いたらすぐ API に当てる（<see cref="ApiRunner.ApplyAsync"/>。
/// ユーザ判断 2026-10-13：オンにしたらすぐ動かし、オフですぐ止める。ポートを変えたらその場で待ち受け直す）。
/// ポートなどの考えが要る開発者向けの機能なので、節は既定で畳む（ユーザ判断 2026-10-12「少し難しい話だと見てわかるようにした方が良い」）。
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>この節だけは既定で畳む（ほかの節は既定で開く）。</summary>
    public bool SectionApiOpen
    {
        get => s_sectionOpen.TryGetValue(nameof(SectionApiOpen), out var open) && open;
        set
        {
            if (SectionApiOpen != value)
            {
                s_sectionOpen[nameof(SectionApiOpen)] = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _apiEnabled;

    /// <summary>API を使うか（<see cref="ApiSettings.Enabled"/>。既定はオフ）。</summary>
    public bool ApiEnabled
    {
        get => _apiEnabled;
        set
        {
            if (SetField(ref _apiEnabled, value))
            {
                SaveApiAndApplyAsync(settings => settings with { Enabled = value }).Forget();
            }
        }
    }

    private bool _apiAllowWrites;

    /// <summary>
    /// 外から書き込みを許すか（<see cref="ApiSettings.AllowWrites"/>。既定はオフ）。API は設定のファイルを見て読み直すので、保存するだけでその場で効く
    /// </summary>
    public bool ApiAllowWrites
    {
        get => _apiAllowWrites;
        set
        {
            if (SetField(ref _apiAllowWrites, value))
            {
                SaveApiAsync(settings => settings with { AllowWrites = value }).Forget();
            }
        }
    }

    private int _apiPort = ApiSettings.DefaultPort;

    /// <summary>API が待つポート。1024 より下は Windows の決まったサービスが使う番号なので受けない。</summary>
    public int ApiPort
    {
        get => _apiPort;
        set
        {
            var clamped = Clamped(value, 1024, 65535, "APIのポート", string.Empty, nameof(ApiPort));
            if (SetField(ref _apiPort, clamped))
            {
                SaveApiAndApplyAsync(settings => settings with { Port = clamped }).Forget();
            }
            else if (clamped != value)
            {
                OnPropertyChanged();
            }
        }
    }

    private bool _apiImportOnHeadlessLaunch;

    /// <summary>画面を出さずに起動したとき、監視フォルダの新着と前回の続きを取り込むか（<see cref="ApiSettings.ImportOnHeadlessLaunch"/>）。</summary>
    public bool ApiImportOnHeadlessLaunch
    {
        get => _apiImportOnHeadlessLaunch;
        set
        {
            if (SetField(ref _apiImportOnHeadlessLaunch, value))
            {
                SaveApiAsync(settings => settings with { ImportOnHeadlessLaunch = value }).Forget();
            }
        }
    }

    /// <summary>許可する Web ページ（CORS を許可するオリジン）。</summary>
    public ObservableCollection<ApiOriginRow> ApiOrigins { get; } = [];

    private string _newApiOrigin = string.Empty;

    public string NewApiOrigin
    {
        get => _newApiOrigin;
        set
        {
            if (SetField(ref _newApiOrigin, value ?? string.Empty))
            {
                ApiOriginNotice = string.Empty;
            }
        }
    }

    private string _apiOriginNotice = string.Empty;

    /// <summary>追加できなかった理由（欄の下に出す）。</summary>
    public string ApiOriginNotice
    {
        get => _apiOriginNotice;
        private set => SetField(ref _apiOriginNotice, value);
    }

    private RelayCommand? _addApiOrigin;

    public RelayCommand AddApiOriginCommand => _addApiOrigin ??= new RelayCommand(AddApiOrigin);

    /// <summary>画面を開いたときに読む（読むだけなので UiCommand を通さない）。</summary>
    internal void LoadApiSettings()
    {
        var settings = _services.Store.ApiSettings.Load();
        _apiEnabled = settings.Enabled;
        _apiAllowWrites = settings.AllowWrites;
        _apiPort = settings.Port;
        _apiImportOnHeadlessLaunch = settings.ImportOnHeadlessLaunch;
        ApiOrigins.Clear();
        foreach (var origin in settings.AllowedOrigins ?? [])
        {
            ApiOrigins.Add(new ApiOriginRow(origin, RemoveApiOrigin));
        }

        OnPropertyChanged(nameof(ApiEnabled));
        OnPropertyChanged(nameof(ApiAllowWrites));
        OnPropertyChanged(nameof(ApiPort));
        OnPropertyChanged(nameof(ApiImportOnHeadlessLaunch));

        // 状態は API の係が知らせる（裏のスレッドから来る）。画面を離れたら外す（OnLeaving）
        _services.Api.StateChanged += OnApiStateChanged;

        LoadApiClients();
        _services.Api.ClientsChanged += OnApiClientsChanged;
    }

    /// <summary>許しを求めて許したソフト（plan 31）。設定の「つないでいるソフト」。</summary>
    public ObservableCollection<ApiClientRow> ApiClients { get; } = [];

    public bool HasApiClients => ApiClients.Count > 0;

    /// <summary>
    /// 一覧を読む（読むだけなので UiCommand を通さない）。許した・使った日時を書いたと API が知らせたら読み直す。
    /// 中身が同じなら作り直さない（使った日時の書き込みのたびに並びが作り直されて、チェックの途中の手触りが崩れないように）
    /// </summary>
    internal void LoadApiClients()
    {
        ApiClientList list;
        try
        {
            list = _services.Store.ApiClients.Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Core.Diagnostics.AppLog.Error("API の許したソフトの一覧を読む", exception);
            list = new ApiClientList();
        }

        LoadApiBlocked(list);

        // 項目は API の索引と同じ検証を通した物を並べる（手で書き換えた合わない宣言はメニューに出ないので、許した項目としても見せない）
        var rows = (list.Clients ?? [])
            .Where(client => client is not null && !string.IsNullOrEmpty(client.Id))
            .Select(client => (Record: client, Menu: Chmonos.Api.PluginMenus.FromRecords(client.Menu), Pending: (IsApiMenuPendingForShot ?? _services.Api.IsMenuPending)(client.Id)))
            .ToList();
        if (rows.Count == ApiClients.Count && rows.Zip(ApiClients).All(pair => pair.Second.Matches(pair.First.Record, pair.First.Menu, pair.First.Pending)))
        {
            return;
        }

        ApiClients.Clear();
        foreach (var (record, menu, pending) in rows)
        {
            ApiClients.Add(new ApiClientRow(record, ChangeApiClientScope, RevokeApiClient)
            {
                Menu = new ApiClientMenu(record.Id, menu, pending, _expandedApiMenus, () => RemoveApiClientMenuAsync(record.Id).Forget()),
            });
        }

        OnPropertyChanged(nameof(HasApiClients));
    }

    /// <summary>
    /// 項目の一覧を開いているソフト（ID）。行は使った日時を書くたび（10分おき）にも作り直すので、開閉は行の外に持つ
    /// （作り直しで畳まれると、見ている途中に閉じる）。画面を開き直したら畳む（32個まで並ぶので、既定は畳む）
    /// </summary>
    private readonly HashSet<string> _expandedApiMenus = new(StringComparer.Ordinal);

    /// <summary>
    /// 聞き直しを待っているかの差し替え（撮る台。台は通信を塞ぐので API を動かせず、「待っています」の姿を作れない）。null なら動いている API に聞く
    /// </summary>
    internal Func<string, bool>? IsApiMenuPendingForShot { get; set; }

    /// <summary>
    /// 項目だけを外す（宣言を空にする。ソフトのトークン・書き込みの種類は残す）。確かめの窓は出さない：
    /// ソフトが次に宣言を送れば、また許しの窓で聞く（取り返しがつく）。消えるだけの変更なので、ソフトの宣言と同じく聞かずに効かせる
    /// </summary>
    private async Task RemoveApiClientMenuAsync(string clientId)
    {
        _expandedApiMenus.Remove(clientId);

        // 欄ごと書かない（空の配列にしない）。宣言の無いソフトと同じ形にする（ApiClientBook.SetMenuAsync と同じ）
        var saved = await SaveApiClientsAsync(list => list with
        {
            Clients = list.Clients.Select(client => client.Id != clientId ? client : client with { Menu = null }).ToList(),
        });

        // 書いた後の値で出し直す（API が止まっていると、読み直しの知らせが来ない）
        if (saved)
        {
            LoadApiClients();
        }
    }

    private void OnApiClientsChanged() => RunOnUiThread(LoadApiClients);

    /// <summary>拒否が続いて止めているソフト（2026-11-11）。設定の「止めているソフト」。</summary>
    public ObservableCollection<ApiBlockedRow> ApiBlockedClients { get; } = [];

    public bool HasApiBlockedClients => ApiBlockedClients.Count > 0;

    private int _apiRefusalLimit = ApiClientList.DefaultRefusalLimit;

    /// <summary>
    /// 続けて拒否したら止める回数（0 なら数えず、止めもしない）。上限の10は、それより多く拒否を押す人は止めを使わないのと同じで、
    /// 大きな数を打ち間違えて事実上止まらないのに気付かない、を避けるため
    /// </summary>
    public int ApiRefusalLimit
    {
        get => _apiRefusalLimit;
        set
        {
            var clamped = Clamped(value, 0, 10, "止めるまでの拒否の回数", " 回", nameof(ApiRefusalLimit));
            if (SetField(ref _apiRefusalLimit, clamped))
            {
                SaveRefusalLimitAsync(clamped).Forget();
            }
            else if (clamped != value)
            {
                OnPropertyChanged();
            }
        }
    }

    private async Task SaveRefusalLimitAsync(int limit)
    {
        // 0 は「覚えない」：今の止めと数えた回数も消す（0 は今までと同じに毎回聞く形。止めの記録を残すと、上限を戻したときに人が見ていない止めが生き返る）
        var saved = await SaveApiClientsAsync(list => list with
        {
            RefusalLimit = limit,
            Refusals = limit == 0 ? [] : list.Refusals ?? [],
        });

        // 一覧を書いた後の値で出し直す（API が止まっていると、読み直しの知らせが来ない）
        if (saved)
        {
            LoadApiClients();
        }
    }

    /// <summary>止めているソフトの一覧と上限を、控えから出し直す（同じなら作り直さない）。</summary>
    private void LoadApiBlocked(ApiClientList list)
    {
        if (_apiRefusalLimit != list.RefusalLimit)
        {
            _apiRefusalLimit = Math.Max(0, list.RefusalLimit);
            OnPropertyChanged(nameof(ApiRefusalLimit));
        }

        // 回数が上限に届いていない（1〜2回拒否した）ソフトは出さない：まだ窓は出るので、人が外す物が無い。数だけ控えに覚える
        var blocked = list.RefusalLimit <= 0
            ? []
            : (list.Refusals ?? []).Where(refusal => refusal is not null && refusal.BlockedAt is not null && !string.IsNullOrEmpty(refusal.Name)).ToList();
        if (blocked.Count == ApiBlockedClients.Count && blocked.Zip(ApiBlockedClients).All(pair => pair.Second.Record == pair.First))
        {
            return;
        }

        ApiBlockedClients.Clear();
        foreach (var refusal in blocked)
        {
            ApiBlockedClients.Add(new ApiBlockedRow(refusal, UnblockApiClient));
        }

        OnPropertyChanged(nameof(HasApiBlockedClients));
    }

    /// <summary>止めを外す：回数も0に戻す（記録ごと消す）。次の求めでまた窓が出る。確かめは出さない（取り返しがつく：また拒否すれば止まる）。</summary>
    private void UnblockApiClient(ApiBlockedRow row)
    {
        ApiBlockedClients.Remove(row);
        OnPropertyChanged(nameof(HasApiBlockedClients));
        var record = row.Record;
        SaveApiClientsAsync(list => list with
        {
            Refusals = (list.Refusals ?? []).Where(refusal => refusal is not null && !refusal.IsSame(record.Name, record.ExePath, record.Origin)).ToList(),
        }).Forget();
    }

    /// <summary>種類のチェックを変えたら、そのソフトの権限を書き換え、動いている API にすぐ読み直させる。</summary>
    private void ChangeApiClientScope(ApiClientRow row, ApiScopeChoice choice)
        => SaveApiClientsAsync(list => list with
        {
            Clients = list.Clients.Select(client => client.Id != row.Id
                ? client
                : client with
                {
                    Scopes = choice.IsChecked
                        ? ApiScopes.All.Where(scope => scope == choice.Scope || client.Scopes.Contains(scope)).ToList()
                        : client.Scopes.Where(scope => scope != choice.Scope).ToList(),
                }).ToList(),
        }).Forget();

    /// <summary>取り消しの確かめ（試験で差し替える）。true なら取り消す。</summary>
    internal Func<ApiClientRow, bool> ConfirmRevokeApiClient { get; set; } = row => Services.Notice.Show(
        $"「{row.Name}」の許可を取り消します。\n\n"
        + "このソフトは、もう一度許可を求めて許可されるまでAPIを使えません。元に戻せません。",
        "許可の取り消し",
        System.Windows.MessageBoxButton.OKCancel,
        System.Windows.MessageBoxImage.Question,
        System.Windows.MessageBoxResult.Cancel) == System.Windows.MessageBoxResult.OK;

    private void RevokeApiClient(ApiClientRow row)
    {
        if (!ConfirmRevokeApiClient(row))
        {
            return;
        }

        ApiClients.Remove(row);
        OnPropertyChanged(nameof(HasApiClients));
        SaveApiClientsAsync(list => list with { Clients = list.Clients.Where(client => client.Id != row.Id).ToList() }).Forget();
    }

    internal async Task<bool> SaveApiClientsAsync(Func<ApiClientList, ApiClientList> change)
    {
        try
        {
            await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeApiClients(change));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = "APIの設定を保存できませんでした。保存先のフォルダに書き込めるかを確かめてください。";
            return false;
        }

        // 取り消し・権限の変更は、見張りの知らせを待たずに効かせる
        _services.Api.ReloadClients();
        return true;
    }

    private void OnApiStateChanged() => RunOnUiThread(() =>
    {
        OnPropertyChanged(nameof(ApiStatusText));

        // 止めると聞いている列も捨てるので、「許可を待っています」を出し直す（中身が同じなら作り直さない）
        LoadApiClients();
    });

    /// <summary>API が動いているか（1行）。始められなかったときは、次にすることを言う。</summary>
    public string ApiStatusText => _services.Api.State switch
    {
        ApiRunState.Running => $"ポート {_services.Api.Port} で動いています。",
        ApiRunState.Starting => "起動しています…",
        ApiRunState.Failed => $"ポート {_services.Api.Port} で待ち受けられませんでした。ほかのアプリが使っているときは、ポートを変えてください。",
        _ => "止まっています。",
    };

    /// <summary>
    /// <c>http://localhost:5173</c> の形（http か https・パスとクエリの無いオリジン）だけを受け、ブラウザが送る形にそろえる。
    /// 読めなければ null。
    /// </summary>
    internal static string? NormalizeOrigin(string text)
        => Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;

    private void AddApiOrigin()
    {
        if (NormalizeOrigin(_newApiOrigin) is not { } origin)
        {
            ApiOriginNotice = "http://localhost:5173 の形で入力してください。";
            return;
        }

        if (ApiOrigins.Any(row => string.Equals(row.Origin, origin, StringComparison.OrdinalIgnoreCase)))
        {
            ApiOriginNotice = "既にあります。";
            return;
        }

        ApiOrigins.Add(new ApiOriginRow(origin, RemoveApiOrigin));
        NewApiOrigin = string.Empty;
        SaveApiAsync(settings => settings with
        {
            AllowedOrigins = settings.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)
                ? settings.AllowedOrigins
                : [.. settings.AllowedOrigins, origin],
        }).Forget();
    }

    private void RemoveApiOrigin(ApiOriginRow row)
    {
        ApiOrigins.Remove(row);
        SaveApiAsync(settings => settings with
        {
            AllowedOrigins = settings.AllowedOrigins.Where(origin => !string.Equals(origin, row.Origin, StringComparison.OrdinalIgnoreCase)).ToList(),
        }).Forget();
    }

    internal async Task<bool> SaveApiAsync(Func<ApiSettings, ApiSettings> change)
    {
        try
        {
            await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeApiSettings(change));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = "APIの設定を保存できませんでした。保存先のフォルダに書き込めるかを確かめてください。";
            return false;
        }
    }

    private string _apiTokenNotice = string.Empty;

    /// <summary>トークンを作り直した後の1行（外のツールに次にしてもらうこと）。</summary>
    public string ApiTokenNotice
    {
        get => _apiTokenNotice;
        private set => SetField(ref _apiTokenNotice, value);
    }

    private RelayCommand? _renewApiToken;

    /// <summary>
    /// トークンを作り直す（前の Chmonos.Api.exe の --new-token の代わり。ユーザ判断 2026-10-13）。
    /// トークンが漏れたかもしれないときに、今のトークンを使えなくする。動いていれば新しいトークンで待ち受け直す
    /// </summary>
    public RelayCommand RenewApiTokenCommand => _renewApiToken ??= new RelayCommand(() => RenewApiTokenAsync().Forget());

    internal async Task RenewApiTokenAsync()
    {
        try
        {
            await _services.Api.RenewTokenAsync();
            ApiTokenNotice = "トークンを作り直しました。ツールには、保存先の api.json から読み直させてください。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Core.Diagnostics.AppLog.Error("API のトークンを作り直す", exception);
            ApiTokenNotice = "トークンを作り直せませんでした。保存先のフォルダに書き込めるかを確かめてください。";
        }
    }

    /// <summary>オン・オフとポートは、保存できたらすぐ API に当てる（許可するオリジンは API が設定のファイルを見て読み直す）。</summary>
    internal async Task SaveApiAndApplyAsync(Func<ApiSettings, ApiSettings> change)
    {
        if (await SaveApiAsync(change))
        {
            await _services.Api.ApplyAsync();
        }
    }

}

/// <summary>
/// つないでいるソフトの1行（名前・exe かオリジン・最後に使った日時・書き込みの種類のチェック・取り消す）。
/// </summary>
public sealed class ApiClientRow
{
    private readonly ApiClientRecord _record;

    public ApiClientRow(ApiClientRecord record, Action<ApiClientRow, ApiScopeChoice> scopeChanged, Action<ApiClientRow> revoke)
    {
        _record = record;
        Scopes = ApiScopes.Writes.Select(scope => new ApiScopeChoice(scope, record.Scopes?.Contains(scope) == true, choice => scopeChanged(this, choice))).ToList();
        Watches = ApiScopes.Watches.Select(scope => new ApiScopeChoice(scope, record.Scopes?.Contains(scope) == true, choice => scopeChanged(this, choice))).ToList();
        RevokeCommand = new RelayCommand(() => revoke(this));
    }

    public string Id => _record.Id;

    public string Name => _record.Name;

    /// <summary>実行ファイル（確かめられなかったときはそう書く）。ブラウザのページならオリジンも添える。</summary>
    public string WhereText => _record.Origin is { } origin
        ? $"{_record.ExePath ?? "実行ファイルは確かめられませんでした"}\n{origin}"
        : _record.ExePath ?? "実行ファイルは確かめられませんでした";

    public string LastUsedText => _record.LastUsedAt is { } used
        ? $"最後に使った日時：{used.ToLocalTime():yyyy/MM/dd HH:mm}"
        : "まだ使っていません";

    public IReadOnlyList<ApiScopeChoice> Scopes { get; }

    /// <summary>知らせる操作のチェック（plan 44。全部の種類を並べ、許している物にチェック。外すとその場で購読も止まる）。</summary>
    public IReadOnlyList<ApiScopeChoice> Watches { get; }

    public RelayCommand RevokeCommand { get; }

    /// <summary>許した項目と、聞き直しを待っているか（plan 44 の②）。</summary>
    public ApiClientMenu Menu { get; init; } = ApiClientMenu.None;

    /// <summary>同じ記録・同じ項目・同じ聞き直しの状態を映しているか（作り直すかを決める）。</summary>
    internal bool Matches(ApiClientRecord record, IReadOnlyList<Chmonos.Query.PluginMenuItemRef> menu, bool pending)
        => Menu.IsPending == pending && Chmonos.Api.PluginMenus.Same(Menu.Items, menu)
            && (_record == record
                || (_record.Id == record.Id && _record.Name == record.Name && _record.ExePath == record.ExePath && _record.Origin == record.Origin
                    && _record.LastUsedAt == record.LastUsedAt && (_record.Scopes ?? []).SequenceEqual(record.Scopes ?? [])));
}

/// <summary>
/// つないでいるソフトが許された、メニューに足す項目（plan 44 の②・docs/spec/plugins-menu.md）。許し直しの窓と同じ行の形で並べ
/// （名前は下の段なら「親 ▸ 子」・表示する場所・渡す情報）、32個まであるので畳む。聞き直しを待っていれば、そう言う
/// </summary>
public sealed class ApiClientMenu
{
    private readonly string _clientId;
    private readonly ISet<string> _expanded;

    public ApiClientMenu(string clientId, IReadOnlyList<Chmonos.Query.PluginMenuItemRef> items, bool pending, ISet<string> expanded, Action remove)
    {
        _clientId = clientId;
        _expanded = expanded;
        Items = items;
        IsPending = pending;

        // 押せる項目ごとに1行（子を持つ項目は段を開くだけなので行にしない。許し直しの窓の「追加する項目」と同じ数え方）
        Lines = Chmonos.Api.PluginMenus.Diff([], items).Added.Select(line => PluginMenuApprovalViewModel.Line(line, withNeeds: true)).ToList();
        RemoveCommand = new RelayCommand(remove);
    }

    /// <summary>宣言の無いソフト（何も出さない）。</summary>
    public static ApiClientMenu None { get; } = new(string.Empty, [], false, new HashSet<string>(), () => { });

    internal IReadOnlyList<Chmonos.Query.PluginMenuItemRef> Items { get; }

    public IReadOnlyList<PluginMenuApprovalLine> Lines { get; }

    public bool HasItems => Lines.Count > 0;

    public string Title => $"メニューの項目（{Lines.Count}）";

    /// <summary>項目の宣言を人に聞いている（窓を出す前の列にある・窓を出している）。</summary>
    public bool IsPending { get; }

    /// <summary>
    /// 聞き直しを待っている知らせ。前に許した項目があれば、許すまでは今の項目のまま出すことも言う
    /// （一覧が古いのか新しいのかを取り違えないように）
    /// </summary>
    public string PendingText => !IsPending
        ? string.Empty
        : HasItems ? "項目の変更の許可を待っています。許可するまでは今の項目のままです。" : "メニューの項目の許可を待っています。";

    public bool IsVisible => HasItems || IsPending;

    /// <summary>開いているか（行を作り直しても保つ。<see cref="SettingsViewModel"/> の集合に持つ）。</summary>
    public bool IsExpanded
    {
        get => _expanded.Contains(_clientId);
        set
        {
            if (value)
            {
                _expanded.Add(_clientId);
            }
            else
            {
                _expanded.Remove(_clientId);
            }
        }
    }

    /// <summary>項目だけを外す（トークンと書き込みの種類は残す）。</summary>
    public RelayCommand RemoveCommand { get; }
}

/// <summary>止めているソフトの1行（名前・exe かオリジン・止めた日時・止めを外す）。</summary>
public sealed class ApiBlockedRow
{
    public ApiBlockedRow(ApiClientRefusal record, Action<ApiBlockedRow> unblock)
    {
        Record = record;
        UnblockCommand = new RelayCommand(() => unblock(this));
    }

    internal ApiClientRefusal Record { get; }

    public string Name => Record.Name;

    /// <summary>つないでいるソフトの行と同じ書き方。</summary>
    public string WhereText => Record.Origin is { } origin
        ? $"{Record.ExePath ?? "実行ファイルは確かめられませんでした"}\n{origin}"
        : Record.ExePath ?? "実行ファイルは確かめられませんでした";

    public string BlockedText => $"止めた日時：{(Record.BlockedAt ?? Record.LastRefusedAt).ToLocalTime():yyyy/MM/dd HH:mm}";

    public RelayCommand UnblockCommand { get; }
}

/// <summary>許可する Web ページの1行。</summary>
public sealed class ApiOriginRow
{
    public ApiOriginRow(string origin, Action<ApiOriginRow> remove)
    {
        Origin = origin;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Origin { get; }

    public RelayCommand RemoveCommand { get; }
}
