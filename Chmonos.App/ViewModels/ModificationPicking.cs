using Chmonos.App.Services;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 「どの改変に足すか」を選ばせる画面の組み立てと、その答えを改変に落とすところ。
///
/// 商品ページ（1件を足す・Unityへ送って足す）と検索画面（選んだ物をまとめて足す #44）の両方が使う。
/// 片方だけ直して選び方が食い違わないよう、ここ1か所に置く。
/// </summary>
public static class ModificationPicking
{
    /// <summary>ダイアログの中身を組む。呼ぶ場面ごとに文言だけ変える。</summary>
    public static PickModificationDialogViewModel BuildDialog(
        AppServiceContainer services,
        string title,
        string headingText,
        string contextText,
        IReadOnlyList<ModificationRecord> records,
        string existingLabel,
        string commitLabel,
        string emptyText)
    {
        var registry = services.Store.Avatars.Load();
        var names = Core.Services.AvatarNames.Map(registry.Entries);

        string NameOf(AvatarRegistryEntry entry) => names[entry.ItemId];

        // 候補はアバターだけ。重なる名前にショップ名を付ける Map は、アバターでない物も含む全件で
        // 作ったまま（アバターの管理画面・商品ページと同じ名前を出すため）
        var avatarEntries = registry.Entries.Where(Core.Services.AvatarService.IsAvatar).ToList();

        var avatarNames = avatarEntries
            .Select(NameOf)
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(text => text, StringComparer.CurrentCulture)
            .ToList();

        var rows = records
            .Select(record => new PickModificationRowViewModel
            {
                Record = record,
                AvatarText = registry.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.ItemId, record.AvatarItemId, StringComparison.Ordinal))
                    is { } found
                        ? NameOf(found)
                        : record.AvatarItemId,
            })
            .ToList();

        return new PickModificationDialogViewModel(
            title,
            headingText,
            contextText,
            rows,
            avatarNames,
            text => avatarEntries.FirstOrDefault(entry =>
                string.Equals(NameOf(entry), text, StringComparison.CurrentCultureIgnoreCase))?.ItemId)
        {
            ExistingLabel = existingLabel,
            CommitLabel = commitLabel,
            EmptyText = emptyText,
        };
    }

    /// <summary>
    /// 選ばれた改変を返す。「新しく作る」なら作ってから返す。
    /// 作れなかったときは理由を出して null を返す。
    /// </summary>
    /// <param name="project">作った改変にその場で紐付けるプロジェクト。送る先が決まっているときだけ渡す。</param>
    public static async Task<ModificationRecord?> ResolvePickedAsync(
        AppServiceContainer services,
        PickModificationDialogViewModel model,
        string title,
        string? project)
    {
        var record = model.Picked?.Record;

        if (model.MakingNew)
        {
            if (model.NewAvatarItemId is not { } avatarItemId)
            {
                return null;
            }

            if (await services.Commands.ExecuteAsync(
                    new Core.Commands.UiCommand.CreateModification(avatarItemId, model.NewName.Trim()))
                is Core.Commands.CommandResult.ModificationCreated created)
            {
                record = created.Record;

                // 作ったばかりの改変には紐付け先が無い。**いま送るプロジェクトで確定している**
                // ので、ここで付けておく（後から手で選ばせる意味が無い）
                if (project is not null)
                {
                    await services.Commands.ExecuteAsync(
                        new Core.Commands.UiCommand.SetModificationProject(record.Id, project));
                }
            }
        }

        if (record is null)
        {
            Services.Notice.Show(
                "改変を作れませんでした。名前を変えて、もう一度試してください。",
                title,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        return record;
    }
}
