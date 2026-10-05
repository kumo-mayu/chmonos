using System.Windows.Media;
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
        => (await LoadOwnedItemsAsync(services)).Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>持っている商品（上と同じ定義）をIDで引けるように。アバターの候補の頭の絵が、持っていれば商品の1枚目を使うため。</summary>
    public static async Task<IReadOnlyDictionary<string, ItemRecord>> LoadOwnedItemsAsync(AppServiceContainer services)
        => (await services.Store.Items.LoadAllAsync()).Items
            .Where(item => item.IsOwned)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);

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
        IReadOnlySet<string>? ownedItemIds = null,
        IReadOnlyDictionary<string, ItemRecord>? ownedItems = null)
    {
        // 持っている商品の一覧（絵のため）を渡されれば、持っているかの判定もそこから取る
        ownedItemIds ??= ownedItems?.Keys.ToHashSet(StringComparer.Ordinal);
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

        // 呼び方でも当たる案内（メモ58）。名前が重なって1つにまとめた物は、呼び方も合わせる
        var infos = new Dictionary<string, Controls.SuggestInfo>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in avatarEntries.OrderByDescending(IsOwned))
        {
            var name = NameOf(entry);
            var more = AvatarSuggestionText.InfoOf(entry, name, IsOwned(entry) ? AvatarSuggestionText.OwnedGroup : AvatarSuggestionText.OtherGroup);
            infos[name] = infos.TryGetValue(name, out var found)
                ? found with { Hints = found.Hints.Concat(more.Hints).DistinctBy(hint => hint.Text).ToList() }
                : more;
        }

        // 絵は窓を開いている間だけ要る小さな物なので、窓専用の読み込み器を、絵が最初に要るときに作る
        ThumbnailLoader? loader = null;
        ThumbnailLoader Loader() => loader ??= new ThumbnailLoader(services.Settings.ThumbnailCacheBudgetMb);

        var rows = records
            .Select(record => new PickModificationRowViewModel
            {
                // 今ある改変の行の絵（メモ58）：改変の写真の1枚目 → そのアバターの絵 → 無ければ頭文字（既定の絵）。
                // 読むのは行が見えたとき（絵の無い行は読み込み器も作らない）
                IconPath = Core.Services.ModificationIcon.PathOf(services.Paths, record, ownedItems?.GetValueOrDefault(record.AvatarItemId)),
                Thumbnails = Loader,
                Record = record,
                AvatarText = registry.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.ItemId, record.AvatarItemId, StringComparison.Ordinal))
                    is { } found
                        ? NameOf(found)
                        : record.AvatarItemId,
                ProjectName = ModificationHubViewModel.ProjectNameOf(record.UnityProject),
            })
            .ToList();

        // 候補の名前から絵を引く。重なる名前は1つにまとめてあるので、持っている方（商品の1枚目がある方）を先に当てる
        var iconIds = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in avatarEntries.OrderByDescending(IsOwned))
        {
            iconIds.TryAdd(NameOf(entry), entry.ItemId);
        }

        ImageSource? IconOf(string name)
        {
            if (!iconIds.TryGetValue(name, out var id)
                || Core.Services.AvatarImageSync.IconPath(services.Paths, id, ownedItems?.GetValueOrDefault(id)) is not { } path)
            {
                return null;
            }

            return Loader().LoadForTile(path);
        }

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
            AvatarIconSelector = IconOf,
            AvatarInfoSelector = name => infos.GetValueOrDefault(name),
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
