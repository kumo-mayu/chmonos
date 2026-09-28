using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

// ResolveViewModel の画面に並べる行と小さな入れ物（技術的負債 4-1：画面のクラスのファイルから分けた。中身は変えていない）

/// <summary>未確定ファイル1件。一覧に並べる分の情報だけを持つ。</summary>
public sealed class UnresolvedRow : ViewModelBase
{
    private bool _isSelected;

    public required UnresolvedFile File { get; init; }

    /// <summary>一括操作の対象。一覧の選択（＝今見ているもの）とは別に持つ。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                SelectionChanged?.Invoke();
            }
        }
    }

    public event Action? SelectionChanged;

    public required string FileName { get; init; }

    public required string SizeText { get; init; }

    public required string DirectoryText { get; init; }

    /// <summary>
    /// 配布物を展開した中身で、元のzipが手元に無いとみなせるか（「展開元のzipファイルが無いフォルダ」）。
    /// 元のzipが残っていれば false——zipで登録すればよく、フォルダで片付ける話ではない。
    /// </summary>
    public bool IsArchiveContent { get; init; }

    /// <summary>そう判断した理由。押し付けにならないよう根拠を見せる。</summary>
    public string? ContentReason { get; init; }

    /// <summary>展開物の根とみなしたフォルダ（目印の見つかった一番外側）。</summary>
    public string? ProductFolder { get; init; }

    /// <summary>
    /// zipが無い展開物の根。左の束・まとめて扱う単位・フォルダのまま登録する先を、このフォルダ1つに揃える
    /// （ユーザ判断 2026-09-17 案A：束はファイルが直接入っているフォルダ、登録先は目印から遡ったフォルダで食い違い、1つの展開物がサブフォルダごとに割れていた）。
    /// zipが無い展開物でなければ null。
    /// </summary>
    public string? UnpackRoot { get; init; }

    /// <summary>
    /// 展開元のzip。エクスプローラーの「すべて展開」が中のファイルに残した記録から分かる
    /// （zipを消した後でも、展開先を別のドライブへ移した後でも残る）。分からなければ null。
    /// </summary>
    public ArchiveOrigin? Origin { get; init; }

    public bool HasOrigin => Origin is not null;

    /// <summary>展開した中身で、元のzipが今もディスクにあるか（zip自身の行は false）。あれば「元zipで登録」を出す。</summary>
    public bool HasOriginZip { get; init; }

    /// <summary>zipを展開した中身（zip自身ではない）か。束にならず1行で出るときに「展開元」を添える。</summary>
    public bool IsExpandedContent => HasOrigin && !IsOriginArchive;

    /// <summary>
    /// 一覧で束ねる単位。元zipが分かれば元zip、分からなければフォルダ。
    /// zipは配布された単位そのもので、中身はたいてい1商品。フォルダは展開の仕方次第で
    /// 1つのzipが何か所にも割れる（友人のデータで元zip 12 本のうち 6 本が複数のフォルダに割れていた）
    /// </summary>
    public string GroupKey => IsOriginArchive
        ? "zip|" + File.Paths[0]
        : Origin?.ArchiveName ?? UnpackRoot ?? DirectoryText;

    /// <summary>
    /// このファイルが元のzipそのものか。**zip自身は束に入れない**——元zipの束の中にそのzipが入っていると、
    /// 束が何をまとめているのか分かりにくかった（ユーザ指摘 2026-09-17）。zip自身は1件の束（畳まずに1行で出る）にし、
    /// 並びでその直後に「そのzipを展開した中身」の束を置く。
    /// 鍵の頭の「zip|」はパスに入らない文字で、中身の束の鍵（zipの名前）やフォルダの鍵とぶつからない。
    /// </summary>
    public bool IsOriginArchive => Origin is { } origin
        && File.Paths.Count > 0
        && string.Equals(origin.ArchivePath, File.Paths[0], StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 登録の単位の鍵。未確定の件数はこれで数える（ユーザ指示 2026-09-29：登録する回数を想像できるように）。
    /// まとめて扱う単位（<see cref="ResolveViewModel"/> の ZipUnit）と同じく、zipの中身はzipの名前、zipが無い展開物は束の鍵、ほかは1件ずつ。
    /// </summary>
    public string UnitKey => UnresolvedUnits.KeyOf(File.Hash, Origin, IsArchiveContent ? UnpackRoot ?? DirectoryText : null);

    /// <summary>元zipの束は畳んでおく。基本はzip単位で扱い、1件ずつ見たいときだけ開く。</summary>
    public bool StartsExpanded => Origin is null;

    /// <summary>取り込み時に拾えた候補の数。0件（手掛かりなし）と複数件（曖昧）がある。</summary>
    public int CandidateCount => File.CandidateItemIds.Count;

    public bool HasCandidates => CandidateCount > 0;

    /// <summary>
    /// 一覧の札。取り込んだときに手掛かりから見つかった商品IDの数で、右の「自動検索」の候補は数えない。
    /// 0件も同じ形で出す（前は「手掛かりなし」「候補 n 件（曖昧）」と言い方が分かれていて、何の数か読めなかった・ユーザ指示 2026-09-17）。
    /// </summary>
    public string CandidateText => $"自動候補:{CandidateCount} 件";
}

/// <summary>提示する候補1件。どこから来た候補なのかを添える。</summary>
public sealed class CandidateRow
{
    public required string ItemId { get; init; }

    public required string Title { get; init; }

    public string? Detail { get; init; }

    /// <summary>この候補の出どころ（取り込み時の手掛かり／検索）。</summary>
    public required string Source { get; init; }

    public bool IsStrong { get; init; }

    /// <summary>
    /// この候補の商品ページをブラウザで開く。
    /// 候補を出している以上、それが目当てのものか確かめる手段が要る。
    /// </summary>
    public RelayCommand? OpenBoothCommand { get; set; }
}
