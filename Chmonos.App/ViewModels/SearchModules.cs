using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 検索の絞り込みのモジュール（ユーザ案 2026-09-15・`docs/history/search-redesign.md`）。
/// 名前は ui-state.json と検索の履歴に書くので、変えると古い状態が読めなくなる（読めなかった物は黙って飛ばす）。
/// </summary>
public enum SearchModuleKind
{
    Category,
    BoothTag,
    Shop,
    WishList,
    Price,
    EndOfSale,
    PublishedAt,
    Adult,
    Owned,
    Gift,
    FreePaid,
    UserTag,
    Attribute,
    Avatar,
    Favorite,
    AcquiredAt,
    Hidden,
    Unedited,

    // 「対応アバターの確認」（AvatarUnconfirmed・2026-09-18）は、編集状況の項目の1つにした（ユーザ判断 2026-10-06）。
    // 種類ごと消したので、前に保存した状態のその条件は読めずに飛ばされる（公開前なので救済はしない）
    Modification,
    UnityProject,
    Path,
    Recent,

    /// <summary>壊れていて開けない zip を持つか（ユーザ判断 2026-09-30）。</summary>
    BrokenZip,

    /// <summary>記録の上では持っているが、置き場がどこにも無いファイルがあるか（ユーザ判断 2026-10-04。カードの印「見つかりません」と同じ数え方）。</summary>
    MissingFile,

    /// <summary>仮のIDで登録した「BOOTHに無い商品」か（ユーザ指示 2026-10-04 メモ31。判定は <see cref="Chmonos.Core.Models.ItemRecord.IsLocalOnly"/> と同じ）。</summary>
    NotOnBooth,

    /// <summary>要確認に未読の「商品の更新」の知らせがあるか（ユーザ指示 2026-10-02。カードの札「更新あり」と同じ数え方）。画面の名前は「更新通知あり」（2026-10-06）。</summary>
    Updated,
}

/// <param name="Headings">「条件を追加」のメニューのどの見出しの下に出すか。重なってよい（ユーザ案：分類の重複を許す）。</param>
/// <param name="AllowsMany">
/// 同じ種類を複数置けるか（ユーザ判断 2026-10-01）。値を選んで積む条件だけ。条件どうしは AND なので、
/// 「(A か B) かつ (C か D)」や「A を含み B を除く」が組める。1商品に1つのショップ・三項・属性・最近は1つまで
/// （2つ置いても組める物が増えない）。メニューのグレーと、足すときに既にある物を返すかの決まりは、どちらもこれを見る。
/// スキ数・価格・公開日・入手日も複数置ける（ユーザ判断 2026-10-02・メモ2-②。<paramref name="OrSameKind"/>）。
/// </param>
/// <param name="OrSameKind">
/// 同じ種類の条件どうしを**どれかに当てはまる物（和集合）**でつなぐか（ユーザ判断 2026-10-02・メモ2-②「0-400&amp;1000-2000ならそれぞれに当てはまるものの和集合」）。
/// 範囲（スキ数・価格）と日付（公開日・入手日）だけ。1つの範囲で2つの帯は指せないので、AND でつなぐと2つ目を置く意味が無い。
/// 除くを付けた物は和集合に入れず、除かない物の和集合からそれぞれ引く（<see cref="SearchFilterPass"/>）。ほかの種類との間は今までどおり AND。
/// </param>
public sealed record SearchModuleInfo(SearchModuleKind Kind, string Label, string Hint, bool AllowsMany = false, bool OrSameKind = false);

/// <summary>
/// 条件の並びの決まり（純粋な関数・試験あり）。足す位置と「同じ種類の条件を隣に並べる」（ユーザ判断 2026-10-01・案の §5）。
/// </summary>
public static class SearchModuleOrder
{
    /// <summary>
    /// 新しく足す条件の位置。既定は一番下。<paramref name="nearSameKind"/>（設定）なら、同じ種類の**一番上の塊**の最後の直後
    /// （塊＝同じ種類が隣り合って続く所。分かれていても一番上の塊に付ける。隣に並べたときにその種類が集まる位置と同じ）。
    /// 同じ種類が無ければ一番下。
    /// </summary>
    public static int InsertIndex(IReadOnlyList<SearchModuleKind> kinds, SearchModuleKind kind, bool nearSameKind)
    {
        if (!nearSameKind)
        {
            return kinds.Count;
        }

        var first = -1;
        for (var index = 0; index < kinds.Count; index++)
        {
            if (kinds[index] == kind)
            {
                first = index;
                break;
            }
        }

        if (first < 0)
        {
            return kinds.Count;
        }

        var end = first;
        while (end < kinds.Count && kinds[end] == kind)
        {
            end++;
        }

        return end;
    }

    /// <summary>
    /// 同じ種類を隣に並べた並び（元の位置の番号の並び）。種類ごとに初めて出た順に、その種類の条件を元の順のまま寄せる（安定な寄せ）。
    /// 例：[タグ1, アバター1, タグ2, カテゴリ, アバター2] → [タグ1, タグ2, アバター1, アバター2, カテゴリ]。
    /// </summary>
    public static IReadOnlyList<int> GroupByKind(IReadOnlyList<SearchModuleKind> kinds)
    {
        var order = new List<SearchModuleKind>();
        var members = new Dictionary<SearchModuleKind, List<int>>();
        for (var index = 0; index < kinds.Count; index++)
        {
            if (!members.TryGetValue(kinds[index], out var list))
            {
                list = [];
                members[kinds[index]] = list;
                order.Add(kinds[index]);
            }

            list.Add(index);
        }

        return order.SelectMany(kind => members[kind]).ToList();
    }

    /// <summary>同じ種類がもう隣に並んでいるか（並べても変わらないか）。</summary>
    public static bool IsGrouped(IReadOnlyList<SearchModuleKind> kinds)
        => GroupByKind(kinds).Select((from, to) => from == to).All(same => same);
}

/// <summary>
/// AND／OR の言い方（ユーザ判断 2026-10-06・メモ82「AND と OR の言い方はモジュール間で共有」）。
/// 形はどの条件も「AND にするか」のチェック1つで、切れば OR。条件ごとに言い方を書くと揃わなくなるので、ここ1か所に置く。
/// 「すべて」はかなで書く（`docs/spec/ui-terms.md` の揃える表記）。
/// </summary>
public static class MatchModeText
{
    public const string All = "すべてを満たす商品のみ（AND）";

    public const string Any = "いずれかを満たす商品（OR）";

    /// <summary>チェックの吹き出し。切ったときに何が出るかを言う。</summary>
    public const string AllHint = "切ると、" + Any + "を表示します。";

    /// <summary>何どうしを結ぶかを言い分ける所（ユーザータグの大分類どうしなど、枠の中にもう1つチェックがある条件）。</summary>
    public static string AllOf(string noun) => $"すべての{noun}を満たす商品のみ（AND）";

    public static string AllHintOf(string noun) => $"切ると、いずれかの{noun}を満たす商品（OR）を表示します。";

    /// <summary>
    /// 1つしか無くて AND／OR が意味を成さないときの吹き出し（ユーザ判断 2026-10-06）。
    /// AND／OR は隠さずに薄く押せなくする——出たり消えたりすると、下の欄が縦に揺れる。押せない理由は使う人の次の手なので、吹き出しで言う。
    /// </summary>
    public const string NeedsTwo = "2つ以上追加すると選べます。";

    /// <summary>何を2つ以上か言い分ける所（枠の中にもう1つ AND／OR がある条件）。</summary>
    public static string NeedsTwoOf(string noun) => $"{noun}を2つ以上追加すると選べます。";

    /// <summary>チェックで選ぶ項目（編集状況の見る項目・更新通知ありの見る種類）。追加ではなく入れる物なので、言い方を分ける。</summary>
    public static string NeedsTwoOn(string noun) => $"{noun}が2つ以上のときに選べます。";
}

/// <param name="Groups">見出しの中の、意味のまとまり。まとまりの間に区切り線を引く。</param>
public sealed record SearchModuleMenuLayout(string Title, IReadOnlyList<IReadOnlyList<SearchModuleKind>> Groups);

/// <summary>モジュールの一覧。名前・説明・メニューの見出しと並びをここだけで決める。</summary>
public static class SearchModuleCatalog
{
    public const string BoothInfo = "BOOTHの情報";
    public const string ItemInfo = "商品の情報";
    public const string FileInfo = "ファイルの情報";
    public const string Calendar = "カレンダー";
    public const string Slider = "スライダー";
    public const string Usage = "利用状況";

    /// <summary>
    /// 保存された並びが無いときに出しておく条件（ユーザ指示 2026-09-29。前は 所持・ユーザータグ・対応アバター＝2026-09-16 Q9）。
    /// どれも足しただけでは絞らない形（カテゴリは空・お気に入りは「両方」）なので、最初に全件が見える。
    /// </summary>
    public static IReadOnlyList<SearchModuleKind> Defaults { get; } =
        [SearchModuleKind.Category, SearchModuleKind.UserTag, SearchModuleKind.Favorite];

    public static IReadOnlyList<SearchModuleInfo> All { get; } =
    [
        new(SearchModuleKind.Category, "カテゴリ", "BOOTHのカテゴリ（自分で入れたカテゴリを含む）で絞ります。", AllowsMany: true),
        new(SearchModuleKind.BoothTag, "BOOTHタグ", "BOOTHのタグで絞ります。", AllowsMany: true),
        new(SearchModuleKind.Shop, "ショップ", "ショップで絞ります。ショップ画面で星を付けたお気に入りのショップもまとめて選べます。"),
        new(SearchModuleKind.WishList, "スキ数", "BOOTHのスキ数で絞ります。", AllowsMany: true, OrSameKind: true),
        new(SearchModuleKind.Price, "価格", "既定は自分が払った額。切り替えるとBOOTH価格で絞ります。", AllowsMany: true, OrSameKind: true),
        // 名前は「公開状況」（ユーザ判断 2026-10-06。前は「販売終了」）。中身が「販売終了・非公開／公開中／両方」になり、販売終了だけの条件ではなくなった。
        // 種類の名前（EndOfSale）は保存した状態に書くので変えない
        new(SearchModuleKind.EndOfSale, "公開状況", "BOOTHで公開中か、販売終了・非公開かで絞ります。"),
        new(SearchModuleKind.PublishedAt, "公開日", "BOOTHでの公開日で絞ります。", AllowsMany: true, OrSameKind: true),
        new(SearchModuleKind.Adult, "R-18", "R-18 の商品で絞ります。"),
        new(SearchModuleKind.Owned, "所持", "手元にファイルがあるかで絞ります。"),
        new(SearchModuleKind.Gift, "ギフト", "購入記録で絞ります。貰って自分でも買った商品は、どちらにも表示されます。"),
        new(SearchModuleKind.FreePaid, "有料・無料", "BOOTH価格で絞ります。"),
        new(SearchModuleKind.UserTag, "ユーザータグ", "自分で付けたタグで絞ります。", AllowsMany: true),
        new(SearchModuleKind.Attribute, "属性", "自分で付けた属性の値で絞ります。評価していない商品は外れます。"),
        new(SearchModuleKind.Avatar, "対応アバター", "対応しているアバター・共通素体で絞ります。", AllowsMany: true),
        new(SearchModuleKind.Favorite, "お気に入り", "カードの星で絞ります。"),
        new(SearchModuleKind.AcquiredAt, "入手日", "入手日か買った日で絞ります。日付を入れていない商品は外れます。", AllowsMany: true, OrSameKind: true),
        new(SearchModuleKind.Hidden, "非表示", "非表示にした商品を表示します。この条件が無いときは、非表示の商品は表示しません。"),
        new(SearchModuleKind.Unedited, "編集状況", "編集画面の項目を入力したか、対応アバターを確認したかで絞ります。", AllowsMany: true),
        new(SearchModuleKind.Modification, "改変", "改変に使った商品で絞ります。アバターだけを選ぶと、そのアバターの改変のどれかに使った商品です。", AllowsMany: true),
        new(SearchModuleKind.UnityProject, "Unityプロジェクト", "そのプロジェクトに紐付けた改変に使った商品で絞ります。", AllowsMany: true),
        new(SearchModuleKind.Path, "ファイルの場所", "手元のファイルのフォルダで絞ります。その下のフォルダも含みます。", AllowsMany: true),
        new(SearchModuleKind.Recent, "最近", "Unity送信・商品閲覧・取り込みをした日で絞ります。"),
        new(SearchModuleKind.BrokenZip, "壊れたzip", "壊れていて開けないzipがある商品で絞ります。"),
        new(SearchModuleKind.MissingFile, "見つからないファイル", "記録にはあるのに、置き場が見つからないファイルやフォルダがある商品で絞ります。"),
        new(SearchModuleKind.NotOnBooth, "BOOTHに無い商品", "BOOTHに無い商品として登録した商品か、BOOTHの商品かで絞ります。"),
        new(SearchModuleKind.Updated, "更新通知あり", "未読の更新の通知がある商品を、変わった内容の種類で絞ります。"),
    ];

    /// <summary>
    /// 「条件を追加」の見出しと、見出しの中の並び（ユーザ判断 2026-10-06・open.md の「条件を追加の並びの監修」。前の形は 2026-09-16・案1）。
    ///
    /// **意味のまとまりで並べ、まとまりの間に区切り線を引く**。並びは見出しごとに決める（全体で1つの並びだと、ある見出しで良い並びが別の見出しで崩れる）。
    /// 同じ条件が2つの見出しに出てよい：対応アバター・価格・R-18・公開日は BOOTH に載っている物で BOOTH の情報に見え、
    /// 自分で直せる・払った額で見る・自分の商品の整理に使うので商品の情報でもある。所持は商品の情報（手に入れ方）にもファイルの情報（手元のファイル）にも見える。
    /// - BOOTHの情報：何の商品か → お金と人気 → 誰が出し何に使えるか → BOOTH の側の時の流れ（出た日 → 今も出ているか → 出た後に変わったか）
    /// - 商品の情報：自分の整理（タグ・評価・星・表示） → 手に入れ方（持っているか・ギフトか・BOOTH に無いか） → 使い方 → 日付 → 入力の片付け（編集状況）
    /// - ファイルの情報（2026-10-06 に商品の情報から分けた。所持の群が7つと長く、ファイルの傷みと手に入れ方が混ざっていた）：
    ///   手元のファイルが在るか → 記録の物が置き場に在るか → 在る物が開けるか → どこに置いたか
    /// - カレンダー・スライダー・利用状況：入れる部品の形・使い方で引く見出し。見出しの並びは情報の3つ → 部品の形 → 使い方
    /// </summary>
    public static IReadOnlyList<SearchModuleMenuLayout> Menu { get; } =
    [
        new(BoothInfo,
        [
            [SearchModuleKind.Category, SearchModuleKind.BoothTag, SearchModuleKind.Adult],
            [SearchModuleKind.Price, SearchModuleKind.WishList, SearchModuleKind.FreePaid],
            [SearchModuleKind.Shop, SearchModuleKind.Avatar],

            // 更新通知ありは公開状況のすぐ後：どちらも BOOTH の側で商品に起きた変化
            [SearchModuleKind.PublishedAt, SearchModuleKind.EndOfSale, SearchModuleKind.Updated],
        ]),
        new(ItemInfo,
        [
            [SearchModuleKind.UserTag, SearchModuleKind.Attribute, SearchModuleKind.Favorite, SearchModuleKind.Price, SearchModuleKind.Avatar, SearchModuleKind.Hidden, SearchModuleKind.Adult],
            [SearchModuleKind.Owned, SearchModuleKind.Gift, SearchModuleKind.NotOnBooth],
            [SearchModuleKind.Modification, SearchModuleKind.Recent, SearchModuleKind.UnityProject],
            [SearchModuleKind.AcquiredAt, SearchModuleKind.PublishedAt],
            [SearchModuleKind.Unedited],
        ]),
        new(FileInfo,
        [
            // 所持が「手元にファイルがあるか」、見つからないファイルが「記録のファイルが置き場に在るか」、壊れたzip が「在るファイルが開けるか」
            [SearchModuleKind.Owned, SearchModuleKind.MissingFile, SearchModuleKind.BrokenZip],
            [SearchModuleKind.Path],
        ]),

        // BOOTH に出た日 → 自分が手に入れた日
        new(Calendar, [[SearchModuleKind.PublishedAt, SearchModuleKind.AcquiredAt]]),

        // BOOTH の数 → 自分の評価 → 自分の足跡（最近も日数の範囲を動かす・2026-10-06）
        new(Slider, [[SearchModuleKind.Price, SearchModuleKind.WishList, SearchModuleKind.Attribute, SearchModuleKind.Recent]]),

        // 広い → 狭い
        new(Usage, [[SearchModuleKind.Recent, SearchModuleKind.Modification, SearchModuleKind.UnityProject]]),
    ];

    public static SearchModuleInfo Of(SearchModuleKind kind) => All.First(entry => entry.Kind == kind);
}

/// <summary>
/// 「最近」の足跡を、商品IDから引ける形にまとめたもの。
///
/// 絞り込みの1回ぶんで使い回す。1商品ごとにファイルを読み直さないため。
/// </summary>
public sealed record RecentTimes(
    IReadOnlyDictionary<string, DateTimeOffset> Added,
    IReadOnlyDictionary<string, DateTimeOffset> Used,
    IReadOnlyDictionary<string, DateTimeOffset> Viewed)
{
    public static RecentTimes Empty { get; } = new(
        new Dictionary<string, DateTimeOffset>(),
        new Dictionary<string, DateTimeOffset>(),
        new Dictionary<string, DateTimeOffset>());

    public DateTimeOffset? Of(string itemId, RecentKind kind)
    {
        var source = kind switch
        {
            RecentKind.Added => Added,
            RecentKind.Used => Used,
            _ => Viewed,
        };

        return source.TryGetValue(itemId, out var at) ? at : null;
    }
}

/// <summary>
/// 改変から引いた「どの改変・アバター・プロジェクトにどの商品を使ったか」。
///
/// **絞り込みの1回ぶんで使い回す**（<see cref="RecentTimes"/> と同じ理由。1商品ごとに改変のファイルを読み直さないため）。
/// </summary>
public sealed record ModificationUsage(
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemIdsByAvatar,
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemIdsByModification,
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemIdsByProject,
    IReadOnlyList<ModificationRecord> Records)
{
    /// <summary>
    /// 商品ごとの、「使ったもの」に入れている改変の数（並べ替えの「改変に使った回数」。ユーザ判断 2026-10-07）。
    /// 1つの改変に同じ商品が2回入っていても1回と数える（改変ごとの集合から数える）
    /// </summary>
    public IReadOnlyDictionary<string, int> UseCounts { get; } = ItemIdsByModification.Values
        .SelectMany(members => members)
        .GroupBy(itemId => itemId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    public static ModificationUsage Empty { get; } = new(
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase),
        []);

    public static ModificationUsage From(IEnumerable<ModificationRecord> records)
    {
        var list = records.ToList();
        var byAvatar = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var byProject = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var byModification = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var record in list)
        {
            // 同じ商品が2回入っていても、絞り込みに要るのは「入っているか」だけ
            var members = record.UsedMembers.Select(member => member.ItemId).ToHashSet(StringComparer.Ordinal);
            byModification[record.Id] = members;
            Collect(byAvatar, record.AvatarItemId, members);

            if (!string.IsNullOrWhiteSpace(record.UnityProject))
            {
                Collect(byProject, record.UnityProject!, members);
            }
        }

        return new ModificationUsage(
            byAvatar.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.Ordinal),
            byModification,
            byProject.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.OrdinalIgnoreCase),
            list);

        static void Collect(Dictionary<string, HashSet<string>> map, string key, IEnumerable<string> members)
        {
            if (!map.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[key] = set;
            }

            set.UnionWith(members);
        }
    }

    public bool Used(string avatarItemId, string itemId)
        => ItemIdsByAvatar.TryGetValue(avatarItemId, out var used) && used.Contains(itemId);

    public bool InModification(string modificationId, string itemId)
        => ItemIdsByModification.TryGetValue(modificationId, out var used) && used.Contains(itemId);

    public bool InProject(string projectPath, string itemId)
        => ItemIdsByProject.TryGetValue(projectPath, out var used) && used.Contains(itemId);
}

/// <summary>絞り込み1回ぶんの材料。モジュールが商品を照らすときに使う。</summary>
public sealed class SearchModuleContext
{
    private readonly Func<AvatarCompatibilityIndex> _compatibility;
    private AvatarCompatibilityIndex? _index;

    public SearchModuleContext(
        Func<AvatarCompatibilityIndex> compatibility,
        ModificationUsage modifications,
        RecentTimes recent,
        Func<string, string>? pathMap,
        DateTimeOffset now)
    {
        _compatibility = compatibility;
        Modifications = modifications;
        Recent = recent;
        PathMap = pathMap;
        Now = now;
    }

    /// <summary>素体経由の対応の索引。対応アバターで絞るときだけ作る。</summary>
    public AvatarCompatibilityIndex Compatibility => _index ??= _compatibility();

    public ModificationUsage Modifications { get; }

    public RecentTimes Recent { get; }

    /// <summary>外付けのドライブ文字が変わった記録を今の場所に読み替える（フォルダの条件）。</summary>
    public Func<string, string>? PathMap { get; }

    public DateTimeOffset Now { get; }
}

/// <summary>
/// 絞り込みのモジュール1つ（ユーザ案 2026-09-15）。追加したまま切れる（<see cref="IsEnabled"/>）、右上の × で外す。
/// 同じ種類を複数置けるのは <see cref="SearchModuleInfo.AllowsMany"/> の種類だけ（検索画面が守る）。
/// </summary>
public abstract class SearchModule : ReorderableRow
{
    /// <summary>
    /// 動かし続けている間、絞り直しを待つ時間。
    ///
    /// スライダのドラッグは1秒に数十回値が変わる。2000件での絞り直しは実測でこれより短いので、
    /// 止まってから1回で追いつく（`docs/feedback/done-2026-09.md` の計測）。
    /// 長くすると結果が遅れて見え、短くするとドラッグ中に何度も走る。
    /// </summary>
    private static readonly TimeSpan FilterWait = TimeSpan.FromMilliseconds(150);

    private bool _isEnabled = true;
    private bool _isCollapsed;
    private bool _isExcluded;
    private string? _disabledReason;
    private RelayCommand? _toggleCollapse;
    private RelayCommand? _toggleExclude;
    private readonly Debounced _changedSoon;

    protected SearchModule(SearchModuleKind kind)
    {
        Kind = kind;
        _changedSoon = new Debounced(FilterWait, () => Changed?.Invoke());
    }

    public SearchModuleKind Kind { get; }

    /// <summary>条件はどれも同じ並びに混ざる（種類が違っても順番を入れ替えられる）。</summary>
    public override object ReorderGroup => typeof(SearchModule);

    public SearchModuleInfo Info => SearchModuleCatalog.Of(Kind);

    public string Label => Info.Label;

    public string Hint => Info.Hint;

    private int _ordinal = 1;

    /// <summary>
    /// パネルの中で、同じ種類の何番目か（1から）。並びが変わるたびに検索側が振り直す（D7）。
    /// 番号は見出しには出さない（動かすと番号が替わって紛らわしい。中身で見分けられる）。読み上げの名前と ID にだけ使う。
    /// </summary>
    public int Ordinal
    {
        get => _ordinal;
        set
        {
            if (SetField(ref _ordinal, Math.Max(1, value)))
            {
                OnPropertyChanged(nameof(IdKey));
                OnPropertyChanged(nameof(SpokenLabel));
                OnPropertyChanged(nameof(NameSuffix));
                OnOrdinalChanged();
            }
        }
    }

    /// <summary>
    /// UI Automation の ID の種類の所（`SearchModule.{IdKey}.Input`）。2つ目から番号を付ける（`Category-2`）。
    /// 1つ目は前と同じ ID のままなので、確かめの道具と今の手順がそのまま動く。
    /// </summary>
    public string IdKey => _ordinal <= 1 ? Kind.ToString() : $"{Kind}-{_ordinal}";

    /// <summary>読み上げの名前に入れる条件名。2つ目から「カテゴリ（2つ目）」。</summary>
    public string SpokenLabel => _ordinal <= 1 ? Label : $"{Label}（{_ordinal}つ目）";

    /// <summary>条件名を含まない読み上げの名前の後ろに付ける番号（1つ目は空）。</summary>
    public string NameSuffix => _ordinal <= 1 ? string.Empty : $"（{_ordinal}つ目）";

    /// <summary>番号を替えたときに、番号を含む名前を知らせ直す（候補から積む条件の入力欄の名前など）。</summary>
    protected virtual void OnOrdinalChanged()
    {
    }

    /// <summary>
    /// 札「除く」の吹き出し。値の分からない商品も外す条件（範囲・日付・属性）はそれも言う（D12：吹き出しにだけ書く）。
    /// </summary>
    public virtual string ExcludedHint => "当てはまる商品を除いています。押すと除くのをやめます。";

    /// <summary>条件のメニューの「折りたたむ」の行。畳んでいれば「開く」。</summary>
    public string CollapseMenuText => _isCollapsed ? "開く" : "折りたたむ";

    /// <summary>条件のメニューの「上へ移動」「下へ移動」（D9。前はドラッグでしか並べ替えられず、キーボードから届かなかった）。検索側が入れる。</summary>
    public RelayCommand? MoveUpCommand { get; set; }

    public RelayCommand? MoveDownCommand { get; set; }

    /// <summary>条件が変わった（検索側が絞り直して、状態を書く）。</summary>
    public event Action? Changed;

    /// <summary>見た目だけが変わった（畳んだ・開いた）。絞り直さずに状態だけ書く。</summary>
    public event Action? ViewChanged;

    public RelayCommand? RemoveCommand { get; set; }

    /// <summary>追加したまま効かせるか（ユーザ案：トグルで追加状態を保ったまま無効化できる）。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetField(ref _isEnabled, value))
            {
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// 畳んでいるか（ユーザ指示 2026-09-16）。条件が増えるとパネルが縦に伸びるので、決め終えた条件は畳めるようにする。
    /// **畳んでも条件は効いたまま**なので、畳んだ姿に効いている中身を1行で出す。
    /// </summary>
    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (SetField(ref _isCollapsed, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(CollapseMenuText));
                ViewChanged?.Invoke();
            }
        }
    }

    public bool IsExpanded => !_isCollapsed;

    public RelayCommand ToggleCollapseCommand => _toggleCollapse ??= new RelayCommand(() => IsCollapsed = !IsCollapsed);

    /// <summary>畳んだ姿に出す1行。効かせていないときは、絞っていないことを言う。</summary>
    public string CollapsedSummary => IsActive ? SummaryText : "絞っていません";

    /// <summary>効かせられない理由（R-18 を設定で隠しているとき）。あれば条件として使わない。</summary>
    public string? DisabledReason
    {
        get => _disabledReason;
        set
        {
            if (SetField(ref _disabledReason, value))
            {
                OnPropertyChanged(nameof(HasDisabledReason));
                OnPropertyChanged(nameof(IsActive));
            }
        }
    }

    public bool HasDisabledReason => _disabledReason is not null;

    /// <summary>実際に絞っているか（効かせていて、理由が無く、何も絞らない値でない）。</summary>
    public bool IsActive => IsEnabled && !HasDisabledReason && HasCondition;

    protected abstract bool HasCondition { get; }

    /// <summary>
    /// 「除く」を持つか（ユーザ判断 2026-10-01）。三項は「ある／ない」を選べるので持たない。
    /// 最近は日数の範囲になったので持つ（2026-10-06。前は「n日以内」だけで、除くと「n日より前」と同じだった・D10）。
    /// </summary>
    public virtual bool SupportsExclude => false;

    /// <summary>
    /// 当てはまる商品を**除く**か（ユーザ判断 2026-10-01・案1）。除くは「除かないときに当てはまる物、以外」。
    /// AND／OR・対応アバターのチェックなどの設定は、除くときもそのまま効く（その設定で当てはまる物を外す）。
    /// </summary>
    public bool IsExcluded
    {
        get => _isExcluded;
        set
        {
            if (SupportsExclude && SetField(ref _isExcluded, value))
            {
                OnPropertyChanged(nameof(SummaryText));
                NotifyChanged();
            }
        }
    }

    public RelayCommand ToggleExcludeCommand => _toggleExclude ??= new RelayCommand(() => IsExcluded = !IsExcluded);

    /// <summary>通知だけ出して除くを切り替える（「条件をクリア」・履歴を当てるとき。絞り直しは呼ぶ側）。</summary>
    public void SetExcludedQuietly(bool value)
    {
        if (!SupportsExclude || _isExcluded == value)
        {
            return;
        }

        _isExcluded = value;
        OnPropertyChanged(nameof(IsExcluded));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    /// <summary>除かないときに当てはまるか（値と AND／OR などの設定どおりに照らす）。</summary>
    public abstract bool Matches(ItemRecord item, SearchModuleContext context);

    /// <summary>
    /// 絞り込みで通すか。**照らす口はここ1つ**（除くかどうかで分ける所を1か所にする）。
    /// </summary>
    public bool Passes(ItemRecord item, SearchModuleContext context)
        => _isExcluded ? MatchesExcluded(item, context) : Matches(item, context);

    /// <summary>
    /// 除くときに通すか。既定は「除かないときに当てはまる物、以外」。
    /// 範囲・日付・属性は**値の分からない商品を、除くときも外す**ように上書きする（ユーザ判断 2026-10-01。
    /// 値の分からない商品は「範囲の外」とは言えない。除かないときも外れているので、除いたら出てくると食い違う）。
    /// </summary>
    protected virtual bool MatchesExcluded(ItemRecord item, SearchModuleContext context) => !Matches(item, context);

    /// <summary>
    /// 絞り込みの1回の始めに1回だけ呼ぶ（照らす重さの案c・`docs/research/search-modules-2026-10-01.md` §9）。
    /// 打った字を数・日付に読み直す・条件の並びを作り直すなど、商品ごとにやり直していた準備をここで済ませる。
    /// 照らす中身は変えない（結果は同じ）。
    /// </summary>
    public void Prepare(SearchModuleContext context)
    {
        PrepareCore(context);
        _prepared = true;
    }

    /// <summary>用意する物を持つ条件が上書きする。</summary>
    protected virtual void PrepareCore(SearchModuleContext context)
    {
    }

    /// <summary>
    /// 用意した物を使う前に呼ぶ。絞り込みは毎回 <see cref="Prepare"/> を通すので、ここで用意するのは
    /// それを通らずに照らしたとき（値を変えた直後に直に照らす試験など）だけ。
    /// </summary>
    protected void EnsurePrepared(SearchModuleContext context)
    {
        if (!_prepared)
        {
            Prepare(context);
        }
    }

    /// <summary>値が変わったので、用意した物を捨てる（次に照らすときに用意し直す）。</summary>
    protected void Unprepare() => _prepared = false;

    private bool _prepared;

    /// <summary>効いている条件の1行（結果の上と、畳んだパネルと、検索の履歴に出す）。</summary>
    public string SummaryText
    {
        get
        {
            var body = SummaryBody;

            // 除くときは頭に「除く：」を付け、条件名の後は空白で続ける（コロンを重ねない・D1）
            if (_isExcluded)
            {
                return body.Length == 0 ? $"除く：{SummaryHead}" : $"除く：{SummaryHead} {body}";
            }

            return body.Length == 0 ? SummaryHead : $"{SummaryHead}{SummaryJoiner}{body}";
        }
    }

    /// <summary>要約の頭（条件名。数の元のように条件名に添える物も含める）。</summary>
    protected virtual string SummaryHead => Label;

    /// <summary>要約の中身（選んだ値など）。空なら頭だけ。</summary>
    protected abstract string SummaryBody { get; }

    /// <summary>頭と中身の間。値を並べる条件は「：」、範囲と日付は空白で続ける。</summary>
    protected virtual string SummaryJoiner => "：";

    /// <summary>
    /// 選んだ値を要約に並べる。どれか（OR）は「・」で、すべて（AND）は「・」で並べた後に「のすべて」（ユーザ判断 2026-10-01）。
    /// 前は AND を「A かつ B」と書いていたが、除くときに「除く：BOOTHタグ A・B」と OR と同じに読めないよう、
    /// 除くとき・除かないときの両方を「A・B のすべて」に揃えた。1つしか無ければ結びは書かない（結果が同じ）。
    /// </summary>
    protected static string JoinValues(IEnumerable<string> values, bool all)
    {
        var list = values.ToList();
        var joined = string.Join("・", list);
        return all && list.Count > 1 ? $"{joined} のすべて" : joined;
    }

    /// <summary>何も絞らない値に戻す（「条件をクリア」）。通知だけ出し、絞り直しは呼ぶ側がまとめて行う。</summary>
    public abstract void Clear();

    /// <summary>選択肢の横に出す件数を数え直す。<paramref name="items"/> はこのモジュールを除いた他の条件を当てた後の商品。</summary>
    public virtual void RefreshCounts(IReadOnlyList<ItemRecord> items, SearchModuleContext context)
    {
    }

    public SearchModuleState Save()
        => Write(new SearchModuleState
        {
            Kind = Kind.ToString(),
            Enabled = IsEnabled,
            Collapsed = IsCollapsed,
            Exclude = _isExcluded,
            Summary = IsActive ? SummaryText : null,
        });

    public void Load(SearchModuleState state)
    {
        _isEnabled = state.Enabled;
        _isCollapsed = state.Collapsed;
        Unprepare();

        // 除くを持たない種類に書かれていても読まない（三項に除くは無い）
        _isExcluded = SupportsExclude && state.Exclude;
        Read(state);
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsCollapsed));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(CollapseMenuText));
        OnPropertyChanged(nameof(IsExcluded));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    protected abstract SearchModuleState Write(SearchModuleState state);

    protected abstract void Read(SearchModuleState state);

    /// <summary>通知だけ出して切り替える。「条件をクリア」や他の画面からの条件で、1つずつ絞り直さない（呼ぶ側がまとめて1回）。</summary>
    public void SetEnabledQuietly(bool value)
    {
        _isEnabled = value;
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    protected void NotifyChanged()
    {
        _changedSoon.Cancel();
        Unprepare();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
        Changed?.Invoke();
    }

    /// <summary>
    /// 値が変わった。**表示はすぐ、絞り直しは止まってから1回。**
    /// スライダを動かしている間に毎回絞り直すと、件数に比例した走査が追いつかない。
    /// </summary>
    protected void NotifyChangedSoon()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
        Unprepare();
        _changedSoon.Request();
    }

    /// <summary>全角の数字・カンマ・円記号が混ざっていても数として読む。読めなければ null。</summary>
    protected static int? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var folded = text.Normalize(NormalizationForm.FormKC).Replace(",", string.Empty).Replace("¥", string.Empty).Trim();
        return int.TryParse(folded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    /// <summary>
    /// <see cref="ParseNumber"/> の 64bit 版。数の範囲（払った額の合計など）が使う。
    /// 合計は 32bit を超え得る（外部の点検 2026-10-06）ので、欄に打てる数も同じ幅にそろえる
    /// </summary>
    protected static long? ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var folded = text.Normalize(NormalizationForm.FormKC).Replace(",", string.Empty).Replace("¥", string.Empty).Trim();
        return long.TryParse(folded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}

/// <summary>分布の帯の棒1本。高さは帯（<see cref="RangeModule.HistogramHeight"/>）に収めた px。</summary>
/// <param name="IsOutside">帯の範囲より外をまとめた1本（最近の「それより前」）。範囲の中の棒と色を分ける。</param>
public sealed record HistogramBar(double Height, bool IsOutside = false);

/// <summary>「条件を追加」のメニューの1行。1つまでの種類は、追加済みならグレー。</summary>
public sealed class SearchModuleMenuEntry : ViewModelBase
{
    private bool _isAvailable = true;

    public SearchModuleMenuEntry(SearchModuleInfo info, Action<SearchModuleKind> add)
    {
        Kind = info.Kind;
        Label = info.Label;
        Hint = info.Hint;
        AddCommand = new RelayCommand(() => add(Kind), () => IsAvailable);
    }

    public SearchModuleKind Kind { get; }

    public string Label { get; }

    public string Hint { get; }

    /// <summary>区切り線ではない（区切り線と同じ一覧に並ぶので、見た目を分ける印を揃えて持つ）。</summary>
    public bool IsSeparator => false;

    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            if (SetField(ref _isAvailable, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand AddCommand { get; }
}

/// <summary>
/// 「条件を追加」のメニューの見出し（ユーザ案：BOOTHの情報・商品の情報・カレンダー・スライダー・利用状況）。
/// <paramref name="Entries"/> は <see cref="SearchModuleMenuEntry"/> と <see cref="SearchModuleMenuSeparator"/> が並ぶ。
/// </summary>
public sealed record SearchModuleMenuHeading(string Title, IReadOnlyList<object> Entries);

/// <summary>
/// 見出しの中の区切り線（意味のまとまりの間）。**区切りごとに別の物を作る**——同じ物を1つの一覧に何度も入れると、
/// WPF の一覧は項目と部品の対応を取り違える。
/// </summary>
public sealed class SearchModuleMenuSeparator
{
    /// <summary>メニューの項目の見た目を、線に差し替える印。</summary>
    public bool IsSeparator => true;
}
