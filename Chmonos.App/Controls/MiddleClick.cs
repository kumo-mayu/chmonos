using System.Windows;
using System.Windows.Input;

namespace Chmonos.App.Controls;

/// <summary>
/// 中クリックで BOOTH（ブラウザ）を開く近道を、押せる字に付ける添付プロパティ（open.md 136・ユーザ判断 2026-11-18）。
/// 左クリックがアプリの中へ行く字（ショップ名・ID のコピー）でも、中クリックは外へ出る。カードの中クリック
/// （<c>ItemCardResources.xaml.cs</c> の <c>OnCardMouseDown</c>）と同じく、知っている人だけが使う近道なので、吹き出しには書かない。
///
/// 下ろした時（MouseDown）に開く。カードと揃える。中のボタンは選ぶ・ドラッグに使わないので、離すまで待つ理由が無い。
/// 済んだ下ろし（中の部品が先に開いた）は受けない。字とその親の両方に付いていても、開くのは1回にする。
/// なぞって選べる字（<see cref="SelectableLink"/>）は押下を自分で済ませるので、そちらは自分で受ける（<see cref="SelectableLink.MiddleCommand"/>）。
/// 行き先が無い時（<see cref="ICommand.CanExecute"/> が偽）は何もせず、下ろしたことも親へそのまま流す。
/// </summary>
public static class MiddleClick
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(MiddleClick), new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    /// <summary>命令に渡す物（一覧の行ならその行）。</summary>
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
        "CommandParameter", typeof(object), typeof(MiddleClick));

    public static object? GetCommandParameter(DependencyObject element) => element.GetValue(CommandParameterProperty);

    public static void SetCommandParameter(DependencyObject element, object? value) => element.SetValue(CommandParameterProperty, value);

    private static readonly MouseButtonEventHandler Handler = OnMouseDown;

    private static void OnCommandChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        // 同じ部品に二度付けないよう、外してから付ける
        switch (element)
        {
            case UIElement ui:
                ui.RemoveHandler(Mouse.MouseDownEvent, Handler);
                if (args.NewValue is not null)
                {
                    ui.AddHandler(Mouse.MouseDownEvent, Handler);
                }

                break;
            case ContentElement content:
                content.RemoveHandler(Mouse.MouseDownEvent, Handler);
                if (args.NewValue is not null)
                {
                    content.AddHandler(Mouse.MouseDownEvent, Handler);
                }

                break;
        }
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle
            || sender is not DependencyObject element
            || GetCommand(element) is not { } command)
        {
            return;
        }

        var parameter = GetCommandParameter(element);
        if (!command.CanExecute(parameter))
        {
            return;
        }

        command.Execute(parameter);
        e.Handled = true;
    }
}
