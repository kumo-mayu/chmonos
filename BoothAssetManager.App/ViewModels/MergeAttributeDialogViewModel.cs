using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 属性を統合するときの確認。
///
/// 両方に値が入っているitemでは、どちらかの値しか残せない。
/// 平均を取ると本人が付けていない数字を作ってしまうので、必ず選ばせる。
/// </summary>
public sealed class MergeAttributeDialogViewModel : ViewModelBase
{
    private bool _useSource;

    public MergeAttributeDialogViewModel(string from, string to, AttributeMergePreview preview)
    {
        From = from;
        To = to;
        Preview = preview;
    }

    public string From { get; }

    public string To { get; }

    public AttributeMergePreview Preview { get; }

    public string HeadingText => $"「{From}」を「{To}」に統合します。";

    public string ImpactText => Preview.ItemCount == 0
        ? "どの商品も評価していないので、商品は書き換わりません。"
        : $"{Preview.ItemCount} 件の商品を書き換えます。メモは「{To}」側へ追記します。";

    /// <summary>値がぶつかるitemがあるときだけ、どちらを残すか聞く。</summary>
    public bool AsksAboutValues => Preview.Conflicts > 0;

    public string ConflictText => $"{Preview.Conflicts} 件の商品には両方に別々の値が入っています。";

    public string KeepTargetText => $"「{To}」に入っている値を残す";

    public string UseSourceText => $"「{From}」の値で上書きする";

    /// <summary>既定は寄せ先を残す。統合は寄せ先へ寄せる操作なので、そちらを基準にする。</summary>
    public bool UseSource
    {
        get => _useSource;
        set => SetField(ref _useSource, value);
    }

    public bool KeepTarget
    {
        get => !_useSource;
        set
        {
            if (value)
            {
                UseSource = false;
            }
        }
    }

    public AttributeMergeValue Keep => UseSource ? AttributeMergeValue.UseSource : AttributeMergeValue.KeepTarget;
}
