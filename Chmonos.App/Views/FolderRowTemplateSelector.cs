using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// フォルダビューの行を、種類ごとの見た目で出す（技術的負債 4-4、2026-09-14）。
///
/// 前は1つの見た目に全種類の部品（フォルダの印・商品の絵・「?」・数・未確定の札・「ほかに n か所」など）を入れ、
/// 要らない物を隠していた。1000本並ぶ根を開くと、開け閉め1回に 65〜90ms 止まり、測ると行を作る処理（数ms）ではなく
/// 画面に出す行の部品を組む方に掛かっていた。種類ごとに要る物だけ組む。
/// </summary>
public sealed class FolderRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Folder { get; set; }

    public DataTemplate? ItemFile { get; set; }

    public DataTemplate? Unresolved { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        FolderViewRow { IsFolderLike: true } => Folder,
        FolderViewRow { IsUnresolved: true } => Unresolved,
        _ => ItemFile,
    };
}
