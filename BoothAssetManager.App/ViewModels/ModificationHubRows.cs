using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

// ModificationHubViewModel の画面に並べる行と小さな入れ物（技術的負債 4-1：画面のクラスのファイルから分けた。中身は変えていない）

// 改変の画面（2026-09-13 ユーザ仕様「改変周りの刷新」）。
//
// 画面の構成をユーザの認識の単位に合わせる。改変を「Unityプロジェクト」「アバター」「改変」の3つの文脈から見る。
// **新しいデータは持たない。**今の改変の記録（ModificationRecord）をそのまま3通りに並べ直す見せ方だけ（ユーザ指示）。
// 左で見方を切り替えて探し、右に押したもののビューを出す。

/// <summary>左の一覧の見方。</summary>
public enum ModificationHubLevel
{
    Project,
    Avatar,
    Modification,
}

public enum ModificationHubSelectionKind
{
    Project,
    Avatar,
    Modification,
    Member,
}

/// <summary>右側に何を出していたか。戻るで戻ったときに同じものを出し直すために履歴へ預ける。</summary>
/// <param name="Key">プロジェクトならパス、アバターなら商品ID、改変と使ったものなら改変のID。</param>
/// <param name="Index">使ったものの位置（並びが導入の順なので位置で指す）。</param>
public sealed record ModificationHubSelection(ModificationHubSelectionKind Kind, string Key, int Index = 0);

/// <summary>
/// 畳んだ・開いたの状態。画面は開くたびに作り直すので、アプリを閉じるまでここに持つ（アバターの管理の見出しと同じ扱い）。
/// </summary>
internal static class HubExpansion
{
    private static readonly Dictionary<string, bool> States = new(StringComparer.OrdinalIgnoreCase);

    public static bool Get(string key, bool fallback) => States.TryGetValue(key, out var value) ? value : fallback;

    public static void Set(string key, bool value) => States[key] = value;
}

/// <summary>畳める行。</summary>
public abstract class HubExpandable : ViewModelBase
{
    private readonly string _key;
    private bool _isExpanded;

    /// <param name="forceOpen">
    /// 探している間は開いて出す（覚えた状態は変えない）。畳んだままだと、当たった改変が見えない
    /// </param>
    protected HubExpandable(string key, bool openByDefault, bool forceOpen)
    {
        _key = key;
        _isExpanded = forceOpen || HubExpansion.Get(key, openByDefault);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                HubExpansion.Set(_key, value);
                OnPropertyChanged(nameof(ExpandGlyph));
            }
        }
    }

    /// <summary>畳む印。中身が無い行は出さない（押しても何も起きない印は嘘になる）。</summary>
    public string ExpandGlyph => !CanExpand ? string.Empty : IsExpanded ? "▾" : "▸";

    /// <summary>畳む印（図形）を出すか。中身が無い行は出さない。</summary>
    public bool HasGlyph => CanExpand;

    protected abstract bool CanExpand { get; }
}

/// <summary>
/// 改変に使ったもの1件。**ファイル単位**（ユーザ指摘 2026-09-13）——Unityへ送って足した分は、
/// どの zip のどの unitypackage かまで記録にある。手で足した分は分からないまま出す（推定で埋めない）。
/// </summary>
public sealed class HubMemberRow : ViewModelBase
{
    public required ModificationRecord Record { get; init; }

    /// <summary>改変の中の位置。同じ商品を別の版で2回足せるので、位置で指す。</summary>
    public required int Index { get; init; }

    public required ModificationMember Member { get; init; }

    public required string Name { get; init; }

    public required string FileText { get; init; }

    public required bool IsMissing { get; init; }

    public string ItemId => Member.ItemId;

    /// <summary>「Unityで選択」の相手。改変に紐付けたプロジェクト。</summary>
    public string? ProjectPath => Record.UnityProject;

    public string? ThumbnailPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    /// <summary>
    /// 裏で読み、届いたら描き直す（アバターの管理の頭の絵と同じ扱い）。出すのは34DIPの枠だけなので、頭の絵の大きさで読む
    /// （<see cref="ThumbnailLoader.IconShortEdgeDip"/>。96DIPで読んでいた）
    /// </summary>
    public BitmapSource? Thumbnail => ThumbnailPath is { } path
        ? Thumbnails?.PeekForIcon(path, () => OnPropertyChanged(nameof(Thumbnail)))
        : null;

    /// <summary>
    /// ホバーで出す大きめの絵（ユーザ指示 2026-09-13）。一覧の絵は小さく縮めて読んでいて、引き伸ばすとぼやけるので、カードの大きさで読み直す。
    /// **窓が開いたときに初めて読む**（行を作るたびに全部読むとメモリを食う）
    /// </summary>
    public BitmapSource? HoverImage => ThumbnailPath is { } path
        ? Thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(HoverImage)))
        : null;

    public bool HasHoverImage => ThumbnailPath is not null;

    public string Initial => AvatarText.InitialOf(Name);
}

/// <summary>改変1件の行。3つの見方すべてで同じ形を使い、見方ごとに出す繋がり（アバター・プロジェクト）を変える。</summary>
public sealed class HubModificationRow(string key, bool openByDefault, bool forceOpen)
    : HubExpandable(key, openByDefault, forceOpen)
{
    public required ModificationRecord Record { get; init; }

    public string Name => Record.Name;

    public required string AvatarName { get; init; }

    public string AvatarItemId => Record.AvatarItemId;

    public string? ProjectPath => Record.UnityProject;

    public string ProjectName => ModificationHubViewModel.ProjectNameOf(Record.UnityProject);

    /// <summary>アバターの見方の中では出さない（見出しがそのアバター）。</summary>
    public bool ShowsAvatar { get; init; }

    /// <summary>プロジェクトの見方の中では出さない（見出しがそのプロジェクト）。</summary>
    public bool ShowsProject { get; init; }

    public bool ShowsProjectLink => ShowsProject && Record.HasUnityProject;

    /// <summary>メモの1行目。一覧では長いメモを全部出さない。</summary>
    public string MemoText => (Record.Memo ?? string.Empty)
        .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault() ?? string.Empty;

    public bool HasMemo => MemoText.Length > 0;

    public required IReadOnlyList<HubMemberRow> Members { get; init; }

    public string CountText => Members.Count == 0 ? "使ったものはまだありません" : $"使ったもの {Members.Count}";

    public string? IconPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    /// <summary>頭の絵。改変に貼った写真の1枚目、無ければアバターの絵。出すのは38DIPの枠だけなので頭の絵の大きさで読む。</summary>
    public BitmapSource? Icon => IconPath is { } path
        ? Thumbnails?.PeekForIcon(path, () => OnPropertyChanged(nameof(Icon)))
        : null;

    /// <summary>ホバーで出す大きめの絵。窓が開いたときに初めて、カードの大きさで読む（使ったものの絵と同じ）。</summary>
    public BitmapSource? HoverImage => IconPath is { } path
        ? Thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(HoverImage)))
        : null;

    public bool HasHoverImage => IconPath is not null;

    public string Initial => AvatarText.InitialOf(Name);

    protected override bool CanExpand => Members.Count > 0;
}

/// <summary>プロジェクトの見方の見出し1つ。紐付けていない改変も1つの見出しにまとめる（Candidate が null）。</summary>
public sealed class HubProjectGroup(string key, bool openByDefault, bool forceOpen)
    : HubExpandable(key, openByDefault, forceOpen)
{
    public UnityProjectCandidate? Candidate { get; init; }

    public bool IsProject => Candidate is not null;

    public string Title => Candidate?.Name ?? "Unityプロジェクトに紐付けていない改変";

    public string? Path => Candidate?.Path;

    public bool IsOpen => Candidate?.IsOpen == true;

    public bool IsMissing => Candidate is { Exists: false };

    public bool CanOpen => Candidate is { Exists: true };

    /// <summary>押せないときの理由も出す（`ui-rules.md`・E11）。</summary>
    public string OpenHint => CanOpen
        ? "既に開いていれば、そのUnityを手前に出します。"
        : Candidate is null
            ? "このまとまりはプロジェクトに紐付いていないので、開くものがありません。"
            : "紐付けたフォルダが見つかりません。場所が変わったなら、改変の右側で紐付け直してください。";

    public string DetailText => Candidate is null
        ? "改変の右側でプロジェクトを紐付けると、そのプロジェクトの下に並びます"
        : IsMissing ? "フォルダが見つかりません" : Candidate.Version ?? "バージョンが読めません";

    public required IReadOnlyList<HubModificationRow> Modifications { get; init; }

    public string CountText => Modifications.Count == 0 ? string.Empty : $"改変 {Modifications.Count}";

    protected override bool CanExpand => Modifications.Count > 0;
}

/// <summary>アバターの見方の見出し1つ。</summary>
public sealed class HubAvatarGroup(string key, bool openByDefault, bool forceOpen)
    : HubExpandable(key, openByDefault, forceOpen)
{
    public required string AvatarItemId { get; init; }

    public required string Title { get; init; }

    public required bool IsOwned { get; init; }

    public string OwnedText => IsOwned ? "所有" : "所有していない";

    public required IReadOnlyList<HubModificationRow> Modifications { get; init; }

    public string CountText => Modifications.Count == 0 ? "改変なし" : $"改変 {Modifications.Count}";

    public string? IconPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    /// <summary>見出しの頭の丸い絵（34DIP）。頭の絵の大きさで読む。</summary>
    public BitmapSource? Icon => IconPath is { } path
        ? Thumbnails?.PeekForIcon(path, () => OnPropertyChanged(nameof(Icon)))
        : null;

    /// <summary>ホバーで出す大きめの絵。窓が開いたときに初めて、カードの大きさで読む（使ったものの絵と同じ）。</summary>
    public BitmapSource? HoverImage => IconPath is { } path
        ? Thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(HoverImage)))
        : null;

    public bool HasHoverImage => IconPath is not null;

    public string Initial => AvatarText.InitialOf(Title);

    protected override bool CanExpand => Modifications.Count > 0;
}

/// <summary>右側：Unityプロジェクト。</summary>
public sealed class HubProjectDetail
{
    public required UnityProjectCandidate Candidate { get; init; }

    public string Name => Candidate.Name;

    public string Path => Candidate.Path;

    public bool Exists => Candidate.Exists;

    /// <summary>押せないときの理由も出す（`ui-rules.md`・E11）。</summary>
    public string OpenProjectHint => Exists
        ? "既に開いていれば、そのUnityを手前に出します。"
        : "このフォルダが見つかりません。場所が変わったなら、改変の右側で紐付け直してください。";

    public bool IsMissing => !Candidate.Exists;

    public bool IsOpen => Candidate.IsOpen;

    public string VersionText => Candidate.Version is { } version ? $"Unity {version}" : "バージョンが読めません";

    public string SourceText => Candidate.Source switch
    {
        UnityProjectSource.Hub | UnityProjectSource.Vcc => "Unity Hub と VCC の一覧",
        UnityProjectSource.Hub => "Unity Hub の一覧",
        UnityProjectSource.Vcc => "VCC の一覧",
        _ => "どちらの一覧にも無い（改変から紐付けたもの）",
    };

    public string OpenText => IsOpen ? "開いています" : "閉じています";

    public string LastWriteText => Candidate.LastWrite is { } time ? $"最後に触った日 {time.ToLocalTime():yyyy-MM-dd}" : string.Empty;

    // ---- 右ビューの「項目：値」の行（ユーザ指示 2026-09-14：版・開いているか・一覧の名前が、内部の言い方のまま札で並んでいた） ----

    /// <summary>状態の値。フォルダが無ければそれを言う（開いている・閉じているより先に困ること）。</summary>
    public string StateValue => IsMissing ? "フォルダが見つかりません" : IsOpen ? "開いている" : "閉じている";

    public string VersionValue => Candidate.Version ?? "読めません";

    /// <summary>どこで見つけたか（Unity Hub・VCC の一覧）。どちらにも無いのは、改変から紐付けたものだけ。</summary>
    public string SourceValue => Candidate.Source switch
    {
        UnityProjectSource.Hub | UnityProjectSource.Vcc => "Unity Hub・VCC",
        UnityProjectSource.Hub => "Unity Hub",
        UnityProjectSource.Vcc => "VCC",
        _ => "改変から紐付けたもの（どちらの一覧にも無い）",
    };

    public string LastWriteValue => Candidate.LastWrite is { } time ? $"{time.ToLocalTime():yyyy-MM-dd}" : string.Empty;

    public bool HasLastWrite => Candidate.LastWrite is not null;

    public required IReadOnlyList<HubModificationRow> Modifications { get; init; }

    public bool HasModifications => Modifications.Count > 0;

    public string EmptyText =>
        "このプロジェクトに紐付けた改変はまだありません。改変の右側の「Unityプロジェクト」で紐付けると、ここに並びます。";
}

/// <summary>右側：アバター。改変に関係する所だけ（名前・所有・改変・作る）。ほかの設定はアバターの管理へ。</summary>
public sealed class HubAvatarDetail : ViewModelBase
{
    public required string AvatarItemId { get; init; }

    public required string Name { get; init; }

    public required string BoothName { get; init; }

    public bool HasBoothName => BoothName.Length > 0 && BoothName != Name;

    public required bool IsOwned { get; init; }

    public string OwnedText => IsOwned ? "所有しているアバター" : "所有していないアバター";

    public required string BaseText { get; init; }

    public bool HasBase => BaseText.Length > 0;

    /// <summary>商品として手元にあるか（あれば商品ページを開ける）。</summary>
    public required bool HasItem { get; init; }

    public string? IconPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    public BitmapSource? Icon => IconPath is { } path
        ? Thumbnails?.PeekForTile(path, () => OnPropertyChanged(nameof(Icon)))
        : null;

    /// <summary>ホバーで出す大きめの絵。窓が開いたときに初めて、カードの大きさで読む（使ったものの絵と同じ）。</summary>
    public BitmapSource? HoverImage => IconPath is { } path
        ? Thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(HoverImage)))
        : null;

    public bool HasHoverImage => IconPath is not null;

    public string Initial => AvatarText.InitialOf(Name);

    public required IReadOnlyList<HubModificationRow> Modifications { get; init; }

    public bool HasModifications => Modifications.Count > 0;

    private string _nameInput = string.Empty;

    /// <summary>新しく作る改変の名前。</summary>
    public string NameInput
    {
        get => _nameInput;
        set
        {
            if (SetField(ref _nameInput, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }
}

/// <summary>
/// 右側：使ったもの1件。**手元にある商品は商品ページをそのまま組み込み**（ユーザ指示 2026-09-14：ほかの画面と同じ商品ページを右に出す。
/// 3つの見方すべて）、この改変に固有の物（使ったファイル・Unity のどこに入るか・「Unityで選択」・この商品を使った改変）は上の帯に出す。
/// 手元に無い商品は商品ページが無いので、これまでの要約を出す
/// </summary>
public sealed class HubItemDetail : ViewModelBase
{
    public required HubMemberRow Row { get; init; }

    public ItemRecord? Item { get; init; }

    /// <summary>組み込んだ商品ページ。手元に無い商品では null。</summary>
    public ItemViewModel? Page { get; init; }

    public bool HasPage => Page is not null;

    public string UsedInHeader => $"この商品を使った改変（{UsedIn.Count}）";

    public string Name => Row.Name;

    public string ItemId => Row.ItemId;

    public bool CanOpenItem => Item is not null;

    public string ShopText => Item?.Booth.Shop?.Name ?? string.Empty;

    public bool HasShop => ShopText.Length > 0;

    public string ModificationName => Row.Record.Name;

    public string FileText => Row.FileText;

    public required string VariationText { get; init; }

    public bool HasVariation => VariationText.Length > 0;

    public bool IsMissing => Row.IsMissing;

    public string ProjectText => Row.ProjectPath is { } path
        ? $"「Unityで選択」は、この改変に紐付けたプロジェクト「{ModificationHubViewModel.ProjectNameOf(path)}」を相手にします。"
        : "この改変はUnityプロジェクトに紐付いていないので、「Unityで選択」は使えません。改変を開いて紐付けてください。";

    public string? ThumbnailPath { get; init; }

    public ThumbnailLoader? Thumbnails { get; init; }

    public BitmapSource? Thumbnail => ThumbnailPath is { } path
        ? Thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(Thumbnail)))
        : null;

    private string _destinationText = "Unityのどこに入るかを読んでいます…";

    /// <summary>Unity のどこに入るか（商品ページの「Assets/〇〇 に入ります」と同じ読み方）。</summary>
    public string DestinationText
    {
        get => _destinationText;
        set => SetField(ref _destinationText, value);
    }

    /// <summary>この商品を使ったほかの改変も含めた一覧。</summary>
    public required IReadOnlyList<HubModificationRow> UsedIn { get; init; }
}

/// <summary>
/// 改変1件の行（使ったものの行を含む）を作る。改変の画面とアバターの管理の「このアバターの改変」で同じ形を使う
/// （ユーザ指示 2026-09-17：アバターの管理の改変を、改変の画面のアバターの項目と同じくアイコン・名前・Unityプロジェクト・畳んだ中身にする）。
/// </summary>
/// <param name="thumbnailPaths">
/// 商品ごとの1枚目の場所を裏で引いておいた物（<see cref="ThumbnailPathsOf"/>）。あればフォルダを見ずにそれを使う。
/// 行を組むのは画面のスレッドなので、ここで商品ごとにフォルダを見ると、使ったものの多い改変の一覧で行の数だけ止まる
/// </param>
internal sealed class ModificationRowBuilder(
    AppServiceContainer services,
    ThumbnailLoader thumbnails,
    IReadOnlyDictionary<string, ItemRecord> items,
    IReadOnlyDictionary<string, string?>? thumbnailPaths = null)
{
    /// <summary>商品ごとの1枚目の場所をまとめて引く。**裏のスレッドで呼ぶ**（フォルダを見る）。</summary>
    public static Dictionary<string, string?> ThumbnailPathsOf(
        AppServiceContainer services, ThumbnailLoader thumbnails, IEnumerable<ItemRecord> targets)
    {
        var builder = new ModificationRowBuilder(services, thumbnails, new Dictionary<string, ItemRecord>());
        var paths = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var item in targets)
        {
            paths[item.Id] = builder.ItemThumbnailPath(item);
        }

        return paths;
    }

    /// <param name="key">畳んだ・開いたを覚える鍵（画面ごとに分ける）。</param>
    public HubModificationRow Build(ModificationRecord record, string key, bool openByDefault, string avatarName, bool showsAvatar, bool showsProject)
        => new(key, openByDefault, forceOpen: false)
        {
            Record = record,
            AvatarName = avatarName,
            ShowsAvatar = showsAvatar,
            ShowsProject = showsProject,
            // 外した行は改変の画面の一覧には出さない（使っている物だけ）。位置は記録の中の位置のまま渡す（外した行を除くとずれる）
            Members = record.Members
                .Select((member, index) => (member, index))
                .Where(entry => !entry.member.Detached)
                .Select(entry => Member(record, entry.member, entry.index))
                .ToList(),
            IconPath = ModificationIconPath(record),
            Thumbnails = thumbnails,
        };

    public HubMemberRow Member(ModificationRecord record, ModificationMember member, int index)
    {
        items.TryGetValue(member.ItemId, out var item);
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
            Thumbnails = thumbnails,
        };
    }

    /// <summary>
    /// どのファイルか。**空欄の意味を言い分ける**（改変の画面と同じ）。Unityへ送って足した分は unitypackage の名前、
    /// 手で足した分は分からないと言う。
    /// </summary>
    public static string FileTextOf(ModificationMember member) => member.Package is { } package
        ? Path.GetFileName(package)
        : member.IsFromUnity ? "Unityへ送った記録あり" : "どのファイルを使ったかは分かりません";

    public string? AvatarIconPath(string id)
        => AvatarImageSync.IconPath(services.Paths, id, items.GetValueOrDefault(id));

    /// <summary>頭の絵。改変に貼った写真の1枚目、無ければアバターの絵。</summary>
    public string? ModificationIconPath(ModificationRecord record)
    {
        if (record.Images.Count > 0)
        {
            var path = Path.Combine(services.Paths.ModificationImagesDir(record.Id), record.Images[0].FileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return AvatarIconPath(record.AvatarItemId);
    }

    /// <summary>商品の1枚目。検索のカードと同じ選び方（BOOTHの並び・★・役割の指定）。</summary>
    public string? ItemThumbnailPath(ItemRecord item)
    {
        if (thumbnailPaths is not null && thumbnailPaths.TryGetValue(item.Id, out var known))
        {
            return known;
        }

        var directory = services.Paths.ItemImagesDir(item.Id);
        var ordered = Core.Images.ItemImageOrder.Arrange(
            directory, item.Booth.Images, thumbnails.ListFiles(directory), item.Local.UserImages);
        return Core.Images.ItemImageOrder.Thumbnail(
            ordered, item.Local.ThumbnailImage, services.Settings.ThumbnailRole, item.Local.ImageRoles);
    }
}
