using System.IO;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 未確定画面：**今の対象**（メモ22・ユーザ判断 2026-10-03「案1」）。
///
/// 前は対象の種類（1件・元zipの束・zipが無いフォルダ・一覧でチェックした物）ごとに別のボタンが並び、
/// 確定と除外は「選んだ行（束）」に、まとめ操作の枠は「チェックした物」に、BOOTHに無い商品の登録は「チェックがあればチェックした物」にと、
/// どのボタンが何に効くかがばらばらだった。対象の切り替えも分かっていること・確定の欄・一覧の見出しに散らばっていた。
/// 対象を決めるのはここ1か所にし、右の欄の上の帯で見せて切り替える。このIDで登録・BOOTHに無い商品として登録・除外は、どれもこの対象に効く。
///
/// 決め方：一覧でチェックした物があればその全部、無ければ選んだ行の単位（元zipの中身・zipが無いフォルダの束、「このファイルだけ」ならその1件）。
/// 商品に結ぶ操作（このIDで登録・BOOTHに無い商品）は、チェックした zip の中身を zip の単位まで広げる（1zip＝1商品。ユーザ判断 2026-09-17）。
/// 除外は広げない（外すのはチェックした物だけで足りる。広げると、選んでいない物まで黙って外れる）。
/// </summary>
public sealed partial class ResolveViewModel
{
    /// <summary>帯の1行目。何を、何件。</summary>
    public string TargetText => HasChecked
        ? $"選択した {CheckedCount} 件"
        : ActiveGroup is not null
            ? GroupSubject
            : Selected?.FileName ?? string.Empty;

    /// <summary>帯の1行目は1行で切るので、全部はここで読む。検索で隠れている分もここで言う。</summary>
    public string TargetToolTip
    {
        get
        {
            var rows = HasChecked ? CheckedRows : ActiveRows;
            var hidden = rows.Count(row => !MatchesFilter(row));
            return hidden > 0 ? $"{TargetText}。うち {hidden} 件は検索で隠れています。" : TargetText;
        }
    }

    /// <summary>束の仲間（同じzipの中身・同じフォルダのファイル）の数。「このファイルだけ」にしていても数える。</summary>
    private int UnitMates => Selected is { } row && (row.IsExpandedContent || row.IsArchiveContent)
        ? Files.Count(other => (other.IsExpandedContent || other.IsArchiveContent)
            && string.Equals(other.GroupKey, row.GroupKey, StringComparison.OrdinalIgnoreCase))
        : 0;

    /// <summary>まとめて扱うか1件だけかを切り替えられるか。チェックしている間は、チェックした物が対象なので出さない。</summary>
    public bool HasUnitChoice => !HasChecked && UnitMates > 1;

    public string UnitChoiceText => Selected is { HasOrigin: true }
        ? $"zipの中身 {UnitMates} 件"
        : $"フォルダの {UnitMates} 件";

    /// <summary>束を対象にしているか。「このファイルだけ」（<see cref="SingleFileOnly"/>）の裏返しで、切り替えの左側に結ぶ。</summary>
    public bool IsUnitTarget
    {
        get => !SingleFileOnly;
        set => SingleFileOnly = !value;
    }

    /// <summary>
    /// 元のzipが今もディスクにあり、未確定の一覧にも載っている中身を選んでいる。帯の右端の「元zipとして扱う」を出す。
    /// 一覧に無いと押しても選ぶ先が無いので、ボタンは出さず帯の文で言う（<see cref="IsOriginZipUnlisted"/>。ユーザ 2026-10-04 メモ30）。
    /// </summary>
    public bool HasOriginZipChoice => !HasChecked && Selected is { HasOriginZip: true, IsExpandedContent: true } row && IsZipListed(row);

    /// <summary>元のzipはディスクにあるが、未確定の一覧に無い中身を選んでいる（既に商品に結び付いている・取り込んでいない）。</summary>
    public bool IsOriginZipUnlisted => !HasChecked && Selected is { HasOriginZip: true, IsExpandedContent: true } row && !IsZipListed(row);

    private static string OriginZipUnlistedText(string archiveName) => $"展開元のzip「{archiveName}」は発見できませんでした。";

    public bool IsCheckedTarget => HasChecked;

    /// <summary>切り替える物が無い1件。帯の2行目に、まとめて扱う方法を1行だけ言う。</summary>
    public bool IsSingleRowTarget => HasSelection && !HasChecked && !HasUnitChoice;

    /// <summary>選んでいる1件（束）が、元のzipが未確定にあるので登録できないか。チェックした物は押したときに確かめる（全件を見るのは重い）。</summary>
    public bool IsTargetBlocked => !HasChecked && IsBlockedByListedZip;

    /// <summary>
    /// 商品に結ぶ操作（このIDで登録・BOOTHに無い商品として登録）の対象。止めるなら理由を返す。
    /// 1か所で決める（前は確定・まとめて確定・BOOTHに無い商品でそれぞれ決めていた）。
    /// </summary>
    private (IReadOnlyList<UnresolvedRow> Rows, string? Blocked) RegisterTargets()
    {
        if (HasChecked)
        {
            var (rows, blocked) = ExpandToZipUnits(CheckedRows);
            return (rows, blocked);
        }

        return (ActiveRows, IsBlockedByListedZip ? BlockedByZipText : null);
    }

    /// <summary>確認の窓・知らせで対象を言う言い方。</summary>
    private string TargetSubject(IReadOnlyList<UnresolvedRow> rows, bool fromChecked)
        => fromChecked
            ? $"選択した {rows.Count} 件"
            : rows.Count == 1 ? rows[0].FileName : GroupSubject;

    // ---- 知らせ（docs/feedback/notice-placement-2026-10-03.md「未確定」） ----

    private string _listNoticeText = string.Empty;

    /// <summary>
    /// 行ごと消える操作（まとめての登録・除外・戻す・フォルダのまま登録）の結果。押した所が消えるので、一覧の見出しの近く（上の帯）に出す。
    /// 次に同じ種類の結果が出るまで残す（行を選び直しても消さない。消えた行の話なので、選び直した行の話と混ざらない）
    /// </summary>
    public string ListNoticeText
    {
        get => _listNoticeText;
        private set
        {
            if (SetField(ref _listNoticeText, value))
            {
                OnPropertyChanged(nameof(HasListNotice));
                OnPropertyChanged(nameof(BandNoticeText));
            }
        }
    }

    public bool HasListNotice => ListNoticeText.Length > 0;

    private string _originZipNote = string.Empty;

    /// <summary>「元zipとして扱う」を押したのに、元のzipが一覧に無かったときの一言。押した帯の中に出す。</summary>
    private string OriginZipNote
    {
        get => _originZipNote;
        set
        {
            if (SetField(ref _originZipNote, value))
            {
                OnPropertyChanged(nameof(BandNoticeText));
            }
        }
    }

    /// <summary>
    /// 帯の3行目。高さを一定に保つため、出す物が無くても1行分を取っておき、ここに1つだけ出す。
    /// 押した「元zipとして扱う」の答え、次に登録できない理由、組み込んだとき（上の帯が無い）の一覧の結果の順。
    /// </summary>
    public string BandNoticeText => OriginZipNote.Length > 0
        ? OriginZipNote
        : IsTargetBlocked
            ? BlockedByZipText
            : IsOriginZipUnlisted && Selected?.Origin is { } origin
                ? OriginZipUnlistedText(origin.ArchiveName)
                : IsEmbedded ? ListNoticeText : string.Empty;

    /// <summary>帯の3行目を注意の色で出すか（登録できない理由・元のzipが無い）。一覧の結果はふつうの色。</summary>
    public bool IsBandNoticeWarning => OriginZipNote.Length > 0 || IsTargetBlocked;

    private string _localStatusText = string.Empty;

    /// <summary>「この名前で登録する」の誤り。ボタンの右に出す。</summary>
    public string LocalStatusText
    {
        get => _localStatusText;
        private set => SetField(ref _localStatusText, value);
    }

    private string _folderStatusText = string.Empty;

    /// <summary>フォルダのまま登録の誤り。ボタンの右に出す。</summary>
    public string FolderStatusText
    {
        get => _folderStatusText;
        private set => SetField(ref _folderStatusText, value);
    }

    private void RaiseTargetChanged()
    {
        foreach (var name in new[]
        {
            nameof(TargetText), nameof(TargetToolTip), nameof(HasUnitChoice), nameof(UnitChoiceText), nameof(IsUnitTarget),
            nameof(HasOriginZipChoice), nameof(IsOriginZipUnlisted), nameof(IsCheckedTarget), nameof(IsSingleRowTarget), nameof(IsTargetBlocked),
            nameof(BandNoticeText), nameof(IsBandNoticeWarning), nameof(AssignOutcomeText),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
