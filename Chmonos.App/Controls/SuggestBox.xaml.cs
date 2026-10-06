using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>候補の名前のほかに、その候補を引ける語1つ（アバターの呼び方など）。<see cref="Label"/> は当たったときに出す札。</summary>
public sealed record SuggestHint(string Text, string Label);

/// <summary>
/// 候補1件に付ける案内。<see cref="Group"/> は何番目の群か（<see cref="SuggestBox.GroupHeadings"/> の添え字。小さい群が先に並ぶ）、
/// <see cref="Hints"/> は名前のほかに当たる語。
/// </summary>
public sealed record SuggestInfo(int Group, IReadOnlyList<SuggestHint> Hints);

/// <summary>候補1件。既存の語か、入力された新しい語か。</summary>
public sealed class Suggestion
{
    private ImageSource? _icon;
    private bool _iconLoaded;

    public required string Value { get; init; }

    public required string Display { get; init; }

    public bool IsNew { get; init; }

    /// <summary>この行の上に区切り線を引く（先に並べる群と、その下の群の境目）。</summary>
    public bool HasDividerAbove { get; init; }

    public Visibility DividerVisibility => HasDividerAbove ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>群の見出し（群の最初の行の上にだけ出す）。</summary>
    public string Heading { get; init; } = string.Empty;

    public Visibility HeadingVisibility => Heading.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>名前以外（呼び方など）で当たったときの「何で当たったか」。名前で当たった行は空。</summary>
    public string Note { get; init; } = string.Empty;

    public Visibility NoteVisibility => Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NewBadgeVisibility => IsNew ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>入らないときに間を「…」にするか（パスの候補。<see cref="SuggestBox.TrimMiddle"/>）。</summary>
    public bool TrimMiddle { get; init; }

    public Visibility EndTrimVisibility => TrimMiddle ? Visibility.Collapsed : Visibility.Visible;

    public Visibility MiddleTrimVisibility => TrimMiddle ? Visibility.Visible : Visibility.Collapsed;

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

    /// <summary>絵の無い候補の頭に出す頭文字（飾り記号を飛ばす。アバターの一覧と同じ）。絵があれば絵の下に隠れる。</summary>
    public string Initial => Chmonos.Core.Services.AvatarText.InitialOf(Display);

    // 候補の行の読み上げ名は、この文字列から作られる。無いと型の名前（…Controls.Suggestion）が読まれていた（点検 2026-09-23）
    public override string ToString() => IsNew ? $"{Display}（新規）" : Display;
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

    /// <summary>
    /// 打てる長さの上限（ユーザ判断 2026-09-20・I13）。名前の類は 60 文字。
    /// 0 なら上限なし（`TextBox.MaxLength` と同じ決まり）。
    /// </summary>
    public static readonly DependencyProperty MaxLengthProperty =
        DependencyProperty.Register(nameof(MaxLength), typeof(int), typeof(SuggestBox),
            new PropertyMetadata(0, OnMaxLengthChanged));

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

    /// <summary>
    /// 入らない候補の**間**を「…」にし、頭と末尾を残すか（既定は末尾を切る）。パスの候補（ファイルの場所・Unityプロジェクト）は
    /// 末尾の名前がいちばん要るので、長いパスを1行に出す決まり（`docs/spec/ui-rules.md`・<see cref="PathLine"/>）に揃える。
    /// 省いた行は吹き出しで全文を出す（メモ83・メモ84：長いと見えなかった）
    /// </summary>
    public static readonly DependencyProperty TrimMiddleProperty =
        DependencyProperty.Register(nameof(TrimMiddle), typeof(bool), typeof(SuggestBox), new PropertyMetadata(false));

    public bool TrimMiddle
    {
        get => (bool)GetValue(TrimMiddleProperty);
        set => SetValue(TrimMiddleProperty, value);
    }

    /// <summary>
    /// 先頭から数えてこの件数までの候補を「先に出す群」とし、残りとの間に区切り線を引く（持っているアバター → ほかのアバター。メモ32-②）。
    /// 0 以下なら群は分けない。絞り込んでも群の順は崩さず、各群の中で前方一致を先に並べる
    /// </summary>
    public static readonly DependencyProperty PrimaryCountProperty =
        DependencyProperty.Register(nameof(PrimaryCount), typeof(int), typeof(SuggestBox), new PropertyMetadata(0));

    public int PrimaryCount
    {
        get => (int)GetValue(PrimaryCountProperty);
        set => SetValue(PrimaryCountProperty, value);
    }

    /// <summary>
    /// 候補の語から、群と「名前以外で当たる語」を引く（アバターの候補。メモ48・メモ58）。
    /// 引けない語は群を分けない（<see cref="PrimaryCount"/> があればそちら）。
    /// </summary>
    public static readonly DependencyProperty InfoSelectorProperty =
        DependencyProperty.Register(nameof(InfoSelector), typeof(Func<string, SuggestInfo?>), typeof(SuggestBox),
            new PropertyMetadata(null));

    public Func<string, SuggestInfo?>? InfoSelector
    {
        get => (Func<string, SuggestInfo?>?)GetValue(InfoSelectorProperty);
        set => SetValue(InfoSelectorProperty, value);
    }

    /// <summary>
    /// 群の見出し（「所持アバター」「共通素体」「未所持アバター」）。<see cref="SuggestInfo.Group"/> の番号で引く。
    /// 候補が残っている群の最初の行の上にだけ出す。無ければ見出しは出さず、群の境目の線だけ
    /// </summary>
    public static readonly DependencyProperty GroupHeadingsProperty =
        DependencyProperty.Register(nameof(GroupHeadings), typeof(IReadOnlyList<string>), typeof(SuggestBox),
            new PropertyMetadata(null));

    public IReadOnlyList<string>? GroupHeadings
    {
        get => (IReadOnlyList<string>?)GetValue(GroupHeadingsProperty);
        set => SetValue(GroupHeadingsProperty, value);
    }

    /// <summary>
    /// 確定した語を欄に残す（ユーザ指摘 2026-09-19：新しい改変のアバターを選んだのに欄が空に戻り、
    /// 下に「アバター：」と出るだけで、何を選んだのか分かりにくかった）。
    /// 既定は消す——タグのように1件ずつ積む欄では、続けて次の語を打つため
    /// </summary>
    public static readonly DependencyProperty KeepsCommittedTextProperty =
        DependencyProperty.Register(nameof(KeepsCommittedText), typeof(bool), typeof(SuggestBox),
            new PropertyMetadata(false));

    public bool KeepsCommittedText
    {
        get => (bool)GetValue(KeepsCommittedTextProperty);
        set => SetValue(KeepsCommittedTextProperty, value);
    }

    /// <summary>
    /// 欄の今の文字（双方向）。選んだ後に打ち直したら、画面の側が「選んだ物」を外せるようにするため
    /// （欄には別の名前、中では前の選択、という食い違いを残さない）
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SuggestBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextPropertyChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (SuggestBox)d;
        var text = e.NewValue as string ?? string.Empty;
        if (box.Input.Text == text)
        {
            return;
        }

        // 外から入れた文字で候補を開かない（人が打ったわけではない）
        box._isCommitting = true;
        try
        {
            box.Input.Text = text;
        }
        finally
        {
            box._isCommitting = false;
        }
    }

    /// <summary>
    /// 欄の枠と地を消し、外側の枠に溶け込ませる。丸い枠の中に置くと、
    /// 既定の四角い枠だけが浮いて見えた（ユーザ指摘 2026-09-18：タグの管理の小分類を足す欄）
    /// </summary>
    public static readonly DependencyProperty IsFramelessProperty =
        DependencyProperty.Register(nameof(IsFrameless), typeof(bool), typeof(SuggestBox),
            new PropertyMetadata(false, OnIsFramelessChanged));

    public bool IsFrameless
    {
        get => (bool)GetValue(IsFramelessProperty);
        set => SetValue(IsFramelessProperty, value);
    }

    private static void OnIsFramelessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (SuggestBox)d;
        if ((bool)e.NewValue)
        {
            box.Input.BorderThickness = new Thickness(0);
            box.Input.Background = Brushes.Transparent;
        }
        else
        {
            box.Input.ClearValue(Control.BorderThicknessProperty);
            box.Input.ClearValue(Control.BackgroundProperty);
        }
    }

    private bool _isCommitting;
    private bool _skipNextFocusOpen;

    public SuggestBox()
    {
        InitializeComponent();
        Candidates.RowInvoked += OnCandidateInvoked;
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

    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
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

    /// <summary>
    /// 候補の元が差し替わったとき、閉じている窓は開かない（開くのは人が欄を触ったときだけ）。
    /// 選んで足すたびに候補の元が作り直される欄（素体の詳細のアバターを足す欄）で、足した後の読み直しが
    /// フォーカスの残った欄の窓を開き直し、欄の位置が決まる前の窓が画面の左上に残った（メモ73-①）。
    /// 開いている窓は、並びだけ引き直す
    /// </summary>
    private static void OnSourceChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        var box = (SuggestBox)element;
        if (box.DropDown.IsOpen)
        {
            box.Refresh();
        }
    }

    private static void OnPlaceholderChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        var box = (SuggestBox)element;
        box.Watermark.Text = args.NewValue as string ?? string.Empty;
        box.UpdateInputName();
    }

    /// <summary>
    /// 読み上げ・自動操作が見る入力欄の名前（点検 2026-09-23：中の TextBox が名前を持たず、何の欄か読み上げられなかった）。
    /// 置き場所で <c>AutomationProperties.Name</c> をこの部品に付ければそれを、無ければ薄い字の案内を中の欄へ渡す。
    /// 部品そのものに付けた名前は中の欄まで届かないので、ここで写す
    /// </summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == System.Windows.Automation.AutomationProperties.NameProperty)
        {
            UpdateInputName();
        }
    }

    private void UpdateInputName()
    {
        // 部品の中身を作る前（InitializeComponent の前）に来ることがある
        if (Input is null)
        {
            return;
        }

        var given = System.Windows.Automation.AutomationProperties.GetName(this);
        System.Windows.Automation.AutomationProperties.SetName(Input, string.IsNullOrEmpty(given) ? Placeholder : given);
    }

    private static void OnMaxLengthChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
        => ((SuggestBox)element).Input.MaxLength = (int)args.NewValue;

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        Watermark.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (Text != Input.Text)
        {
            Text = Input.Text;
        }

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

    /// <summary>
    /// フォーカスが残っている欄を押し直したときも、候補を開く。
    ///
    /// 決めた後はフォーカスを欄に残す（続けて足せるように）ので、次の1件を足そうと同じ欄を押しても
    /// GotKeyboardFocus は来ない。検索のユーザータグで、2つ目の大分類・小分類を足すときだけ候補が出なかった
    /// （ユーザ指摘 2026-09-29）。押し直しは人が候補を見たい合図なので開く。
    /// 文字を選んでいる（ドラッグで範囲を取った）ときは、選ぶ操作の邪魔をしないよう開かない。
    ///
    /// 離す直前（Preview）にその場で開く。まだ TextBox がマウスを掴んでいるので、候補の窓はマウスを掴まない。
    /// 離した後へ回すと候補の窓がマウスを掴み、次に別の欄を押した1回目が「候補を閉じる」だけに食われる
    /// （フォーカスで開くときと同じ掴まない形に揃える）
    /// </summary>
    private void OnInputClicked(object sender, MouseButtonEventArgs e)
    {
        if (DropDown.IsOpen || !Input.IsKeyboardFocused || Input.SelectionLength > 0)
        {
            return;
        }

        Refresh();
    }

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 候補の側へフォーカスが移っただけなら閉じない
        if (!Candidates.IsKeyboardFocusWithin)
        {
            DropDown.IsOpen = false;
        }
    }

    /// <summary>並べ終えた候補1行。<see cref="Hit"/> は名前でなく呼び方などで当たったときの、その語。</summary>
    public sealed record ArrangedRow(string Entry, int Group, bool DividerAbove, bool GroupStart, SuggestHint? Hit);

    /// <summary>
    /// 候補の並べ方。前方一致を先に、部分一致を後に（探している語が上に来る）。
    /// 群（<paramref name="info"/> の番号。無ければ先頭から primaryCount 件までが先の群）があるときは、群を先にして、各群の中でその並びにする。
    /// 区切り線は、群が変わる行の上に引く（候補が残っていない群は飛ばす）。
    /// 名前に入っていなくても、名前以外の語（<see cref="SuggestInfo.Hints"/>）に入っていれば当たる
    /// </summary>
    public static IReadOnlyList<ArrangedRow> Arrange(
        IReadOnlyList<string> all, string text, int primaryCount, Func<string, SuggestInfo?>? info = null)
    {
        bool Has(string value) => value.Contains(text, StringComparison.CurrentCultureIgnoreCase);

        var tagged = all
            .Select((entry, index) =>
            {
                var extra = info?.Invoke(entry);
                var group = extra?.Group ?? (primaryCount > 0 && index >= primaryCount ? 1 : 0);
                var hit = text.Length == 0 || Has(entry) ? null : extra?.Hints.FirstOrDefault(hint => Has(hint.Text));
                return (Entry: entry, Group: group, Hit: hit, Matches: text.Length == 0 || Has(entry) || hit is not null);
            })
            .Where(row => row.Matches);

        var ordered = text.Length == 0
            ? tagged.OrderBy(row => row.Group).ToList()
            : tagged.OrderBy(row => row.Group)
                .ThenByDescending(row => row.Entry.StartsWith(text, StringComparison.CurrentCultureIgnoreCase)
                    || (row.Hit?.Text.StartsWith(text, StringComparison.CurrentCultureIgnoreCase) ?? false))
                .ThenBy(row => row.Hit is not null)
                .ThenBy(row => row.Entry, StringComparer.CurrentCulture)
                .ToList();

        return ordered
            .Select((row, index) =>
            {
                var start = index == 0 || row.Group != ordered[index - 1].Group;
                return new ArrangedRow(row.Entry, row.Group, start && index > 0, start, row.Hit);
            })
            .ToList();
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

        var headings = GroupHeadings;
        var items = Arrange(all, text, PrimaryCount, InfoSelector)
            .Select(row => new Suggestion
            {
                Value = row.Entry,
                Display = row.Entry,
                IconFactory = IconSelector,
                HasDividerAbove = row.DividerAbove,
                Heading = row.GroupStart && headings is not null && row.Group >= 0 && row.Group < headings.Count ? headings[row.Group] : string.Empty,
                Note = row.Hit?.Label ?? string.Empty,
                TrimMiddle = TrimMiddle,
            })
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
                // 候補が出ていないとき（＝もう決めた後）は、食べずに窓の既定のボタンへ抜けさせる（D2）。
                // 常に食べていたので、打ち終えて Enter を何度押しても窓が決まらなかった。
                // 決める動き自体は変えない——候補に無い語をそのまま使う欄があるため
                e.Handled = DropDown.IsOpen;
                Commit();
                break;

            // 候補が出ているときだけ受ける。閉じているのに食べてしまうと、
            // この欄を載せた窓が Esc で閉じられなかった（名前を変える・小分類を移す・改変を選ぶ。D1）
            case Key.Escape when DropDown.IsOpen:
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

    /// <summary>読み上げ・自動操作が候補の行を「押した」。その行を選んで、クリックと同じに決める。</summary>
    private void OnCandidateInvoked(object row)
    {
        if (row is not Suggestion || !DropDown.IsOpen)
        {
            return;
        }

        Candidates.SelectedItem = row;
        Commit();
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
            if (KeepsCommittedText)
            {
                // 選んだ語をそのまま見せる（打ちかけの語ではなく、候補の表記で）
                Input.Text = value;
                Input.CaretIndex = value.Length;
            }
            else
            {
                Input.Clear();
                Watermark.Visibility = Visibility.Visible;
            }
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
