using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 設定の「API」の節（外から使う API の設定。docs/spec/local-api.md・2026-10-12）。
/// 書くのは api-settings.json だけ（<see cref="Core.Commands.UiCommand.ChangeApiSettings"/>）。api.json は API が書くので、ここでは読むだけ。
/// ポートなどの考えが要る開発者向けの機能なので、節は既定で畳む（ユーザ判断 2026-10-12「少し難しい話だと見てわかるようにした方が良い」）。
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>API の実行ファイル。アプリと同じフォルダに置いて配る。</summary>
    internal static string ApiExecutablePath => Path.Combine(AppContext.BaseDirectory, "Chmonos.Api.exe");

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
                SaveApiAsync(settings => settings with { Port = clamped }).Forget();
                OnPropertyChanged(nameof(ApiRestartNote));
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

    /// <summary>許可する Web ページ（CORS を許す住所）。</summary>
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

    private ApiEndpointRecord? _apiEndpoint;
    private bool _apiRunning;

    /// <summary>API が動いているか（1行）。</summary>
    public string ApiStatusText => _apiRunning && _apiEndpoint is { } endpoint
        ? $"ポート {endpoint.Port} で動いています。"
        : "止まっています。";

    /// <summary>動いている API と違うポートにしたとき。</summary>
    public string ApiRestartNote => _apiRunning && _apiEndpoint is { } endpoint && endpoint.Port != _apiPort
        ? "変えたポートは、APIを起動し直すと使われます。"
        : string.Empty;

    public bool CanStartApi => !_apiRunning && !_apiStarting && File.Exists(ApiExecutablePath);

    /// <summary>
    /// 押してから、API が動いていると分かるまで（最大5秒）。その間は押せなくする（前は押せたままで、2回押すと2つ目の窓がすぐエラーで閉じた）
    /// </summary>
    private bool _apiStarting;

    /// <summary>起動の吹き出し。押せないときは理由を言う。</summary>
    public string StartApiTip => _apiRunning
        ? "APIは動いています。"
        : File.Exists(ApiExecutablePath)
            ? "APIを別の窓で起動します。止めるときは、その窓を閉じます。"
            : "Chmonos.Api.exeが見つかりません。";

    private RelayCommand? _startApi;

    public RelayCommand StartApiCommand => _startApi ??= new RelayCommand(() => StartApiAsync().Forget(), () => CanStartApi);

    /// <summary>画面を開いたときに読む（読むだけなので UiCommand を通さない）。</summary>
    internal void LoadApiSettings()
    {
        var settings = _services.Store.ApiSettings.Load();
        _apiPort = settings.Port;
        _apiImportOnHeadlessLaunch = settings.ImportOnHeadlessLaunch;
        ApiOrigins.Clear();
        foreach (var origin in settings.AllowedOrigins ?? [])
        {
            ApiOrigins.Add(new ApiOriginRow(origin, RemoveApiOrigin));
        }

        OnPropertyChanged(nameof(ApiPort));
        OnPropertyChanged(nameof(ApiImportOnHeadlessLaunch));
        RefreshApiStatus();
    }

    /// <summary>
    /// api.json と、この保存先の api.lock を見る。動いているかは錠で決める：api.json の番号だけで見ると、
    /// API を動かしたまま引越した先で、古い保存先の API を「動いています」と取り違えた（点検35）
    /// </summary>
    internal void RefreshApiStatus()
    {
        try
        {
            _apiEndpoint = File.Exists(_services.Paths.ApiFile) ? JsonStore.Read<ApiEndpointRecord>(_services.Paths.ApiFile) : null;
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            _apiEndpoint = null;
        }

        _apiRunning = _apiEndpoint?.ProcessId is not null && SingleInstanceLock.IsHeld(_services.Paths.ApiLockFile);
        if (_apiRunning && !_watchingApi)
        {
            WatchApiAsync().Forget();
        }

        OnPropertyChanged(nameof(ApiStatusText));
        OnPropertyChanged(nameof(ApiRestartNote));
        OnPropertyChanged(nameof(CanStartApi));
        OnPropertyChanged(nameof(StartApiTip));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private bool _watchingApi;

    /// <summary>
    /// 動いている間は、止まったかを見直し続ける。API は別の窓を閉じて止めるので、知らせが来ない
    /// （前は起動を確かめた所で見るのをやめ、止めても画面を開き直すまで「起動」を押せなかった。点検35）。
    /// 錠を見るだけなので軽い。止まったと分かるか、画面を離れたら終わる
    /// </summary>
    private async Task WatchApiAsync()
    {
        _watchingApi = true;
        try
        {
            while (_apiRunning)
            {
                await Task.Delay(ApiWatchInterval, _leaving.Token);
                RefreshApiStatus();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _watchingApi = false;
        }
    }

    /// <summary>止まったかを見直す間合い。止めてからボタンが戻るまでの待ちで、人が気にならない長さ。</summary>
    internal static TimeSpan ApiWatchInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// <c>http://localhost:5173</c> の形（http か https・場所と問い合わせの無い住所）だけを受け、ブラウザが送る形にそろえる。
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

    internal async Task SaveApiAsync(Func<ApiSettings, ApiSettings> change)
    {
        try
        {
            await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeApiSettings(change));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = "APIの設定を保存できませんでした。保存先のフォルダに書き込めるかを確かめてください。";
        }
    }

    /// <summary>
    /// API を別の窓（コンソール）で起動する。止めるのはその窓を閉じる・Ctrl+C（起動は本体と連動させない。ユーザ判断 2026-10-12）。
    /// 起動して api.json を書き終えるまで少しかかるので、数秒だけ状態を見直す。
    /// </summary>
    private async Task StartApiAsync()
    {
        _apiStarting = true;
        RefreshApiStatus();
        try
        {
            try
            {
                Process.Start(new ProcessStartInfo(ApiExecutablePath) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
            {
                Status = "APIを起動できませんでした。";
                return;
            }

            for (var attempt = 0; attempt < 20 && !_apiRunning; attempt++)
            {
                await Task.Delay(250);
                RefreshApiStatus();
            }
        }
        finally
        {
            _apiStarting = false;
            RefreshApiStatus();
        }
    }
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
