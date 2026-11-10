namespace Chmonos.App.ViewModels;

/// <summary>
/// カードの右クリックのメニュー（<c>Views/ItemCardResources.xaml</c> の CardMenu）が押す命令の一式（R1・2026-11-10）。
///
/// **なぜ型にしたか：**前はメニューがカードの Tag（＝カードを並べる画面）から命令を名前で引いていて、
/// 画面ごとに11本の受け渡しを写していた。1本書き忘れても組み立ては通り、メニューは出るのに押して何も起きなかった（2回起きた）。
/// 今はメニューが <see cref="IItemCardHost.MenuCommands"/> の中から引き、どの命令も <c>required</c> なので、
/// 新しい項目を足して一式を作る所で渡し忘れると組み立てが落ちる。
/// 中身は検索画面の物を借りるのが既定（<see cref="BorrowedFrom"/>）。画面で違う物だけ <c>with</c> で差し替える
/// </summary>
public sealed record CardMenuCommands
{
    public required RelayCommand OpenBoothCommand { get; init; }

    public required RelayCommand CopyLinkCommand { get; init; }

    public required RelayCommand OpenShopCommand { get; init; }

    public required RelayCommand EditItemCommand { get; init; }

    /// <summary>「改変に追加…」。選びのある画面は、選んだ物の上で押したら選んだ全部にする版へ差し替える。</summary>
    public required RelayCommand CardAddToModificationCommand { get; init; }

    public required RelayCommand RevealCommand { get; init; }

    public required RelayCommand CardUnpackCommand { get; init; }

    public required RelayCommand CardSendToUnityCommand { get; init; }

    public required RelayCommand CardSendToUnityWithRecordCommand { get; init; }

    public required RelayCommand CardSelectInUnityCommand { get; init; }

    /// <summary>「非表示にする」。自分の一覧を持つ画面は、書いた後にその一覧からも外す版へ差し替える。</summary>
    public required RelayCommand HideItemCommand { get; init; }

    /// <summary>検索画面の命令をそのまま借りる（カードを並べるどの画面でも、押したときの働きは検索画面と同じにする）。</summary>
    public static CardMenuCommands BorrowedFrom(SearchViewModel search) => new()
    {
        OpenBoothCommand = search.OpenBoothCommand,
        CopyLinkCommand = search.CopyLinkCommand,
        OpenShopCommand = search.OpenShopCommand,
        EditItemCommand = search.EditItemCommand,
        CardAddToModificationCommand = search.CardAddToModificationCommand,
        RevealCommand = search.RevealCommand,
        CardUnpackCommand = search.CardUnpackCommand,
        CardSendToUnityCommand = search.CardSendToUnityCommand,
        CardSendToUnityWithRecordCommand = search.CardSendToUnityWithRecordCommand,
        CardSelectInUnityCommand = search.CardSelectInUnityCommand,
        HideItemCommand = search.HideItemCommand,
    };
}
