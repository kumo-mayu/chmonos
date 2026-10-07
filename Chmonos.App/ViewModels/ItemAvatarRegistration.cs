using Chmonos.Core.Commands;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品ページと編集ページの「アバター」の欄（ユーザ判断 2026-10-07）。
///
/// 作者がカテゴリを3Dキャラクターにしていないアバターは、登録簿に載らず、アバターの管理から扱いを変える場所が無かった。
/// ここで「アバターとして登録」を押すと、人がアバターと指定した物として登録簿に載る（<see cref="UiCommand.RegisterAvatar"/>）。
/// 2つの画面で同じ動きにするため、状態とボタンはこの1つにまとめる。押した時にすぐ登録簿へ書く（編集ページの「保存」とは別）
/// </summary>
public sealed class ItemAvatarRegistration : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private string? _itemId;
    private bool? _override;
    private bool _isAvatar;
    private bool _isBusy;
    private string _notice = string.Empty;

    public ItemAvatarRegistration(AppServiceContainer services, MainViewModel main, string? itemId = null)
    {
        _services = services;
        _main = main;
        RegisterCommand = new RelayCommand(() => RunAsync(id => new UiCommand.RegisterAvatar(id)).Forget(), () => CanRegister);
        UnregisterCommand = new RelayCommand(() => RunAsync(id => new UiCommand.SetAvatarOverride(id, null)).Forget(), () => CanUnregister);
        OpenInAvatarsCommand = new RelayCommand(() =>
        {
            if (_itemId is { } id)
            {
                _main.ShowAvatar(id);
            }
        }, () => _isAvatar);
        Show(itemId);
    }

    /// <summary>手で「アバターとして登録」した（人の指定でアバターになっている）か。</summary>
    public bool IsManual => _isAvatar && _override == true;

    /// <summary>アバターとして扱われているか（自動の判定でも、人の指定でも）。</summary>
    public bool IsAvatar => _isAvatar;

    public bool CanRegister => _itemId is not null && !_isAvatar && !_isBusy;

    public bool CanUnregister => IsManual && !_isBusy;

    /// <summary>今の扱いの1行。アバターでなければ空（ボタンだけを出す）。</summary>
    public string StateText => !_isAvatar
        ? _override == false ? "アバターとして扱わないよう指定しています。" : string.Empty
        : IsManual ? "手で登録したアバターです。" : "アバターとして扱っています。";

    /// <summary>押した結果が失敗だったときの1行（成功は扱いの1行で分かるので出さない）。</summary>
    public string Notice
    {
        get => _notice;
        private set => SetField(ref _notice, value);
    }

    public RelayCommand RegisterCommand { get; }

    public RelayCommand UnregisterCommand { get; }

    public RelayCommand OpenInAvatarsCommand { get; }

    /// <summary>表示する商品を替える（編集ページで次の商品へ進んだとき）。登録簿は読むだけなので直に読む。</summary>
    public void Show(string? itemId)
    {
        _itemId = itemId;
        Notice = string.Empty;
        var entry = itemId is null
            ? null
            : _services.Store.Avatars.Load().Entries.FirstOrDefault(candidate => candidate.ItemId == itemId);
        _override = entry?.AvatarOverride;
        _isAvatar = entry is not null && AvatarService.IsAvatar(entry);
        Raise();
    }

    private async Task RunAsync(Func<string, UiCommand> command)
    {
        if (_itemId is not { } id || _isBusy)
        {
            return;
        }

        _isBusy = true;
        Raise();
        try
        {
            var result = await _services.Commands.ExecuteAsync(command(id));
            Notice = result is CommandResult.Failed failed ? failed.Message : string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 書けなかったことを言う（外部の点検 2026-10-07。前はログに残るだけで、押しても何も起きないように見えた）
            Core.Diagnostics.AppLog.Error("アバターとして登録する", exception);
            Notice = "登録簿に書けませんでした。" + FailureText.Cause(exception);
        }
        finally
        {
            _isBusy = false;
            if (_itemId == id)
            {
                var notice = Notice;
                Show(id);
                Notice = notice;
            }
        }

        // 検索の「対応アバター」の候補・アバターの一覧は登録簿から作るので、組み直させる
        _main.Search.RefreshFacets();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(IsAvatar));
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(CanRegister));
        OnPropertyChanged(nameof(CanUnregister));
        OnPropertyChanged(nameof(StateText));
        RelayCommand.RaiseCanExecuteChanged();
    }
}
