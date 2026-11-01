using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace Chmonos.App.Controls;

/// <summary>
/// なぞると選べて、ドラッグせずに押すと開く字（商品ページのショップ名）。
///
/// **Hyperlink では作れない。**RichTextBox の中の Hyperlink は、上でのドラッグが選択にならない（2026-11-01 にユーザが実機で指摘）。
/// そこで字はふつうの本文にし、離したときに「選んでいない・動かしていない・字の上」のときだけ開く。
/// 字の無い所（欄の右の余白）を押して開くと、押した覚えの無い画面へ移るので、字の上だけにする。
/// </summary>
public sealed class SelectableLink : RichTextBox
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SelectableLink));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(SelectableLink));

    /// <summary>字の無い所を押しても開くか（行全体を押して開く所。通知の行）。</summary>
    public static readonly DependencyProperty OpensOnBlankProperty =
        DependencyProperty.Register(nameof(OpensOnBlank), typeof(bool), typeof(SelectableLink));

    private Point? _pressedAt;

    public SelectableLink()
    {
        // RichTextBox は押下を自分で受けて「済んだ」にするので、済んだ物も受ける
        AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnReleased), handledEventsToo: true);
    }

    public bool OpensOnBlank
    {
        get => (bool)GetValue(OpensOnBlankProperty);
        set => SetValue(OpensOnBlankProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>押した扱いにするか。</summary>
    /// <param name="moved">押してから離すまでに動いた距離。</param>
    internal static bool IsClick(bool hasSelection, Vector moved, bool overText)
        => !hasSelection
            && overText
            && Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(this);
        base.OnPreviewMouseLeftButtonDown(e);
    }

    private void OnReleased(object sender, MouseButtonEventArgs e)
    {
        if (_pressedAt is not { } pressedAt)
        {
            return;
        }

        _pressedAt = null;
        var point = e.GetPosition(this);
        if (IsClick(!Selection.IsEmpty, point - pressedAt, OpensOnBlank || GetPositionFromPoint(point, snapToText: false) is not null))
        {
            Open();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            Open();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void Open()
    {
        if (Command is { } command && command.CanExecute(CommandParameter))
        {
            command.Execute(CommandParameter);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LinkPeer(this);

    /// <summary>読み上げと自動操作には、前の Hyperlink と同じくリンクとして見せる。</summary>
    private sealed class LinkPeer(SelectableLink owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Hyperlink;

        protected override string GetClassNameCore() => nameof(SelectableLink);

        public override object? GetPattern(PatternInterface patternInterface)
            => patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        public void Invoke() => ((SelectableLink)Owner).Open();
    }
}
