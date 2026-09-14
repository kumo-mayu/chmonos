using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>改変の画面：左の一覧を組む（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ModificationHubViewModel
{
    // ---- 左の一覧を組む ----

    private void Rebuild()
    {
        Groups.Clear();

        if (!_isLoading)
        {
            var query = Query.Trim();
            switch (Level)
            {
                case ModificationHubLevel.Project:
                    BuildProjects(query);
                    break;
                case ModificationHubLevel.Avatar:
                    BuildAvatars(query);
                    break;
                default:
                    BuildModifications(query);
                    break;
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>
    /// プロジェクトの見方。**改変の無いプロジェクトも出す**（この見方はランチャーも兼ねる）。
    /// 並びは Hub・VCC の一覧と同じ（開いているもの、次に新しく触ったもの）。
    /// </summary>
    private void BuildProjects(string query)
    {
        foreach (var candidate in _projects)
        {
            var records = RecordsOf(candidate.Path);
            var nameHit = query.Length == 0 || Hit(candidate.Name, query);
            var shown = nameHit ? records : records.Where(record => Hit(record.Name, query)).ToList();
            if (!nameHit && shown.Count == 0)
            {
                continue;
            }

            Groups.Add(new HubProjectGroup($"project:{candidate.Path}", openByDefault: true, forceOpen: query.Length > 0)
            {
                Candidate = candidate,
                Modifications = shown.Select(record => ModRow(record, ModificationHubLevel.Project, query.Length > 0)).ToList(),
            });
        }

        // 紐付けていない改変も、この見方から辿れるようにする（見方を変えないと見つからない改変を作らない）
        var unlinked = _records
            .Where(record => !record.HasUnityProject && (query.Length == 0 || Hit(record.Name, query)))
            .OrderByDescending(record => record.UpdatedAt)
            .ToList();
        if (unlinked.Count > 0)
        {
            Groups.Add(new HubProjectGroup("project:(none)", openByDefault: true, forceOpen: query.Length > 0)
            {
                Modifications = unlinked.Select(record => ModRow(record, ModificationHubLevel.Project, query.Length > 0)).ToList(),
            });
        }
    }

    /// <summary>
    /// アバターの見方。出すのは**所有しているアバターと、改変のあるアバター**。
    /// 対応表記で見かけただけのアバターまで出すと、改変を探す一覧が数百行になる（それはアバターの管理の役）。
    /// </summary>
    private void BuildAvatars(string query)
    {
        var byAvatar = _records
            .GroupBy(record => record.AvatarItemId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ModificationRecord>)group.OrderByDescending(record => record.UpdatedAt).ToList(),
                StringComparer.Ordinal);

        var ids = _avatars.Values
            .Where(summary => summary.IsOwned && summary.Entry.AvatarOverride != false)
            .Select(summary => summary.Entry.ItemId)
            .Concat(byAvatar.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(id => (Id: id, Records: byAvatar.GetValueOrDefault(id) ?? []))
            // 改変のあるアバターを先に、最近触った改変のあるものから
            .OrderByDescending(entry => entry.Records.Count > 0)
            .ThenByDescending(entry => entry.Records.FirstOrDefault()?.UpdatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(entry => AvatarNameOf(entry.Id), StringComparer.CurrentCulture);

        foreach (var (id, records) in ids)
        {
            var nameHit = query.Length == 0 || AvatarHit(id, query);
            var shown = nameHit ? records : records.Where(record => Hit(record.Name, query)).ToList();
            if (!nameHit && shown.Count == 0)
            {
                continue;
            }

            Groups.Add(new HubAvatarGroup($"avatar:{id}", openByDefault: true, forceOpen: query.Length > 0)
            {
                AvatarItemId = id,
                Title = AvatarNameOf(id),
                IsOwned = _avatars.TryGetValue(id, out var summary) && summary.IsOwned,
                IconPath = AvatarIconPath(id),
                Thumbnails = _thumbnails,
                Modifications = shown.Select(record => ModRow(record, ModificationHubLevel.Avatar, query.Length > 0)).ToList(),
            });
        }
    }

    /// <summary>改変の見方。新しく触ったものから。改変そのものが見出しなので、使ったものを開いて出す。</summary>
    private void BuildModifications(string query)
    {
        foreach (var record in _records.OrderByDescending(record => record.UpdatedAt))
        {
            if (query.Length > 0
                && !Hit(record.Name, query)
                && !Hit(AvatarNameOf(record.AvatarItemId), query)
                && !Hit(ProjectNameOf(record.UnityProject), query))
            {
                continue;
            }

            Groups.Add(ModRow(record, ModificationHubLevel.Modification, forceOpen: false));
        }
    }

    /// <summary>
    /// 改変の行。プロジェクト・アバターの見方の中では畳んで出す（見出しの下が長くなりすぎないように）。
    /// 改変の見方では改変そのものが見出しなので開いて出す。
    /// </summary>
    private HubModificationRow ModRow(ModificationRecord record, ModificationHubLevel level, bool forceOpen)
        => new($"mod:{level}:{record.Id}", openByDefault: level == ModificationHubLevel.Modification, forceOpen: false)
        {
            Record = record,
            AvatarName = AvatarNameOf(record.AvatarItemId),
            ShowsAvatar = level != ModificationHubLevel.Avatar,
            ShowsProject = level != ModificationHubLevel.Project,
            Members = record.Members.Select((member, index) => MemberRow(record, member, index)).ToList(),
            IconPath = ModificationIconPath(record),
            Thumbnails = _thumbnails,
        };

    private HubMemberRow MemberRow(ModificationRecord record, ModificationMember member, int index)
    {
        _items.TryGetValue(member.ItemId, out var item);
        return new HubMemberRow
        {
            Record = record,
            Index = index,
            Member = member,
            Name = item?.DisplayName ?? member.ItemId,
            FileText = FileTextOf(member),

            // 手元に無くても記録は残す。そのとき使ったのは事実
            IsMissing = item is null || !item.IsDownloaded,
            ThumbnailPath = item is null ? null : ItemThumbnailPath(item),
            Thumbnails = _thumbnails,
        };
    }

    /// <summary>
    /// どのファイルか。**空欄の意味を言い分ける**（改変の画面と同じ）。Unityへ送って足した分は unitypackage の名前、
    /// 手で足した分は分からないと言う。
    /// </summary>
    private static string FileTextOf(ModificationMember member) => member.Package is { } package
        ? Path.GetFileName(package)
        : member.IsFromUnity ? "Unityへ送った記録あり" : "どのファイルを使ったかは分かりません";

    private IReadOnlyList<ModificationRecord> RecordsOf(string projectPath) => _records
        .Where(record => ModificationService.SamePath(record.UnityProject, projectPath))
        .OrderByDescending(record => record.UpdatedAt)
        .ToList();

    private static bool Hit(string? text, string query)
        => !string.IsNullOrEmpty(text) && Compare.IndexOf(text, query, Loose) >= 0;

    /// <summary>アバターを引ける語。表示名・BOOTHの正式名・別名・商品ID（アバターの管理と同じ）。</summary>
    private bool AvatarHit(string id, string query)
    {
        if (Hit(AvatarNameOf(id), query) || id.Contains(query, StringComparison.Ordinal))
        {
            return true;
        }

        return _avatars.TryGetValue(id, out var summary)
            && (Hit(summary.Entry.BoothName, query) || summary.Entry.Aliases.Any(alias => Hit(alias.Text, query)));
    }

    private string AvatarNameOf(string id)
    {
        if (_avatars.TryGetValue(id, out var summary))
        {
            if (!string.IsNullOrWhiteSpace(summary.Name))
            {
                return summary.Name;
            }

            return summary.Entry.DisplayName ?? summary.Entry.BoothName ?? id;
        }

        return _items.TryGetValue(id, out var item) ? item.DisplayName : id;
    }

    /// <summary>プロジェクトの名前はフォルダ名（Unity の窓の題に出るのもフォルダ名）。</summary>
    public static string ProjectNameOf(string? path) => string.IsNullOrWhiteSpace(path)
        ? string.Empty
        : Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    private string? AvatarIconPath(string id)
        => AvatarImageSync.IconPath(_services.Paths, id, _items.GetValueOrDefault(id));

    private string? ModificationIconPath(ModificationRecord record)
    {
        if (record.Images.Count > 0)
        {
            var path = Path.Combine(_services.Paths.ModificationImagesDir(record.Id), record.Images[0].FileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return AvatarIconPath(record.AvatarItemId);
    }

    /// <summary>商品の1枚目。検索のカードと同じ選び方（BOOTHの並び・★・役割の指定）。</summary>
    private string? ItemThumbnailPath(ItemRecord item)
    {
        var directory = _services.Paths.ItemImagesDir(item.Id);
        var ordered = Core.Images.ItemImageOrder.Arrange(
            directory, item.Booth.Images, _thumbnails.ListFiles(directory), item.Local.UserImages);
        return Core.Images.ItemImageOrder.Thumbnail(
            ordered, item.Local.ThumbnailImage, _services.Settings.ThumbnailRole, item.Local.ImageRoles);
    }
}
