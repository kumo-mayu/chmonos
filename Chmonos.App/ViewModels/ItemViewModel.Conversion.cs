using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品ページの「変換扱い」の欄（<c>docs/spec/conversion.md</c>「画面」）。中身は <see cref="ItemConversionEditor"/>（アバターの管理の「変換」と同じ部品）。
/// 書いた後は、ほかの書き込みと同じ道（読み直して対応アバターを組み直し、検索の写しへ知らせる）を通す。
/// </summary>
public partial class ItemViewModel
{
    private ItemConversionEditor? _conversion;

    public ItemConversionEditor Conversion => _conversion ??= new ItemConversionEditor(
        _services,
        command => RunItemCommandAsync(command, Conversion.Notice),
        () => IsEditLocked);

    /// <summary>対応アバターを組み直すたびに呼ぶ（書いた後の読み直し・取り込みの後など、組み直す道を全部通るため）。</summary>
    private void BuildConversion(AvatarRegistry registry, IReadOnlyDictionary<string, string> names)
        => Conversion.Show(Item, registry, names);
}
