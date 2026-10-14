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
    }

    private void OnApiStateChanged() => RunOnUiThread(() => OnPropertyChanged(nameof(ApiStatusText)));

    /// <summary>API が動いているか（1行）。始められなかったときは、次にすることを言う。</summary>
    public string ApiStatusText => _services.Api.State switch
    {
        ApiRunState.Running => $"ポート {_services.Api.Port} で動いています。",
        ApiRunState.Starting => "起動しています…",
        ApiRunState.Failed => $"ポート {_services.Api.Port} で待ち受けられませんでした。ほかのアプリが使っているときは、ポートを変えてください。",
        _ => "止まっています。",
    };

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

    /// <summary>オン・オフとポートは、保存できたらすぐ API に当てる（許す住所は API が設定のファイルを見て読み直す）。</summary>
    internal async Task SaveApiAndApplyAsync(Func<ApiSettings, ApiSettings> change)
    {
        if (await SaveApiAsync(change))
        {
            await _services.Api.ApplyAsync();
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
