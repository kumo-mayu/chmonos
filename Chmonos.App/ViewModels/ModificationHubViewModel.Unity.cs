using Chmonos.App.Services;

namespace Chmonos.App.ViewModels;

/// <summary>改変の画面：Unity・VCC（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ModificationHubViewModel
{
    // ---- Unity・VCC ----

    private void OpenVcc()
    {
        var result = VccLaunch.Open();
        ToolNotice.Set(OpenResultText(result, "VCC"), !IsOpenSuccess(result));
    }

    private void OpenAlcom()
    {
        var result = AlcomLaunch.Open();
        ToolNotice.Set(OpenResultText(result, "ALCOM"), !IsOpenSuccess(result));
    }

    /// <summary>起動できた・手前に出せたのは済んだこと。それ以外は、自分で切り替えるか入れ直すかが要るので警告の色で出す。</summary>
    private static bool IsOpenSuccess(AppOpenResult result) => result is AppOpenResult.Launched or AppOpenResult.BroughtToFront;

    /// <summary>VCC と ALCOM で同じ結果を同じ文で言う（どちらも起動するだけで、結果の種類が同じ）。</summary>
    internal static string OpenResultText(AppOpenResult result, string app) => result switch
    {
        AppOpenResult.Launched => $"{app}を起動しました。",
        AppOpenResult.BroughtToFront => $"{app}は開いていたので、手前に出しました。",
        AppOpenResult.AlreadyOpenNotFront =>
            $"{app}は開いています。手前に出せなかったので、タスクバーの{app}を押して切り替えてください。",
        // 画面を開いたときにはあったが、押すまでの間に消された（アンインストールなど）
        AppOpenResult.NotInstalled => $"{app}が見つかりませんでした。{app}を入れ直すと、ここから開けます。",
        _ => $"{app}を起動できませんでした。スタートメニューから開いてみてください。",
    };

    /// <summary>プロジェクトを開く。**結果を必ず言う**（開いていたら手前に出るだけで、何も起きなかったように見える）。</summary>
    private async Task OpenProjectAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var result = UnityLaunch.OpenProject(path);
        var text = await UnityOpenText.ForAsync(_services, result, ProjectNameOf(path));

        // 開けなかった結果は別の操作が要るので警告の色で出す。プロジェクトの行と右の詳細は同じ鍵なので、両方に出る
        ShowNotice(
            $"project:{path}",
            text,
            warning: result is UnityOpenResult.NoEditorNoHub or UnityOpenResult.Missing or UnityOpenResult.Failed);
    }

    /// <summary>
    /// 使ったもの1件を Unity で示す（ユーザ仕様 2026-09-13）。入っていなければ取り込むか聞く。
    /// 中身は改変の詳細の「Unity ▾」の「選択」と共通（<see cref="UnityMemberSelect"/>）。
    /// </summary>
    private async Task SelectInUnityAsync(HubMemberRow? row)
    {
        if (row is null)
        {
            return;
        }

        var item = _items.GetValueOrDefault(row.ItemId) ?? await _services.Store.Items.LoadAsync(row.ItemId);
        var recorded = await UnityMemberSelect.RunAsync(
            _services, row.Record, row.Member, row.Name, row.FileText, item, Notices.LineOrWindow("Unityで選択", text => ShowNotice(row.NoticeKey, text)));

        // 記録した行（どのファイルを使ったか）を一覧に出す
        if (recorded)
        {
            await RefreshRecordsAsync();
        }
    }
}
