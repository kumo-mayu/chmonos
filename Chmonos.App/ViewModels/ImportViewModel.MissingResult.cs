using System.IO;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 「見つからないファイルを探す」の結果の欄の、平らな一覧の1行（仮想化の単位。メモ74）。
///
/// 結果は数千件になり得る（監視フォルダの中を丸ごと移した回）。前は見出しごとの一覧を入れ子にして画面全体の1本のスクロールに置いていたので
/// 仮想化されず、3000件ずつの結果を開くと全部の行を作って約4.7秒固まり、メモリが約300MB増えた（2026-10-06・台で測った）。
/// 通知の画面と同じく、見出しと行を1本に並べて見えている行だけを作る。畳んだ見出しの中の行は一覧から抜く。
/// </summary>
public abstract class MissingResultLine : ViewModelBase
{
}

/// <summary>結果の欄の見出しの行（紐付け直したファイル・見つからなかったファイル・見つからない登録フォルダ）。</summary>
public sealed class MissingResultHeadLine(string title, string automationId, Action toggled) : MissingResultLine
{
    private bool _isExpanded;
    private int _count;

    public string Title { get; } = title;

    public string AutomationId { get; } = automationId;

    /// <summary>
    /// うまくいった物の見出しか（紐付け直したファイル）。緑と赤で分け、読まなくても成否が分かるようにする
    /// （ユーザ 2026-10-06「成功と失敗が読まないとわからない。成功を緑、失敗を赤で分けましょう」）
    /// </summary>
    public bool Succeeded { get; init; }

    public int Count
    {
        get => _count;
        set
        {
            if (SetField(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public string CountText => $"  {Count} 件";

    /// <summary>開いているか。替わったら一覧の行を出し入れする。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                toggled();
            }
        }
    }
}

/// <summary>見つからなかったファイルの見出しの後ろに出す、次の手の1行。</summary>
public sealed class MissingResultHintLine(string text) : MissingResultLine
{
    public string Text { get; } = text;
}

/// <summary>「見つからないファイルを探す」の結果の、商品のファイル1行（紐付け直した物・見つからなかった物）。</summary>
public sealed class MissingFileResultRow : MissingResultLine
{
    private string _statusText = string.Empty;

    public required string ItemId { get; init; }

    public required string ItemName { get; init; }

    public required string FileName { get; init; }

    /// <summary>紐付け直した先のフォルダ。見つからなかった行は空。</summary>
    public string FolderText { get; init; } = string.Empty;

    public bool HasFolder => FolderText.Length > 0;

    /// <summary>紐付け直せた行か（見出しと同じく、行の左の線を緑と赤で分ける。流して見出しが見えなくても成否が分かる）。</summary>
    public bool Succeeded => HasFolder;

    /// <summary>吹き出しに出す、ファイルの場所（紐付け直した行は新しい場所、見つからなかった行は元の場所）。</summary>
    public string PathTip { get; init; } = string.Empty;

    /// <summary>商品名を押す（商品ページを開く）。</summary>
    public RelayCommand? OpenItemCommand { get; set; }

    /// <summary>行の左に出す商品の絵。</summary>
    public ResultThumbnail? Picture { get; set; }

    /// <summary>
    /// 場所の行に出す文字。紐付け直した行は新しい場所のフォルダ、見つからなかった行は元の場所のフォルダ
    /// （行はいつも3行：商品名・ファイル名・場所。名前の分からないファイルは場所も無いので空）。
    /// </summary>
    public string PlaceLabel => Succeeded ? "新しい場所：" : PlaceText.Length > 0 ? "元の場所：" : string.Empty;

    /// <summary>場所の行の本体（フォルダ。長ければ画面が頭を切る）。</summary>
    public string PlaceText => Succeeded ? FolderText : System.IO.Path.GetDirectoryName(PathTip) ?? string.Empty;

    /// <summary>商品を開けなかったときの1行（押した行のすぐ下に出す。D6）。</summary>
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (SetField(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => StatusText.Length > 0;
}

public sealed partial class ImportViewModel
{
    /// <summary>
    /// これより多ければ、結果の見出しを畳んで出す（メモ74 で 5 から上げた）。
    /// 行は見えている分だけ作るので、数は重さに効かない（3000件を開いても1歩の並べ直しは数ms）。畳むのは、次の見出し
    /// （特に下の「見つからない登録フォルダ」。押して選ぶ物）へ届くまでの送りを短くするためだけ。
    /// 結果の一覧の高さは窓の見えている高さまでで、高さ800の窓で約10行。20件なら2画面ほど送れば次の見出しに届く。
    /// </summary>
    internal const int ResultFoldOver = 20;

    private MissingResultHeadLine? _relinkedHead;
    private MissingResultHeadLine? _notFoundHead;
    private MissingResultHeadLine? _foldersHead;
    private MissingResultHintLine? _notFoundHintLine;
    private IReadOnlyList<MissingFileResultRow> _relinkedFiles = [];
    private IReadOnlyList<MissingFileResultRow> _notFoundFiles = [];
    private string _missingSearchNotes = string.Empty;
    private bool _fillingResult;

    /// <summary>
    /// 探して紐付け直したファイル（手触りの確認 2026-10-06・メモ73）。
    /// 前は「n 件を紐付け直しました」の1行だけで、どの商品のどのファイルがどこへ移ったかが分からず、
    /// 監視していない場所を足して探した回に結び直っていたのに、見つからなかったように見えた。
    /// 画面に並べるのは <see cref="MissingResultLines"/>。
    /// </summary>
    public IReadOnlyList<MissingFileResultRow> RelinkedFiles => _relinkedFiles;

    /// <summary>探しても見つからなかったファイル。</summary>
    public IReadOnlyList<MissingFileResultRow> NotFoundFiles => _notFoundFiles;

    /// <summary>
    /// 結果の欄の一覧：見出し・その中の行・次の手を1本に並べた物（メモ74）。畳んだ見出しの中の行は入れない。
    /// 探し直したときはまとめて1回知らせる（1行ずつだと数千回の知らせが一覧へ飛ぶ）。畳む・開くは差分で寄せ、押した見出しの部品を残す
    /// （丸ごと作り直すと、キーボードで押した見出しからフォーカスが窓へ落ちる）。
    /// </summary>
    public RangeObservableCollection<MissingResultLine> MissingResultLines { get; } = [];

    public bool HasRelinkedFiles => RelinkedFiles.Count > 0;

    public bool HasNotFoundFiles => NotFoundFiles.Count > 0;

    /// <summary>結果の欄を出すか。並べる物も探せなかった場所の文も無ければ、欄ごと出さない。</summary>
    public bool HasMissingResult => HasRelinkedFiles || HasNotFoundFiles || HasMissingFolders || HasMissingSearchNotes;

    public string RelinkedCountText => RelinkedHead.CountText;

    public string NotFoundCountText => NotFoundHead.CountText;

    public bool IsRelinkedExpanded
    {
        get => RelinkedHead.IsExpanded;
        set => RelinkedHead.IsExpanded = value;
    }

    public bool IsNotFoundExpanded
    {
        get => NotFoundHead.IsExpanded;
        set => NotFoundHead.IsExpanded = value;
    }

    public bool IsMissingFoldersExpanded
    {
        get => FoldersHead.IsExpanded;
        set => FoldersHead.IsExpanded = value;
    }

    // 見出しの行は使い回す（差し替えると、見えている見出しの部品が作り直される）
    private MissingResultHeadLine RelinkedHead => _relinkedHead ??= new MissingResultHeadLine(
        "紐付け直したファイル", "ImportRelinkedFiles", () => OnHeadToggled(nameof(IsRelinkedExpanded))) { Succeeded = true };

    private MissingResultHeadLine NotFoundHead => _notFoundHead ??= new MissingResultHeadLine(
        "見つからなかったファイル", "ImportNotFoundFiles", () => OnHeadToggled(nameof(IsNotFoundExpanded)));

    private MissingResultHeadLine FoldersHead => _foldersHead ??= new MissingResultHeadLine(
        "見つからない登録フォルダ", "ImportMissingFolders", () => OnHeadToggled(nameof(IsMissingFoldersExpanded)));

    /// <summary>探せなかった場所・読めなかった物の文（1行に1つ）。1行目の要約に混ぜると長くなり、切れて読めなかった。</summary>
    public string MissingSearchNotes
    {
        get => _missingSearchNotes;
        private set
        {
            if (SetField(ref _missingSearchNotes, value))
            {
                OnPropertyChanged(nameof(HasMissingSearchNotes));
            }
        }
    }

    public bool HasMissingSearchNotes => MissingSearchNotes.Length > 0;

    /// <summary>探し直すたびに並べ直す（前の回の結果は古いので残さない）。結果が無い（失敗・窓を閉じた）なら空にする。</summary>
    internal void ShowMissingFiles(MissingFileSearchResult? result)
    {
        _relinkedFiles = RowsOf(result?.RelinkedFiles ?? []);
        _notFoundFiles = RowsOf(result?.NotFoundFiles ?? []);
        MissingSearchNotes = result is null ? string.Empty : string.Join("\n", MissingSearchNoteLines(result, _services.Settings.WatchedFolders ?? []));
        RelinkedHead.Count = RelinkedFiles.Count;
        NotFoundHead.Count = NotFoundFiles.Count;
        FillResultLines(() =>
        {
            IsRelinkedExpanded = RelinkedFiles.Count <= ResultFoldOver;
            IsNotFoundExpanded = NotFoundFiles.Count <= ResultFoldOver;
        });

        OnPropertyChanged(nameof(RelinkedFiles));
        OnPropertyChanged(nameof(NotFoundFiles));
        OnPropertyChanged(nameof(HasRelinkedFiles));
        OnPropertyChanged(nameof(HasNotFoundFiles));
        OnPropertyChanged(nameof(RelinkedCountText));
        OnPropertyChanged(nameof(NotFoundCountText));
    }

    /// <summary>
    /// 開き具合を入れてから、一覧をまとめて並べ直す。開き具合を1つ入れるたびに並べ直すと、数千行を何度も寄せ直すことになる。
    /// </summary>
    private void FillResultLines(Action setExpanded)
    {
        _fillingResult = true;
        try
        {
            setExpanded();
        }
        finally
        {
            _fillingResult = false;
        }

        MissingResultLines.ReplaceAll(BuildResultLines());
        OnPropertyChanged(nameof(HasMissingResult));
    }

    private void OnHeadToggled(string property)
    {
        OnPropertyChanged(property);
        if (!_fillingResult)
        {
            CollectionSync.Apply(MissingResultLines, BuildResultLines());
        }
    }

    /// <summary>見出しの並びと開き具合から、平らな一覧の目当ての並びを作る。中身の無い見出しは出さない。</summary>
    private List<MissingResultLine> BuildResultLines()
    {
        var lines = new List<MissingResultLine>();
        if (RelinkedFiles.Count > 0)
        {
            lines.Add(RelinkedHead);
            if (RelinkedHead.IsExpanded)
            {
                lines.AddRange(RelinkedFiles);
            }
        }

        if (NotFoundFiles.Count > 0)
        {
            lines.Add(NotFoundHead);
            if (NotFoundHead.IsExpanded)
            {
                lines.AddRange(NotFoundFiles);
            }

            // 次の手は畳んでいても出す（何をすればよいかは、中を開かなくても要る）
            lines.Add(_notFoundHintLine ??= new MissingResultHintLine(NotFoundHint));
        }

        if (MissingFolders.Count > 0)
        {
            lines.Add(FoldersHead);
            if (FoldersHead.IsExpanded)
            {
                lines.AddRange(MissingFolders);
            }
        }

        return lines;
    }

    private List<MissingFileResultRow> RowsOf(IReadOnlyList<MissingFileOutcome> outcomes)
    {
        var rows = outcomes
            .Select(ResultRowOf)
            .OrderBy(row => row.ItemName, StringComparer.CurrentCulture)
            .ThenBy(row => row.FileName, StringComparer.CurrentCulture)
            .ToList();
        foreach (var row in rows)
        {
            row.OpenItemCommand = new RelayCommand(() => OpenResultItemAsync(row.ItemId, text => row.StatusText = text).Forget());
            row.Picture = PictureOf(row.ItemId, row.ItemName);
        }

        return rows;
    }

    /// <summary>
    /// 行の絵（メモ76）。絵の場所は、行が見えて絵を読むときに初めて、商品の記録から決める（数千行ぶんの記録を先に読まない）。
    /// 記録を読む・絵の一覧を取るのは裏で行う。商品が消えていれば絵は無く、頭文字のまま。
    /// </summary>
    internal ResultThumbnail PictureOf(string itemId, string itemName)
        => ResultThumbnail.ForItem(_services, _main.Thumbnails, itemId, itemName);

    /// <summary>結果の1つを行にする。紐付け直した物は新しい場所の名前とフォルダ、見つからなかった物は元の名前。</summary>
    internal static MissingFileResultRow ResultRowOf(MissingFileOutcome outcome)
    {
        var path = outcome.NewPath ?? outcome.OldPaths.FirstOrDefault() ?? string.Empty;
        var name = Path.GetFileName(path);
        return new MissingFileResultRow
        {
            ItemId = outcome.ItemId,
            ItemName = outcome.ItemName,
            // 取り込みが場所を全部外したファイルは、元の名前が記録に残っていない
            FileName = name.Length > 0 ? name : "名前の分からないファイル",
            FolderText = outcome.NewPath is { } found ? Path.GetDirectoryName(found) ?? string.Empty : string.Empty,
            PathTip = path,
        };
    }

    /// <summary>
    /// 1行目の要約の下に出す、探せなかった場所・読めなかった物の文（どれも句点で終わる）。
    /// **ここは何が起きたかだけを言い、直し方は言わない**（ユーザ 2026-10-06「ここでは監視フォルダ「...」は見つからなかったため
    /// 確認できませんでした。ということを出して直し方は監視対象の方に書くべきだろう」）。監視フォルダが見つからないときの直し方は、
    /// 監視対象の欄の注意（「監視フォルダ「…」が見つかりません。名前を変えたか移したなら…」）が言う
    /// </summary>
    internal static IEnumerable<string> MissingSearchNoteLines(MissingFileSearchResult result, IReadOnlyList<string> watchedFolders)
    {
        foreach (var line in PlaceNotes(result.Unreachable, watchedFolders, "つながっていないため確認できませんでした。"))
        {
            yield return line;
        }

        // ドライブは在ってフォルダだけが無い（名前を変えた・移した）。つないでも直らないので、外付けとは分けて言う
        // （見つからない・移動の点検 9・2026-10-05）
        foreach (var line in PlaceNotes(result.NotFoundFolders, watchedFolders, "見つからなかったため確認できませんでした。"))
        {
            yield return line;
        }

        if (UnreadableInSearchText(result.UnreadableFiles, result.UnreadableFolders) is { Length: > 0 } unreadable)
        {
            yield return unreadable + "。";
        }
    }

    /// <summary>
    /// 探せなかった場所を1つずつ名前で言う（監視フォルダなら「監視フォルダ「名前」は…」、その回だけ足したフォルダなら「フォルダ「名前」は…」）。
    /// 多いと欄が文で埋まるので、4つ以上は数でまとめる
    /// </summary>
    private static IEnumerable<string> PlaceNotes(IReadOnlyList<string> places, IReadOnlyList<string> watchedFolders, string ending)
    {
        if (places.Count >= 4)
        {
            yield return $"{places.Count} 個のフォルダは{ending}";
            yield break;
        }

        foreach (var place in places)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(place));
            if (name.Length == 0)
            {
                name = place;
            }

            var kind = watchedFolders.Any(watched => Core.Services.PathText.Same(watched, place)) ? "監視フォルダ" : "フォルダ";
            yield return $"{kind}「{name}」は{ending}";
        }
    }

    /// <summary>結果の行の商品名を押したとき（ファイルの行・登録フォルダの行）。</summary>
    private async Task OpenResultItemAsync(string itemId, Action<string> say)
    {
        if (await _services.Store.Items.LoadAsync(itemId) is { } record)
        {
            _main.ShowItem(record);
            return;
        }

        // 探した後に商品IDを変えた・管理から外した。黙って何も起きないと壊れたように見える
        say("この商品は見つかりませんでした。商品IDを変えたか、管理対象から除外した可能性があります。");
    }
}
