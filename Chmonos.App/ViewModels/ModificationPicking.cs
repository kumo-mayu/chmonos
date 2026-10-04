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
    /// <summary>持っている商品のID（ファイルかフォルダを1つ以上持つ。所持の定義）。アバターの候補を「持っている」で分けるのに使う。</summary>
    public static async Task<IReadOnlySet<string>> LoadOwnedItemIdsAsync(AppServiceContainer services)
        => (await services.Store.Items.LoadAllAsync()).Items
            .Where(item => item.Local.OwnedFiles.Count > 0 || item.Local.LocalFolders.Count > 0)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>ダイアログの中身を組む。呼ぶ場面ごとに文言だけ変える。</summary>
    public static PickModificationDialogViewModel BuildDialog(
        AppServiceContainer services,
        string title,
        string headingText,
        string contextText,
        IReadOnlyList<ModificationRecord> records,
        string existingLabel,
        string commitLabel,
        string emptyText,
        MemberFilePickViewModel? files = null,
        IReadOnlySet<string>? ownedItemIds = null)
    {
        var registry = services.Store.Avatars.Load();
        var names = Core.Services.AvatarNames.Map(registry.Entries);

        string NameOf(AvatarRegistryEntry entry) => names[entry.ItemId];

        // 候補はアバターだけ。重なる名前にショップ名を付ける Map は、アバターでない物も含む全件で
        // 作ったまま（アバターの管理画面・商品ページと同じ名前を出すため）
        var avatarEntries = registry.Entries.Where(Core.Services.AvatarService.IsAvatar).ToList();

        // 持っているアバターを先に、その下にほかのアバター（メモ32-②。検索の対応アバターの候補と同じ分け方）。
        // 名前が重なる物は1つにまとめるので、持っている方に1つでも入れば先に出す
        bool IsOwned(AvatarRegistryEntry entry) => entry.IsOwnedManually || (ownedItemIds?.Contains(entry.ItemId) ?? false);
        List<string> NamesOf(IEnumerable<AvatarRegistryEntry> entries) => entries
            .Select(NameOf)
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(text => text, StringComparer.CurrentCulture)
            .ToList();
        var ownedNames = NamesOf(avatarEntries.Where(IsOwned));
        var otherNames = NamesOf(avatarEntries.Where(entry => !IsOwned(entry)))
            .Where(text => !ownedNames.Contains(text, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        var avatarNames = ownedNames.Concat(otherNames).ToList();

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
            Files = files is { HasChoices: true } ? files : null,
            OwnedAvatarCount = ownedNames.Count,
        };
    }

    /// <summary>
    /// 手で足すときの窓の前提の1行。選べるファイルがあれば選べることを、無ければ記録されないことと記録の仕方を言う。
    /// </summary>
    /// <param name="howToRecord">選べるファイルが無いときの、記録の仕方（呼ぶ場所で道が違う）。</param>
    public static string FilesContextText(MemberFilePickViewModel files, string howToRecord)
        => files.HasChoices
            ? "使ったファイルを選ぶと記録します。選ばなくても追加できます。"
            : "使ったファイルは記録されません。" + howToRecord;

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
