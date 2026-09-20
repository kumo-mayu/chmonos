using BoothAssetManager.App.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>改変の画面：Unity・VCC（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ModificationHubViewModel
{
    // ---- Unity・VCC ----

    private void OpenVcc()
    {
        Status = VccLaunch.Open() switch
        {
            VccOpenResult.Launched => "VCC を起動しました。",
            VccOpenResult.BroughtToFront => "VCC は開いていたので、手前に出しました。",
            VccOpenResult.AlreadyOpenNotFront =>
                "VCC は開いています。手前に出せなかったので、タスクバーの VCC を押して切り替えてください。",
            VccOpenResult.NotInstalled => VccMissingText,
            _ => "VCC を起動できませんでした。スタートメニューから開いてみてください。",
        };
    }

    /// <summary>プロジェクトを開く。**結果を必ず言う**（開いていたら手前に出るだけで、何も起きなかったように見える）。</summary>
    private void OpenProject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Status = UnityOpenText.For(UnityLaunch.OpenProject(path), ProjectNameOf(path));
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
            _services, row.Record, row.Index, row.Member, row.Name, row.FileText, item, (text, _) => Status = text);

        // 記録した行（どのファイルを使ったか）を一覧に出す
        if (recorded)
        {
            await RefreshRecordsAsync();
        }
    }
}
