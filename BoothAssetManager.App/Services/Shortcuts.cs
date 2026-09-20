using System.Windows.Input;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.Services;

/// <summary>ショートカットで呼べる操作（#43）。</summary>
public enum ShortcutAction
{
    SaveAndNext,
    Skip,
    FocusSearch,
    Back,
    Forward,
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
        ShortcutAction.SaveAndNext => "保存して次へ（編集）",
        ShortcutAction.Skip => "スキップ（編集）",
        ShortcutAction.FocusSearch => "検索欄へ",
        ShortcutAction.Back => "直前の画面へ戻る",
        ShortcutAction.Forward => "戻った先から進む",
        _ => action.ToString(),
    };

    public static string GestureOf(ShortcutSettings settings, ShortcutAction action) => action switch
    {
        ShortcutAction.SaveAndNext => settings.SaveAndNext,
        ShortcutAction.Skip => settings.Skip,
        ShortcutAction.FocusSearch => settings.FocusSearch,
        ShortcutAction.Back => settings.Back,
        ShortcutAction.Forward => settings.Forward,
        _ => string.Empty,
    };

    public static ShortcutSettings With(ShortcutSettings settings, ShortcutAction action, string gesture) => action switch
    {
        ShortcutAction.SaveAndNext => settings with { SaveAndNext = gesture },
        ShortcutAction.Skip => settings with { Skip = gesture },
        ShortcutAction.FocusSearch => settings with { FocusSearch = gesture },
        ShortcutAction.Back => settings with { Back = gesture },
        ShortcutAction.Forward => settings with { Forward = gesture },
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
                _ => part,
            });
        return string.Join(" + ", parts);
    }

    /// <summary>文字の欄でカーソルを動かすキー。文字の欄にいるときは、この割り当ては働かせない。</summary>
    public static bool IsTextEditingKey(Key key)
        => key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End
            or Key.PageUp or Key.PageDown or Key.Back or Key.Delete;
}
