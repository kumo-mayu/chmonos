using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 「このファイルについて分かっていること」の、同じ中身のほかの場所と、見つからない／取り外しているドライブの1行（open.md 97・ユーザ判断 2026-11-19 案A）。
/// 前はフォルダの木のファイルの行の札にだけ出て、右に組み込んだ未確定の欄を読んでも分からなかった。言葉はフォルダの札と揃える。
/// </summary>
public sealed partial class ResolveViewModel
{
    /// <summary>
    /// 記録の場所が2つ以上あれば、その数から1を引いた数。フォルダの札（「ほかに n か所」）も、同じ記録の場所から自分の行の場所を除いて数える。
    /// 場所の一覧はすぐ上に全部並ぶが、並んだ行を数えなくても重なりに気付けるように1行で言う。
    /// </summary>
    public string SelectedOthersText
        => Selected?.File.Paths.Count is > 1 and var count ? $"同じ中身がほかに {count - 1} か所" : string.Empty;

    public bool HasSelectedOthers => SelectedOthersText.Length > 0;

    private string _selectedPresenceText = string.Empty;

    /// <summary>選んだファイルが今ディスクに見えないときの1行。見ている間と、在るときは空。</summary>
    public string SelectedPresenceText
    {
        get => _selectedPresenceText;
        private set
        {
            if (SetField(ref _selectedPresenceText, value))
            {
                OnPropertyChanged(nameof(HasSelectedPresence));
            }
        }
    }

    public bool HasSelectedPresence => SelectedPresenceText.Length > 0;

    /// <summary>選び直すたびに増やす。見終わった時に別の行を選んでいれば、古い答えを捨てる。</summary>
    private int _presenceTicket;

    /// <summary>
    /// フォルダの札と同じ言葉。どこにも無ければ「見つかりません」、つながっていないドライブの上の場所しか無ければ「取り外しているドライブ」
    /// （無くなったとは限らないので分ける）。確かめられない（権限が無い等）は、無いとは言えないので出さない。
    /// </summary>
    internal static string PresenceText(FilePresence presence) => presence switch
    {
        FilePresence.Missing => "見つかりません",
        FilePresence.OnDetachedDrive => "取り外しているドライブ",
        _ => string.Empty,
    };

    /// <summary>
    /// 選んだファイルを見る。ディスクを見るので画面のスレッドの外で（落ちたネットワークドライブで選ぶたびに固まらないように）。
    /// 見るのはドライブ文字を読み替えた後の場所（フォルダの木・商品ページと同じ答えにする。<see cref="FilePresenceNotes"/>）。
    /// 未確定の記録は書かない（見回りと取り込みが書く。ここは見せるだけ）。
    /// </summary>
    private async Task RefreshSelectedPresenceAsync()
    {
        OnPropertyChanged(nameof(SelectedOthersText));
        OnPropertyChanged(nameof(HasSelectedOthers));
        SelectedPresenceText = string.Empty;
        var ticket = ++_presenceTicket;
        if (Selected?.File.Paths is not { Count: > 0 } paths)
        {
            return;
        }

        var volumes = _services.Volumes;
        var presence = await Task.Run(() => new FilePresenceProbe(volumes: volumes.Snapshot()).Of(paths, volumes.Current));
        if (ticket == _presenceTicket)
        {
            SelectedPresenceText = PresenceText(presence);
        }
    }
}
