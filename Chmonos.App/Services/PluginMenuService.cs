using Chmonos.Api;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Services;

/// <summary>
/// ほかのソフトが足した項目（plan 44 の②・docs/spec/plugins-menu.md）を画面に出す係と、押した後の下の帯。
/// 何を出すか・押せるかは列（<see cref="PluginEventHub"/>）が決め、ここは文と帯を持つ。帯は主の窓が1本だけ出す
/// （押した所が右クリックで、知らせる行を持たないため。非表示の帯と同じ置き場）
/// </summary>
public sealed class PluginMenuService : ViewModelBase
{
    private readonly PluginEventHub _hub;
    private readonly Action<Action> _post;

    /// <param name="hub">ソフトへの出来事の列。</param>
    /// <param name="post">画面のスレッドで動かす口（ソフトの結果は API の要求のスレッドから来る）。無ければその場で動かす（試験）。</param>
    public PluginMenuService(PluginEventHub hub, Action<Action>? post = null)
    {
        _hub = hub;
        _post = post ?? (action => action());
        _hub.MenuResult += result => _post(() => ShowResult(result));
        _hub.MenusChanged += Refresh;

        // 作る前に API が始まっていれば、その知らせは過ぎている
        Refresh();
    }

    private bool _hasItemMenus;
    private bool _hasSelectionMenus;

    /// <summary>商品の右クリック・商品ページに出す項目があるか（無ければ「ほかのソフト」自体を出さない）。</summary>
    public bool HasItemMenus
    {
        get => _hasItemMenus;
        private set => SetField(ref _hasItemMenus, value);
    }

    /// <summary>複数選択の帯に出す項目があるか。</summary>
    public bool HasSelectionMenus
    {
        get => _hasSelectionMenus;
        private set => SetField(ref _hasSelectionMenus, value);
    }

    /// <summary>
    /// 出す項目があるかを見直す（許しの控えを読み直した・API が始まった・止まった）。どこから呼ばれてもよい。
    /// 押せるかは開くたびに決めるので、ここでは有無だけ（商品を渡さずに聞く）
    /// </summary>
    public void Refresh() => _post(() =>
    {
        HasItemMenus = _hub.MenusFor(PluginMenus.WhereItem, []).Count > 0;
        HasSelectionMenus = _hub.MenusFor(PluginMenus.WhereSelection, []).Count > 0;
    });

    /// <summary>開いた時点で、出す項目と押せるか。</summary>
    public IReadOnlyList<PluginMenuSoftware> Build(string where, IReadOnlyList<string> itemIds) => _hub.MenusFor(where, itemIds);

    /// <summary>押した：ソフトの列に積み、帯で「送りました」か、送れなかった理由を言う。</summary>
    public async Task PressAsync(PluginMenuSoftware software, PluginMenuEntry entry, string where, IReadOnlyList<string> itemIds)
    {
        var sent = await _hub.SendMenuAsync(software.ClientId, entry.Id, where, itemIds);
        if (sent.Sent)
        {
            _current = (software.ClientId, sent.Seq);
            SetNotice($"{software.Name}に送りました。", failed: false);
        }
        else
        {
            _current = null;
            SetNotice(ReasonText(sent.Reason, software.Name), failed: true);
        }
    }

    /// <summary>帯が今出している送り（ソフトの結果は、これと同じ通番のときだけ文を差し替える）。</summary>
    private (string ClientId, long Seq)? _current;

    /// <summary>
    /// ソフトが結果を返した。帯が出しているのがその送りのときだけ差し替える（続けて押した後に、前の結果で今の知らせを上書きしない）。
    /// 文の前にソフトの名前を付ける：ソフトが書いた文を、Chmonos の言葉と取り違えないように
    /// </summary>
    internal void ShowResult(PluginMenuResult result)
    {
        if (_current is not { } current || current.ClientId != result.ClientId || current.Seq != result.Seq)
        {
            return;
        }

        SetNotice($"{result.Name}：{result.Message}", failed: !result.Ok);
    }

    private string _noticeText = string.Empty;
    private bool _noticeFailed;
    private RelayCommand? _dismissNoticeCommand;

    public bool HasNotice => _noticeText.Length > 0;

    public string NoticeText => _noticeText;

    /// <summary>送れなかった・ソフトがうまくいかなかったと返した（注意の色で出す）。</summary>
    public bool NoticeFailed => _noticeFailed;

    public RelayCommand DismissNoticeCommand => _dismissNoticeCommand ??= new RelayCommand(() => SetNotice(string.Empty, false));

    private void SetNotice(string text, bool failed)
    {
        _noticeText = text;
        _noticeFailed = failed;
        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(NoticeText));
        OnPropertyChanged(nameof(NoticeFailed));
    }

    /// <summary>
    /// 撮る道具（ViewShot）が、API を動かさずに「ほかのソフト」のボタンと帯を出す。API が始まると <see cref="Refresh"/> で本当の値に戻る
    /// </summary>
    internal void Preview(bool hasItemMenus, bool hasSelectionMenus, string? notice = null, bool failed = false)
    {
        HasItemMenus = hasItemMenus;
        HasSelectionMenus = hasSelectionMenus;
        SetNotice(notice ?? string.Empty, failed);
    }

    /// <summary>押せない理由の文（吹き出しと、押した後に送れなかったときの帯）。</summary>
    public static string ReasonText(PluginMenuReason reason, string name) => reason.Block switch
    {
        PluginMenuBlock.NotResponding => $"{name}が応答していません。",
        PluginMenuBlock.Gone => "この項目はもうありません。",
        PluginMenuBlock.TooMany => $"一度に送れるのは{PluginMenus.MaxTargets} 件までです。",
        PluginMenuBlock.NotLoaded => "読み込みの途中です。少し待ってからお試しください。",
        PluginMenuBlock.NoUnityPackage => "unitypackageがある商品で使えます。",
        PluginMenuBlock.HasUnityPackage => "unitypackageが無い商品で使えます。",
        PluginMenuBlock.NotOwned => "ファイルを持っている商品で使えます。",
        PluginMenuBlock.Owned => "ファイルを持っていない商品で使えます。",
        PluginMenuBlock.NoAvatar => "対応アバターがある商品で使えます。",
        PluginMenuBlock.HasAvatar => "対応アバターが無い商品で使えます。",
        PluginMenuBlock.OtherCategory => $"カテゴリが「{reason.Category}」の商品で使えます。",
        _ => string.Empty,
    };
}
