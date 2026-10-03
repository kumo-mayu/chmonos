using Chmonos.App.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 未確定画面をキーボードだけで1件ずつ片付ける（ユーザ判断 2026-10-01「8は直そう」）。
///
/// 流れは「商品IDの欄に打つ → Enter で確かめる → 出た商品を見て、確定のショートカットで確定 → 次の行の欄へ戻る」。
/// 確定のキーは編集画面の「保存して次へ」と同じ割り当て（既定 Ctrl+Enter）を使う。どちらも「この1件を決めて次へ」で、
/// 覚えるキーを1つにできる。設定の同じキーの検査は操作ごとに1つのキーしか許さないので、別の操作にすると既定の時点でぶつかる。
/// Enter をもう一度で確定にしないのは、Enter は確かめるキーとして決めてあり（I12：確定は取り返しがつかないので Enter では走らせない）、
/// 確かめの答えを待つ間に打った2回目の Enter で、見ていない商品に結んでしまうため。
/// </summary>
public sealed partial class ResolveViewModel
{
    /// <summary>
    /// 商品IDの欄へフォーカスを戻してほしいとき（片付けて次の行へ移った後・確かめていないIDで確定のキーを押したとき）。
    /// 片付けると次の行が選ばれて欄は空になるので、そのまま次のIDを打てるようにする。
    /// 確定・BOOTHに無い商品・除外のどれも同じ（押したボタンは終わるまで押せなくなり、フォーカスが入れ物へ落ちていた。実機で確かめた）。
    /// </summary>
    public event Action<ItemIdFocusReason>? ItemIdFocusRequested;

    private void RequestItemIdFocus(ItemIdFocusReason reason)
    {
        if (HasSelection)
        {
            ItemIdFocusRequested?.Invoke(reason);
        }
    }

    /// <summary>
    /// 「このIDで登録」の吹き出し。登録のショートカットがあることだけを言う（割り当てが無ければ出さない）。
    /// 設定で変えたキーを出すため、決め打ちにしない。
    /// </summary>
    public string? AssignKeyHint
        => Shortcuts.Parse(Shortcuts.GestureOf(_main.Shortcuts, ShortcutAction.SaveAndNext)) is null
            ? null
            : $"{Shortcuts.Display(Shortcuts.GestureOf(_main.Shortcuts, ShortcutAction.SaveAndNext))} でも登録できます。";

    /// <summary>
    /// 登録のショートカット。守りは「このIDで登録」のボタンと同じ（確かめた商品がある・取得や登録の最中でない・元zipで止めていない）に、
    /// **欄の文字が確かめた商品と同じであること**を足す。ボタンは下に出ている商品を見て押すが、キーは欄に打った直後に押せるので、
    /// 打ち直して確かめていないIDのまま、前に確かめた商品へ結んでしまう。
    /// 未確定の画面でファイルを選んでいれば、何もしなかったときもキーを受け取ったことにする（欄へ流しても何も起きない）。
    /// </summary>
    public bool AssignByShortcut()
    {
        if (!HasSelection)
        {
            return false;
        }

        // 取得・登録の最中は押せない（ボタンと同じ）。答えが届く前の2回目を確定にしない
        if (IsBusy || IsTargetBlocked)
        {
            return true;
        }

        var typed = ParseItemIdInput(ItemIdInput);
        if (Preview is not { } preview || typed != preview.Id)
        {
            // 欄のIDを確かめて「BOOTHに無い」と答えが出ていれば、その文を残す（そのIDのまま登録する道がそこに出ている）。
            // ほかの文は前に打ったIDの物かもしれないので置き換える（実機で、読み取れなかった文が打ち直した後も残った）
            if (typed is null || typed != NotOnBoothItemId)
            {
                StatusText = "先に商品IDを確認してください。";
                OnPropertyChanged(nameof(HasStatus));
            }

            RequestItemIdFocus(ItemIdFocusReason.NeedsPreview);
            return true;
        }

        if (AssignCommand.CanExecute(null))
        {
            AssignCommand.Execute(null);
        }

        return true;
    }

    /// <summary>
    /// 欄の文字を商品IDとして読む。数字か BOOTH の商品URL、または手元の商品のID（「BOOTHに無い商品」の仮のIDを含む）。
    /// 読めなければ null。
    /// </summary>
    private string? ParseItemIdInput(string text)
        => Core.Services.BoothItemId.Parse(text)
            ?? (_itemNames.ContainsKey(text.Trim()) ? text.Trim() : null);
}

/// <summary>商品IDの欄へフォーカスを戻す訳。画面は訳で、左の一覧にいる人を動かすかを決める。</summary>
public enum ItemIdFocusReason
{
    /// <summary>片付けて次の行へ移った。左の一覧・検索欄で操作していたなら、そこに残す（一覧の右クリックで除外したときなど）。</summary>
    Settled,

    /// <summary>確かめていないIDで確定のキーを押した。次にするのは欄での確認なので、どこにいても欄へ移す。</summary>
    NeedsPreview,
}
