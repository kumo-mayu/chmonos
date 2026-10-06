namespace ViewShot;

/// <summary>
/// 場面の登録。足すときは、場面を書いたファイル（<c>Scenes.*.cs</c>）の一覧をここへ並べる。
///
/// 場面の書き方：
/// 1. <c>context.Seed</c>・<c>context.Fake</c> で作り物のデータを書く
/// 2. <c>context.StartAsync()</c> でサービス一式と主画面の ViewModel を組み、目当ての画面へ移る（<c>main.ShowResolve()</c> など）
/// 3. <c>context.MainWindow()</c>（主の窓の中身）か部品を <c>context.PresentAsync</c> で舞台に載せ、読み込みを待つ
/// 4. ViewModel の値で状態を作る（選ぶ・開く）。マウスやキーは使えない
/// 5. <c>Shot</c> を返す。見たい所があれば <c>Focus</c> で部品を指す
/// </summary>
internal static partial class Scenes
{
    public static IReadOnlyList<Scene> All { get; } =
    [
        .. Resolve,
        .. Import,
        .. Item,
        .. ItemPages,
        .. ItemChangeScenes,
        .. Edit,
        EditDescription(),
        .. Nav,
        .. Bands,
        .. Modifications,
        .. ModificationMembers,
        .. ManageMemo32Scenes,
        .. AvatarSuggestScenes,
        .. ManageRowMenuScenes,
        .. ModificationDuplicateScenes,
        .. DragEdgeScrollScenes,
        .. AvatarScenes,
        .. AvatarBaseMemberScenes,
        .. Search,
        .. SearchFilters,
        .. SavedSearchScenes,
        .. SortDividers,
        .. CardLists,
        .. CardInfo,
        .. CardBadges,
        .. ShopScenes,
        .. ShopPerfScenes,
        .. ImportMissingPerfScenes,
        .. FolderTree,
        .. Inbox,
        .. Dialogs,
        .. Settings,
        .. FirstRun,
        .. UnityToolScenes,
        .. Empties,
        .. Parts,
        .. CatalogScenes,
    ];

    public static Scene Find(string name)
        => All.FirstOrDefault(scene => scene.Name == name)
           ?? throw new ArgumentException($"場面「{name}」はありません。ViewShot list で名前を確かめてください。");
}
