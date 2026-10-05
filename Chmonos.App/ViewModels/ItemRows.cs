using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Chmonos.App.Controls;
using Chmonos.App.Services;
using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using BoothZipInspector;

namespace Chmonos.App.ViewModels;

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

    /// <summary>一覧の何枚目か（1から）。一覧を組み直すたびに振り直す（並べ替え・足す・消すは、どれも組み直しを通る）。</summary>
    public int Number { get; init; }

    /// <summary>
    /// 読み上げ・自動操作での名前。前は全部「この画像を表示」で、どの1枚かを名前で指せなかった
    /// （行ごとに繰り返す部品は、どの行の物かを名前に入れる。ui-input.md）。絵には名前が無いので、並びの番号で言う
    /// </summary>
    public string ShowName => IsAddTile ? string.Empty : $"{Number} 枚目の画像を表示";

    /// <summary>今メインに出ている画像か。一覧のどれを見ているか分かるようにする。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetField(ref _isSelected, value);
    }
}

public sealed record VariationRow
{
    public required string Name { get; init; }

    public bool IsPurchased { get; init; }

    /// <summary>BOOTH のバリエーションの ID（知らせの値段の変化を当てる鍵）。買った記録だけの行・知らせるために差し込んだ行では null。</summary>
    public long? VariationId { get; init; }

    /// <summary>購入記録1件ずつの札の文（「¥1,200 で買った」「¥1,500 で贈った」「貰った」）。同じバリエーションの購入を全部並べる。買っていない行は空（メモ54）。</summary>
    public IReadOnlyList<string> Purchases { get; init; } = [];

    /// <summary>行の右端に固定する BOOTH の価格（「¥1,500」。変わっていれば「¥1,200 → ¥1,500」。メモ27-⑤・54）。BOOTH に無い行（消えた・差し込んだ行）は空。</summary>
    public string BoothPrice { get; init; } = string.Empty;

    /// <summary>BOOTH側に現存しない購入記録か。</summary>
    public bool IsGone { get; init; }

    /// <summary>
    /// 知らせの差と突き合わせる名前（BOOTH の名前を差と同じ形に詰めた物。名前の無いバリエーションは空）。
    /// 画面に出す名前（<see cref="Name"/>）は名前の無い物を言い換えているので、突き合わせには使わない
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// BOOTH の更新で足された（緑の帯）・消えた（赤の帯。メモ17）・値段の変わった（青の帯。メモ27-⑤）バリエーションか。変わっていなければ null。
    /// 足された・消えた行の値段が同時に変わることは無い（値段の変化は前後の両方にある物だけ）ので、帯は1つで足りる
    /// </summary>
    public ChangeTone? Band { get; init; }

    /// <summary>BOOTH で消えたことを知らせるためだけに差し込んだ行（今の商品にも買った記録にも無い）。欄の件数には数えない。</summary>
    public bool IsNoticeOnly { get; init; }

    public string BandTip => Band switch
    {
        ChangeTone.Added => "BOOTHで追加されたバリエーションです。",
        ChangeTone.Removed => "BOOTHで削除されたバリエーションです。",
        ChangeTone.Price => "BOOTHで価格が変更されたバリエーションです。",
        _ => string.Empty,
    };
}

/// <summary>フォルダとして所有している1件。中身は個別に記録していない。</summary>
/// <summary>
/// 商品説明のh2セクション1つ。開閉を持つ。
///
/// **開閉は画面の状態なので Core には置かない。**`H2Section` は観測した中身で、
/// 畳んでいるかどうかは見る人の都合。
/// </summary>
public sealed class SectionRow : ViewModelBase
{
    private bool _isOpen = true;

    public SectionRow(Core.Models.H2Section section)
        : this(
            section.Heading,
            section.Text,
            section.NormalizedHeading.Length > 0 ? section.NormalizedHeading : Core.Services.BoothChanges.DescriptionField,
            isRemoved: false)
    {
    }

    private SectionRow(string heading, string text, string key, bool isRemoved)
    {
        Heading = heading;
        Text = text;
        Key = key;
        IsRemoved = isRemoved;
    }

    /// <summary>
    /// BOOTH で消えた見出し（メモ17：元の位置に見出しごと赤の帯で並べる）。前のページは保存していないので、
    /// 見出しは知らせの正規化した名前、本文は知らせに残した消えた行だけ（<see cref="Lines"/>）
    /// </summary>
    public static SectionRow ForRemoved(string key) => new(key, string.Empty, key, isRemoved: true);

    public string Heading { get; }

    public string Text { get; }

    /// <summary>
    /// 更新の知らせの差と突き合わせる名前。見出しの原文は装飾記号付きなので、知らせを作る側（<c>BoothChanges</c>）と同じく
    /// 正規化した見出しで引き、空なら「説明文」
    /// </summary>
    public string Key { get; }

    /// <summary>BOOTH で消えた見出しの行か（<see cref="ForRemoved"/>）。</summary>
    public bool IsRemoved { get; }

    private bool _isShown = true;

    /// <summary>出しているか。消えた見出しは「既読にする」で隠す（一覧を作り直すと見ている所が動くので、行は残して畳む）。</summary>
    public bool IsShown
    {
        get => _isShown;
        set => SetField(ref _isShown, value);
    }

    private ChangeTone? _band;

    /// <summary>
    /// 見出しごと足された（緑）・消えた（赤）ときの、見出しの行の帯（メモ17・ユーザ指示 2026-10-03「見出しごと足されたり消えたりが
    /// 表示上わかりにくいので見出しから色を付けよう」）。本文の行の帯と同じ色で、見出しから本文までひと続きに見せる
    /// </summary>
    public ChangeTone? Band
    {
        get => _band;
        set => SetField(ref _band, value);
    }

    private ChangeSlot _change = ChangeSlot.Empty;

    /// <summary>未読の更新で変わった見出しの印（メモ7-①）。「既読にする」で外す。</summary>
    public ChangeSlot Change
    {
        get => _change;
        set => SetField(ref _change, value);
    }

    private ChangedLineMarks _lines = ChangedLineMarks.None;

    /// <summary>
    /// 本文の中の変わった行（メモ13-②・ユーザ指示 2026-10-02「項目だけではどこが変更されたのか、要確認画面と往復しないと分からない」）。
    /// 足した行も消えた行も本文の上の帯で示し、消えた行は元の位置に差し込む（メモ17）。「既読にする」で外す
    /// </summary>
    public ChangedLineMarks Lines
    {
        get => _lines;
        set => SetField(ref _lines, value);
    }

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
    /// <summary>記録のパス（登録を外す相手の見分けに使う。書き換えない）。</summary>
    public required string Path { get; init; }

    /// <summary>開く場所。ドライブ文字が変わっていれば今の文字に読み替えた物（<see cref="VolumeTable.Current"/>）。</summary>
    public string OpenPath { get => _openPath ?? Path; init => _openPath = value; }

    private readonly string? _openPath;

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
        ? $"{Path}\nフォルダが見つかりません。押すと近くのフォルダを開きます。"
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

    /// <summary>
    /// この包みが入っているファイルの行。「Unity ▾」が押せるか・吹き出しは、ファイルが在るかで決まる
    /// （中の一覧は記録から出るので、ファイルが見つからなくても包みの行は並ぶ）。
    /// </summary>
    public required LocalFileRow FileRow { get; init; }

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

    private Core.Services.FilePresence _presence = Core.Services.FilePresence.Present;

    /// <summary>
    /// 記録の場所に今あるか（点検 2026-09-30 の B：前は記録のパスが空のときだけ「見つかりません」を出し、
    /// 移したファイルも普通の行と［開く ▾］で出ていた）。
    /// **行を出した後で、画面のスレッドの外で確かめて付ける**（外付け・ネットワークで待たされないように。技術的負債 4-2）。
    /// 確かめるまでは在るとして出す——ほとんどの行は在るので、全部の［開く ▾］を一度隠してから出すとちらつく
    /// </summary>
    public Core.Services.FilePresence Presence
    {
        get => _presence;
        set
        {
            if (SetField(ref _presence, value))
            {
                OnPropertyChanged(nameof(IsMissing));
                OnPropertyChanged(nameof(IsUnverifiable));
                OnPropertyChanged(nameof(IsOnDetachedDrive));
                OnPropertyChanged(nameof(CanReveal));
                OnPropertyChanged(nameof(OpenMenuTip));
                OnPropertyChanged(nameof(UnityMenuTip));
                OnPropertyChanged(nameof(PathToolTip));
                OnPropertyChanged(nameof(ShowsBrokenArchive));
            }
        }
    }

    /// <summary>取り込みで zip として開けなかった物（記録の印。未確定の行の「壊れたzip」と同じ事実）。</summary>
    public bool IsBrokenArchive { get; init; }

    /// <summary>
    /// 札「壊れたzip」を出すか（ユーザ判断 2026-09-30）。**在る物にだけ出す。**無い物・取り外しているドライブの上の物は、
    /// 次の手が「取り込み直す」「つなぐ」で、札を2つ並べると「ダウンロードし直す」とどちらを先にするのか分からない。
    /// 無い物は壊れているかをもう見られないので、「見つかりません」だけを言う
    /// </summary>
    public bool ShowsBrokenArchive => IsBrokenArchive && Paths.Count > 0 && Presence == Core.Services.FilePresence.Present;

    /// <summary>
    /// 同じ場所で新しい中身に置き換わった古い版（記録の印 <c>replaced</c>・2026-10-05・点検の8）。
    /// 探して見つかる物ではないので「見つかりません」と言わず、「古い版」と出して片付けられるようにする。
    /// </summary>
    public bool IsOldVersion { get; init; }

    /// <summary>「古い版の記録を片付ける」を出すか。外した行は設定の「外した記録を消す」で片付くので、ここには出さない。</summary>
    public bool CanForgetOldVersion => IsOldVersion && !IsDetached;

    // 確かめられない（親のフォルダを読む権限が無い・ドライブが答えない。点検の13）は「見つかりません」と分ける（2026-10-05・MB-B）。
    // 記録には「無い」と書かない（カードの印も出ない）のに、ここだけ「見つかりません」と出ていて食い違っていた。次の手も取り込みではなく権限を見ること
    public bool IsMissing => !IsOldVersion && (Paths.Count == 0 || Presence == Core.Services.FilePresence.Missing);

    /// <summary>どの場所にも在ると言えず、確かめられない場所がある（権限が無い等）。</summary>
    public bool IsUnverifiable => Paths.Count > 0 && Presence == Core.Services.FilePresence.Unverifiable;

    /// <summary>つながっていないドライブの上にしか場所が無い。無くなったとは限らないので「見つかりません」と分ける</summary>
    public bool IsOnDetachedDrive => Paths.Count > 0 && Presence == Core.Services.FilePresence.OnDetachedDrive;

    /// <summary>名前を押したときに開く場所（1つめ）。見つからないファイルには無い。</summary>
    public string? FirstPath => Paths.Count > 0 ? Paths[0] : null;

    public bool CanReveal => Paths.Count > 0 && Presence == Core.Services.FilePresence.Present;

    // 「開く ▾」「Unity ▾」は押せないときも出したまま薄くし、吹き出しで理由を言う（ユーザ判断 2026-10-04。右クリックのメニューと同じ形）。
    // 前はボタンごと消していて、見つからないファイルの行だけ形が変わり、なぜ無いのか分からなかった。
    // 押せる条件は名前のリンクと同じ CanReveal（在る場所が1つ以上ある）

    private const string UnverifiableTip = "ファイルを確かめられません。権限を確かめてください。";
    private const string OldVersionTip = "新しい版に置き換わっています。";

    /// <summary>「開く ▾」の吹き出し。押せないときはその理由。</summary>
    public string OpenMenuTip => CanReveal ? "このファイルの開き方を選びます"
        : IsOnDetachedDrive ? "ドライブをつなぐと開けます。"
        : IsUnverifiable ? UnverifiableTip
        : IsOldVersion ? OldVersionTip
        : "ファイルが見つかりません。";

    /// <summary>この行の包みの「Unity ▾」の吹き出し。押せないときはその理由。</summary>
    public string UnityMenuTip => CanReveal
        ? "開いているUnityへ送るか、Unityのプロジェクトタブで場所を示します。"
        : IsOnDetachedDrive ? "ドライブをつなぐと送れます。"
        : IsUnverifiable ? UnverifiableTip
        : IsOldVersion ? OldVersionTip
        : "ファイルが見つかりません。";

    /// <summary>
    /// 名前に乗せたときに出す場所（ユーザ指示 2026-09-19：名前の下に場所の行が続くと、同じ名前が2回並んで読みにくい）。
    /// 場所が2つ以上あるときは全部を並べ、ほかの場所は下の行から開けることを言う
    /// </summary>
    public string PathToolTip => Paths.Count switch
    {
        0 when IsOldVersion => OldVersionTip,
        0 => "ファイルが見つかりません。",
        _ when IsMissing => $"{string.Join("\n", Paths)}\nファイルが見つかりません。",
        _ when IsUnverifiable => $"{string.Join("\n", Paths)}\n{UnverifiableTip}",
        _ when Presence == Core.Services.FilePresence.OnDetachedDrive
            => $"{string.Join("\n", Paths)}\nドライブをつなぐと開けます。",
        1 => $"{Paths[0]}\n押すと、エクスプローラでこのファイルの場所を開きます。",
        _ => $"同じ中身が {Paths.Count} 箇所にあります：\n{string.Join("\n", Paths)}\n"
            + "押すと1つめの場所を開きます。ほかは下の行から開けます。",
    };

    /// <summary>ほかの場所（2つめ以降）。1つめは名前を押せば開くので、行にしない。</summary>
    public IReadOnlyList<string> OtherPaths => Paths.Count > 1 ? Paths.Skip(1).ToList() : [];

    /// <summary>この商品から外したファイル（ユーザ判断 2026-09-12：消さずに灰色で残す）。</summary>
    public bool IsDetached { get; init; }

    /// <summary>「この商品から外す」を出すか（外していない行だけ）。</summary>
    public bool IsAttached => !IsDetached;

    /// <summary>「この商品から外す」を出すか。古い版はもうどこにも無く未確定へ戻す物が無いので、代わりに「古い版の記録を片付ける」を出す。</summary>
    public bool CanDetach => !IsDetached && !IsOldVersion;

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
                // 件数を文に入れているので、これも知らせる（知らせないと、後から読んで付けたときに「0 件」のまま残る）
                OnPropertyChanged(nameof(UnityPackageNote));
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
        $"Unityへ送れるもの {UnityPackages.Count} 件。依存するものから先に送ってください。";
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

    /// <summary>商品ページのユーザータグ（メモ3-③・ユーザ判断 2026-10-02：BOOTHのタグと揃えて畳めるようにする）。</summary>
    public static bool UserTagsExpanded { get; set; } = true;

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

/// <summary>説明文に書かれた共通素体の候補1件（ユーザ判断 2026-09-29：自動では入れず、押したときだけ入れる）。</summary>
public sealed class BaseMentionRow
{
    public required string Name { get; init; }

    /// <summary>共通素体の一覧にまだ無い素体。押すと一覧にも足す。</summary>
    public bool IsNew { get; init; }

    public string AddTooltip => IsNew
        ? "共通素体の一覧とこの商品に追加します。"
        : "この商品の共通素体に追加します。";

    public RelayCommand? AddCommand { get; init; }

    /// <summary>違う候補として消す（「消したもの」に入り、戻せる。ユーザ判断 2026-09-29）。</summary>
    public RelayCommand? DismissCommand { get; init; }
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
