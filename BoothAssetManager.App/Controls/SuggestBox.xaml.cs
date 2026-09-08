using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BoothAssetManager.App.Controls;

/// <summary>候補1件。既存の語か、入力された新しい語か。</summary>
public sealed class Suggestion
{
    public required string Value { get; init; }

    public required string Display { get; init; }

    public bool IsNew { get; init; }

    public Visibility NewBadgeVisibility => IsNew ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// 候補付きの入力欄。
///
/// このツールの共通ルールとして、候補を出せる入力欄には必ず候補を付ける。
/// appTagや属性はユーザが自分で作る語彙なので、表記揺れ（「かわいい」「可愛い」）が
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

    private bool _isCommitting;

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
        => Dispatcher.BeginInvoke(new Action(Refresh), System.Windows.Threading.DispatcherPriority.Input);

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
            .Select(entry => new Suggestion { Value = entry, Display = entry })
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

        if (CommitCommand?.CanExecute(value) == true)
        {
            CommitCommand.Execute(value);
        }

        // 続けて足せるようにフォーカスは残す（候補は次に入力したときに出す）
        Input.Focus();
    }
}
