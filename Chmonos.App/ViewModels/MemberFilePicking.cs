using System.IO;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 手で改変に足すときに選べる「使ったファイル」1つ（メモ26-②・ユーザ判断 2026-10-04）。
///
/// 記録の形は「Unityへ送って足す」と同じ：zip の中に unitypackage があれば unitypackage ごと（どちらを入れたかが再現に要る）、
/// 無いファイル（unitypackage の無い zip・zip でないファイル）はファイルごと。
/// **フォルダは出さない**：フォルダは中身のハッシュを持たない（中の1ファイルで別物になる）ので、
/// 記録の <see cref="ModificationMember.FileHash"/> に入れる物が無い。
/// </summary>
public sealed class MemberFileOption
{
    /// <summary>「選ばない」。記録は今までどおり空のまま。</summary>
    public static readonly MemberFileOption None = new() { Label = "選ばない", Detail = string.Empty };

    public required string Label { get; init; }

    /// <summary>どのファイルの中か（同じ名前の unitypackage を見分ける）。無ければ空。</summary>
    public required string Detail { get; init; }

    public string? FileHash { get; init; }

    public string? Package { get; init; }

    public long? VariationId { get; init; }

    public bool IsNone => FileHash is null;

    /// <summary>選ぶ欄の1行。閉じた欄にも同じ1行を出す。</summary>
    public string Text => Detail.Length > 0 ? $"{Label}　{Detail}" : Label;

    /// <summary>
    /// 商品の手元のファイルから選べる物を作る（手元のファイルの順、zip の中は zip の中の順）。
    ///
    /// **zip を開かない**：中の一覧は記録の中身の一覧（<see cref="LocalFileRecord.Contents"/>）から引く。
    /// 窓を出すまでの間に 1GB 級の zip を読むと止まって見える（送る窓で踏んだ。2026-09-29）。
    /// 外したファイルは出さない（今は使っていない物）。
    /// </summary>
    public static IReadOnlyList<MemberFileOption> ForItem(ItemRecord item)
    {
        var options = new List<MemberFileOption>();
        foreach (var file in item.Local.OwnedFiles)
        {
            var fileName = Path.GetFileName(file.Paths.FirstOrDefault() ?? string.Empty);
            var packages = UnityHandoff.PackageEntriesIn(file);
            if (packages.Count == 0)
            {
                options.Add(new MemberFileOption
                {
                    Label = fileName.Length > 0 ? fileName : "名前の分からないファイル",
                    Detail = string.Empty,
                    FileHash = file.Hash,
                    VariationId = file.VariationId,
                });
                continue;
            }

            options.AddRange(packages.Select(entry => new MemberFileOption
            {
                Label = Path.GetFileName(entry),
                Detail = fileName.Length > 0 ? $"{fileName}の中" : string.Empty,
                FileHash = file.Hash,
                Package = entry,
                VariationId = file.VariationId,
            }));
        }

        return options;
    }
}

/// <summary>商品1つぶんの「使ったファイル」の選び。</summary>
public sealed class MemberFileChoice : ViewModelBase
{
    private MemberFileOption _selected;

    /// <param name="candidates">選べるファイル（「選ばない」を除く）。</param>
    public MemberFileChoice(ItemRecord item, IReadOnlyList<MemberFileOption> candidates)
    {
        Item = item;
        Options = [MemberFileOption.None, .. candidates];

        // **1つだけなら最初から選んでおく**（ユーザ判断 2026-10-04）。窓で人が見て確定するので、推定で埋めるのとは違う。
        // 2つ以上なら、どれかに寄せると外れても気付きにくいので選ばない
        _selected = candidates.Count == 1 ? candidates[0] : MemberFileOption.None;
    }

    public ItemRecord Item { get; }

    public string Name => Item.DisplayName;

    public IReadOnlyList<MemberFileOption> Options { get; }

    public MemberFileOption Selected
    {
        get => _selected;
        set => SetField(ref _selected, value ?? MemberFileOption.None);
    }

    /// <summary>記録する行。選ばなければファイルの欄は空のまま。</summary>
    public ModificationMember ToMember(DateTimeOffset addedAt) => new()
    {
        ItemId = Item.Id,
        VariationId = Selected.VariationId,
        FileHash = Selected.FileHash,
        Package = Selected.Package,
        AddedAt = addedAt,
    };
}

/// <summary>
/// 手で改変に足すときの「使ったファイル」の欄（メモ26-②）。改変を選ぶ窓の中（商品ページ・右クリック・まとめて足す）と、
/// 改変の詳細で名前で足すときの小さな窓の両方に同じ物を出す。
///
/// **まとめて足すときも窓は1つ**：商品ごとに1行（名前と選ぶ欄）を並べる。商品ごとに窓を出すと、数十件で数十回聞かれる。
/// 選べるファイルの無い商品は行を出さない（選ぶ物が無い）。
/// </summary>
public sealed class MemberFilePickViewModel
{
    public MemberFilePickViewModel(IEnumerable<ItemRecord> items)
    {
        var all = items.ToList();
        Choices = all
            .Select(item => (item, options: MemberFileOption.ForItem(item)))
            .Where(pair => pair.options.Count > 0)
            .Select(pair => new MemberFileChoice(pair.item, pair.options))
            .ToList();
        ShowsNames = all.Count > 1;
    }

    public IReadOnlyList<MemberFileChoice> Choices { get; }

    public bool HasChoices => Choices.Count > 0;

    /// <summary>行ごとに商品名を出すか。1件だけなら窓の見出しに名前があるので出さない。</summary>
    public bool ShowsNames { get; }

    /// <summary>この商品の記録する行。欄が無い商品（選べるファイルが無い）は、ファイルの欄が空の行。</summary>
    public ModificationMember MemberFor(string itemId, DateTimeOffset addedAt)
        => Choices.FirstOrDefault(choice => choice.Item.Id == itemId)?.ToMember(addedAt)
            ?? new ModificationMember { ItemId = itemId, AddedAt = addedAt };

    /// <summary>
    /// 窓を出す代わりに答える口。**アプリでは null のまま**（窓を出す）。試験と ViewShot が入れる
    /// （窓を出すと答える人がいないので止まる。<see cref="Services.Notice.Intercept"/> と同じ）
    /// </summary>
    internal static Func<MemberFilePickViewModel, bool>? Intercept { get; set; }

    /// <summary>改変の詳細で名前で足すときの窓。「追加」なら true。</summary>
    public static bool Ask(MemberFilePickViewModel model, string title, string headingText)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.PickMemberFilesDialog(model, title, headingText).ShowDialog() == true;
}
