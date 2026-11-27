using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// ソフトが足す項目の宣言（plan 44 の②・docs/spec/plugins-menu.md）：検証・控えとの比べ・変わった所・押せる条件・押したときに詰める情報。
/// 画面の文は持たない（押せない理由は <see cref="PluginMenuBlock"/> で返し、文はアプリが決める）
/// </summary>
public static class PluginMenus
{
    public const string WhereItem = "item";
    public const string WhereSelection = "selection";
    public const string WhereFile = "file";
    public const string WhereUnityPackage = "unityPackage";
    public const string WhereModification = "modification";
    public const string WhereAvatar = "avatar";
    public const string WhereShop = "shop";

    /// <summary>
    /// 出せる場所。最初は item・selection の2つ（ユーザ判断 2026-11-12「おすすめに賛成します」）。
    /// 2026-11-27 に file・unityPackage・modification・avatar・shop を足した（docs/spec/plugins.md「開いた画面に付いていく」）
    /// </summary>
    public static IReadOnlyList<string> Wheres { get; } = [WhereItem, WhereSelection, WhereFile, WhereUnityPackage, WhereModification, WhereAvatar, WhereShop];

    /// <summary>商品の場所（item・selection）で詰められる情報。files は手元のファイルと登録したフォルダの両方を詰める。</summary>
    private static readonly IReadOnlyList<string> ItemNeeds = ["itemId", "name", "shop", "category", "files", "unityPackages", "avatars", "tags"];

    /// <summary>
    /// 場所ごとに詰められる情報（並べる順）。先頭はいつも渡す鍵（押した物をソフトが見分けられないと使えないので、頼まれなくても入れる）。
    /// 場所をまたいで同じ意味の物は同じ名前にした（name・itemId）。ファイルの場所は個人の情報を含むので、頼まれた時だけ渡す
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> NeedsByWhere { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        [WhereItem] = ItemNeeds,
        [WhereSelection] = ItemNeeds,
        [WhereFile] = ["hash", "itemId", "paths", "state"],
        [WhereUnityPackage] = ["itemId", "fileHash", "entry", "roots"],
        [WhereModification] = ["modificationId", "name", "avatar", "members", "unityProject"],
        [WhereAvatar] = ["avatarId", "name", "baseName"],
        [WhereShop] = ["shopKey", "name", "url"],
    };

    /// <summary>詰められる情報の全部（整えた宣言はこの順に並べる）。</summary>
    public static IReadOnlyList<string> NeedNames { get; } = Wheres.SelectMany(where => NeedsByWhere[where]).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>場所ごとにいつも渡す鍵（許しの窓でも必ず「渡す」と言う）。</summary>
    public static string KeyOf(string where) => NeedsByWhere.TryGetValue(where, out var needs) ? needs[0] : "itemId";

    /// <summary>
    /// 持ち主の商品が無い場所。when の条件は商品に当てる物なので使えない。黙って無条件に押せる項目にしないよう、
    /// when と一緒に宣言したら断る（場所ごとの条件を足す時に緩める。緩めるのは互換の変更）
    /// </summary>
    private static readonly IReadOnlyList<string> WheresWithoutItem = [WhereModification, WhereShop];

    /// <summary>
    /// 1つ目の段の項目の上限。メニューは画面の高さに収まらないと送りになって見つけにくい。ふつうのメニューの1段は十数項目までで、
    /// 1つのソフトが足すのはそれより少ない見込み
    /// </summary>
    public const int MaxTopItems = 16;

    /// <summary>下の段も含めた項目の上限（許しの窓に並べる行の数でもある。窓が画面に収まる程度）。</summary>
    public const int MaxItems = 32;

    /// <summary>ラベルの上限（字）。メニューの幅が画面の3分の1を超えない程度。ほかのメニューの項目は長くて15字ほど。</summary>
    public const int MaxLabelLength = 40;

    /// <summary>ID の上限（字）。ソフトが自分の項目を見分けるだけの物なので、名前・UUID が入る長さで足りる。</summary>
    public const int MaxIdLength = 64;

    /// <summary>カテゴリの条件の上限（字）。BOOTH のカテゴリ名は長くて20字ほど。自分で入れたカテゴリも同じ程度。</summary>
    public const int MaxCategoryLength = 64;

    /// <summary>
    /// 1回に送れる商品の上限。選択の出来事の ID と同じ（2万件を全部選んで送ると、詰めた情報で数十MB になる）。
    /// 超えたら黙って減らさず、押せなくして理由を言う
    /// </summary>
    public const int MaxTargets = PluginEventHub.MaxSelectionIds;

    /// <summary><c>PUT /v1/plugin/menu</c> の本文を読み、整えた宣言にする（順はソフトのまま、where・needs は決まった順で重なりを除く）。合わなければ 400。</summary>
    public static IReadOnlyList<PluginMenuItemRef> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw QueryException.Invalid("invalidRequest", "items に、項目の配列を書いてください（消すときは空の配列）。");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = ParseList(items, depth: 1, ids);
        if (result.Count > MaxTopItems)
        {
            throw QueryException.Invalid("tooManyItems", $"1つ目の段の項目は{MaxTopItems}個までです。");
        }

        if (ids.Count > MaxItems)
        {
            throw QueryException.Invalid("tooManyItems", $"項目は下の段も合わせて{MaxItems}個までです。");
        }

        return result;
    }

    private static List<PluginMenuItemRef> ParseList(JsonElement items, int depth, HashSet<string> ids)
    {
        var result = new List<PluginMenuItemRef>();
        foreach (var element in items.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw QueryException.Invalid("invalidItem", "items の1つ1つは、id と label を持つオブジェクトにしてください。");
            }

            var id = StringOf(element, "id")?.Trim() ?? string.Empty;
            if (id.Length == 0 || id.Length > MaxIdLength || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                throw QueryException.Invalid("invalidId", $"id は英数字と - _ . の{MaxIdLength}字までで書いてください。");
            }

            // 押されたら ID だけで知らせるので、下の段も含めて重ならないようにする
            if (!ids.Add(id))
            {
                throw QueryException.Invalid("duplicateId", $"id「{id}」が2つあります。下の段も含めて重ならないようにしてください。");
            }

            // 画面に出す名前なので、改行などの制御文字は断る（ソフトの名前と同じ決まり）
            var label = StringOf(element, "label")?.Trim() ?? string.Empty;
            if (label.Length == 0 || label.Length > MaxLabelLength || label.Any(char.IsControl))
            {
                throw QueryException.Invalid("invalidLabel", $"項目「{id}」の label を{MaxLabelLength}字までで書いてください（改行などの制御文字は使えません）。");
            }

            var children = element.TryGetProperty("children", out var childArray) && childArray.ValueKind == JsonValueKind.Array
                ? childArray
                : (JsonElement?)null;
            if (children is { } list && list.GetArrayLength() > 0)
            {
                if (depth >= 2)
                {
                    throw QueryException.Invalid("tooDeep", "children は2段までです。下の段の項目に children は書けません。");
                }

                // 子を持つ項目は下の段を開くだけ。条件や詰める物を書いても使われないので、書き間違いとして断る
                if (Has(element, "where") || Has(element, "when") || Has(element, "needs"))
                {
                    throw QueryException.Invalid("invalidItem", $"children を持つ項目「{id}」には where・when・needs を書けません。下の段の項目に書いてください。");
                }

                result.Add(new PluginMenuItemRef(id, label, [], null, [], ParseList(list, depth + 1, ids)));
                continue;
            }

            var where = NamesOf(element, "where", Wheres, "unknownWhere", "where に知らない場所があります。使えるのは {0} です。");
            if (where.Count == 0)
            {
                throw QueryException.Invalid("invalidWhere", $"項目「{id}」の where に、出す場所を1つ以上書いてください（{string.Join("・", Wheres)}）。");
            }

            // needs は項目に1本なので、出す場所のどれかで使える名前を受け、押した場所の分だけを詰める
            var usable = NeedNames.Where(need => where.Any(place => NeedsByWhere[place].Contains(need, StringComparer.Ordinal))).ToList();
            var needs = NamesOf(element, "needs", usable, "unknownNeed", "needs に、出す場所で使えない名前があります。使えるのは {0} です。");
            var when = WhenOf(element);
            if (when is not null && where.Any(WheresWithoutItem.Contains))
            {
                throw QueryException.Invalid("invalidWhen", $"項目「{id}」の when は、modification・shop と一緒には書けません（商品に当てる条件のため）。");
            }

            result.Add(new PluginMenuItemRef(id, label, where, when, needs, []));
        }

        return result;
    }

    /// <summary>書いてあるか（空の配列は書いていないのと同じ：控えは子を持つ項目にも空の where・needs を書く）。</summary>
    private static bool Has(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind != JsonValueKind.Null
            && !(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);

    private static string? StringOf(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>名前の並び（where・needs）。知らない名前は 400（黙って捨てると、ソフトが思った所に出ない・思った物が届かない）。</summary>
    private static List<string> NamesOf(JsonElement element, string name, IReadOnlyList<string> known, string code, string message)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw QueryException.Invalid("invalidItem", $"{name} は文字列の配列にしてください。");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not { } text || !known.Contains(text))
            {
                throw QueryException.Invalid(code, string.Format(message, string.Join("・", known)));
            }

            names.Add(text);
        }

        return known.Where(names.Contains).ToList();
    }

    /// <summary>
    /// 押せる条件。知らない名前は 400：黙って捨てると、ソフトが押させたくない商品で押せてしまう
    /// （新しい版が足した条件を古い版が知らないときも、押せる方へ倒さない）
    /// </summary>
    private static PluginMenuWhenRef? WhenOf(JsonElement element)
    {
        if (!element.TryGetProperty("when", out var when) || when.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (when.ValueKind != JsonValueKind.Object)
        {
            throw QueryException.Invalid("invalidWhen", "when はオブジェクトにしてください。");
        }

        bool? hasUnityPackage = null, owned = null, hasAvatar = null;
        string? category = null;
        foreach (var property in when.EnumerateObject())
        {
            switch (property.Name)
            {
                case "hasUnityPackage":
                    hasUnityPackage = BoolOf(property);
                    break;
                case "owned":
                    owned = BoolOf(property);
                    break;
                case "hasAvatar":
                    hasAvatar = BoolOf(property);
                    break;
                case "category":
                    category = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()?.Trim() : null;
                    if (string.IsNullOrEmpty(category) || category.Length > MaxCategoryLength || category.Any(char.IsControl))
                    {
                        throw QueryException.Invalid("invalidWhen", $"when の category は、カテゴリの名前を{MaxCategoryLength}字までで書いてください。");
                    }

                    break;
                default:
                    throw QueryException.Invalid("unknownWhen", "when に知らない条件があります。使えるのは hasUnityPackage・owned・hasAvatar・category です。");
            }
        }

        return hasUnityPackage is null && owned is null && hasAvatar is null && category is null
            ? null
            : new PluginMenuWhenRef(hasUnityPackage, owned, hasAvatar, category);
    }

    private static bool BoolOf(JsonProperty property)
        => property.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw QueryException.Invalid("invalidWhen", $"when の {property.Name} は true か false にしてください。"),
        };

    /// <summary>同じ宣言か（整えた形で比べる。where・needs の順の違いは整えた時に消えている）。</summary>
    public static bool Same(IReadOnlyList<PluginMenuItemRef> a, IReadOnlyList<PluginMenuItemRef> b) => Text(a) == Text(b);

    private static string Text(IReadOnlyList<PluginMenuItemRef> items)
        => JsonSerializer.Serialize(items, QueryJson.Context.IReadOnlyListPluginMenuItemRef);

    /// <summary>控えの形から（手で書いた控えに合わない物があれば、その宣言は無い物として扱う：人が見ていない項目を出さない）。</summary>
    public static IReadOnlyList<PluginMenuItemRef> FromRecords(IReadOnlyList<PluginMenuItemRecord>? records)
    {
        if (records is null || records.Count == 0)
        {
            return [];
        }

        try
        {
            // 入口と同じ検証を通す（控えを手で書き換えて上限や知らない場所を入れても、そのまま出さない）
            var text = JsonSerializer.Serialize(new { items = records }, StoreJsonOptions);
            using var document = JsonDocument.Parse(text);
            return Parse(document.RootElement);
        }
        catch (QueryException exception)
        {
            Core.Diagnostics.AppLog.Note("外から使う API", $"控えの項目の宣言が読めないので出さない（{exception.Code}）");
            return [];
        }
    }

    private static readonly JsonSerializerOptions StoreJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>控えに書く形へ。</summary>
    public static IReadOnlyList<PluginMenuItemRecord> ToRecords(IReadOnlyList<PluginMenuItemRef> items)
        => items.Select(item => new PluginMenuItemRecord
        {
            Id = item.Id,
            Label = item.Label,
            Where = item.Where,
            When = item.When is { } when
                ? new PluginMenuWhen { HasUnityPackage = when.HasUnityPackage, Owned = when.Owned, HasAvatar = when.HasAvatar, Category = when.Category }
                : null,
            Needs = item.Needs,
            Children = ToRecords(item.Children),
        }).ToList();

    /// <summary>子を持たない項目を、親の名前と並べて平らにする（許しの窓の行・押した項目を ID から引く）。</summary>
    public static IEnumerable<(PluginMenuItemRef Item, string? Parent)> Leaves(IReadOnlyList<PluginMenuItemRef> items)
    {
        foreach (var item in items)
        {
            if (item.Children.Count == 0)
            {
                yield return (item, null);
                continue;
            }

            foreach (var child in item.Children)
            {
                yield return (child, item.Label);
            }
        }
    }

    /// <summary>
    /// 前の宣言から変わった所（許しの窓に並べる。足す・変わる・消える）。比べるのは押せる項目ごとで、ID で対にする。
    /// 親の名前が変わっただけでも、見える場所が変わるので「変わる」に入れる
    /// </summary>
    public static PluginMenuChange Diff(IReadOnlyList<PluginMenuItemRef> before, IReadOnlyList<PluginMenuItemRef> after)
    {
        var old = Leaves(before).ToDictionary(leaf => leaf.Item.Id, StringComparer.Ordinal);
        var added = new List<PluginMenuLine>();
        var changed = new List<PluginMenuLine>();
        foreach (var (item, parent) in Leaves(after))
        {
            var line = new PluginMenuLine(item.Id, item.Label, parent, item.Where, item.Needs, item.When);
            if (!old.Remove(item.Id, out var previous))
            {
                added.Add(line);
            }
            else if (previous.Parent != parent || !Same([previous.Item], [item]))
            {
                changed.Add(line);
            }
        }

        var removed = old.Values.Select(leaf => new PluginMenuLine(leaf.Item.Id, leaf.Item.Label, leaf.Parent, leaf.Item.Where, leaf.Item.Needs, leaf.Item.When)).ToList();
        return new PluginMenuChange(added, changed, removed);
    }

    /// <summary>
    /// 押せない理由（押せれば <see cref="PluginMenuBlock.None"/>）。選択は全部が合うときだけ押せる：
    /// 合う物だけを黙って送ると、人が選んだ物とソフトが受けた物が食い違う
    /// </summary>
    public static PluginMenuReason Check(PluginMenuWhenRef? when, IReadOnlyList<string> itemIds, QuerySnapshot snapshot)
    {
        if (itemIds.Count > MaxTargets)
        {
            return new(PluginMenuBlock.TooMany);
        }

        foreach (var itemId in itemIds)
        {
            if (!snapshot.ItemsById.TryGetValue(itemId, out var item))
            {
                // 取り込んだ直後で API の写しにまだ無い（写しは保存先の変化を少し遅れて読む）
                return new(PluginMenuBlock.NotLoaded);
            }

            if (when is null)
            {
                continue;
            }

            if (when.HasUnityPackage is { } wantsPackage && Catalogs.HasSendablePackage(item) != wantsPackage)
            {
                return new(wantsPackage ? PluginMenuBlock.NoUnityPackage : PluginMenuBlock.HasUnityPackage);
            }

            if (when.Owned is { } wantsOwned && item.IsOwned != wantsOwned)
            {
                return new(wantsOwned ? PluginMenuBlock.NotOwned : PluginMenuBlock.Owned);
            }

            // 対応アバターは読み取りの compatibleAvatars と同じ数え方（同じ索引を引く。要約を作るとサムネイルまで調べて、千件の選択で重い）
            if (when.HasAvatar is { } wantsAvatar && snapshot.Compatibility.Resolve(item.Local).Count > 0 != wantsAvatar)
            {
                return new(wantsAvatar ? PluginMenuBlock.NoAvatar : PluginMenuBlock.HasAvatar);
            }

            if (when.Category is { } category && !string.Equals(item.CategoryName, category, StringComparison.Ordinal))
            {
                return new(PluginMenuBlock.OtherCategory, category);
            }
        }

        return PluginMenuReason.Ok;
    }

    /// <summary>
    /// 押せない理由（場所ごと）。押した物が API の写しに無ければ読み込みの途中とする（取り込み・作った直後は写しが少し遅れる）。
    /// when は持ち主の商品に当てる（ファイル・unitypackage はその商品、アバターはアバターの商品）
    /// </summary>
    public static PluginMenuReason Check(PluginMenuWhenRef? when, PluginMenuSpot spot, QuerySnapshot snapshot)
    {
        switch (spot.Where)
        {
            case WhereItem or WhereSelection:
                return Check(when, spot.ItemIds, snapshot);
            case WhereModification:
                return snapshot.Modifications.Records.Any(record => record.Id == spot.Key) ? PluginMenuReason.Ok : new(PluginMenuBlock.NotLoaded);
            case WhereShop:
                return Catalogs.Shops(snapshot).Any(shop => string.Equals(shop.Key, spot.Key, StringComparison.OrdinalIgnoreCase))
                    ? PluginMenuReason.Ok
                    : new(PluginMenuBlock.NotLoaded);
            case WhereAvatar:
                if (!snapshot.Registry.Entries.Any(entry => entry.ItemId == spot.Key))
                {
                    return new(PluginMenuBlock.NotLoaded);
                }

                // 条件の無い項目は、商品の記録が無いアバター（名指しされただけ）でも押せる
                return when is null ? PluginMenuReason.Ok : Check(when, [spot.Key!], snapshot);
            case WhereFile:
                if (spot.ItemIds.Count != 1 || !snapshot.ItemsById.TryGetValue(spot.ItemIds[0], out var owner)
                    || !owner.Local.LocalFiles.Any(file => string.Equals(file.Hash, spot.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    return new(PluginMenuBlock.NotLoaded);
                }

                return Check(when, spot.ItemIds, snapshot);
            default:
                return spot.ItemIds.Count == 1 ? Check(when, spot.ItemIds, snapshot) : new(PluginMenuBlock.NotLoaded);
        }
    }

    /// <summary>押した物を、押した場所の needs の欄だけ詰める（形はほかの読み取りと同じ写し方を通す）。</summary>
    public static PluginMenuPressed Pressed(string id, PluginMenuSpot spot, QuerySnapshot snapshot, IReadOnlyList<string> needs)
    {
        bool Wants(string name) => needs.Contains(name, StringComparer.Ordinal);
        switch (spot.Where)
        {
            case WhereFile:
            {
                var itemId = spot.ItemIds[0];
                var file = ItemMapper.Detail(snapshot.ItemsById[itemId], snapshot).Files
                    .FirstOrDefault(file => string.Equals(file.Hash, spot.Key, StringComparison.OrdinalIgnoreCase));
                return new PluginMenuPressed(id, spot.Where, null, File: new PluginMenuFile(
                    file?.Hash ?? spot.Key!,
                    Wants("itemId") ? itemId : null,
                    Wants("paths") ? file?.Paths ?? [] : null,
                    Wants("state") ? file?.State : null));
            }

            case WhereUnityPackage:
            {
                var itemId = spot.ItemIds[0];
                var roots = UnityPackageRoots(snapshot.ItemsById[itemId], spot.Key, spot.Entry);
                return new PluginMenuPressed(id, spot.Where, null, UnityPackage: new PluginMenuUnityPackage(
                    itemId,
                    Wants("fileHash") ? spot.Key : null,
                    Wants("entry") ? spot.Entry : null,
                    Wants("roots") ? roots : null));
            }

            case WhereModification:
            {
                var modification = Catalogs.Modification(snapshot, spot.Key!);
                return new PluginMenuPressed(id, spot.Where, null, Modification: new PluginMenuModification(
                    modification.Id,
                    Wants("name") ? modification.Name : null,
                    Wants("avatar") ? modification.AvatarId : null,
                    Wants("avatar") ? modification.AvatarName : null,
                    Wants("members") ? modification.Members : null,
                    Wants("unityProject") ? modification.UnityProject : null));
            }

            case WhereAvatar:
            {
                var avatar = Catalogs.Avatars(snapshot).FirstOrDefault(entry => entry.Id == spot.Key);
                return new PluginMenuPressed(id, spot.Where, null, Avatar: new PluginMenuAvatar(
                    spot.Key!,
                    Wants("name") ? avatar?.Name : null,
                    Wants("baseName") ? avatar?.BaseName : null));
            }

            case WhereShop:
            {
                var shop = Catalogs.Shop(snapshot, spot.Key!);
                return new PluginMenuPressed(id, spot.Where, null, Shop: new PluginMenuShop(
                    shop.Key,
                    Wants("name") ? shop.Name : null,
                    Wants("url") ? shop.Url : null));
            }

            default:
                return new PluginMenuPressed(id, spot.Where, spot.ItemIds.Select(itemId => Target(snapshot.ItemsById[itemId], snapshot, needs)).ToList());
        }
    }

    /// <summary>unitypackage の入る先（<c>/v1/unitypackages</c> の roots と同じ。登録したフォルダの中の物と、単体の物は空）。</summary>
    private static IReadOnlyList<string> UnityPackageRoots(ItemRecord item, string? fileHash, string? entry)
        => fileHash is null || entry is null
            ? []
            : item.Local.OwnedFiles
                .Where(file => string.Equals(file.Hash, fileHash, StringComparison.OrdinalIgnoreCase))
                .SelectMany(UnityHandoff.RecordedPackagesIn)
                .FirstOrDefault(package => string.Equals(package.Entry, entry, StringComparison.Ordinal))?.Roots ?? [];

    /// <summary>押した時点の商品1つを、needs の欄だけ詰める（形はほかの読み取りと同じ写し方を通す）。</summary>
    public static PluginMenuTarget Target(ItemRecord item, QuerySnapshot snapshot, IReadOnlyList<string> needs)
    {
        bool Wants(string name) => needs.Contains(name, StringComparer.Ordinal);

        // 詳細を作るのは、詳細からしか取れない欄を頼まれたときだけ（選択で千件送るときの手間を減らす）
        var detail = Wants("files") || Wants("avatars") || Wants("shop") ? ItemMapper.Detail(item, snapshot) : null;
        return new PluginMenuTarget(
            item.Id,
            Wants("name") ? item.DisplayName : null,
            Wants("shop") ? detail!.Shop : null,
            Wants("category") ? item.CategoryName : null,
            Wants("files") ? detail!.Files : null,
            Wants("files") ? detail!.Folders : null,
            Wants("unityPackages") ? Catalogs.UnityPackagesOf(item, snapshot) : null,
            Wants("avatars") ? detail!.CompatibleAvatars : null,
            Wants("tags") ? item.Local.UserTags.Select(tag => new UserTagRef(tag.Top, tag.Subs)).ToList() : null);
    }
}

/// <summary>
/// 右クリックした物（どの場所の、どれか）。<see cref="ItemIds"/> は持ち主の商品（item・selection はその商品、file・unityPackage は持ち主の1つ。
/// modification・shop・avatar は空）。<see cref="Key"/> はファイルのハッシュ・unitypackage の入った zip のハッシュ（フォルダの中なら null）・
/// 改変の ID・アバターの ID・ショップの鍵。<see cref="Entry"/> は unitypackage の名前
/// </summary>
public sealed record PluginMenuSpot(string Where, IReadOnlyList<string> ItemIds, string? Key = null, string? Entry = null)
{
    public static PluginMenuSpot Items(string where, IReadOnlyList<string> itemIds) => new(where, itemIds);

    public static PluginMenuSpot File(string itemId, string hash) => new(PluginMenus.WhereFile, [itemId], hash);

    public static PluginMenuSpot UnityPackage(string itemId, string? fileHash, string entry) => new(PluginMenus.WhereUnityPackage, [itemId], fileHash, entry);

    public static PluginMenuSpot Modification(string id) => new(PluginMenus.WhereModification, [], id);

    public static PluginMenuSpot Avatar(string avatarId) => new(PluginMenus.WhereAvatar, [], avatarId);

    public static PluginMenuSpot Shop(string key) => new(PluginMenus.WhereShop, [], key);

    /// <summary>送る件数（ログ）。</summary>
    public int Count => ItemIds.Count > 0 && Where is PluginMenus.WhereItem or PluginMenus.WhereSelection ? ItemIds.Count : 1;
}

/// <summary>許しの窓の1行（押せる項目1つ）。<see cref="Parent"/> は下の段なら親の名前。</summary>
public sealed record PluginMenuLine(
    string Id, string Label, string? Parent, IReadOnlyList<string> Where, IReadOnlyList<string> Needs, PluginMenuWhenRef? When);

/// <summary>宣言が前から変わった所。</summary>
public sealed record PluginMenuChange(IReadOnlyList<PluginMenuLine> Added, IReadOnlyList<PluginMenuLine> Changed, IReadOnlyList<PluginMenuLine> Removed)
{
    /// <summary>消えるだけ（足す・変わるが無い）。人に聞かずに効かせる：出す物が減るだけで、人が見ていない物は増えない。</summary>
    public bool OnlyRemoves => Added.Count == 0 && Changed.Count == 0;
}

/// <summary>押せない理由（文はアプリが決める）。</summary>
public enum PluginMenuBlock
{
    None,

    /// <summary>ソフトが取りに来ていない。</summary>
    NotResponding,

    /// <summary>開いた後に、項目が消えた・ソフトが取り消された・止められた。</summary>
    Gone,

    /// <summary>送れる数を超えた。</summary>
    TooMany,

    /// <summary>API の写しにまだ無い商品。</summary>
    NotLoaded,

    NoUnityPackage,
    HasUnityPackage,
    NotOwned,
    Owned,
    NoAvatar,
    HasAvatar,

    /// <summary>カテゴリが違う（<see cref="PluginMenuReason.Category"/> が求めたカテゴリ）。</summary>
    OtherCategory,
}

/// <summary>押せない理由と、カテゴリの条件のときの求めたカテゴリ。</summary>
public sealed record PluginMenuReason(PluginMenuBlock Block, string? Category = null)
{
    public static PluginMenuReason Ok { get; } = new(PluginMenuBlock.None);

    public bool IsOk => Block == PluginMenuBlock.None;
}
