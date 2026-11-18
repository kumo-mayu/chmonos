using Chmonos.Core.Commands;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>
/// 統計の各欄を開いているか（ui-state.json の <c>statsPanelsOpen</c>。ユーザ判断 2026-11-19・open.md 110）。
///
/// 左の一覧のつまみ（<see cref="PaneWidths.IsCollapsed"/>）と同じく次の起動まで覚える。
/// 畳んだ鍵の一覧ではなく「人が決めた開き方」を鍵ごとに持つ：「軸どうしの関係」だけ既定が畳みなので、畳んだ一覧では「人が開いた」を表せない。
/// 統計の画面は開くたびに作り直すが、ここは起動の間ずっと居るので手元に持つ（書き込みは裏で済むので、ファイルから読み直すと古い値を読み得る）
/// </summary>
public sealed class StatsPanels(SettingsService settings, CommandHandler commands)
{
    /// <summary>既定で畳む欄（ユーザ指示 2026-11-18：軸どうしの関係は見に行く人だけが開く）。</summary>
    public const string Correlations = "correlations";

    private readonly object _gate = new();
    private Dictionary<string, bool>? _open;

    /// <summary>人が決めていなければ既定（「軸どうしの関係」だけ畳み、ほかは開く）。</summary>
    public static bool DefaultOpen(string key) => key != Correlations;

    public bool IsOpen(string key)
    {
        lock (_gate)
        {
            return Open().TryGetValue(key, out var open) ? open : DefaultOpen(key);
        }
    }

    public void SetOpen(string key, bool open)
    {
        lock (_gate)
        {
            if (Open().TryGetValue(key, out var current) ? current == open : DefaultOpen(key) == open)
            {
                return;
            }

            Open()[key] = open;
        }

        // 書くときは今のファイルの値に当てる（手元の控えを丸ごと書くと、間にほかから書いた分を潰す）
        commands.ExecuteAsync(new UiCommand.ChangeUiState(state =>
        {
            var next = new Dictionary<string, bool>(state.StatsPanelsOpen ?? new Dictionary<string, bool>(), StringComparer.Ordinal)
            {
                [key] = open,
            };
            return state with { StatsPanelsOpen = next };
        })).Forget();
    }

    private Dictionary<string, bool> Open()
        => _open ??= new Dictionary<string, bool>(
            settings.UiState.StatsPanelsOpen ?? new Dictionary<string, bool>(), StringComparer.Ordinal);
}
