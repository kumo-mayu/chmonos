namespace Chmonos.Query;

/// <summary>商品をまとめて引く（<c>POST /v1/items/batch</c>）。一覧から詳細へ、1件ずつ聞かずに済むように。</summary>
/// <param name="Include">商品1件と同じ include（<c>avatarLinks,allFiles</c> など）。名前の配列でもよい（<see cref="IncludeListConverter"/>）。</param>
public sealed record ItemsBatchRequest(
    IReadOnlyList<string>? Ids,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(IncludeListConverter))] string? Include = null);

/// <param name="Missing">見つからなかった ID（送った順）。</param>
public sealed record ItemsBatchAnswer(IReadOnlyList<ItemDetail> Items, IReadOnlyList<string> Missing);

/// <summary>動いているか（<c>GET /v1/health</c>。トークンなし）。</summary>
/// <param name="Status"><c>ok</c>（答えられる）・<c>tooNew</c>（保存の形がこの API より新しい）。</param>
/// <param name="FailedItemCount">
/// 読めなかった商品（手で壊した JSON など）の数。黙って減らさず知らせる。ID はトークンの要る <c>/v1/failed-items</c> で返す
/// （health はトークンなしで答えるので、保存先の中身を入れない。点検30）。
/// </param>
public sealed record HealthAnswer(string Status, DateTimeOffset LoadedAt, int ItemCount, int FailedItemCount);

/// <summary>バージョン（<c>GET /v1/version</c>。トークンなし）。</summary>
/// <param name="Api">URL のバージョン（<c>v1</c>）。</param>
/// <param name="App">アプリと同じ番号（同じ zip で配る）。</param>
/// <param name="StoreFormat">保存先の形の版。</param>
/// <param name="StoreFormatSupported">この API が読める版の上限。</param>
/// <param name="Notice">BOOTH の情報の扱いについての注意（docs/spec/local-api.md「注意」）。</param>
/// <param name="Epoch">この API の起動ごとに変わる名前。通番は起動ごとに数え直すので、外の道具は名前が変わったら全部を取り直す。</param>
/// <param name="Sequence">今の通番（変化を1つ記録するたびに進む）。</param>
/// <param name="Busy">アプリがまとめて書いている途中なら、その種類（<c>import</c> など）。その間は古い答えを返し続ける。</param>
/// <param name="Progress">取り込み（<c>busy</c> が <c>import</c>）の済んだ数と全体。走査の途中・取る物が無い・ほかの種類のときは null（<see cref="ImportProgress"/>）。</param>
public sealed record VersionAnswer(string Api, string App, int StoreFormat, int StoreFormatSupported, string Notice, string Epoch, long Sequence, string? Busy, ImportProgressRef? Progress);

/// <summary>変化1つ（<c>/v1/changes</c> と SSE の出来事）。</summary>
/// <param name="Kind"><c>changed</c>（写しを読み直した）・<c>reloaded</c>（全部を読み直した。外の道具も全部を取り直す）・<c>busy</c>（まとめて書く操作の始まりと終わり）・<c>formatTooNew</c>・<c>stopping</c>。</param>
/// <param name="Parts">読み直した部分（<c>items</c>・<c>images</c>・<c>registry</c>・<c>userTags</c> など）。</param>
/// <param name="ItemIds">変わった商品（増えた・直した・消えた）。</param>
/// <param name="Busy"><c>busy</c> のとき、始まりならその種類、終わりなら null。</param>
/// <param name="SendId">Unity へ送る送りの ID（<c>kind: unity</c> のときだけ。<c>/v1/unity/sends/{sendId}</c> で結果を引く）。</param>
public sealed record ChangeEventRef(
    long Seq, string Kind, IReadOnlyList<string> Parts, IReadOnlyList<string> ItemIds, string? Busy, DateTimeOffset At,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? SendId = null);

/// <param name="Reload">頼んだ通番からの変化を全部は持っていない（古すぎる・API が起動し直した）。外の道具は全部を取り直す。</param>
public sealed record ChangesAnswer(string Epoch, long Sequence, bool Reload, IReadOnlyList<ChangeEventRef> Changes);

/// <summary>書き込みの要求：オン・オフ（お気に入り・非表示など）。</summary>
public sealed record FlagRequest(bool Value);

/// <summary>書き込みの要求：ユーザータグ1つ（大分類と小分類）。</summary>
public sealed record UserTagInput(string Top, IReadOnlyList<string>? Subs);

/// <summary>書き込みの要求：商品のユーザータグを丸ごと置き換える。</summary>
public sealed record UserTagsRequest(IReadOnlyList<UserTagInput> Tags);

/// <summary>書き込みの要求：ユーザータグを1つ足す・外す（小分類を省くと大分類だけ）。一覧に足すときも使う。</summary>
public sealed record UserTagChangeRequest(string Top, string? Sub);

/// <summary>書き込みの要求：属性の値（0〜100。null は外して未評価に戻す）。名指しした属性だけ書く。</summary>
public sealed record AttributesRequest(IReadOnlyDictionary<string, int?> Values);

/// <summary>書き込みの要求：メモ（null か空で消す）。</summary>
public sealed record MemoRequest(string? Memo);

/// <summary>書き込みの要求：名前（属性を一覧に足す）。</summary>
public sealed record NameRequest(string Name);

/// <summary>書き込みの要求：足跡の種類（今は <c>used</c> だけ）。</summary>
public sealed record RecentRequest(string Kind);

/// <summary>書き込みの要求：ID の一覧（通知の既読・解消）。</summary>
public sealed record IdsRequest(IReadOnlyList<string> Ids);

/// <summary>書き込みの要求：対応アバター1体（商品 ID）。</summary>
public sealed record AvatarLinkRequest(string AvatarId);

/// <summary>書き込みの要求：確認待ちの対応アバターのうち、確認済みにする物（名指し。まとめての確定は出さない）。</summary>
public sealed record AvatarConfirmRequest(IReadOnlyList<string> AvatarIds);

/// <summary>書き込みの要求：共通素体1つ（名前）。</summary>
public sealed record BaseLinkRequest(string BaseName);

/// <summary>書き込みの要求：改変を作る（アバター1体＋名前）。</summary>
public sealed record ModificationCreateRequest(string AvatarId, string Name);

/// <summary>書き込みの要求：改変の blueprint ID（<c>avtr_</c>＋GUID。null か空で外す）。</summary>
public sealed record BlueprintIdRequest(string? BlueprintId);

/// <summary>エクスプローラで開く場所（省くと、場所が1つだけの商品ならそこ）。</summary>
public sealed record RevealRequest(string? Path);

/// <summary>書き込みの要求：改変の Unity プロジェクトのフォルダ（null で外す）。</summary>
public sealed record UnityProjectRequest(string? Path);

/// <summary>
/// 書き込みの要求：改変の使った物1行。足すときは商品と、分かればファイル（zip のハッシュ・中の unitypackage）。
/// 外す・戻すときは行を名指しする（同じ商品が2行あるときは fileHash・package・addedAt で絞る。答えの members と同じ値）
/// </summary>
public sealed record ModificationMemberRequest(string ItemId, long? VariationId, string? FileHash, string? Package, DateTimeOffset? AddedAt);

/// <summary>
/// 書き込みを受け付けたが、アプリがまとめて書いている途中（取り込み）なので、答えの写しはまだ差し替えていない（202）。
/// 終わったら変化の知らせ（SSE・changes）で伝える。<see cref="Busy"/> は何をしている途中か
/// </summary>
public sealed record AcceptedAnswer(string Busy);

/// <summary>ソフトが許しを求める（plan 31。トークンなしで受ける）。<see cref="Scopes"/> は欲しい書き込みの種類（読むのはいつも許す）。</summary>
public sealed record ClientRequestBody(string Name, IReadOnlyList<string>? Scopes, string? Namespace = null);

/// <summary>許しを求めたのを受け付けた（202）。<see cref="RequestId"/> は結果を取る合言葉なので、URL に入れず本文で送り返してもらう。</summary>
public sealed record ClientRequestAccepted(string RequestId, string State);

/// <summary>結果を取る。<see cref="Wait"/> は決まるまで待つ秒（0〜30。省けば待たない）。</summary>
public sealed record ClientResultRequest(string RequestId, int? Wait);

/// <summary>
/// 結果（<see cref="State"/>：pending・approved・denied・delivered）。トークンは approved の1回だけ入る。
/// 2回目からは delivered で、トークンは入らない（Chmonos はハッシュしか持たない）
/// </summary>
public sealed record ClientResultAnswer(string State, string? Token, string? ClientId, IReadOnlyList<string>? Scopes);

/// <summary>情報の欄の宣言を置き換える要求（<c>PUT /v1/extensions/{ns}/schema</c>）。欄の形は保存する宣言と同じ。</summary>
public sealed record ExtensionSchemaRequest(string? Label, IReadOnlyList<Chmonos.Core.Models.ExtensionField?>? Fields);

/// <summary>
/// ソフトが宣言した情報の欄（1つの名前空間）。<see cref="Software"/> は宣言した時のソフトの名前、<see cref="Mine"/> は問い合わせたソフトが持ち主か。
/// <see cref="HiddenFields"/>（宣言から外した欄）は、持ち主が <c>?include=hidden</c> で頼んだ時と、宣言を書いた答えだけ（ほかは null）
/// </summary>
public sealed record ExtensionSchemaRef(
    string Namespace, string Label, string Software, bool Mine,
    IReadOnlyList<Chmonos.Core.Models.ExtensionField> Fields, IReadOnlyList<Chmonos.Core.Models.ExtensionField>? HiddenFields);

/// <summary>宣言の一覧（<c>GET /v1/extensions</c>。名前空間の順）。</summary>
public sealed record ExtensionSchemaList(IReadOnlyList<ExtensionSchemaRef> Extensions);

/// <summary>情報の値を書く要求（PUT は今の宣言の欄を丸ごと・PATCH は名指しした欄だけで null は消す。属性の書き込みと同じ <c>values</c> の包み）。</summary>
public sealed record ExtensionValuesRequest(IReadOnlyDictionary<string, System.Text.Json.JsonElement>? Values);

/// <summary>
/// 付け先1つ・名前空間1つの値。<see cref="Target"/> は付け先の種類（items・modifications・files・shops・global）、<see cref="Key"/> はその鍵。
/// 宣言から外した欄は、持ち主が <c>?include=hidden</c> で頼んだ時だけ入る。ETag は保存した値の全部から作る（include で変わらない）
/// </summary>
public sealed record ExtensionValuesRef(string Namespace, string Target, string Key, IReadOnlyDictionary<string, System.Text.Json.JsonElement> Values);

/// <summary>メモ欄を書く要求（中身は自由な JSON。null で消す）。</summary>
public sealed record ExtensionMemoRequest(System.Text.Json.JsonElement? Memo);

/// <summary>メモ欄（持ち主のソフトだけが読める）。無ければ <see cref="Memo"/> は null。</summary>
public sealed record ExtensionMemoRef(string Namespace, string Target, string Key, System.Text.Json.JsonElement? Memo);

/// <summary>今のトークンが誰で、何を許されているか（<c>GET /v1/clients/me</c>）。<see cref="Kind"/> は legacy（api.json のトークン）・client。</summary>
public sealed record ClientSelfAnswer(string Kind, string? ClientId, string? Name, IReadOnlyList<string> Scopes, string? Namespace = null);

/// <summary>
/// ソフトへの出来事1つ（<c>GET /v1/plugin/events</c>。plan 44・docs/spec/plugins.md）。<see cref="Seq"/> はソフトごとの通番（起動ごとに 1 から）。
/// 中身は種類と同じ名前の欄に入れ、ほかの種類の欄は null（項目を省かない決まり）
/// </summary>
public sealed record PluginEvent(
    long Seq, string Epoch, string Kind, DateTimeOffset At, PluginItemOpened? ItemOpened, PluginSelection? SelectionChanged, PluginImport? ImportFinished,
    PluginMenuPressed? Menu = null, PluginScreen? ScreenChanged = null);

/// <summary>商品ページを開いた。</summary>
public sealed record PluginItemOpened(string ItemId);

/// <summary>
/// 開いている画面（<c>screenChanged</c> と <c>GET /v1/plugin/current</c>。docs/spec/plugins.md「開いた画面に付いていく」）。
/// <see cref="Screen"/> は画面の英字の名前（search・item・folder など）、<see cref="Target"/> はそこで開いている物（無ければ null）、
/// <see cref="Embedded"/> は開いている物が一覧の画面の右の欄に出ているか（行を選ぶたびに替わる物）
/// </summary>
public sealed record PluginScreen(string Screen, PluginTarget? Target, bool Embedded);

/// <summary>開いている物。<see cref="Type"/>：item・modification・avatar・shop。<see cref="Id"/> は読み取りの API と同じ鍵（ショップは <c>/v1/shops/{key}</c> の key）。</summary>
public sealed record PluginTarget(string Type, string Id);

/// <summary>
/// 今の画面（<c>GET /v1/plugin/current</c>）。<see cref="Last"/> はこのソフトの今の最後の通番（これを after にして流れを追えば取りこぼさない）。
/// <see cref="Selection"/> は複数選択を持つ画面で、選択の見張り（watch.selectionChanged）も許したソフトにだけ入る。ほかは null
/// </summary>
public sealed record PluginCurrentAnswer(string Epoch, long Last, PluginScreen Screen, PluginSelection? Selection);

/// <summary>
/// 複数選択が変わった（まとめてから流す）。<see cref="Screen"/>：search・folder・shop。<see cref="ItemIds"/> は先頭から上限まで、
/// <see cref="Count"/> は選んだ全部の数、<see cref="Truncated"/> は上限で切ったか
/// </summary>
public sealed record PluginSelection(string Screen, IReadOnlyList<string> ItemIds, int Count, bool Truncated);

/// <summary>取り込みが終わった。<see cref="Outcome"/>：imported・failed・cancelled。件数は imported のときだけ入る。</summary>
public sealed record PluginImport(string Outcome, int? ItemsAdded, int? ItemsAlreadyKnown, int? UnresolvedFiles);

/// <summary>
/// 出来事の答え。<see cref="Last"/> は返した中の最後の通番（無ければ送られた after。次はこれを after に）。
/// <see cref="Reset"/> は起動の名前が変わった・after が先へ行きすぎていたので頭から返した印（購読を送り直してもらう）。
/// <see cref="Dropped"/> は列があふれて渡せなかった数
/// </summary>
public sealed record PluginEventsAnswer(string Epoch, long Last, bool Reset, long Dropped, IReadOnlyList<PluginEvent> Events);

/// <summary>購読（丸ごと置き換える。空で止める）。</summary>
public sealed record PluginSubscriptionsBody(IReadOnlyList<string>? Kinds);

/// <summary>購読の答え。<see cref="Last"/> は今の最後の通番（ここから after にすれば、これより後だけを受ける）。</summary>
public sealed record PluginSubscriptionsAnswer(string Epoch, long Last, IReadOnlyList<string> Kinds);

/// <summary>
/// ソフトが足す項目1つ（<c>PUT /v1/plugin/menu</c>。plan 44 の②・docs/spec/plugins-menu.md）。子を持つ項目は下の段を開くだけで、
/// <see cref="Where"/>・<see cref="Needs"/> は空、<see cref="When"/> は null
/// </summary>
public sealed record PluginMenuItemRef(
    string Id, string Label, IReadOnlyList<string> Where, PluginMenuWhenRef? When, IReadOnlyList<string> Needs, IReadOnlyList<PluginMenuItemRef> Children);

/// <summary>押せる条件（書いた物が全部合うときだけ押せる。null は見ない）。</summary>
public sealed record PluginMenuWhenRef(bool? HasUnityPackage, bool? Owned, bool? HasAvatar, string? Category);

/// <summary>
/// 宣言の答え。<see cref="State"/>：approved（控えた宣言のとおり）・pending（人に聞いている。<see cref="Pending"/> が聞いている宣言）・
/// denied（送った宣言を人が許さなかった。控えた前の宣言のまま）。<see cref="Items"/> は今出している（人が許した）宣言
/// </summary>
public sealed record PluginMenuAnswer(string State, IReadOnlyList<PluginMenuItemRef> Items, IReadOnlyList<PluginMenuItemRef>? Pending);

/// <summary>
/// 項目が押された（<c>kind: "menu"</c>）。<see cref="Id"/> は押した項目の ID、<see cref="Where"/> は押した場所。
/// 押した物は場所と同じ名前の欄に入れ、ほかの場所の欄は null（出来事の種類と同じ決まり）。<see cref="Items"/> は item・selection の時の商品
/// （選択なら表示の並びの順）。どれも宣言の needs の欄だけ詰め、ほかは null
/// </summary>
public sealed record PluginMenuPressed(
    string Id,
    string Where,
    IReadOnlyList<PluginMenuTarget>? Items,
    PluginMenuFile? File = null,
    PluginMenuUnityPackage? UnityPackage = null,
    PluginMenuModification? Modification = null,
    PluginMenuAvatar? Avatar = null,
    PluginMenuShop? Shop = null);

/// <summary>押したファイル（<c>where: file</c>）。<see cref="Hash"/> はいつも入る。ほかは <c>/v1/items/{id}</c> の files の1行と同じ値。</summary>
public sealed record PluginMenuFile(string Hash, string? ItemId, IReadOnlyList<string>? Paths, string? State);

/// <summary>
/// 押した unitypackage（<c>where: unityPackage</c>）。<see cref="ItemId"/> はいつも入る。<see cref="FileHash"/> は入っている zip（単体の
/// unitypackage はそのファイル）のハッシュで、登録したフォルダの中の物は null。<see cref="Entry"/>・<see cref="Roots"/> は <c>/v1/unitypackages</c> の1行と同じ
/// </summary>
public sealed record PluginMenuUnityPackage(string ItemId, string? FileHash, string? Entry, IReadOnlyList<string>? Roots);

/// <summary>押した改変（<c>where: modification</c>）。<see cref="Id"/> はいつも入る。ほかは <c>/v1/modifications/{id}</c> と同じ値（外した商品は入れない）。</summary>
public sealed record PluginMenuModification(
    string Id, string? Name, string? AvatarId, string? AvatarName, IReadOnlyList<ModificationMemberRef>? Members, string? UnityProject);

/// <summary>押したアバター（<c>where: avatar</c>）。<see cref="Id"/> はいつも入る。ほかは <c>/v1/avatars</c> の1行と同じ値。</summary>
public sealed record PluginMenuAvatar(string Id, string? Name, string? BaseName);

/// <summary>押したショップ（<c>where: shop</c>）。<see cref="Key"/> はいつも入る。ほかは <c>/v1/shops/{key}</c> と同じ値。</summary>
public sealed record PluginMenuShop(string Key, string? Name, string? Url);

/// <summary>
/// 押された商品1つ。形はほかの読み取りと同じ（<c>/v1/items/{id}</c> の name・shop・category・files・folders・compatibleAvatars・userTags、
/// <c>/v1/unitypackages</c> の1行）。needs に無い欄は null（頼んでいない）、頼んで無ければ []
/// </summary>
public sealed record PluginMenuTarget(
    string ItemId,
    string? Name,
    ShopRef? Shop,
    string? Category,
    IReadOnlyList<FileRef>? Files,
    IReadOnlyList<FolderRef>? Folders,
    IReadOnlyList<UnityPackageEntryRef>? UnityPackages,
    IReadOnlyList<CompatibleAvatarRef>? Avatars,
    IReadOnlyList<UserTagRef>? Tags);

/// <summary>押した項目の結果（<c>POST /v1/plugin/events/{seq}/result</c>）。<see cref="Epoch"/> は書けば今の起動と照らす。</summary>
public sealed record PluginResultBody(bool? Ok, string? Message, string? Epoch);

/// <summary>結果を受けた答え（帯に出した文。切った後の物）。</summary>
public sealed record PluginResultAnswer(long Seq, bool Ok, string Message);

/// <summary>
/// 誤りの答え（RFC 9457 <c>application/problem+json</c>）。<see cref="Code"/> は機械が読む名前で変えない。<see cref="Detail"/> は人が読む説明。
/// </summary>
public sealed record ProblemAnswer(string Type, string Title, int Status, string Detail, string Code);

public static class ServiceText
{
    /// <summary>注意の文面（ユーザ判断 2026-10-12「注意は入れましょう」。根拠は docs/research/booth-terms.md §5）。</summary>
    public const string Notice =
        "この API が返す BOOTH の商品の情報（商品名・説明文・画像など）の権利は、それぞれの作者にあります。"
        + "自分で使うための物なので、他の人に渡したり、公開したりしないでください。"
        + "購入したファイルの扱いは、各ショップの利用規約に従ってください。";
}
