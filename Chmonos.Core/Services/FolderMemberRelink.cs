using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 「zipで登録し直す」の後に、改変の使ったもののうち登録したフォルダの中の包みを指す行を、zip の中の同じ包みへ付け替える
/// （ユーザ判断 2026-11-19・open 144・150）。
///
/// フォルダの中の物を送った行はハッシュを持たず、フォルダからの場所（<see cref="ModificationMember.Package"/>）だけを持つ。
/// フォルダの登録を外すと <see cref="ModificationSendPlan.PlacesFor"/> がその場所を見付けられず、
/// **商品の送れる物全部を送る形に落ちる**（どの包みを使ったかが効かなくなる）。zip のハッシュと zip の中の場所に付け替えれば、
/// 版まで同じ物を送れる記録に戻る。
///
/// 当て方は名前だけ：zip の中の場所は頭にフォルダ名が付くことがある（展開した時にフォルダ名が1段減る）ので、まず後ろが
/// フォルダからの場所と一致する物、無ければファイル名が同じ物を探す。**1つに決まらない行は付け替えずに残す**（推定で埋めない。
/// 違う包みを記録すると、送ったときに別の物が入る）。
/// </summary>
public static class FolderMemberRelink
{
    /// <summary>付け替えの結果（画面で数を言う）。</summary>
    /// <param name="Relinked">zip の中の包みへ付け替えた行の数。</param>
    /// <param name="Unmatched">当たらない・2つ以上当たって残した行の数。</param>
    public readonly record struct Counts(int Relinked, int Unmatched)
    {
        public static Counts operator +(Counts left, Counts right)
            => new(left.Relinked + right.Relinked, left.Unmatched + right.Unmatched);
    }

    /// <summary>
    /// 付け替える行か：この商品の行で、ハッシュが無く（フォルダの中の物）、外したフォルダの包みの場所を持ち、
    /// 残るほかのフォルダには同じ場所が無い（ほかのフォルダに在るなら、そちらで今も送れるので触らない）。
    /// </summary>
    public static bool Targets(
        ModificationMember member,
        string itemId,
        IReadOnlySet<string> folderPackages,
        IReadOnlySet<string> otherFolderPackages)
        => string.Equals(member.ItemId, itemId, StringComparison.Ordinal)
            && member.FileHash is null
            && member.Package is { } package
            && folderPackages.Contains(package)
            && !otherFolderPackages.Contains(package);

    /// <summary>
    /// フォルダからの場所に当たる zip の中の場所。1つに決まらなければ null。
    /// </summary>
    /// <param name="folderPackage">フォルダからの場所（区切りは <c>/</c>）。</param>
    /// <param name="zipPackages">zip の中の unitypackage の場所（区切りは <c>/</c>。<see cref="UnityHandoff.PackageEntriesIn"/>）。</param>
    public static string? Match(string folderPackage, IReadOnlyList<string> zipPackages)
    {
        // 同じ場所か、頭にフォルダ名などが足された場所。区切りの境で比べる（「xfoo.unitypackage」を「foo.unitypackage」に当てない）
        var bySuffix = zipPackages
            .Where(entry => string.Equals(entry, folderPackage, StringComparison.OrdinalIgnoreCase)
                || entry.EndsWith("/" + folderPackage, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (bySuffix.Count > 0)
        {
            return bySuffix.Count == 1 ? bySuffix[0] : null;
        }

        // 展開する時に中の段を変えた物もあるので、ファイル名だけでも当てる。同じ名前が2つあれば決めない
        var name = FileNameOf(folderPackage);
        var byName = zipPackages
            .Where(entry => string.Equals(FileNameOf(entry), name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    /// <summary>
    /// 改変1件の付け替え。錠の中で読み直した今の値に当てる（呼ぶ側は <see cref="Storage.ModificationRepository.UpdateAsync"/> の中で呼ぶ）。
    /// 種類（<see cref="ModificationMember.VariationId"/>）は付け替えた zip の物を入れる（フォルダはどの種類か持たないので、前は空のはず）。
    /// </summary>
    public static (ModificationRecord Record, Counts Counts) Apply(
        ModificationRecord record,
        string itemId,
        IReadOnlySet<string> folderPackages,
        IReadOnlySet<string> otherFolderPackages,
        string zipHash,
        long? zipVariationId,
        IReadOnlyList<string> zipPackages)
    {
        var relinked = 0;
        var unmatched = 0;
        var members = record.Members
            .Select(member =>
            {
                if (!Targets(member, itemId, folderPackages, otherFolderPackages))
                {
                    return member;
                }

                if (Match(member.Package!, zipPackages) is not { } entry)
                {
                    unmatched++;
                    return member;
                }

                relinked++;
                return member with
                {
                    FileHash = zipHash,
                    Package = entry,
                    VariationId = member.VariationId ?? zipVariationId,
                };
            })
            .ToList();

        return relinked == 0
            ? (record, new Counts(0, unmatched))
            : (record with { Members = members, UpdatedAt = DateTimeOffset.Now }, new Counts(relinked, unmatched));
    }

    private static string FileNameOf(string entry)
    {
        var slash = entry.LastIndexOf('/');
        return slash < 0 ? entry : entry[(slash + 1)..];
    }
}
