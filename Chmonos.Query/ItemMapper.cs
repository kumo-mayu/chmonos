using System.Text.Json;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Query;

/// <summary>
/// 商品の記録を答えの形（<see cref="ItemDetail"/>・<see cref="ItemSummary"/>）に変える。
/// 計算で出す値（所持・対応アバター・サムネイル）は画面と同じ所（Core）から取る。外の道具に計算させるとずれる。
/// </summary>
public static class ItemMapper
{
    public static ItemSummary Summary(ItemRecord item, QuerySnapshot snapshot)
        => new(
            item.Id,
            item.DisplayName,
            Shop(item),
            item.CategoryName,
            item.IsOwned,
            item.Booth.IsAdult,
            item.Local.IsHidden,
            item.Local.IsFavorite,
            Compatible(item, snapshot).Select(avatar => avatar.AvatarId).ToList(),
            Thumbnail(item, snapshot));

    public static ItemDetail Detail(ItemRecord item, QuerySnapshot snapshot, ItemIncludes includes = ItemIncludes.None)
    {
        var booth = item.Booth;
        var local = item.Local;
        var variationNames = booth.Variations.ToDictionary(variation => variation.Id, variation => variation.Name);

        return new ItemDetail
        {
            Id = item.Id,
            Name = item.DisplayName,
            BoothName = booth.Name,
            Url = booth.Url,
            Shop = Shop(item),
            Category = item.CategoryName,
            CategorySource = item.HasUserCategory ? "user" : booth.Category is null ? null : "booth",
            BoothCategory = booth.Category is { } category ? new BoothCategoryRef(category.Id, category.Name, category.ParentName) : null,
            BoothTags = booth.Tags,
            PriceText = booth.PriceText,
            WishCount = booth.WishListsCount,
            PublishedAt = booth.PublishedAt,
            IsAdult = booth.IsAdult,
            IsEndOfSale = booth.IsEndOfSale,
            IsSoldOut = booth.IsSoldOut,
            IsDelisted = !item.IsLocalOnly && SearchConditions.IsDelisted(item),
            IsLocalOnly = item.IsLocalOnly,
            IsHidden = local.IsHidden,
            IsFavorite = local.IsFavorite,
            IsOwned = item.IsOwned,
            IsAvatar = snapshot.Registry.Entries.Any(entry =>
                string.Equals(entry.ItemId, item.Id, StringComparison.Ordinal) && AvatarService.IsAvatar(entry)),
            HasUpdate = snapshot.UnreadUpdates.ContainsKey(item.Id),
            HasBrokenArchive = item.HasBrokenArchive,
            HasMissingFiles = item.HasMissingFile,
            Variations = booth.Variations.Select(variation => new VariationRef(variation.Id, variation.Name, variation.Price)).ToList(),
            Purchases = local.Purchases.Select(purchase => new PurchaseRef(
                purchase.VariationId,
                purchase.NameSnapshot ?? (purchase.VariationId is { } id && variationNames.TryGetValue(id, out var name) ? name : null),
                purchase.Price,
                KindOf(purchase.Kind),
                purchase.PurchasedAt,
                purchase.Note)).ToList(),
            AcquiredAt = local.AcquiredAt,
            CompatibleAvatars = Compatible(item, snapshot),
            UserTags = local.UserTags.Select(tag => new UserTagRef(tag.Top, tag.Subs)).ToList(),
            Attributes = local.Attributes,
            Memo = local.Memo,
            Files = local.LocalFiles
                .Where(file => includes.HasFlag(ItemIncludes.AllFiles) || FileState(file) is "present" or "missing")
                .Select(file => File(file, includes.HasFlag(ItemIncludes.Contents)))
                .ToList(),
            Folders = local.LocalFolders.Select(folder => new FolderRef(
                folder.Path, folder.FileCount, folder.TotalBytes, folder.MissingSince, folder.UnityPackages)).ToList(),
            Thumbnail = Thumbnail(item, snapshot),
            Videos = (snapshot.Videos.GetValueOrDefault(item.Id) ?? [])
                .Select(video => new VideoRef(video.VideoId, video.Url, snapshot.VideoTitles.GetValueOrDefault(video.VideoId)))
                .ToList(),
            Images = includes.HasFlag(ItemIncludes.Images) ? Images(item, snapshot).All : null,
            AvatarLinks = includes.HasFlag(ItemIncludes.AvatarLinks) ? AvatarLinks(item, snapshot) : null,
            Meta = includes.HasFlag(ItemIncludes.Meta) ? new ItemMetaRef(booth.FetchedAt) : null,
        };
    }

    /// <summary>
    /// 対応アバターの紐付けの帳簿（頼まれたときだけ）。消した紐付け・説明文のリンクも状態つきでそのまま返す（中の区別を潰さない）。
    /// </summary>
    private static AvatarLinksRef AvatarLinks(ItemRecord item, QuerySnapshot snapshot)
        => new(
            item.Local.Avatars.Select(link => new AvatarLinkRef(
                link.AvatarItemId,
                AvatarName(link.AvatarItemId, link.Name, snapshot),
                Camel(link.Source.ToString()),
                StateOf(link.Confirmed, link.Rejected),
                snapshot.OwnedAvatarIds.Contains(link.AvatarItemId))).ToList(),
            item.Local.AvatarBases.Select(link => new AvatarBaseLinkRef(
                link.BaseName,
                Camel(link.Source.ToString()),
                StateOf(link.Confirmed, link.Rejected))).ToList(),
            item.Local.AvatarsDetectedAt);

    internal static ImageRef? Thumbnail(ItemRecord item, QuerySnapshot snapshot) => Images(item, snapshot).Thumbnail;

    /// <summary>
    /// 画面の検索と同じ数え方の対応アバター（<see cref="AvatarCompatibilityIndex.Resolve"/>）。人が消した紐付けと、
    /// 説明文のリンク（要確認）は数えず、まだ人が確認していない推定は数える。素体経由も含め、直接か素体経由かを添える。
    /// </summary>
    internal static IReadOnlyList<CompatibleAvatarRef> Compatible(ItemRecord item, QuerySnapshot snapshot)
        => snapshot.Compatibility.Resolve(item.Local)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CompatibleAvatarRef(
                pair.Key,
                AvatarName(pair.Key, null, snapshot),
                pair.Value == AvatarMatch.Direct ? "direct" : "viaBase",
                snapshot.OwnedAvatarIds.Contains(pair.Key)))
            .ToList();

    private static ShopRef? Shop(ItemRecord item)
        => item.ShopSubdomain is { } key
            ? new ShopRef(
                key,
                item.ShopName,
                LocalShopKey.IsLocal(key) ? null : key,
                LocalShopKey.IsLocal(key),
                item.Local.Shop?.Url ?? item.Booth.Shop?.Url)
            : null;

    private static FileRef File(LocalFileRecord file, bool contents)
        => new(
            file.Hash,
            file.SizeBytes,
            file.VariationId,
            file.Paths,
            FileState(file),
            file.MissingSince,
            file.ArchiveBroken,
            file.Replaced is { } replaced ? new ReplacedRef(replaced.Path, replaced.At) : null,
            file.UnityPackages?.Select(package => new UnityPackageRef(package.Entry, package.Roots)).ToList(),
            contents ? file.Contents : null);

    /// <summary>ファイルの状態（<see cref="FileRef.State"/>）。見つからないの数え方はカードの印と同じ（<see cref="ItemRecord.HasMissingFile"/>）。</summary>
    internal static string FileState(LocalFileRecord file)
        => file.Detached ? "detached" : file.IsOldVersion ? "oldVersion" : file.Paths.Count == 0 || file.MissingSince is not null ? "missing" : "present";

    /// <summary>画面のカードと同じ並べ方・同じ1枚目（<see cref="ItemImageOrder"/>）。</summary>
    private static (ImageRef? Thumbnail, IReadOnlyList<ImageRef> All) Images(ItemRecord item, QuerySnapshot snapshot)
    {
        var directory = snapshot.Paths.ItemImagesDir(item.Id);
        var files = snapshot.ImageFiles.GetValueOrDefault(item.Id) ?? [];
        var ordered = ItemImageOrder.Arrange(directory, item.Booth.Images, files, item.Local.UserImages, snapshot.Settings.ShowRemovedBoothImages);
        var thumbnail = ItemImageOrder.Thumbnail(ordered, item.Local.ThumbnailImage, snapshot.Settings.ThumbnailRole, item.Local.ImageRoles);

        var captions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in item.Booth.Images)
        {
            captions.TryAdd(ImagePipeline.FileNameFor(image.OriginalUrl), image.Caption);
        }

        foreach (var image in item.Local.UserImages)
        {
            captions.TryAdd(image.FileName, image.Caption);
        }

        var all = ordered.Select(image =>
        {
            var name = System.IO.Path.GetFileName(image.Path);
            return new ImageRef(
                name,
                image.Path,
                image.Origin switch
                {
                    ImageOrigin.Booth => "booth",
                    ImageOrigin.UserAdded => "user",
                    _ => "orphaned",
                },
                Camel(ItemImageOrder.RoleOf(image, item.Local.ImageRoles).ToString()),
                captions.GetValueOrDefault(name));
        }).ToList();

        return (
            thumbnail is null ? null : all.FirstOrDefault(image => string.Equals(image.Path, thumbnail, StringComparison.OrdinalIgnoreCase)),
            all);
    }

    private static string AvatarName(string avatarId, string? cached, QuerySnapshot snapshot)
        => snapshot.AvatarNames.TryGetValue(avatarId, out var name) ? name : cached ?? avatarId;

    private static string StateOf(bool confirmed, bool rejected) => rejected ? "rejected" : confirmed ? "confirmed" : "candidate";

    private static string KindOf(PurchaseKind kind) => kind switch
    {
        PurchaseKind.Given => "given",
        PurchaseKind.Received => "received",
        _ => "self",
    };

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);
}
