using System.Text;
using System.Text.Json.Nodes;

namespace Chmonos.Mcp;

/// <summary>
/// AI に出す道具（読むだけ。docs/spec/mcp.md「道具」）。名前は英語の snake_case、説明は日本語（使う人と商品の名前が日本語で、
/// AI は日本語の説明も読める。言葉を揃えると、答えの欄の名前と説明の言葉が食い違わない）。
/// 説明と例には作り物の名前だけを書く。
/// </summary>
internal static class ChmonosTools
{
    private const string PagingSchema = """
        "offset": { "type": "integer", "minimum": 0, "description": "何件目から返すか（前の答えの nextOffset）。既定 0" },
        "limit": { "type": "integer", "minimum": 1, "maximum": 100, "description": "返す件数（既定 30。答えが長ければ自動で減らし、nextOffset で続きを示す）" }
        """;

    public static IReadOnlyList<Tool> All { get; } =
    [
        new("chmonos_status", "つないでいる Chmonos",
            "どの Chmonos（保存先）につないでいるか、ほかに動いている Chmonos、許可を受け取っているかを返す。Chmonos に問い合わせず、手元の札を読むだけ。"
            + "ほかの道具が「動いていません」「2つ動いています」と答えたときに、状況を確かめるのに使う。",
            """{ "type": "object", "properties": {} }""", null, StatusAsync),

        new("search_items", "商品を探す",
            "手元の商品（BOOTH で買った物・手で足した物）を探す。query は Chmonos の検索欄と同じ書き方（空白で AND、OR、-で除く、前置きなど）。"
            + "細かい条件（対応アバター・カテゴリ・ショップ・値段・入手日・タグ・属性など）は where に検索の式（and・or・not・text・condition の木）で渡す。"
            + "condition の種類と値は get_search_conditions で調べる。例：{\"condition\":{\"kind\":\"avatar\",\"avatars\":[\"<アバターのID>\"]}}。"
            + "答えは要約の一覧と全体の数と続きの印（nextCursor）。詳しくは get_item で引く。非表示の商品と、設定で隠している R-18 は出ない。",
            """
            { "type": "object", "properties": {
                "query": { "type": "string", "description": "検索欄と同じ文字列（例：\"ワンピース 夏\"）" },
                "where": { "type": "object", "description": "検索の式（POST /v1/search の where と同じ形）。query と両方渡すと AND" },
                "sort": { "type": "string", "description": "並べ方。欄:向き をカンマで並べる（欄は id・name・publishedAt・acquiredAt・wishCount・paid・fetchedAt、向きは asc・desc）。例：\"acquiredAt:desc\"" },
                "has_unity_package": { "type": "boolean", "description": "unitypackage を持つ商品だけ（true）・持たない商品だけ（false）" },
                "limit": { "type": "integer", "minimum": 1, "maximum": 50, "description": "返す件数（既定 20）" },
                "cursor": { "type": "string", "description": "前の答えの nextCursor（続きを引く）" }
            } }
            """, null, SearchAsync),

        new("get_search_conditions", "検索の条件の種類",
            "search_items の where に書ける条件（condition）の種類・値の形・選択肢を返す。",
            """{ "type": "object", "properties": {} }""", null, ConditionsAsync),

        new("get_item", "商品の詳細",
            "商品1件の詳細（名前・ショップ・カテゴリ・対応アバター・ユーザータグ・属性・メモ・手元のファイルとフォルダの場所・中の unitypackage・購入）。"
            + "説明文は get_item_description で引く。",
            """
            { "type": "object", "required": ["id"], "properties": {
                "id": { "type": "string", "description": "商品の ID" },
                "include": { "type": "array", "items": { "type": "string", "enum": ["avatarLinks", "allFiles", "contents", "meta"] },
                    "description": "足す情報。avatarLinks：対応アバターの紐付けの出どころと状態、allFiles：外したファイル・古い版も、contents：zip の中の一覧、meta：BOOTH の情報を取った日時" }
            } }
            """, null, ItemAsync),

        new("get_item_description", "商品の説明文",
            "商品の BOOTH の説明文を、見出しごとの素の文で返す（長ければ区切って、nextOffset で続きを示す）。",
            """
            { "type": "object", "required": ["id"], "properties": {
                "id": { "type": "string", "description": "商品の ID" },
                "offset": { "type": "integer", "minimum": 0, "description": "何文字目から返すか（前の答えの nextOffset）。既定 0" },
                "max_chars": { "type": "integer", "minimum": 500, "maximum": 6000, "description": "返す文字数の上限（既定 3000）" }
            } }
            """, null, DescriptionAsync),

        new("list_avatars", "アバターの一覧",
            "Chmonos の登録簿のアバター（名前・別名・素体・持っているか・対応する商品の数）。query で名前・別名を絞る。",
            $$"""
            { "type": "object", "properties": {
                "query": { "type": "string", "description": "名前・BOOTH の名前・別名・素体に含まれる文字（大文字小文字は区別しない）" },
                "owned_only": { "type": "boolean", "description": "持っているアバターだけ" },
                {{PagingSchema}}
            } }
            """, null, AvatarsAsync),

        new("get_avatar", "アバター1体",
            "登録簿のアバター1体。対応する商品を探すときは search_items の where に avatar の条件を渡す。",
            """{ "type": "object", "required": ["id"], "properties": { "id": { "type": "string", "description": "アバターの ID（list_avatars の id）" } } }""",
            null, AvatarAsync),

        new("list_avatar_bases", "共通素体",
            "共通素体（同じ体型を共有するアバターのまとまり）と、属するアバターの ID・呼び方。",
            $$"""
            { "type": "object", "properties": {
                "query": { "type": "string", "description": "素体の名前・呼び方に含まれる文字" },
                {{PagingSchema}}
            } }
            """, null, AvatarBasesAsync),

        new("lookup_file", "ファイルから商品を引く",
            "手元のファイル・フォルダの場所か、ファイルの SHA-256 から、それを持つ商品を引く。path か hash のどちらかを渡す。",
            """
            { "type": "object", "properties": {
                "path": { "type": "string", "description": "ファイルかフォルダの完全なパス" },
                "hash": { "type": "string", "description": "ファイルの SHA-256（16進）" }
            } }
            """, null, LookupFileAsync),

        new("list_unitypackages", "手元の unitypackage",
            "手元の .unitypackage（入っている zip・フォルダと中の場所・入れる先・その商品と対応アバター）。avatar で対応アバターを絞る。",
            $$"""
            { "type": "object", "properties": {
                "avatar": { "type": "string", "description": "対応アバターの ID で絞る" },
                "query": { "type": "string", "description": "商品名・中の場所に含まれる文字" },
                {{PagingSchema}}
            } }
            """, null, UnityPackagesAsync),

        new("find_unitypackage_assets", "unitypackage の中身",
            "unitypackage の中身（GUID と Assets の下のパス）。item_id でその商品の包みの中身を、guids で GUID から包みと商品を逆に引く。どちらかを渡す。",
            """
            { "type": "object", "properties": {
                "item_id": { "type": "string", "description": "商品の ID（その商品の unitypackage の中身を返す）" },
                "path_contains": { "type": "string", "description": "item_id のとき、中身のパスに含まれる文字で絞る" },
                "guids": { "type": "array", "items": { "type": "string" }, "maxItems": 200, "description": "引く GUID（32桁の16進。200件まで）" }
            } }
            """, null, UnityPackageAssetsAsync),

        new("list_modifications", "改変",
            "改変（アバターに商品を組み合わせた記録：使った商品・Unity プロジェクト・メモ）。id で1つを詳しく、avatar・query で絞る。",
            $$"""
            { "type": "object", "properties": {
                "id": { "type": "string", "description": "改変の ID（1つを詳しく返す）" },
                "avatar": { "type": "string", "description": "アバターの ID か名前の一部で絞る" },
                "query": { "type": "string", "description": "改変の名前・メモに含まれる文字" },
                {{PagingSchema}}
            } }
            """, null, ModificationsAsync),

        new("get_unity_scans", "Unity プロジェクトを調べた結果",
            "Chmonos が前に Unity プロジェクトを調べた結果の控え（入っている商品と割合・VPM のパッケージ）。調べ直しはしない。",
            $$"""
            { "type": "object", "properties": {
                "project": { "type": "string", "description": "Unity プロジェクトのフォルダの完全なパスで絞る" },
                {{PagingSchema}}
            } }
            """, null, UnityScansAsync),

        new("list_notifications", "通知",
            "Chmonos の通知（商品の更新・取り込みの結果など。既読・解消の印つき）。新しい順。",
            $$"""
            { "type": "object", "properties": {
                "unread_only": { "type": "boolean", "description": "未読だけ" },
                {{PagingSchema}}
            } }
            """, null, NotificationsAsync),

        new("list_classifications", "分類の一覧",
            "分類と件数：user_tags（ユーザータグ）・attributes（属性）・categories（カテゴリ）・booth_tags（BOOTH のタグ）・shops（ショップ。星・メモ）。"
            + "search_items の where の値を選ぶのに使う。",
            $$"""
            { "type": "object", "required": ["kind"], "properties": {
                "kind": { "type": "string", "enum": ["user_tags", "attributes", "categories", "booth_tags", "shops"] },
                "query": { "type": "string", "description": "名前に含まれる文字" },
                {{PagingSchema}}
            } }
            """, null, ClassificationsAsync),
    ];

    // ---- 道具の中身 ----

    private static Task<string> StatusAsync(ToolCall call)
    {
        var bridge = call.Bridge;
        var alive = bridge.Finder.Alive();
        var find = bridge.Finder.Find();
        var answer = new JsonObject
        {
            ["clientName"] = bridge.ClientName,
            ["connected"] = find is FindResult.Found found
                ? new JsonObject
                {
                    ["storeRoot"] = found.Instance.StoreRoot,
                    ["version"] = found.Instance.Version,
                    ["headless"] = found.Instance.Headless,
                }
                : null,
            ["problem"] = find is FindResult.Problem problem ? problem.Message : null,
            ["running"] = new JsonArray(alive.Select(record => (JsonNode)new JsonObject
            {
                ["storeRoot"] = record.StoreRoot,
                ["apiOn"] = record.Port is not null,
                ["version"] = record.Version,
            }).ToArray()),
        };
        return Task.FromResult(AnswerShaper.Fit(answer));
    }

    private static async Task<string> SearchAsync(ToolCall call)
    {
        var args = call.Args;
        var query = args.String("query");
        var where = args.Node("where");
        if (where is not null and not JsonObject)
        {
            throw new ToolArgumentException("where は検索の式のオブジェクトで渡してください。");
        }

        JsonNode? expression = (query, where) switch
        {
            (null, null) => null,
            ({ } text, null) => new JsonObject { ["text"] = text },
            (null, { } tree) => tree,
            ({ } text, { } tree) => new JsonObject { ["and"] = new JsonArray(new JsonObject { ["text"] = text }, tree) },
        };

        var showAdult = await Visibility.ShowAdultAsync(call.Bridge, call.CancellationToken);
        var limit = args.Int("limit", 20, 1, 50);
        while (true)
        {
            var body = new JsonObject
            {
                ["where"] = expression?.DeepClone(),
                ["includeHidden"] = false,
                ["includeAdult"] = showAdult,
                ["hasUnityPackage"] = args.Bool("has_unity_package"),
                ["sort"] = Sort(args.String("sort")),
                ["cursor"] = args.String("cursor"),
                ["limit"] = limit,
            };
            var page = await Tool.CallAsync(call.Bridge.PostAsync("/v1/search", body, call.CancellationToken));
            var items = page?["items"]?.AsArray() ?? [];
            var answer = new JsonObject
            {
                ["total"] = page?["total"]?.DeepClone(),
                ["returned"] = items.Count,
                ["nextCursor"] = page?["nextCursor"]?.DeepClone(),
                ["items"] = AnswerShaper.Slim(items),
            };
            var text = answer.ToJsonString(AnswerShaper.Json);

            // 長すぎれば件数を減らして引き直す（通訳の側で切ると、API の続きの印と合わなくなる）
            if (text.Length <= AnswerShaper.MaxAnswerChars || limit == 1)
            {
                return text.Length <= AnswerShaper.MaxAnswerChars ? text : AnswerShaper.Fit(answer);
            }

            limit = Math.Max(1, limit / 2);
        }
    }

    /// <summary>"wishCount:desc,name" を API の並べ方の配列にする。</summary>
    private static JsonArray? Sort(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var sort = new JsonArray();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':', StringSplitOptions.TrimEntries);
            var order = pieces.Length > 1 ? pieces[1].ToLowerInvariant() : "asc";
            if (order is not ("asc" or "desc"))
            {
                throw new ToolArgumentException("sort の向きは asc か desc です（例：\"acquiredAt:desc\"）。");
            }

            sort.Add(new JsonObject { ["field"] = pieces[0], ["order"] = order });
        }

        return sort.Count == 0 ? null : sort;
    }

    private static async Task<string> ConditionsAsync(ToolCall call)
    {
        var conditions = await Tool.CallAsync(call.Bridge.GetAsync("/v1/search/conditions", call.CancellationToken));
        var answer = new JsonObject
        {
            ["howToWrite"] = "where の1段は and・or・not・text・condition のどれか1つ。例：{\"and\":[{\"text\":\"夏\"},{\"condition\":{\"kind\":\"category\",\"values\":[\"3D衣装\"]}}]}。"
                + "choice の種類は choice に選択肢の1つ、list は values、avatar は avatars（ID）か bases（素体名）、range は min・max、date は from・to（YYYY-MM-DD）。",
            ["conditions"] = conditions?.DeepClone(),
        };
        return AnswerShaper.Fit(answer);
    }

    private static async Task<JsonNode?> VisibleItemAsync(ToolCall call, string id, string? include = null)
    {
        var path = "/v1/items/" + Uri.EscapeDataString(id) + (include is null ? string.Empty : "?include=" + Uri.EscapeDataString(include));
        var item = await Tool.CallAsync(call.Bridge.GetAsync(path, call.CancellationToken));
        var showAdult = await Visibility.ShowAdultAsync(call.Bridge, call.CancellationToken);
        return Visibility.RefusalFor(item, showAdult) is { } refusal ? throw new ToolProblemException(refusal) : item;
    }

    private static async Task<string> ItemAsync(ToolCall call)
    {
        var id = call.Args.String("id") ?? throw new ToolArgumentException("id を渡してください。");
        var include = call.Args.Strings("include");
        var item = await VisibleItemAsync(call, id, include.Count == 0 ? null : string.Join(',', include));
        return AnswerShaper.Fit(item);
    }

    private static async Task<string> DescriptionAsync(ToolCall call)
    {
        var id = call.Args.String("id") ?? throw new ToolArgumentException("id を渡してください。");
        var offset = call.Args.Int("offset", 0, 0, int.MaxValue);
        var max = call.Args.Int("max_chars", 3000, 500, 6000);
        var item = await VisibleItemAsync(call, id);
        var description = await Tool.CallAsync(call.Bridge.GetAsync("/v1/items/" + Uri.EscapeDataString(id) + "/description", call.CancellationToken));

        var text = new StringBuilder();
        if (description?["summary"]?.GetValue<string>() is { Length: > 0 } summary)
        {
            text.AppendLine(summary.Trim()).AppendLine();
        }

        foreach (var section in description?["sections"]?.AsArray() ?? [])
        {
            if (section?["heading"]?.GetValue<string>() is { Length: > 0 } heading)
            {
                text.Append("## ").AppendLine(heading.Trim());
            }

            text.AppendLine(section?["text"]?.GetValue<string>()?.Trim()).AppendLine();
        }

        // AppendLine は Windows で \r\n を足し、BOOTH の本文にも \r\n が混ざる。AI には \n だけを渡す（字数の数えも offset もこの後の文字列で行う）
        var whole = text.ToString().Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
        var start = Math.Min(offset, whole.Length);
        var slice = whole.Substring(start, Math.Min(max, whole.Length - start));
        var answer = new JsonObject
        {
            ["id"] = id,
            ["name"] = item?["name"]?.DeepClone(),
            ["totalChars"] = whole.Length,
            ["offset"] = start,
            ["nextOffset"] = start + slice.Length < whole.Length ? start + slice.Length : null,
            ["text"] = slice,
        };
        return answer.ToJsonString(AnswerShaper.Json);
    }

    private static bool Contains(JsonNode? node, string? query, params string[] keys)
        => query is null || keys.Any(key => node?[key] switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) => text.Contains(query, StringComparison.OrdinalIgnoreCase),
            JsonArray array => array.Any(element => element is JsonValue value && value.TryGetValue<string>(out var text) && text.Contains(query, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        });

    private static string Page(ToolCall call, IEnumerable<JsonNode?> items, int omitted)
        => AnswerShaper.Page(
            items.ToList(),
            call.Args.Int("offset", 0, 0, int.MaxValue),
            call.Args.Int("limit", AnswerShaper.ListDefaultLimit, 1, AnswerShaper.ListMaxLimit),
            Omitted(omitted));

    /// <summary>表示の設定で落とした数を添える（AI が「無い」と「隠した」を取り違えないように）。</summary>
    private static JsonObject? Omitted(int omitted)
        => omitted > 0 ? new JsonObject { ["omittedByDisplaySettings"] = omitted } : null;

    private static async Task<string> AvatarsAsync(ToolCall call)
    {
        var avatars = await Tool.CallAsync(call.Bridge.GetAsync("/v1/avatars", call.CancellationToken)) as JsonArray ?? [];
        var visibility = await Visibility.LoadAsync(call.Bridge, call.CancellationToken);
        var omitted = visibility.Remove(avatars, "id");
        var query = call.Args.String("query");
        var ownedOnly = call.Args.Bool("owned_only") == true;
        return Page(call, avatars.Where(avatar =>
            Contains(avatar, query, "name", "boothName", "aliases", "baseName")
            && (!ownedOnly || avatar?["isOwned"]?.GetValue<bool>() == true)), omitted);
    }

    private static async Task<string> AvatarAsync(ToolCall call)
    {
        var id = call.Args.String("id") ?? throw new ToolArgumentException("id を渡してください。");
        var avatar = await Tool.CallAsync(call.Bridge.GetAsync("/v1/avatars/" + Uri.EscapeDataString(id), call.CancellationToken));
        var visibility = await Visibility.LoadAsync(call.Bridge, call.CancellationToken);
        return visibility.HiddenIds.Contains(id)
            ? throw new ToolProblemException("このアバターは、Chmonos の表示の設定で隠している商品なので答えません。")
            : AnswerShaper.Fit(avatar);
    }

    private static async Task<string> AvatarBasesAsync(ToolCall call)
    {
        var bases = await Tool.CallAsync(call.Bridge.GetAsync("/v1/avatar-bases", call.CancellationToken)) as JsonArray ?? [];
        var query = call.Args.String("query");
        return Page(call, bases.Where(entry => Contains(entry, query, "name", "aliases")), 0);
    }

    private static async Task<string> LookupFileAsync(ToolCall call)
    {
        var path = call.Args.String("path");
        var hash = call.Args.String("hash");
        if ((path is null) == (hash is null))
        {
            throw new ToolArgumentException("path か hash のどちらか1つを渡してください。");
        }

        var query = path is not null ? "path=" + Uri.EscapeDataString(path) : "hash=" + Uri.EscapeDataString(hash!);
        var owners = await Tool.CallAsync(call.Bridge.GetAsync("/v1/files/lookup?" + query, call.CancellationToken)) as JsonArray ?? [];
        var visibility = await Visibility.LoadAsync(call.Bridge, call.CancellationToken);
        var omitted = visibility.Remove(owners, "itemId");
        return Page(call, owners, omitted);
    }

    private static async Task<string> UnityPackagesAsync(ToolCall call)
    {
        var avatar = call.Args.String("avatar");
        var path = "/v1/unitypackages" + (avatar is null ? string.Empty : "?avatar=" + Uri.EscapeDataString(avatar));
        var packages = await Tool.CallAsync(call.Bridge.GetAsync(path, call.CancellationToken)) as JsonArray ?? [];
        var visibility = await Visibility.LoadAsync(call.Bridge, call.CancellationToken);
        var omitted = visibility.Remove(packages, "itemId");
        var query = call.Args.String("query");
        return Page(call, packages.Where(package => Contains(package, query, "itemName", "entry", "container")), omitted);
    }

    private static async Task<string> UnityPackageAssetsAsync(ToolCall call)
    {
        var itemId = call.Args.String("item_id");
        var guids = call.Args.Strings("guids");
        if ((itemId is null) == (guids.Count == 0))
        {
            throw new ToolArgumentException("item_id か guids のどちらか1つを渡してください。");
        }

        if (itemId is not null)
        {
            await VisibleItemAsync(call, itemId);
            var packages = await Tool.CallAsync(call.Bridge.GetAsync("/v1/items/" + Uri.EscapeDataString(itemId) + "/unitypackage-assets", call.CancellationToken)) as JsonArray ?? [];
            if (call.Args.String("path_contains") is { } contains)
            {
                foreach (var package in packages)
                {
                    if (package?["assets"] is JsonArray assets)
                    {
                        package["assetsTotal"] = assets.Count;
                        package["assets"] = new JsonArray(assets.Where(asset => Contains(asset, contains, "path")).Select(asset => asset?.DeepClone()).ToArray());
                    }
                }
            }

            return AnswerShaper.Fit(new JsonObject { ["itemId"] = itemId, ["packages"] = packages.DeepClone() });
        }

        if (guids.Count > 200)
        {
            throw new ToolArgumentException("guids は200件までです。");
        }

        var body = new JsonObject { ["guids"] = new JsonArray(guids.Select(guid => (JsonNode?)JsonValue.Create(guid)).ToArray()) };
        var found = await Tool.CallAsync(call.Bridge.PostAsync("/v1/unitypackages/lookup", body, call.CancellationToken));
        var visibility = await Visibility.LoadAsync(call.Bridge, call.CancellationToken);
        var omitted = visibility.Remove(found?["found"], "itemId");
        if (found is JsonObject obj && omitted > 0)
        {
            obj["omittedByDisplaySettings"] = omitted;
        }

        return AnswerShaper.Fit(found);
    }

    private static async Task<string> ModificationsAsync(ToolCall call)
    {
        var visibility = await Visibility.LoadAsync(call.Bridge, call.CancellationToken);
        if (call.Args.String("id") is { } id)
        {
            var modification = await Tool.CallAsync(call.Bridge.GetAsync("/v1/modifications/" + Uri.EscapeDataString(id), call.CancellationToken));
            var hidden = visibility.Remove(modification, "itemId");
            if (modification is JsonObject obj && hidden > 0)
            {
                obj["omittedByDisplaySettings"] = hidden;
            }

            return AnswerShaper.Fit(modification);
        }

        var all = await Tool.CallAsync(call.Bridge.GetAsync("/v1/modifications", call.CancellationToken)) as JsonArray ?? [];
        var omitted = visibility.Remove(all, "itemId");
        var avatar = call.Args.String("avatar");
        var query = call.Args.String("query");
        return Page(call, all.Where(modification =>
            (avatar is null || modification?["avatarId"]?.GetValue<string>() == avatar || Contains(modification, avatar, "avatarName"))
            && Contains(modification, query, "name", "memo")), omitted);
    }

    private static async Task<string> UnityScansAsync(ToolCall call)
    {
        var project = call.Args.String("project");
        var path = "/v1/unity-scans" + (project is null ? string.Empty : "?project=" + Uri.EscapeDataString(project));
        var scans = await Tool.CallAsync(call.Bridge.GetAsync(path, call.CancellationToken)) as JsonArray ?? [];
        var visibility = await Visibility.LoadAsync(call.Bridge, call.CancellationToken);
        var omitted = visibility.Remove(scans, "itemId");
        return Page(call, scans, omitted);
    }

    private static async Task<string> NotificationsAsync(ToolCall call)
    {
        var notifications = await Tool.CallAsync(call.Bridge.GetAsync("/v1/notifications", call.CancellationToken)) as JsonArray ?? [];
        var visibility = await Visibility.LoadAsync(call.Bridge, call.CancellationToken);
        var omitted = visibility.Remove(notifications, "itemId");
        var unreadOnly = call.Args.Bool("unread_only") == true;
        var list = notifications
            .Where(notification => !unreadOnly || notification?["isRead"]?.GetValue<bool>() == false)
            .OrderByDescending(notification => notification?["createdAt"]?.GetValue<string>(), StringComparer.Ordinal);
        return Page(call, list, omitted);
    }

    private static async Task<string> ClassificationsAsync(ToolCall call)
    {
        var path = call.Args.String("kind") switch
        {
            "user_tags" => "/v1/user-tags",
            "attributes" => "/v1/attributes",
            "categories" => "/v1/categories",
            "booth_tags" => "/v1/booth-tags",
            "shops" => "/v1/shops",
            _ => throw new ToolArgumentException("kind は user_tags・attributes・categories・booth_tags・shops のどれかです。"),
        };
        var entries = await Tool.CallAsync(call.Bridge.GetAsync(path, call.CancellationToken)) as JsonArray ?? [];
        var query = call.Args.String("query");
        return Page(call, entries.Where(entry => Contains(entry, query, "name", "top", "value", "subdomain")), 0);
    }
}
