namespace Chmonos.App.ViewModels;

/// <summary>選ぶ窓（<see cref="Views.ChoiceDialog"/>）に出す中身（試験が受けて答える）。<paramref name="Third"/> が null なら2つ＋キャンセル。</summary>
internal sealed record ChoiceRequest(string Title, string Question, string Detail, string First, string Second, string? Third = null);

/// <summary>
/// 選ぶ窓を出す口。窓は答える人がいないと止まるので、試験だけが窓の手前で受ける（<see cref="Intercept"/>）。
/// 通知の「zipで登録し直す」と商品ページの「ファイルを紐付ける」で、同じ問い（ほかの商品が持つファイル）を同じ文で聞く。
/// </summary>
internal static class ChoiceQuestion
{
    /// <summary>試験だけが差し替える。</summary>
    internal static Func<ChoiceRequest, Views.ChoiceDialogResult>? Intercept { get; set; }

    public static Views.ChoiceDialogResult Ask(ChoiceRequest request)
        => Intercept is { } intercept
            ? intercept(request)
            : request.Third is { } third
                ? Views.ChoiceDialog.Ask(request.Title, request.Question, request.Detail, request.First, request.Second, third)
                : Views.ChoiceDialog.Ask(request.Title, request.Question, request.Detail, request.First, request.Second);

    /// <summary>
    /// ファイルがほかの商品に付いているときに聞く中身（ユーザ判断 2026-10-05：断るだけにせず、開くか付け直すかを選ばせる）。
    /// 付け直すと向こうの手元の物が無くなるなら、押す前に言う。
    /// 3つ目に「両方に付ける」（向こうにも残す。ユーザ判断 2026-11-11 plan 27）。答えは First＝開く・Second＝付け直す・Third＝両方に付ける
    /// （<see cref="Answer"/>）。持ち主が2件以上なら「両方」ではないので「すべてに付ける」と言う。
    /// </summary>
    public static ChoiceRequest OwnedElsewhere(string title, string fileName, IReadOnlyList<Core.Services.ArchiveHolder> holders)
    {
        var first = holders[0];
        var who = holders.Count == 1 ? $"「{first.Name}」" : $"「{first.Name}」ほか {holders.Count - 1} 件の商品";
        var emptied = holders.Where(holder => holder.LosesLastFile).Select(holder => $"「{holder.Name}」").ToList();
        var emptiedText = emptied.Count == 0 ? string.Empty : $"{string.Join("・", emptied)}にはファイルが残りません。\n";
        var share = holders.Count == 1 ? "両方に付ける" : "すべてに付ける";

        return new ChoiceRequest(
            title,
            $"「{fileName}」は{who}に登録されています。",
            $"「その商品を開く」\n「{first.Name}」の商品ページを開きます。何も変えません。\n\n"
            + "「この商品に付け直す」\n"
            + $"{who}から外して、この商品に登録します。\n"
            + emptiedText
            + "戻すときは、この商品から外してから、元の商品で「この商品に戻す」を押します。\n\n"
            + $"「{share}」\n"
            + $"{who}にも残したまま、この商品にも登録します。\n"
            + "あとでこの商品から外せます。",
            "その商品を開く",
            "この商品に付け直す",
            share);
    }

    /// <summary>
    /// <see cref="OwnedElsewhere"/> の答えを、命令に渡す値にする。開く・キャンセルは null（呼ぶ側が扱う）。
    /// </summary>
    public static Core.Services.OtherHolders? Answer(Views.ChoiceDialogResult choice) => choice switch
    {
        Views.ChoiceDialogResult.Second => Core.Services.OtherHolders.TakeOver,
        Views.ChoiceDialogResult.Third => Core.Services.OtherHolders.Share,
        _ => null,
    };
}
