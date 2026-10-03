using System.IO;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 未確定の右の欄の組み立ての案（メモ22。案の枝だけに置く仮の物）：「今の対象」を1か所で見せて切り替え、
/// 確定・BOOTHに無い商品として登録・除外のボタンを対象の種類ごとに分けず1つずつにするための値。
/// 対象の決め方は、チェックがあればチェックした全部、無ければ選んだ行の単位（束・フォルダ・このファイルだけ）
/// </summary>
public sealed partial class ResolveViewModel
{
    /// <summary>今の対象を言う1行。チェック・束・1件のどれかで変わる。</summary>
    public string TargetText => HasChecked
        ? $"選択した {CheckedCount} 件"
        : ActiveGroup is not null
            ? GroupSubject
            : Selected is { } row ? row.FileName : string.Empty;

    /// <summary>今の対象の件数（ボタンに件数を入れる案で使う）。</summary>
    public int TargetCount => HasChecked ? CheckedCount : ActiveRows.Count;

    /// <summary>束の単位にできる行か（zipの中身・zipが無いフォルダのファイルで、仲間が2件以上）。</summary>
    public bool HasUnitChoice => !HasChecked && UnitMates > 1;

    private int UnitMates => Selected is { } row && (row.IsExpandedContent || row.IsArchiveContent)
        ? Files.Count(other => (other.IsExpandedContent || other.IsArchiveContent)
            && string.Equals(other.GroupKey, row.GroupKey, StringComparison.OrdinalIgnoreCase))
        : 0;

    public string UnitChoiceText => Selected is { HasOrigin: true }
        ? $"zipの中身 {UnitMates} 件"
        : $"フォルダの {UnitMates} 件";

    public string UnitChoiceLongText => Selected is { Origin: { } origin }
        ? $"元zip「{origin.ArchiveName}」の中身 {UnitMates} 件"
        : $"フォルダ「{Path.GetFileName(Selected?.GroupKey ?? string.Empty)}」のファイル {UnitMates} 件";

    /// <summary>束を対象にしているか（「このファイルだけ」と表と裏）。</summary>
    public bool IsUnitTarget
    {
        get => !SingleFileOnly;
        set => SingleFileOnly = !value;
    }

    public string SingleChoiceText => Selected is { } row ? $"このファイルだけ：{row.FileName}" : string.Empty;

    /// <summary>元のzipが残っていて、そちらへ切り替えられるか。</summary>
    public bool HasOriginZipChoice => !HasChecked && Selected is { HasOriginZip: true, IsExpandedContent: true };

    public string OriginZipChoiceText => Selected?.Origin is { } origin ? $"元のzip：{origin.ArchiveName}" : string.Empty;

    public string OriginZipShortText => Selected?.Origin is { } origin ? $"元のzip {origin.ArchiveName}" : string.Empty;

    /// <summary>チェックした物が対象のとき（切り替えは「選択を解除」だけ）。</summary>
    public bool IsCheckedTarget => HasChecked;

    public bool IsSingleRowTarget => !HasChecked && !HasUnitChoice;

    /// <summary>ボタンに件数を入れる案：1件なら件数を言わない。</summary>
    public string AssignTargetText => TargetCount > 1 ? $"{TargetCount} 件をこのIDで確定する" : "このIDで確定する";

    public string LocalTargetText => TargetCount > 1 ? $"{TargetCount} 件をこの名前で登録する" : "この名前で登録する";

    public string ExcludeTargetText => TargetCount > 1 ? $"{TargetCount} 件を管理対象から除外する" : "管理対象から除外する";

    private RelayCommand? _assignTargetCommand;
    private RelayCommand? _excludeTargetCommand;

    /// <summary>今の対象に効く確定。チェックがあればまとめて確定、無ければ1件（束）の確定。</summary>
    public RelayCommand AssignTargetCommand => _assignTargetCommand ??= new RelayCommand(
        () => (HasChecked ? AssignCheckedCommand : AssignCommand).Execute(null),
        () => (HasChecked ? AssignCheckedCommand : AssignCommand).CanExecute(null));

    public RelayCommand ExcludeTargetCommand => _excludeTargetCommand ??= new RelayCommand(
        () => (HasChecked ? ExcludeCheckedCommand : ExcludeCommand).Execute(null),
        () => (HasChecked ? ExcludeCheckedCommand : ExcludeCommand).CanExecute(null));

    /// <summary>元のzipへ切り替える（一覧のzipの行を選ぶ。今の「元zipとして扱う」と同じ動き）。</summary>
    private RelayCommand? _pickOriginZipCommand;

    public RelayCommand PickOriginZipCommand => _pickOriginZipCommand ??= new RelayCommand(
        () => TreatAsOriginZipCommand.Execute(Selected), () => HasOriginZipChoice);

    private void RaiseTargetChanged()
    {
        foreach (var name in new[]
        {
            nameof(TargetText), nameof(TargetCount), nameof(HasUnitChoice), nameof(UnitChoiceText), nameof(UnitChoiceLongText),
            nameof(IsUnitTarget), nameof(SingleChoiceText), nameof(HasOriginZipChoice), nameof(OriginZipChoiceText),
            nameof(OriginZipShortText), nameof(IsCheckedTarget), nameof(IsSingleRowTarget), nameof(AssignTargetText),
            nameof(LocalTargetText), nameof(ExcludeTargetText),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
