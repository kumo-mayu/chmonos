using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 改変の「使ったものを順にUnityへ送る」で、どの行から何を送るかの決め方。**画面と外から使う API で同じ物を使う**
/// （前は画面の ViewModel の中にあり、API の使い手は同じ判断を手元で組み直していた。API-GAPS 3-8）。
/// 決めるだけで、送らない・書かない。ファイルが在るかだけはディスクを見る（無い物は送れないので候補に出さない）。
/// </summary>
public static class ModificationSendPlan
{
    /// <summary>行の決め方の結果。</summary>
    /// <param name="Index">改変の使ったものの中の位置（外した行も数える。画面の行の番号と同じ）。</param>
    /// <param name="Item">行の商品（手元の記録に無ければ null）。</param>
    /// <param name="Packages">送る物（選ぶ必要があるときは、その候補）。空なら送れない。</param>
    /// <param name="NeedsChoice">
    /// どのファイルを使ったかの記録が無く、送れる物が2つ以上ある。黙って全部は送らず、人に選ばせる
    /// （ユーザ判断 2026-09-13。前は全部送っていて、古い版や別の種類まで入った）
    /// </param>
    public sealed record Row(int Index, ModificationMember Member, ItemRecord? Item, IReadOnlyList<UnityPackageEntry> Packages, bool NeedsChoice)
    {
        public bool Sendable => Packages.Count > 0;
    }

    /// <summary>
    /// 送る行か。外した行は送らない（使った数・送る・絞り込みには数えない決め事）。並びは記録の順（＝入れた順）のまま送る——
    /// 見えている順で送ると、依存物より先に本体が入る
    /// </summary>
    public static bool Sends(ModificationMember member) => !member.Detached;

    /// <summary>改変の送る行を、記録の順に決める。</summary>
    /// <param name="itemOf">商品の記録を引く（無ければ null）。</param>
    public static IReadOnlyList<Row> RowsOf(ModificationRecord record, Func<string, ItemRecord?> itemOf)
        => record.Members
            .Select((member, index) => (member, index))
            .Where(pair => Sends(pair.member))
            .Select(pair => Decide(pair.index, pair.member, itemOf(pair.member.ItemId)))
            .ToList();

    /// <summary>1行の送る物を決める（画面は行ごとに商品を読み直して、これを呼ぶ）。</summary>
    public static Row Decide(int index, ModificationMember member, ItemRecord? item)
    {
        var packages = item is null ? [] : PackagesFor(item, member);
        return new Row(index, member, item, packages, member.FileHash is null && packages.Count > 1);
    }

    /// <summary>
    /// この構成物で送るもの。**Unityへ送って足した分は、そのとき使った zip と unitypackage をそのまま使う**
    /// ——版まで同じにするため（1年後に組み直すとき v1.01 と v1.06 は別物）。
    /// 手で足した分は、どのファイルを使ったかが分からないので、今ある zip の中身を zip の順に送る。
    /// </summary>
    public static IReadOnlyList<UnityPackageEntry> PackagesFor(ItemRecord item, ModificationMember member)
        => PlacesFor(item, member).Select(place => place.Entry).ToList();

    /// <summary><see cref="PackagesFor"/> に、item に書いてある入る先を添えた物（書いていなければ null）。</summary>
    public static IReadOnlyList<UnityPackagePlace> PlacesFor(ItemRecord item, ModificationMember member)
    {
        if (member.FileHash is { } hash && member.Package is { } package
            && item.Local.OwnedFiles.FirstOrDefault(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)) is { } owner
            && owner.Paths.FirstOrDefault(File.Exists) is { } zip)
        {
            // 要約は zip のハッシュについて書いた物なので、記録した包みの分だけ引けば足りる（全部そろっているかは問わない）
            var roots = owner.UnityPackages?.FirstOrDefault(summary => string.Equals(summary.Entry, package, StringComparison.Ordinal))?.Roots;
            return [new UnityPackagePlace(new UnityPackageEntry(zip, package, 0) { ZipHash = hash }, roots)];
        }

        // 使ったファイルを記録してある物は、そのファイルが手元に無ければ送らない（外部の点検 2026-10-07）。
        // 前は商品の送れる物全部に落ちて、別の版・別の種類のファイルを黙って送っていた（版まで同じにするための記録なのに）。
        // 包みの名前が無い記録（手で足すときに使ったファイルを選んだ物）は、その zip の中の物だけを送る
        if (member.FileHash is { } recorded)
        {
            var recordedFile = item.Local.OwnedFiles.FirstOrDefault(file => string.Equals(file.Hash, recorded, StringComparison.OrdinalIgnoreCase));
            return recordedFile is not null && member.Package is null && recordedFile.Paths.Any(File.Exists)
                ? [.. UnityHandoff.PlacesOf(recordedFile)]
                : [];
        }

        // 登録したフォルダの中の物を送った記録（ハッシュ無しで場所だけ。メモ65-③）。今もフォルダに在れば、その1つを送る
        if (member.Package is { } folderPackage
            && item.Local.LocalFolders
                .SelectMany(UnityHandoff.PlacesOf)
                .FirstOrDefault(place => string.Equals(place.Entry.EntryPath, folderPackage, StringComparison.Ordinal)) is { } inFolder)
        {
            return [inFolder];
        }

        return PlacesOf(item);
    }

    /// <summary>
    /// この商品の zip と登録したフォルダに入っている、Unity へ送れるもの（zip に入っている順、その後にフォルダの中の物）。
    /// 包んでいる手元のファイルを添える（選んだ物を改変に記録するとき、ハッシュと種類はそこから取る。フォルダの中の物は null）
    /// </summary>
    public static IReadOnlyList<(UnityPackagePlace Place, LocalFileRecord? Owner)> CandidatesOf(ItemRecord item)
        => [.. item.Local.OwnedFiles.SelectMany(file => UnityHandoff.PlacesOf(file).Select(place => (place, (LocalFileRecord?)file))),
            .. item.Local.LocalFolders.SelectMany(folder => UnityHandoff.PlacesOf(folder).Select(place => (place, (LocalFileRecord?)null)))];

    /// <summary><see cref="CandidatesOf"/> の場所だけ（全部を送っていたときの順。商品ページの「Unityへ送る」もこの順）。</summary>
    public static IReadOnlyList<UnityPackagePlace> PlacesOf(ItemRecord item) => [.. CandidatesOf(item).Select(candidate => candidate.Place)];

    /// <summary>
    /// 選んだ unitypackage を改変の使ったものとして記録する行。商品ページの「改変に足して送る」が作る行と同じ形
    /// （どのファイルか・どの版か・zip の中のどれか）。フォルダの中の物はハッシュも種類も持たないので、場所だけ
    /// </summary>
    public static ModificationMember MemberFor(string itemId, LocalFileRecord? owner, UnityPackageEntry package) => new()
    {
        ItemId = itemId,
        VariationId = owner?.VariationId,
        FileHash = owner?.Hash,
        Package = package.EntryPath,
    };
}
