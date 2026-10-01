using System.Windows.Controls;

namespace Chmonos.App.Views;

/// <summary>
/// 取り込みの画面。
///
/// **落とすのは窓全体で受ける**（`ui-rules.md`・B5）。以前はこの画面が自分で受けていて、
/// ファイルは積めるのに BOOTH の URL を落としても何も起きなかった。
/// ファイルの行き先は `DropRouting.Decide` が取り込みに決めるので、この画面でも同じに働く。
/// </summary>
public partial class ImportView : UserControl
{
    public ImportView()
    {
        InitializeComponent();
    }
}
