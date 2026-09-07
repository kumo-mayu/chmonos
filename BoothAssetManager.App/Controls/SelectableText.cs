using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 読み取り専用の <see cref="RichTextBox"/> に、URLをリンク化した本文を流し込む添付プロパティ。
///
/// TextBlockを使わない理由は2つある。WPFのTextBlockは文字を選択できないこと、
/// そしてBOOTHの説明文には対応アバターのURLが並ぶ文化があり、そこを踏めるようにしたいこと。
/// RichTextBoxの Document は依存関係プロパティではないのでバインドできず、ここで橋渡しする。
/// </summary>
public static class SelectableText
{
    /// <summary>URLとして拾う範囲。日本語の括弧や引用符は本文側の記号なので含めない。</summary>
    private static readonly Regex UrlPattern = new(
        @"https?://[^\s　<>""'（）「」『』【】]+",
        RegexOptions.Compiled);

    /// <summary>URLの末尾に紛れ込みやすい記号。リンクからは外して本文へ戻す。</summary>
    private static readonly char[] TrailingNoise = ".,;:!?。、）)】」』>".ToCharArray();

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(SelectableText),
            new PropertyMetadata(null, OnTextChanged));

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not RichTextBox box)
        {
            return;
        }

        box.IsReadOnly = true;
        box.IsDocumentEnabled = true;

        // 中身のScrollViewerに横取りされると外側のページがスクロールしなくなるので、親へ流し直す。
        box.PreviewMouseWheel -= ForwardMouseWheel;
        box.PreviewMouseWheel += ForwardMouseWheel;

        box.Document = Build(args.NewValue as string);
    }

    private static void ForwardMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (args.Handled || sender is not RichTextBox box)
        {
            return;
        }

        args.Handled = true;
        box.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = box,
        });
    }

    private static FlowDocument Build(string? text)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            LineHeight = 21,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        document.Blocks.Add(paragraph);

        if (string.IsNullOrEmpty(text))
        {
            return document;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                paragraph.Inlines.Add(new LineBreak());
            }

            AppendLine(paragraph, lines[index]);
        }

        return document;
    }

    private static void AppendLine(Paragraph paragraph, string line)
    {
        var position = 0;

        foreach (Match match in UrlPattern.Matches(line))
        {
            var url = match.Value.TrimEnd(TrailingNoise);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (match.Index > position)
            {
                paragraph.Inlines.Add(new Run(line[position..match.Index]));
            }

            var link = new Hyperlink(new Run(url)) { NavigateUri = uri };
            link.RequestNavigate += OnRequestNavigate;
            paragraph.Inlines.Add(link);

            // 末尾から外した記号は本文として続ける
            position = match.Index + url.Length;
        }

        if (position < line.Length)
        {
            paragraph.Inlines.Add(new Run(line[position..]));
        }
    }

    private static void OnRequestNavigate(object sender, RequestNavigateEventArgs args)
    {
        args.Handled = true;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = args.Uri.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // ブラウザが開けなくてもアプリは動き続ける
        }
    }
}
