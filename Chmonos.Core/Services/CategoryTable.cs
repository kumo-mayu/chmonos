using System.Text.Json;

namespace Chmonos.Core.Services;

/// <summary>BOOTHのカテゴリ表の親1件。</summary>
public sealed record CategoryParent
{
    public required string Name { get; init; }

    /// <summary>BOOTHの市場カテゴリID。親にしか出ていないので子は持たない。</summary>
    public int? Id { get; init; }

    public IReadOnlyList<string> Children { get; init; } = [];
}

/// <summary>
/// 同梱したBOOTHのカテゴリ表（`assets/booth-categories.json`）。
///
/// **BOOTHから取れない商品にはカテゴリが無い。**ユーザが選んで入れられるように、
/// 候補の一覧が要る。取りに行かず同梱するのは、全員が同じ静的な表を
/// 別々に取れば、1000人が使えば1000回、同じ内容のために問い合わせが飛ぶため。
///
/// **子の名前だけを使う。**絞り込みは子の名前だけの平坦な一覧で、
/// 商品JSONの category も子の名前で持っている。親は候補を並べる順にだけ使う。
///
/// 読めなければ空で通す。**候補が出ないだけで、手で打てば入る。**
/// </summary>
public sealed class CategoryTable
{
    /// <summary>VRChat向けのものを先に出す親。この12件で足りると判断した。</summary>
    public const string PreferredParent = "3Dモデル";

    private readonly Lazy<IReadOnlyList<CategoryParent>> _parents;

    private readonly Lazy<Dictionary<string, int>> _ranks;

    public CategoryTable(string filePath)
    {
        _parents = new Lazy<IReadOnlyList<CategoryParent>>(() => Load(filePath));
        _ranks = new Lazy<Dictionary<string, int>>(() => Suggestions()
            .Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.Ordinal));
    }

    /// <summary>既定の置き場所（アプリと同じ場所の assets/）。</summary>
    public static CategoryTable Bundled()
        => new(Path.Combine(AppContext.BaseDirectory, "assets", "booth-categories.json"));

    public IReadOnlyList<CategoryParent> Parents => _parents.Value;

    public bool IsAvailable => Parents.Count > 0;

    /// <summary>
    /// 候補に出す子カテゴリを、出す順に。
    ///
    /// **3Dモデルの子を先に出す。**このツールはVRChatのアセットを持つ人が使うもので、
    /// 3Dモデルの子12件でほぼ足りる。残りも後ろに全部並べる——
    /// 選べる範囲を勝手に狭めると、BOOTHにある分類が入れられなくなる。
    /// </summary>
    public IReadOnlyList<string> Suggestions()
        => Parents
            .OrderByDescending(parent => string.Equals(parent.Name, PreferredParent, StringComparison.Ordinal))
            .SelectMany(parent => parent.Children)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// この商品の分類を画面に出す形にする。
    ///
    /// **商品ページと編集画面で同じ式を使うためにここに置く。**
    /// 別々に書いていたせいで、編集画面の下見だけが `Local.Category` を
    /// 見ておらず、自分で入れた分類が出ないままになっていた。
    /// </summary>
    /// <param name="userValue">ユーザが入れた子の名前（編集中なら入力欄の値）。</param>
    /// <param name="observed">BOOTHから取れている分類。親を持っている。</param>
    public string TextFor(string? userValue, Models.BoothCategory? observed)
        => string.IsNullOrWhiteSpace(userValue)
            ? Models.DisplayText.CategoryText(observed?.Name, observed?.ParentName)
            : Models.DisplayText.CategoryText(userValue, ParentOf(userValue.Trim()));

    /// <summary>
    /// 検索の「カテゴリ」の並べ替えで、この子カテゴリが何番目か。表に無ければ null。
    ///
    /// 並びは候補の並び（<see cref="Suggestions"/>：3Dモデルの子を先に、その後は表の親の順・親の中は子の順）と同じにする。
    /// 並べ替えだけ別の順にすると、候補で見慣れた並びと食い違う。
    /// </summary>
    public int? RankOf(string? childName)
        => childName is { Length: > 0 } && _ranks.Value.TryGetValue(childName.Trim(), out var rank) ? rank : null;


    /// <summary>この子カテゴリの親。分からなければ null。</summary>
    public string? ParentOf(string? childName)
        => childName is null
            ? null
            : Parents.FirstOrDefault(parent => parent.Children.Contains(childName, StringComparer.Ordinal))?.Name;

    private static IReadOnlyList<CategoryParent> Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var loaded = JsonSerializer.Deserialize<Bundle>(
                File.ReadAllText(filePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return loaded?.Parents ?? [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // 候補が出ないだけで手で打てば入るので、ここで落とさない
            return [];
        }
    }

    private sealed record Bundle
    {
        public IReadOnlyList<CategoryParent> Parents { get; init; } = [];
    }
}
