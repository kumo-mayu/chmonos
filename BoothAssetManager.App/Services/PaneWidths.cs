using System.Windows;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Services;

/// <summary>
/// ドラッグで変えられる画面の幅（ユーザ判断 2026-09-14）。
///
/// **覚えるのは画面ごと**（ui-state.json の <c>paneWidths</c>）。境目のダブルクリックでその1か所、設定画面で全部を既定に戻す。
/// **範囲は場所ごとに決めてある（<see cref="All"/>）。**最小は中身が崩れない幅、最大は反対側を潰さない幅。
/// 手で書き換えた値もここで範囲に収める。
/// ドラッグの間は手元で持ち、止まってから 0.4 秒で1回だけ書く（1px 動くたびにファイルを書かない）。
/// </summary>
public sealed class PaneWidths(SettingsService settings, CommandHandler commands)
{
    public sealed record Pane(double Default, double Min, double Max);

    /// <summary>
    /// 場所ごとの既定・最小・最大（px）。既定は分ける前の固定幅。
    /// 最小：一覧は名前が「…」だけにならない幅、商品ページの左は絵と説明が読める幅、編集の右は入力欄のラベルと値が1行に並ぶ幅。
    /// 最大：一覧は反対側に詳細が読める幅を残す、商品ページの左は右の列（最小320）を残す。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Pane> All = new Dictionary<string, Pane>(StringComparer.Ordinal)
    {
        ["nav"] = new(208, 180, 320),
        ["search.filter"] = new(286, 240, 480),
        ["folder.list"] = new(420, 300, 760),
        ["modifications.list"] = new(400, 300, 720),
        ["resolve.list"] = new(330, 260, 640),
        ["avatars.list"] = new(360, 280, 640),
        ["tags.list"] = new(340, 260, 600),
        ["attributes.list"] = new(340, 260, 600),
        ["item.left"] = new(660, 460, 1100),
        ["modification.left"] = new(660, 460, 1100),
        ["edit.right"] = new(480, 380, 800),
    };

    private readonly object _gate = new();
    private Dictionary<string, double>? _current;
    private int _version;

    public double Get(string key)
    {
        var pane = All[key];
        lock (_gate)
        {
            return Current().TryGetValue(key, out var width) ? Math.Clamp(width, pane.Min, pane.Max) : pane.Default;
        }
    }

    public GridLength Column(string key) => new(Get(key));

    /// <summary>ドラッグで変わった幅。範囲に収めて覚え、少し待ってから書く。</summary>
    public void Set(string key, double width)
    {
        if (double.IsNaN(width) || width <= 0)
        {
            return;
        }

        var pane = All[key];
        lock (_gate)
        {
            Current()[key] = Math.Round(Math.Clamp(width, pane.Min, pane.Max));
        }

        ScheduleSave();
    }

    /// <summary>その場所を既定の幅に戻す（境目のダブルクリック）。</summary>
    public void Reset(string key)
    {
        lock (_gate)
        {
            Current().Remove(key);
        }

        ScheduleSave();
    }

    /// <summary>全部を既定の幅に戻す（設定画面）。</summary>
    public void ResetAll()
    {
        lock (_gate)
        {
            _current = new Dictionary<string, double>(StringComparer.Ordinal);
        }

        ScheduleSave();
        AllReset?.Invoke();
    }

    /// <summary>
    /// 全部を戻した。**開きっぱなしの画面（ナビ・検索の絞り込み）だけが聞く。**
    /// ほかの画面は開くたびに作り直すので、次に開いたときに既定の幅で出る（聞かせると、閉じた画面を放せなくなる）。
    /// </summary>
    public event Action? AllReset;

    private Dictionary<string, double> Current()
        => _current ??= new Dictionary<string, double>(settings.UiState.PaneWidths, StringComparer.Ordinal);

    private void ScheduleSave()
    {
        var version = Interlocked.Increment(ref _version);
        SaveLaterAsync(version).Forget();
    }

    private async Task SaveLaterAsync(int version)
    {
        await Task.Delay(400);
        if (version != Volatile.Read(ref _version))
        {
            return;
        }

        Dictionary<string, double> snapshot;
        lock (_gate)
        {
            snapshot = new Dictionary<string, double>(Current(), StringComparer.Ordinal);
        }

        await commands.ExecuteAsync(new UiCommand.ChangeUiState(state => state with { PaneWidths = snapshot }));
    }
}
