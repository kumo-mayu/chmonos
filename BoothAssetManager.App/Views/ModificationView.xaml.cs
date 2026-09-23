using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

public partial class ModificationView : UserControl
{
    public ModificationView()
    {
        InitializeComponent();

        // 落とすのは**窓全体**で受け、中身で行き先を決める（`ui-rules.md`・B5）。
        // この画面が自分で受けていた頃は、改変を見ている間は zip も BOOTH の URL も落とせなかった。
        // 画像を写真に回す判断は `DropRouting.DecideOnModification`。
        // 貼る（Ctrl+V）はここに残す——絵そのものは落とす経路では来ないので、画面ごとの受け口が要る
        PreviewKeyDown += OnPreviewKeyDown;

        // 絵・星の列の幅は見出しの境目で変えられるので、変わったら名前の列を合わせ直す
        // （DependencyPropertyDescriptor.AddValueChanged は列を静的な表に掴ませて画面ごと残すので、列自身の知らせを聞く）
        foreach (var column in MemberColumns.Where(column => column != MemberNameColumn))
        {
            ((INotifyPropertyChanged)column).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GridViewColumn.ActualWidth))
                {
                    FitMemberNameColumn();
                }
            };
        }
    }

    private IEnumerable<GridViewColumn> MemberColumns => ((GridView)MemberList.View).Columns;

    private void OnMemberListSizeChanged(object sender, SizeChangedEventArgs e) => FitMemberNameColumn();

    /// <summary>
    /// 名前の列を「一覧の幅 − ほかの列」にする。GridView の列は残りを埋める指定を持たないので、幅が変わるたびに決め直す。
    /// 狭くても 160（ui-rules.md の名前の列の最小）は割らず、足りない分は一覧の横送りに任せる
    /// </summary>
    private void FitMemberNameColumn()
    {
        const double minimum = 160;

        // 行の枠と端の余白の分。これを引かないと、ちょうどの幅で横のスクロールバーが出たり消えたりする
        const double slack = 8;

        var others = MemberColumns.Where(column => column != MemberNameColumn).Sum(column => column.ActualWidth);
        var width = Math.Max(minimum, Math.Floor(MemberList.ActualWidth - others - slack));
        if (Math.Abs(MemberNameColumn.Width - width) >= 1)
        {
            MemberNameColumn.Width = width;
        }
    }

    /// <summary>
    /// 使ったものの「Unity ▾」。押すと、インポートと選択の2択を下に出す（ユーザ指示 2026-09-14：「開く」がエクスプローラなのか
    /// Unity なのか分かりにくかった）。項目はボタンの ContextMenu に置き、左クリックでも開く（右クリックでも同じ物が出る）
    /// </summary>
    private void OnUnityMenuClick(object sender, RoutedEventArgs e) => Controls.MenuButton.OpenBelow(sender, e);

    /// <summary>
    /// Ctrl+V でクリップボードの絵を貼る。
    ///
    /// スクリーンショットは**ファイルではなく絵そのもの**で置かれるので、
    /// 落とす経路では受け取れない。入力欄にいるときは横取りしない——
    /// そこでの Ctrl+V は文字の貼り付けで、取り上げると打てなくなる。
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V
            || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control
            || DataContext is not ModificationViewModel view
            || Keyboard.FocusedElement is TextBox or ComboBox)
        {
            return;
        }

        if (ReadClipboardImage() is not { } bytes)
        {
            return;
        }

        e.Handled = true;
        view.PasteImageAsync(bytes).Forget();
    }

    /// <summary>クリップボードの絵をPNGの生データにする。商品ページと同じ扱い。</summary>
    private static byte[]? ReadClipboardImage()
    {
        try
        {
            if (Clipboard.GetImage() is not { } source)
            {
                return null;
            }

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception exception)
            when (exception is System.Runtime.InteropServices.ExternalException or NotSupportedException)
        {
            // 他のアプリがクリップボードを掴んでいることがある。次に押せば入る
            return null;
        }
    }
}
