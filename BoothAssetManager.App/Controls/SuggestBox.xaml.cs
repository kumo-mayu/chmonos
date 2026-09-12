using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BoothAssetManager.App.Controls;

/// <summary>候補1件。既存の語か、入力された新しい語か。</summary>
public sealed class Suggestion
{
    private ImageSource? _icon;
    private bool _iconLoaded;

    public required string Value { get; init; }

    public required string Display { get; init; }

    public bool IsNew { get; init; }

    public Visibility NewBadgeVisibility => IsNew ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>頭に出す小さな絵を作るもの（U18）。無ければ絵の欄ごと出さない。</summary>
    public Func<string, ImageSource?>? IconFactory { get; init; }

    /// <summary>
    /// 頭の絵。**見えた行で初めて読む。**候補は空のまま触ると全部（アバターなら数百）並ぶので、
    /// 開くたびに全部の絵を読むと固まる。一覧は見えている行しか作らないので、読むのもその分だけになる
    /// </summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _icon = IconFactory?.Invoke(Value);
            }

            return _icon;
        }
    }

    public Visibility IconVisibility => IconFactory is null || IsNew ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>
/// 候補付きの入力欄。
///
/// このツールの共通ルールとして、候補を出せる入力欄には必ず候補を付ける。
/// userTagや属性はユーザが自分で作る語彙なので、表記揺れ（「かわいい」「可愛い」）が
/// 起きるとマスタが汚れ、名前で参照しているitem側の絞り込みが分裂するため。
///
/// 候補を全部並べて選ばせるUIは使わない。要素が増えるほど画面が縦に伸びてしまう。
/// ここから1件ずつ追加して、選んだものだけをリストに積む。
/// </summary>
public partial class SuggestBox : UserControl
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(IEnumerable), typeof(SuggestBox),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SuggestBox),
            new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    /// <summary>候補に無い語をそのまま確定できるか。マスタへの新規追加を兼ねる入力欄で使う。</summary>
    public static readonly DependencyProperty AllowNewProperty =
        DependencyProperty.Register(nameof(AllowNew), typeof(bool), typeof(SuggestBox),
            new PropertyMetadata(true));

    /// <summary>確定したときに、選ばれた文字列を引数にして実行される。</summary>
    public static readonly DependencyProperty CommitCommandProperty =
        DependencyProperty.Register(nameof(CommitCommand), typeof(ICommand), typeof(SuggestBox),
            new PropertyMetadata(null));

    /// <summary>候補の語から、頭に出す小さな絵を作る（U18・アバターの候補）。無ければ絵は出さない。</summary>
    public static readonly DependencyProperty IconSelectorProperty =
        DependencyProperty.Register(nameof(IconSelector), typeof(Func<string, ImageSource?>), typeof(SuggestBox),
            new PropertyMetadata(null));

    public Func<string, ImageSource?>? IconSelector
    {
        get => (Func<string, ImageSource?>?)GetValue(IconSelectorProperty);
        set => SetValue(IconSelectorProperty, value);
    }

    private bool _isCommitting;
    private bool _skipNextFocusOpen;

    public SuggestBox()
    {
        InitializeComponent();
    }

    public IEnumerable? Source
    {
        get => (IEnumerable?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool AllowNew
    {
        get => (bool)GetValue(AllowNewProperty);
        set => SetValue(AllowNewProperty, value);
    }

    public ICommand? CommitCommand
    {
        get => (ICommand?)GetValue(CommitCommandProperty);
        set => SetValue(CommitCommandProperty, value);
    }

    /// <summary>入力欄にフォーカスを移す。ボタンで出した直後にすぐ打てるように（対応アバターの「＋ 追加」）。</summary>
    public void FocusInput() => Input.Focus();

    private static void OnSourceChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
        => ((SuggestBox)element).Refresh();

    private static void OnPlaceholderChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
        => ((SuggestBox)element).Watermark.Text = args.NewValue as string ?? string.Empty;

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        Watermark.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_isCommitting)
        {
            return;
        }

        Refresh();
    }

    /// <summary>
    /// 空のまま触ったときは、何が選べるのかを全部見せる。
    ///
    /// ここで直接開くと、クリックで入った場合に同じ操作のマウスアップで
    /// Popup（StaysOpen=False）が閉じてしまうので、1回後回しにする。
    /// </summary>
    private void OnInputFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 確定のあと確認ダイアログを挟むと、閉じた拍子にここへ戻ってきて
        // 候補が開く。ユーザが入力欄を触ったわけではないので、その1回は開かない。
        if (_skipNextFocusOpen)
        {
            _skipNextFocusOpen = false;
            return;
        }

        Dispatcher.BeginInvoke(new Action(Refresh), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 候補の側へフォーカスが移っただけなら閉じない
        if (!Candidates.IsKeyboardFocusWithin)
        {
            DropDown.IsOpen = false;
        }
    }

    private void Refresh()
    {
        if (!IsKeyboardFocusWithin)
        {
            return;
        }

        var text = Input.Text.Trim();
        var all = (Source?.Cast<object?>().Select(entry => entry?.ToString())
            .Where(entry => !string.IsNullOrEmpty(entry))
            .Select(entry => entry!)
            .ToList()) ?? [];

        // 前方一致を先に、部分一致を後に。探している語が上に来るようにする
        var matches = text.Length == 0
            ? all
            : all.Where(entry => entry.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(entry => entry.StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
                .ThenBy(entry => entry, StringComparer.CurrentCulture)
                .ToList();

        var items = matches
            .Select(entry => new Suggestion { Value = entry, Display = entry, IconFactory = IconSelector })
            .ToList();

        var exists = all.Any(entry => string.Equals(entry, text, StringComparison.CurrentCultureIgnoreCase));
        if (AllowNew && text.Length > 0 && !exists)
        {
            items.Insert(0, new Suggestion { Value = text, Display = text, IsNew = true });
        }

        Candidates.ItemsSource = items;
        Candidates.SelectedIndex = items.Count > 0 ? 0 : -1;
        DropDown.IsOpen = items.Count > 0;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                Commit();
                e.Handled = true;
                break;

            case Key.Escape:
                DropDown.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (!DropDown.IsOpen)
        {
            Refresh();
            return;
        }

        var count = Candidates.Items.Count;
        if (count == 0)
        {
            return;
        }

        var next = Candidates.SelectedIndex + delta;
        Candidates.SelectedIndex = ((next % count) + count) % count;
        Candidates.ScrollIntoView(Candidates.SelectedItem);
    }

    private void OnCandidateClicked(object sender, MouseButtonEventArgs e)
    {
        // 離した場所が候補の行の上のときだけ決める。一覧全体で拾っているので、
        // スクロールバーをドラッグして離しただけでも、先頭（開くたびに選ばれている）で確定していた
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(Candidates, source) is not ListBoxItem)
        {
            return;
        }

        if (Candidates.SelectedItem is Suggestion)
        {
            Commit();
            e.Handled = true;
        }
    }

    private void Commit()
    {
        var value = Candidates.SelectedItem is Suggestion suggestion
            ? suggestion.Value
            : Input.Text.Trim();

        if (value.Length == 0)
        {
            return;
        }

        // 入力を消すとTextChangedが走って候補が開き直してしまう。
        // 選び終えた直後に一覧が出るのは邪魔だし、この後の処理で候補自体が
        // 変わることもあるので、その場に古い一覧が残ってしまう。
        _isCommitting = true;
        try
        {
            DropDown.IsOpen = false;
            Input.Clear();
            Watermark.Visibility = Visibility.Visible;
        }
        finally
        {
            _isCommitting = false;
        }

        // 確認ダイアログはこの中で開く。閉じた拍子にフォーカスが戻ってくるので、
        // 実行する前から「その戻りでは開かない」と決めておく必要がある。
        _skipNextFocusOpen = true;

        if (CommitCommand?.CanExecute(value) == true)
        {
            CommitCommand.Execute(value);
        }

        // 続けて足せるようにフォーカスは残す（候補は次に入力したときに出す）
        Input.Focus();

        // ダイアログが出ずフォーカスも動かなかったときは、印が余る。
        // 溜まった入力を処理し切ったところで片付ける。
        Dispatcher.BeginInvoke(
            new Action(() => _skipNextFocusOpen = false),
            System.Windows.Threading.DispatcherPriority.Input);
    }
}
