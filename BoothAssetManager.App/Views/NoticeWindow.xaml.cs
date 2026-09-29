using System.ComponentModel;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Views;

/// <summary>
/// 知らせと確認の窓。<see cref="Services.Notice"/> だけが作る（MessageBox の代わり。ユーザ指示 2026-09-29）。
/// ボタンの並び・既定・Esc の答えは <see cref="NoticeLayout"/>（MessageBox と同じに決める）。
/// </summary>
public partial class NoticeWindow : Window
{
    private readonly NoticeLayout _layout;
    private readonly string _caption;
    private readonly string _text;
    private Button? _defaultButton;
    private bool _answered;

    private NoticeWindow(Window? owner, string text, string caption, NoticeLayout layout, MessageBoxImage image)
    {
        InitializeComponent();
        _layout = layout;
        _caption = caption;
        _text = text;

        Title = caption;
        Body.Text = text;

        // 読み上げと確かめの道具（ui-check の Get-ChmonosDialogText）が本文を名前で読む。入力欄の名前は既定で空
        AutomationProperties.SetName(Body, text);

        ShowMark(image);
        BuildButtons();

        if (owner is not null)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            // 持ち主が無い（主の窓より前・アプリが後ろにいる）ときは、MessageBox と同じく画面の中央に出し、
            // タスクバーにも出す（出さないと、ほかの窓の後ろに回ったときに戻る先が無い）
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
        }

        // 本文を自分で縦に送るので、DialogFit には中身を包ませない（包むと送る入れ物が2重になり、ボタンの帯まで流れる）
        Services.DialogFit.Prepare(this, scrollsItself: true);

        SourceInitialized += (_, _) =>
        {
            if (!_layout.CanDismiss)
            {
                DisableCloseButton();
            }
        };
        Loaded += (_, _) =>
        {
            // フォーカスは既定のボタン（Enter でも Space でも既定の答えになる。MessageBox と同じ）
            _defaultButton?.Focus();
            PlaySound(image);
        };
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
    }

    /// <summary>押された答え。Esc・× で閉じたときは <see cref="NoticeLayout.Dismiss"/>。</summary>
    private NoticeAnswer Answer { get; set; }

    /// <summary>出して答えを待つ。画面のスレッドから呼ぶ（<see cref="Services.Notice"/> が渡してくる）。</summary>
    internal static MessageBoxResult Ask(
        Window? owner, string text, string caption, NoticeLayout layout, MessageBoxImage image)
    {
        var dialog = new NoticeWindow(owner, text, caption, layout, image);
        dialog.ShowDialog();
        return (MessageBoxResult)dialog.Answer;
    }

    private void BuildButtons()
    {
        foreach (var choice in _layout.Choices)
        {
            var isDefault = choice.Answer == _layout.Default;
            var button = new Button
            {
                Content = choice.Label,
                MinWidth = 88,
                Margin = new Thickness(ButtonRow.Children.Count == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = choice.Answer == _layout.Dismiss,
            };

            if (isDefault)
            {
                // 主の答え（OK・はい）が既定なら重要なボタンの見た目にする（ほかの窓の主のボタンと同じ）。
                // 取り返しのつかない操作は既定をキャンセル側に倒して止めている（ui-dialogs.md「知らせのアイコン」）ので、
                // そのときにキャンセルを青く塗ると「押してほしいボタン」に見える。枠だけ青くして、Enter の行き先だけを示す
                if (choice.Answer is NoticeAnswer.Ok or NoticeAnswer.Yes)
                {
                    button.Style = (Style)FindResource("PrimaryButton");
                }
                else
                {
                    button.SetResourceReference(BorderBrushProperty, "Accent");
                }

                _defaultButton = button;
            }

            var answer = choice.Answer;
            button.Click += (_, _) => Choose(answer);
            ButtonRow.Children.Add(button);
        }
    }

    private void Choose(NoticeAnswer answer)
    {
        Answer = answer;
        _answered = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_answered)
        {
            return;
        }

        // × と Alt+F4。Esc は IsCancel のボタンを押すので、ここへは来ない
        if (_layout.Dismiss is { } dismiss)
        {
            Answer = dismiss;
            _answered = true;
        }
        else
        {
            // 「はい・いいえ」だけの窓はどちらかを選ぶまで閉じない（MessageBox と同じ。NoticeLayout）
            e.Cancel = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+C（と Ctrl+Insert）で題・本文・ボタンを写す（MessageBox と同じ）。
        // 本文の一部を選んでいるときは、入力欄が選んだ所だけを写す
        var copy = Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.C or Key.Insert;
        if (!copy || (Body.IsKeyboardFocusWithin && Body.SelectionLength > 0))
        {
            return;
        }

        Services.ClipboardText.TrySet(_layout.CopyText(_caption, _text));
        e.Handled = true;
    }

    private void ShowMark(MessageBoxImage image)
    {
        // MessageBoxImage は同じ値に2つずつ名前がある（Hand と Error と Stop・Exclamation と Warning・Asterisk と Information）
        var (circleFill, glyphStroke, glyph, triangle) = image switch
        {
            MessageBoxImage.Error => ("BadFill", "OnAccent", "M10.5,10.5 L19.5,19.5 M19.5,10.5 L10.5,19.5", false),
            MessageBoxImage.Question => ("AccentFill", "OnAccent", "M11,11.2 C11,6.4 19,6.4 19,11 C19,14.2 15,14.4 15,17.6 M15,22 L15,22.2", false),
            MessageBoxImage.Warning => ("Warn", "Surface", "M15,11 L15,19.2 M15,23.4 L15,23.6", true),
            MessageBoxImage.Information => ("AccentFill", "OnAccent", "M15,8.6 L15,8.8 M15,13 L15,21.6", false),
            _ => ((string?)null, (string?)null, (string?)null, false),
        };

        if (circleFill is null || glyphStroke is null || glyph is null)
        {
            return;
        }

        Mark.Visibility = Visibility.Visible;
        var shape = triangle ? (System.Windows.Shapes.Shape)MarkTriangle : MarkCircle;
        MarkCircle.Visibility = triangle ? Visibility.Collapsed : Visibility.Visible;
        MarkTriangle.Visibility = triangle ? Visibility.Visible : Visibility.Collapsed;
        shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, circleFill);
        MarkGlyph.Data = Geometry.Parse(glyph);
        MarkGlyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, glyphStroke);
    }

    /// <summary>MessageBox は印ごとに Windows の音を鳴らしていた。自前にしても同じ音を鳴らす。</summary>
    private static void PlaySound(MessageBoxImage image)
    {
        var sound = image switch
        {
            MessageBoxImage.Error => SystemSounds.Hand,
            MessageBoxImage.Question => SystemSounds.Question,
            MessageBoxImage.Warning => SystemSounds.Exclamation,
            MessageBoxImage.Information => SystemSounds.Asterisk,
            _ => null,
        };
        sound?.Play();
    }

    private const uint ScClose = 0xF060;
    private const uint MfGrayed = 0x0001;

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr window, bool revert);

    [DllImport("user32.dll")]
    private static extern int EnableMenuItem(IntPtr menu, uint item, uint enable);

    /// <summary>× を押せなくする（「はい・いいえ」だけの窓。MessageBox と同じ見た目にする）。</summary>
    private void DisableCloseButton()
    {
        var menu = GetSystemMenu(new WindowInteropHelper(this).Handle, false);
        if (menu != IntPtr.Zero)
        {
            EnableMenuItem(menu, ScClose, MfGrayed);
        }
    }
}
