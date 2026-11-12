using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Chmonos.App.ViewModels;

/// <summary>複数選択をキーで変える操作。</summary>
internal enum SelectionKey
{
    None,

    /// <summary>Ctrl+A</summary>
    SelectAll,

    /// <summary>Ctrl+Shift+A（Esc と同じ）</summary>
    ClearAll,

    /// <summary>Ctrl+Shift+I</summary>
    Invert,
}

/// <summary>
/// 選択の帯がある3つの画面（検索・フォルダの右側・ショップ）で、キーで全選択・全解除・反転する（ユーザ判断 2026-11-12
/// 「Ctrl-aは、入力欄以外では全選択としよう」、全解除・反転は「おすすめの形で進めよう」）。
///
/// Ctrl+D・Ctrl+I は使わない。Ctrl+D は Explorer でごみ箱へ送るキーで、慣れた人が押すと「消える」と身構える。
/// Ctrl+I は斜体・情報で一般に使われる。Shift を足した組み合わせなら、ほかのソフトで別の意味に慣れていることが少ない。
/// 窓のキーの処理（MainWindow）から呼び、判断はここに置く（窓の外で試験できるように）
/// </summary>
internal static class SelectionKeys
{
    /// <summary>
    /// キーを読む。修飾は完全に一致したときだけ（Alt が混じる組み合わせは AltGr の文字入力にもなるので取らない）
    /// </summary>
    public static SelectionKey Classify(Key key, ModifierKeys modifiers) => (key, modifiers) switch
    {
        (Key.A, ModifierKeys.Control) => SelectionKey.SelectAll,
        (Key.A, ModifierKeys.Control | ModifierKeys.Shift) => SelectionKey.ClearAll,
        (Key.I, ModifierKeys.Control | ModifierKeys.Shift) => SelectionKey.Invert,
        _ => SelectionKey.None,
    };

    /// <summary>
    /// 焦点が文字の部品（入力欄・選べる文）の中か。中なら選択のキーは使わず、文字の操作（欄の中の全選択など）に譲る。
    ///
    /// 型は TextBoxBase（TextBox・読み取り専用の RichTextBox の選べる文と <c>SelectableLink</c>）・PasswordBox・ComboBox。
    /// SuggestBox は中の TextBox に焦点が行くので TextBoxBase で拾う。
    /// 焦点の部品そのものだけでなく親もたどる：開いた ComboBox の項目や、選べる文の中のリンク（Hyperlink は部品でなく文の一部）に
    /// 焦点があるとき、型だけを見ると漏れる
    /// </summary>
    public static bool IsInTextInput(DependencyObject? focused)
    {
        for (var node = focused; node is not null; node = ParentOf(node))
        {
            if (node is TextBoxBase or PasswordBox or ComboBox)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 今の画面のうち、キーで選べる一覧。フォルダの表示は右がフォルダの中身のときだけ
    /// （右が商品ページ・未確定のときは選択の帯が無く、キーを横取りしない）
    /// </summary>
    public static ICardClickSelectionHost? HostOf(object? current) => current switch
    {
        ICardClickSelectionHost host => host,
        FolderViewModel { Detail: FolderViewDetail detail } => detail,
        _ => null,
    };

    /// <summary>
    /// キーを1回分行う。行ったら true（呼ぶ側は済んだ印を付ける）。
    /// Ctrl+Shift+A は選んでいないときは何もせず譲る（Esc と同じ。何も起きないのにキーを食べない）。
    /// Ctrl+A と Ctrl+Shift+I は、選んでいなくても選び始める入口なので受ける（反転は全選択と同じ結果になる。Explorer も同じ）
    /// </summary>
    public static bool Run(SelectionKey action, ICardClickSelectionHost host)
    {
        switch (action)
        {
            case SelectionKey.SelectAll:
                host.SelectAllShown();
                return true;
            case SelectionKey.Invert:
                host.InvertSelection();
                return true;
            case SelectionKey.ClearAll when host.HasSelection:
                host.ClearSelection();
                return true;
            default:
                return false;
        }
    }

    private static DependencyObject? ParentOf(DependencyObject node)
        => (node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : null)
            ?? LogicalTreeHelper.GetParent(node);
}
