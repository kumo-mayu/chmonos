namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 主画面：カードの右クリックで非表示にした商品の知らせと、その場での解除（点検 2026-09-23・動線の点検 B2）。
///
/// 非表示は設定の「非表示にした商品」から戻せる＝取り返しがつくので、窓で聞かない（戻せるものまで聞くと、
/// 聞かれること自体が読み飛ばされる。ui-dialogs.md「何回聞くか」）。ただ前は黙ってカードが消えるだけで、
/// 戻し方が画面のどこにも出ていなかった。登録の知らせと同じ帯に、戻す場所と「非表示を解除」を出す。
///
/// 続けて非表示にした分は束ねる（1件ごとに差し替えると、押したかった1件が押す前に消える）。
/// 解除は束ねた全部に効く。押すか閉じると次からまた数え直す。
/// </summary>
public sealed partial class MainViewModel
{
    private readonly List<(string Id, string Name)> _hidden = [];
    private RelayCommand? _unhideRecent;
    private RelayCommand? _dismissHidden;

    public bool HasHiddenNotice => _hidden.Count > 0;

    public string HiddenNoticeText => _hidden.Count switch
    {
        0 => string.Empty,
        1 => $"「{_hidden[0].Name}」を非表示にしました。設定の「非表示にした商品」から戻せます。",
        _ => $"「{_hidden[0].Name}」ほか {_hidden.Count - 1} 件を非表示にしました。設定の「非表示にした商品」から戻せます。",
    };

    public RelayCommand UnhideRecentCommand => _unhideRecent ??= new RelayCommand(() => UnhideRecentAsync().Forget());

    public RelayCommand DismissHiddenCommand => _dismissHidden ??= new RelayCommand(() => SetHidden(clear: true));

    /// <summary>非表示を書けたあとに呼ぶ（書けなかったときは呼ばない。していないことを「しました」と言わない）。</summary>
    internal void NoteHidden(Core.Models.ItemRecord item)
    {
        if (_hidden.All(entry => entry.Id != item.Id))
        {
            _hidden.Add((item.Id, item.DisplayName));
        }

        SetHidden(clear: false);
    }

    private async Task UnhideRecentAsync()
    {
        var ids = _hidden.Select(entry => entry.Id).ToList();
        SetHidden(clear: true);

        foreach (var id in ids)
        {
            await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.UnhideItem(id));
        }

        // 検索は一覧を読み直せば戻る。フォルダビュー・ショップの一覧はその場で外しただけなので、
        // 開き直すまでは戻らない（その画面の一覧を作り直す道が画面ごとに違い、ここから触ると持ち主が割れる）
        Search.ReloadAsync().Forget();
    }

    private void SetHidden(bool clear)
    {
        if (clear)
        {
            _hidden.Clear();
        }

        OnPropertyChanged(nameof(HasHiddenNotice));
        OnPropertyChanged(nameof(HiddenNoticeText));
    }
}
