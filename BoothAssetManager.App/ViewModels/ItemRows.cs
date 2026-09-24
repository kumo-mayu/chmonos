using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothZipInspector;

namespace BoothAssetManager.App.ViewModels;

// ItemViewModel の画面に並べる行と小さな入れ物（技術的負債 4-1：画面のクラスのファイルから分けた。中身は変えていない）

public sealed class GalleryImage : ViewModelBase
{
    private bool _isSelected;

    public required string Path { get; init; }

    private BitmapSource? _image;

    /// <summary>一覧に出す小さな絵。裏で読み、届いたら入る（<see cref="LoadTile"/>）。</summary>
    public BitmapSource? Image
    {
        get => _image;
        set => SetField(ref _image, value);
    }

    /// <summary>
    /// 小さな絵を裏で読み、届いたら入れる（編集の帯と同じ扱い）。
    /// 前は一覧を組むときに画面のスレッドで1枚ずつ読み、画像の多い商品（20枚超）を開くたびにその分止まっていた
    /// </summary>
    public void LoadTile(Services.ThumbnailLoader thumbnails)
        => Image = thumbnails.PeekForTile(Path, () => LoadTile(thumbnails));

    /// <summary>BOOTH側の一覧から消えた画像。手元には残しておく。</summary>
    public bool IsOrphaned { get; init; }

    /// <summary>ユーザが自分で足した画像。**観測と入力を隠さない。**</summary>
    public bool IsUserAdded { get; init; }

    /// <summary>ファイル名。サムネイルの指名と、消すときに使う。</summary>
    public required string FileName { get; init; }

    /// <summary>サムネイルに指名されている1枚か。検索カードに出る絵。</summary>
    public bool IsPinned { get; init; }

    /// <summary>
    /// この画像の役割。付けていなければ出どころから決まる。
    /// 「改変例」だけは人が付けたものなので、札に出す。
    /// </summary>
    public Core.Models.ImageRole Role { get; init; }

    /// <summary>札に出す役割の名前。既定のままのものは出さない（札で埋まる）。</summary>
    public string RoleLabel => Role == Core.Models.ImageRole.Modified
        ? Core.Models.ImageRoles.Label(Role)
        : string.Empty;

    public bool HasRoleLabel => RoleLabel.Length > 0;

    /// <summary>
    /// 一覧の末尾に置く「足す」枠。画像ではない。
    ///
    /// 同じ並びに混ぜているのは、**折り返しても末尾に付いてくる**ようにするため。
    /// 別に置くと、画像が折り返したときだけ次の行へ落ちる。
    /// </summary>
    public bool IsAddTile { get; init; }

    public bool IsImage => !IsAddTile;

    /// <summary>今メインに出ている画像か。一覧のどれを見ているか分かるようにする。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetField(ref _isSelected, value);
    }
}

public sealed class VariationRow
{
    public required string Name { get; init; }

    public required string PriceText { get; init; }

    public bool IsPurchased { get; init; }

    /// <summary>BOOTH側に現存しない購入記録か。</summary>
    public bool IsGone { get; init; }
}

/// <summary>フォルダとして所有している1件。中身は個別に記録していない。</summary>
/// <summary>
/// 商品説明のh2セクション1つ。開閉を持つ。
///
/// **開閉は画面の状態なので Core には置かない。**`H2Section` は観測した中身で、
/// 畳んでいるかどうかは見る人の都合。
/// </summary>
public sealed class SectionRow(Core.Models.H2Section section) : ViewModelBase
{
    private bool _isOpen = true;

    public string Heading { get; } = section.Heading;

    public string Text { get; } = section.Text;

    /// <summary>既定は開いた状態（ユーザ指示）。畳んだ状態で出すと、あることに気付けない。</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (SetField(ref _isOpen, value))
            {
                OnPropertyChanged(nameof(Marker));
            }
        }
    }

    /// <summary>開閉の印。畳めることが分からないと押されない。</summary>
    public string Marker => _isOpen ? "▾" : "▸";
}

public sealed class LocalFolderRow
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public required string SummaryText { get; init; }

    /// <summary>登録した場所に今もあるか。無ければ指し直しが要る。</summary>
    public bool IsMissing { get; init; }

    /// <summary>
    /// 対応するzipが手元に入ったか。入っていればフォルダ登録は役目を終えている。
    /// 放っておくと容量が二重に数えられるので、その場で気付けるようにする。
    /// </summary>
    public bool HasArchive { get; init; }

    public string ArchiveNoticeText { get; init; } = string.Empty;

    /// <summary>
    /// 名前に乗せたときに出す場所（ユーザ指示 2026-09-19：名前の下に場所の行が続くと、同じ名前が2回並んで読みにくい）。
    /// 名前を押すとエクスプローラで開く
    /// </summary>
    public string PathToolTip => IsMissing
        ? $"{Path}\n記録にある場所にフォルダがありません。押すと、近くの残っているフォルダを開きます。"
        : $"{Path}\n押すと、エクスプローラでこのフォルダを開きます。";
}

/// <summary>この商品を使った改変1件。</summary>
public sealed class UsedInModificationRowViewModel
{
    public required Core.Models.ModificationRecord Record { get; init; }

    public required string AvatarText { get; init; }

    /// <summary>この商品が何回入っているか。同じ商品を別のバージョンで2回足せる。</summary>
    public required int UseCount { get; init; }

    public string Name => Record.Name;

    public string Detail => UseCount > 1
        ? $"{AvatarText}　この商品は {UseCount} 回入っています"
        : AvatarText;
}

/// <summary>Unityへ送れるもの1件と、Unityのどこに入るか。</summary>
public sealed class UnityPackageRow : ViewModelBase
{
    private string _destinationText = string.Empty;

    public required Core.Services.UnityPackageEntry Entry { get; init; }

    public string Name => Entry.Name;

    /// <summary>「Assets/〇〇 に入ります」。中を最後まで読むので、画面を出してから裏で埋まる。</summary>
    public string DestinationText
    {
        get => _destinationText;
        set
        {
            if (SetField(ref _destinationText, value))
            {
                OnPropertyChanged(nameof(HasDestination));
            }
        }
    }

    public bool HasDestination => DestinationText.Length > 0;
}

public sealed class LocalFileRow : ViewModelBase
{
    /// <summary>このファイルの同一性。商品から外すときに指す。</summary>
    public required string Hash { get; init; }

    public required string FileName { get; init; }

    public required string SizeText { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }

    public string? VariationLabel { get; init; }

    /// <summary>どの種類のファイルか。改変に積むときに、そのまま記録に入れる。</summary>
    public long? VariationId { get; init; }

    public bool HasVariationLabel => VariationLabel is not null;

    /// <summary>同じ中身が複数箇所にある状態。容量は1回しか数えない。</summary>
    public bool HasMultiplePaths => Paths.Count > 1;

    // 「実体」は内部の言葉に読めた（ユーザ指摘 2026-09-19）。ほかの場所の見出し・ツールチップと同じ「同じ中身」で言う
    public string DuplicateNote => $"同じ中身が {Paths.Count} 箇所に";

    public bool IsMissing => Paths.Count == 0;

    /// <summary>名前を押したときに開く場所（1つめ）。見つからないファイルには無い。</summary>
    public string? FirstPath => Paths.Count > 0 ? Paths[0] : null;

    public bool CanReveal => Paths.Count > 0;

    /// <summary>
    /// 名前に乗せたときに出す場所（ユーザ指示 2026-09-19：名前の下に場所の行が続くと、同じ名前が2回並んで読みにくい）。
    /// 場所が2つ以上あるときは全部を並べ、ほかの場所は下の行から開けることを言う
    /// </summary>
    public string PathToolTip => Paths.Count switch
    {
        0 => "記録にある場所にファイルがありません。",
        1 => $"{Paths[0]}\n押すと、エクスプローラでこのファイルの場所を開きます。",
        _ => $"同じ中身が {Paths.Count} 箇所にあります：\n{string.Join("\n", Paths)}\n"
            + "押すと、1つめの場所をエクスプローラで開きます。ほかの場所は下の行から開けます。",
    };

    /// <summary>ほかの場所（2つめ以降）。1つめは名前を押せば開くので、行にしない。</summary>
    public IReadOnlyList<string> OtherPaths => Paths.Count > 1 ? Paths.Skip(1).ToList() : [];

    /// <summary>この商品から外したファイル（ユーザ判断 2026-09-12：消さずに灰色で残す）。</summary>
    public bool IsDetached { get; init; }

    /// <summary>「この商品から外す」を出すか（外していない行だけ）。</summary>
    public bool IsAttached => !IsDetached;

    /// <summary>「この商品に戻す」を押せるか。外した後で別の商品へ紐付けてあれば押せない。</summary>
    public bool CanReattach { get; init; }

    /// <summary>「この商品に戻す」の説明。押せないときはその理由。</summary>
    public string ReattachTip { get; init; } = string.Empty;

    private IReadOnlyList<Core.Services.UnityPackageEntry> _unityPackages = [];
    private IReadOnlyList<UnityPackageRow> _unityPackageRows = [];

    /// <summary>
    /// このzipに入っている、Unityへ送れるもの。
    /// zipを開いて数えるので、**行を出した後で、画面のスレッドの外で読んで付ける**（技術的負債 4-2）。
    /// </summary>
    public IReadOnlyList<Core.Services.UnityPackageEntry> UnityPackages
    {
        get => _unityPackages;
        set
        {
            if (SetField(ref _unityPackages, value))
            {
                OnPropertyChanged(nameof(HasUnityPackages));
                OnPropertyChanged(nameof(HasManyUnityPackages));
            }
        }
    }

    public bool HasUnityPackages => UnityPackages.Count > 0;

    private bool _canUnpack;

    /// <summary>
    /// 一時フォルダへ展開できるか（手元にある zip のときだけ）。
    /// **在るかは行を出した後で、画面のスレッドの外で確かめて付ける**（技術的負債 4-2）。
    /// </summary>
    public bool CanUnpack
    {
        get => _canUnpack;
        set => SetField(ref _canUnpack, value);
    }

    /// <summary>画面に並べる行。入る先を後から埋めるので、中身とは別に持つ。</summary>
    public IReadOnlyList<UnityPackageRow> UnityPackageRows
    {
        get => _unityPackageRows;
        set => SetField(ref _unityPackageRows, value);
    }

    /// <summary>
    /// 複数入っているときの注意。
    ///
    /// **順番を当てにいかない。**実データでは2件とも片方が依存物だったが、
    /// 2件から規則は決められない。人に決めてもらう。
    /// </summary>
    public bool HasManyUnityPackages => UnityPackages.Count > 1;

    public string UnityPackageNote =>
        $"Unityへ送れるもの {UnityPackages.Count} 件（依存するものを先に入れてください）";
}

public sealed class AttributeBar
{
    public required string Name { get; init; }

    public required int Value { get; init; }

    public double BarWidth => Value * 2.4;
}

/// <summary>商品ページに出す対応アバター1件（出品者の宣言）。</summary>
public sealed class AvatarRow : ChipTile
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }

    /// <summary>どこから拾ったか。推定を確定と同じ顔で出さないために添える。</summary>
    public required string SourceText { get; init; }

    public bool IsUnconfirmed { get; init; }

    /// <summary>このアバターを持っているか（U25）。札を「所持」の緑にして先頭へ寄せる。</summary>
    public bool IsOwned { get; init; }

    private System.Windows.Media.Imaging.BitmapSource? _icon;
    private bool _iconLoaded;

    /// <summary>ツールチップに出す絵を作るもの（R3）。</summary>
    public Func<System.Windows.Media.Imaging.BitmapSource?>? IconFactory { get; init; }

    /// <summary>ツールチップの絵。乗せたときに初めて読む。</summary>
    public System.Windows.Media.Imaging.BitmapSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _icon = IconFactory?.Invoke();
            }

            return _icon;
        }
    }

    public bool HasIcon => Icon is not null;

    /// <summary>
    /// どこから拾ったか、確認済みかをホバーで出す。
    /// 常時出すとチップが横に長くなり、1行に1〜2個しか入らなくなる。
    /// 所持は色だけに頼らず、ここでも言葉で言う。
    /// </summary>
    public string SourceTooltip => (IsOwned ? "持っているアバターです。" : string.Empty)
        + (IsUnconfirmed
            ? $"{SourceText}から読み取りました（未確認）。押すとこのアバターを開きます"
            : $"{SourceText}から読み取りました。押すとこのアバターを開きます");

    /// <summary>この対応は違う、と消すための操作。行にホバーしたときだけ出す。</summary>
    public RelayCommand? RejectCommand { get; init; }

    /// <summary>このアバターを開く（U13）。持っていれば商品ページ、持っていなければアバター画面で選んだ状態。</summary>
    public RelayCommand? OpenCommand { get; init; }
}

/// <summary>
/// 畳める欄の開き具合。商品ページと編集画面で共通にし、商品を移っても保つ（アプリを閉じるまで・ユーザ指示 2026-09-12）。
/// 既定は開く——畳むのは多過ぎる商品を見たときの操作で、普段は見えている方が早い。
/// </summary>
public static class SectionFolds
{
    public static bool BoothTagsExpanded { get; set; } = true;

    public static bool AvatarsExpanded { get; set; } = true;

    /// <summary>商品ページの「商品説明」（ユーザ指示 2026-09-14：説明も畳めるように）。</summary>
    public static bool DescriptionExpanded { get; set; } = true;
}

/// <summary>ユーザが消した対応アバターの1行（「消したもの」の欄）。</summary>
public sealed class RejectedAvatarRow
{
    public required string Name { get; init; }

    /// <summary>この対応を戻す。出どころは「手入力」になる。</summary>
    public RelayCommand? RestoreCommand { get; init; }
}

/// <summary>
/// 商品が対応している共通素体の1件（ユーザ判断 2026-09-21・X3/X4）。
/// 足す道（検出）しか無く、誤検出を消せなかったので、対応アバターと同じ形にした。
/// </summary>
public sealed class AvatarBaseRow
{
    public required string Name { get; init; }

    /// <summary>この対応は違う、として消す。次の検出でも復活しない。</summary>
    public RelayCommand? RejectCommand { get; init; }
}
