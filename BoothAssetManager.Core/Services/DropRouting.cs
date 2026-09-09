namespace BoothAssetManager.Core.Services;

/// <summary>落ちてきた／貼られたものに対して何をするか。</summary>
public enum DropAction
{
    /// <summary>受け取る理由が無い。何もしない。</summary>
    Ignore,

    /// <summary>取り込みの対象に積む。**実行は押してから。**</summary>
    Import,

    /// <summary>その商品を開く。手元にあるので通信は要らない。</summary>
    OpenItem,

    /// <summary>手元に無い商品。取得して登録するかを尋ねる。</summary>
    OfferToRegister,

    /// <summary>ショップのURLだった。そのショップの画面を開く（手持ちが見える方）。</summary>
    OpenShop,
}

public readonly record struct DropDecision(DropAction Action, string? ItemId, string? Shop = null);

/// <summary>
/// ウィンドウに落とされた／貼られたものの行き先を決める。
///
/// **落ちてくるものは2種類しかない**（ファイルかBOOTHのURL）ので、規則も短い。
/// 画面ごとに受けると同じものを落としたのに結果が変わるので、規則は1つに保つ。
///
/// 判断だけをここに置いてあるのは、画面を立ち上げずに確かめられるようにするため。
/// 実際の動き（画面を切り替える・尋ねる）は呼ぶ側が持つ。
/// </summary>
public static class DropRouting
{
    /// <param name="isKnown">その商品IDを既にライブラリが持っているか。</param>
    public static DropDecision Decide(
        IReadOnlyList<string>? paths,
        string? text,
        Func<string, bool> isKnown)
    {
        // ファイルが先。URLとファイルが同時に来ることは無いが、来たならファイルを採る
        // （ファイルは実体で、URLは参照。実体の方が意図がはっきりしている）
        if (paths is { Count: > 0 })
        {
            return new DropDecision(DropAction.Import, null);
        }

        if (BoothItemId.Parse(text) is { } itemId)
        {
            return isKnown(itemId)
                ? new DropDecision(DropAction.OpenItem, itemId)
                : new DropDecision(DropAction.OfferToRegister, itemId);
        }

        // 商品が見つからなければショップを見る。ショップの画面があるので、
        // 「そのショップの手持ち」へ送れる（外のBOOTHへ飛ばすより役に立つ）
        if (BoothItemId.ParseShop(text) is { } shop)
        {
            return new DropDecision(DropAction.OpenShop, null, shop);
        }

        return new DropDecision(DropAction.Ignore, null);
    }
}
