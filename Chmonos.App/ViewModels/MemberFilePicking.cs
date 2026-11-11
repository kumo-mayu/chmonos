using System.IO;
using Chmonos.Core.Commands;
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

    /// <summary>どの zip に入っているか（同じ名前の unitypackage を見分ける）。無ければ空。</summary>
    public required string Detail { get; init; }

    public string? FileHash { get; init; }

    public string? Package { get; init; }

    public long? VariationId { get; init; }

    /// <summary>
    /// 「選ばない」か。ハッシュだけでは見ない：登録したフォルダの中の物の記録はハッシュが無く unitypackage の場所だけを持つ
    /// （選び直しの窓で「今の記録のまま」に出る）
    /// </summary>
    public bool IsNone => FileHash is null && Package is null;

    /// <summary>今の記録が手元の一覧に無いときに、選び直しの窓だけに足す「今の記録のまま」。</summary>
    public bool IsKeepCurrent { get; init; }

    /// <summary>この選びが記録の行と同じファイルを指すか。種類は見ない（候補の種類は今のファイルの物で、記録の時と違い得る）。</summary>
    public bool Matches(ModificationMember member)
        => !IsNone
            && string.Equals(FileHash, member.FileHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Package, member.Package, StringComparison.Ordinal);

    public ModificationMemberFile ToFile() => new(FileHash, Package, VariationId);

    /// <summary>選ぶ欄の1行。閉じた欄にも同じ1行を出す。</summary>
    /// <summary>主はパッケージの名前で、zip の名前は補足。主が2つに見えないよう「名前 (zip名)」の形にする（メモ59・ユーザ決定 2026-10-05）。</summary>
    public string Text => Detail.Length > 0 ? $"{Label} ({Detail})" : Label;

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
                Detail = fileName,
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
    private MemberFileOption _second = MemberFileOption.None;

    /// <param name="candidates">選べるファイル（「選ばない」を除く）。</param>
    public MemberFileChoice(ItemRecord item, IReadOnlyList<MemberFileOption> candidates)
    {
        Item = item;
        Options = [MemberFileOption.None, .. candidates];

        // **1つだけなら最初から選んでおく**（ユーザ判断 2026-10-04）。窓で人が見て確定するので、推定で埋めるのとは違う。
        // 2つ以上なら、どれかに寄せると外れても気付きにくいので選ばない
        _selected = candidates.Count == 1 ? candidates[0] : MemberFileOption.None;
    }

    /// <summary>選び直しの窓の1行（<see cref="MemberFilePickViewModel.ForRepick"/>）。最初に選ぶ物は呼び手が決める。</summary>
    internal MemberFileChoice(ItemRecord item, IReadOnlyList<MemberFileOption> options, MemberFileOption selected)
    {
        Item = item;
        Options = options;
        _selected = selected;
        AllowsSecond = options.Count(option => !option.IsNone) >= 2;
    }

    public ItemRecord Item { get; }

    public string Name => Item.DisplayName;

    public IReadOnlyList<MemberFileOption> Options { get; }

    public MemberFileOption Selected
    {
        get => _selected;
        set => SetField(ref _selected, value ?? MemberFileOption.None);
    }

    /// <summary>
    /// 2つ目の欄を出すか（選び直しの窓だけ）。依存物と本体が同じ zip にあって両方入れたとき、1行を2行に分けて記録する
    /// （送るときに2つ選んだのと同じ形）。足す窓は1つのままにする：足した後に選び直せるので、足す窓を重くしない
    /// </summary>
    public bool AllowsSecond { get; }

    public MemberFileOption Second
    {
        get => _second;
        set => SetField(ref _second, value ?? MemberFileOption.None);
    }

    /// <summary>選んだファイル（「選ばない」を除き、同じ物は1つにまとめる）。0件なら記録を空に戻す。</summary>
    public IReadOnlyList<MemberFileOption> Picked => new[] { Selected, AllowsSecond ? Second : MemberFileOption.None }
        .Where(option => !option.IsNone)
        .DistinctBy(option => (option.FileHash?.ToUpperInvariant(), option.Package))
        .ToList();

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

    private MemberFilePickViewModel(MemberFileChoice choice, string notice)
    {
        Choices = [choice];
        Notice = notice;
    }

    /// <summary>
    /// 記録した使ったファイルを選び直す窓の中身（ユーザ指示 2026-11-11：版を人が変えられない）。
    ///
    /// **今の記録を選んだ状態で開く**：確定を押しただけで記録が替わったり消えたりしない。今の記録が手元の一覧に無い
    /// （外した・消えたファイル、登録したフォルダの中の物、商品ごと手元に無い）ときは「今の記録のまま」を足してそれを選び、
    /// 一覧の上に1行で理由を出す。
    /// </summary>
    /// <param name="item">手元の商品。無ければ null（選べるのは「選ばない」と「今の記録のまま」だけ）。</param>
    /// <param name="preferNewVersion">
    /// 使ったものの更新の促し（used-updates）の「新しい版を選ぶ…」から開くとき。新しい版のファイル（控えの後に持った物）から
    /// 1つに決まるときだけそれを選んで開く。決まらなければ今の記録のまま開く（推定で入れない）
    /// </param>
    public static MemberFilePickViewModel ForRepick(ModificationMember member, string name, ItemRecord? item, bool preferNewVersion = false)
    {
        var owner = item ?? new ItemRecord
        {
            Id = member.ItemId,
            Booth = new BoothBlock { Name = name, FetchedAt = DateTimeOffset.MinValue },
            Local = new LocalBlock(),
        };
        var candidates = item is null ? [] : MemberFileOption.ForItem(item);
        var current = candidates.FirstOrDefault(option => option.Matches(member));

        var notice = string.Empty;
        MemberFileOption? keep = null;
        if (member.HasFile && current is null)
        {
            keep = new MemberFileOption
            {
                Label = "今の記録のまま：" + ModificationRowBuilder.FileTextOf(member, item),
                Detail = string.Empty,
                FileHash = member.FileHash,
                Package = member.Package,
                VariationId = member.VariationId,
                IsKeepCurrent = true,
            };
            notice = member.FileHash is null
                ? "今の記録は登録したフォルダの中のファイルで、一覧からは選べません。"
                : "記録しているファイルは手元にありません。";
        }

        List<MemberFileOption> options = [MemberFileOption.None];
        if (keep is not null)
        {
            options.Add(keep);
        }

        options.AddRange(candidates);
        var selected = keep ?? current ?? MemberFileOption.None;

        if (preferNewVersion && item?.Local.ContentUpdate is { } update)
        {
            var newer = candidates
                .Where(option => option.FileHash is { } hash && !update.FilesBefore.Contains(hash, StringComparer.OrdinalIgnoreCase))
                .ToList();

            // 同じ名前の unitypackage があれば同じ物の新しい版と見る（依存物と本体が同じ zip にある商品で、本体を選び直したいとき）
            var samePackage = member.Package is { } package
                ? newer.Where(option => string.Equals(Path.GetFileName(option.Package), Path.GetFileName(package), StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
            selected = newer.Count == 1 ? newer[0]
                : samePackage.Count == 1 ? samePackage[0]
                : selected;
        }

        return new MemberFilePickViewModel(new MemberFileChoice(owner, options, selected), notice);
    }

    /// <summary>一覧の上に出す1行（選び直しの窓で、今の記録が一覧に無いとき）。無ければ空。</summary>
    public string Notice { get; } = string.Empty;

    public bool HasNotice => Notice.Length > 0;

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

    /// <summary>選び直しの窓。「変更」なら true。</summary>
    public static bool AskRepick(MemberFilePickViewModel model, string headingText)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.PickMemberFilesDialog(model, MemberFileRepick.Title, headingText, repick: true).ShowDialog() == true;
}

/// <summary>
/// 使ったものの行の「使ったファイルを選び直す…」（ユーザ指示 2026-11-11）。改変の詳細と改変の画面の左の一覧の両方から使う。
/// 書くのは <see cref="UiCommand.ChangeModificationMemberFiles"/> 1本で、錠の中で今の行に当てる。
/// </summary>
public static class MemberFileRepick
{
    public const string Title = "使ったファイルを選び直す";

    /// <summary>窓を出して、選び直した物を書く。返すのは行の下に出す文（何もしなかったら null）と、失敗か。</summary>
    public static async Task<(string? Text, bool Failed)> RunAsync(
        AppServiceContainer services,
        string modificationId,
        ModificationMember member,
        string name,
        bool preferNewVersion = false)
    {
        var item = await services.Store.Items.LoadAsync(member.ItemId);
        var model = MemberFilePickViewModel.ForRepick(member, name, item, preferNewVersion);
        var choice = model.Choices[0];
        if (choice.Options.Count <= 1)
        {
            return ("選べるファイルが手元にありません。", false);
        }

        if (!MemberFilePickViewModel.AskRepick(model, $"「{name}」で使ったファイルを選びます。"))
        {
            return (null, false);
        }

        var picked = choice.Picked;

        // 選びを変えずに確定したなら書かない（足した日時や並びを触らずに済む。書く必要が無い）
        var unchanged = picked.Count == 0
            ? !member.HasFile
            : picked.Count == 1 && (picked[0].IsKeepCurrent || picked[0].Matches(member));
        if (unchanged)
        {
            return (null, false);
        }

        var result = await services.Commands.ExecuteAsync(new UiCommand.ChangeModificationMemberFiles(
            modificationId, member, picked.Select(option => option.ToFile()).ToList()));
        if (result is CommandResult.Failed failed)
        {
            return (failed.Message, true);
        }

        return (picked.Count == 0
            ? $"「{name}」の使ったファイルの記録を削除しました。"
            : $"「{name}」の使ったファイルを変更しました。", false);
    }
}
