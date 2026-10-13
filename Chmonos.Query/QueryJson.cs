using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chmonos.Query;

/// <summary>
/// 答えの JSON の書き方（docs/spec/local-api.md「共通の決まり」）：名前は camelCase、値が無くても項目を省かず null、
/// 日本語はそのまま書く（\u のエスケープにしない。人が読めるように）。
/// Native AOT で組むので、型ごとの書き方は source generator で作る（実行時に型を調べない）。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = false)]
[JsonSerializable(typeof(ItemDetail))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(ItemSummary))]
[JsonSerializable(typeof(IReadOnlyList<ItemSummary>))]
[JsonSerializable(typeof(Search.SearchRequest))]
[JsonSerializable(typeof(Search.SearchPage))]
[JsonSerializable(typeof(Search.FacetsRequest))]
[JsonSerializable(typeof(Search.FacetsAnswer))]
[JsonSerializable(typeof(IReadOnlyList<AvatarEntryRef>))]
[JsonSerializable(typeof(IReadOnlyList<AvatarBaseRef>))]
[JsonSerializable(typeof(IReadOnlyList<ShopEntryRef>))]
[JsonSerializable(typeof(ShopEntryRef))]
[JsonSerializable(typeof(IReadOnlyList<UserTagTopRef>))]
[JsonSerializable(typeof(IReadOnlyList<AttributeEntryRef>))]
[JsonSerializable(typeof(IReadOnlyList<CategoryEntryRef>))]
[JsonSerializable(typeof(IReadOnlyList<CountRef>))]
[JsonSerializable(typeof(IReadOnlyList<FileOwnerRef>))]
[JsonSerializable(typeof(IReadOnlyList<UnityPackageEntryRef>))]
[JsonSerializable(typeof(IReadOnlyList<ModificationRef>))]
[JsonSerializable(typeof(ModificationRef))]
[JsonSerializable(typeof(IReadOnlyList<UnityProjectRef>))]
[JsonSerializable(typeof(IReadOnlyList<NotificationRef>))]
[JsonSerializable(typeof(DisplaySettingsRef))]
[JsonSerializable(typeof(IReadOnlyList<ConditionKindRef>))]
[JsonSerializable(typeof(AvatarEntryRef))]
[JsonSerializable(typeof(ItemsBatchRequest))]
[JsonSerializable(typeof(ItemsBatchAnswer))]
[JsonSerializable(typeof(HealthAnswer))]
[JsonSerializable(typeof(VersionAnswer))]
[JsonSerializable(typeof(ProblemAnswer))]
[JsonSerializable(typeof(FlagRequest))]
[JsonSerializable(typeof(UserTagsRequest))]
[JsonSerializable(typeof(UserTagChangeRequest))]
[JsonSerializable(typeof(AttributesRequest))]
[JsonSerializable(typeof(MemoRequest))]
[JsonSerializable(typeof(NameRequest))]
[JsonSerializable(typeof(UnityEditorRef))]
[JsonSerializable(typeof(IReadOnlyList<UnityEditorRef>))]
[JsonSerializable(typeof(UnitySendRequest))]
[JsonSerializable(typeof(UnitySendStatus))]
[JsonSerializable(typeof(RecentRequest))]
[JsonSerializable(typeof(IdsRequest))]
[JsonSerializable(typeof(AcceptedAnswer))]
[JsonSerializable(typeof(ChangeEventRef))]
[JsonSerializable(typeof(ChangesAnswer))]
public sealed partial class QueryJsonContext : JsonSerializerContext
{
}

public static class QueryJson
{
    /// <summary>答えに使う書き方。日本語をエスケープしない（ブラウザの HTML に埋めないので、HTML 向けの用心は要らない）。</summary>
    public static JsonSerializerOptions Options { get; } = new(QueryJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static QueryJsonContext Context { get; } = new(Options);

    public static string Serialize(ItemDetail detail, bool indented = false)
        => JsonSerializer.Serialize(detail, indented ? Indented.ItemDetail : Context.ItemDetail);

    public static string Serialize(ItemSummary summary, bool indented = false)
        => JsonSerializer.Serialize(summary, indented ? Indented.ItemSummary : Context.ItemSummary);

    /// <summary>試験の期待する答え（ゴールデン）を人が読める形で比べるための、字下げした書き方。</summary>
    private static QueryJsonContext Indented { get; } = new(new JsonSerializerOptions(Options) { WriteIndented = true });
}
