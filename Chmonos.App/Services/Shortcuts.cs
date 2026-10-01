using System.Windows.Input;
using Chmonos.Core.Models;

namespace Chmonos.App.Services;

/// <summary>ショートカットで呼べる操作（#43）。</summary>
public enum ShortcutAction
{
    SaveAndNext,
    Skip,
    Previous,
    FindInPage,
    Back,
    Forward,
    ZoomIn,
    ZoomOut,
    ZoomReset,
}

/// <summary>
/// ショートカットの文字（"Ctrl+Enter"）とキーの行き来。
///
/// 設定には人が読める文字で持つので、読み書きをここ1か所に集める。
/// WPF のキーの型を使うので Core ではなく App に置く。
/// </summary>
public static class Shortcuts
{
    public static string ActionLabel(ShortcutAction action) => action switch
    {
        // 未確定の画面の確定も同じキー（ユーザ判断 2026-10-01。どちらも「この1件を決めて次へ」）。設定の行で両方が分かるように書く
        ShortcutAction.SaveAndNext => "編集画面で保存して次へ・未確定で確定",
        ShortcutAction.Skip => "編集画面でスキップ",
        ShortcutAction.Previous => "編集画面で前へ",
        ShortcutAction.FindInPage => "画面の中を探す",
        ShortcutAction.Back => "直前の画面へ戻る",
        ShortcutAction.Forward => "戻った先から進む",
        ShortcutAction.ZoomIn => "表示を大きくする",
        ShortcutAction.ZoomOut => "表示を小さくする",
        ShortcutAction.ZoomReset => "表示の大きさを100%に戻す",
        _ => action.ToString(),
    };

    public static string GestureOf(ShortcutSettings settings, ShortcutAction action) => action switch
    {
        ShortcutAction.SaveAndNext => settings.SaveAndNext,
        ShortcutAction.Skip => settings.Skip,
        ShortcutAction.Previous => settings.Previous,
        ShortcutAction.FindInPage => settings.FindInPage,
        ShortcutAction.Back => settings.Back,
        ShortcutAction.Forward => settings.Forward,
        ShortcutAction.ZoomIn => settings.ZoomIn,
        ShortcutAction.ZoomOut => settings.ZoomOut,
        ShortcutAction.ZoomReset => settings.ZoomReset,
        _ => string.Empty,
    };

    public static ShortcutSettings With(ShortcutSettings settings, ShortcutAction action, string gesture) => action switch
    {
        ShortcutAction.SaveAndNext => settings with { SaveAndNext = gesture },
        ShortcutAction.Skip => settings with { Skip = gesture },
        ShortcutAction.Previous => settings with { Previous = gesture },
        ShortcutAction.FindInPage => settings with { FindInPage = gesture },
        ShortcutAction.Back => settings with { Back = gesture },
        ShortcutAction.Forward => settings with { Forward = gesture },
        ShortcutAction.ZoomIn => settings with { ZoomIn = gesture },
        ShortcutAction.ZoomOut => settings with { ZoomOut = gesture },
        ShortcutAction.ZoomReset => settings with { ZoomReset = gesture },
        _ => settings,
    };

    /// <summary>"Ctrl+Shift+Enter" を読む。読めなければ（空・知らない名前）null。</summary>
    public static (Key Key, ModifierKeys Modifiers)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var modifiers = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < parts.Length - 1; index++)
        {
            modifiers |= parts[index].ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => ModifierKeys.None,
            };
        }

        return parts.Length > 0 && Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key) && key != Key.None
            ? (key, modifiers)
            : null;
    }

    /// <summary>押されたキーを設定に書く形にする。</summary>
    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        // Enter と Return は同じ値で、名前にすると Return になる。見慣れた方で書く
        parts.Add(key == Key.Return ? "Enter" : key.ToString());
        return string.Join('+', parts);
    }

    /// <summary>
    /// 設定画面に見せる形。矢印キーは言葉で書く——記号の「→」は小さい字だと横棒に見えた（実機で確認）。
    /// </summary>
    public static string Display(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return "（割り当てなし）";
        }

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part switch
            {
                "Right" => "右矢印",
                "Left" => "左矢印",
                "Up" => "上矢印",
                "Down" => "下矢印",

                // 記号のキーは .NET の名前（OemPlus・D0）のままだと読めない。矢印と同じく言葉で書く
                // （「Ctrl + +」は区切りと見分けにくい）
                "OemPlus" => "プラス",
                "OemMinus" => "マイナス",
                "Add" => "テンキーのプラス",
                "Subtract" => "テンキーのマイナス",
                ['D', >= '0' and <= '9'] => part[1..],
                _ when part.StartsWith("NumPad", StringComparison.Ordinal) => "テンキーの" + part["NumPad".Length..],
                _ => part,
            });
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// 押されたキーが割り当てに当たるか。同じ記号を打つキーは同じものとみなす：
    /// テンキーの＋－0 は上の段の＋－0 と同じ（ブラウザの拡大と同じ）。
    /// さらに「プラス」の割り当ては Shift を足して押しても当たる——「＋」を打つのに、US 配列は Shift＋「=」、
    /// JIS 配列は Shift＋「;」が要り、Ctrl＋＋ と覚えた人はそのまま Shift も押すため。
    /// </summary>
    public static bool Matches((Key Key, ModifierKeys Modifiers) gesture, Key key, ModifierKeys modifiers)
    {
        var wanted = SameSymbol(gesture.Key);
        if (wanted != SameSymbol(key))
        {
            return false;
        }

        if (wanted == Key.OemPlus && (gesture.Modifiers & ModifierKeys.Shift) == 0)
        {
            modifiers &= ~ModifierKeys.Shift;
        }

        return gesture.Modifiers == modifiers;

        static Key SameSymbol(Key key) => key switch
        {
            Key.Add => Key.OemPlus,
            Key.Subtract => Key.OemMinus,
            >= Key.NumPad0 and <= Key.NumPad9 => Key.D0 + (key - Key.NumPad0),
            _ => key,
        };
    }

    /// <summary>
    /// 文字の欄でカーソルを動かすキー。文字の欄にいるときは、この割り当ては働かせない。
    /// ただし Alt＋矢印と Ctrl+Shift＋矢印だけは欄の中でも横取りする（<c>MainWindow.xaml.cs</c>。前へは除く）。
    /// </summary>
    public static bool IsTextEditingKey(Key key)
        => key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End
            or Key.PageUp or Key.PageDown or Key.Back or Key.Delete;
}
