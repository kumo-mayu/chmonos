namespace Chmonos.Core.Models;

/// <summary>
/// 画面に出す文字の作り方を1箇所に集めたもの。
///
/// **同じ概念が二箇所にあると、片方だけ直して食い違う。**実際に起きている：
/// 容量の整形は7つのViewModelに写され、購入の種類は
/// 「自分用」と「購入」に割れ、名前・ショップ・分類の決め方は
/// 商品ページと編集画面で別々に書かれて中身がずれた。
///
/// ここに集めると、**二箇所が食い違えない形**になり、Coreのテスト網にも入る。
/// ViewModelにはテストが無いので、この差は大きい。
/// </summary>
public static class DisplayText
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>容量。1024で繰り上げ、小数は1桁まで（「188.1 MB」）。</summary>
    public static string Size(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < SizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {SizeUnits[unit]}";
    }

    /// <summary>
    /// 一時展開の間、下の帯に出す1行（ユーザ判断 2026-09-30）。
    ///
    /// 進み具合は件数ではなく大きさで出す——配布物は数GBの1ファイル（PSD・動画）が入っていることがあり、
    /// 件数だとその間ずっと止まって見える。合計が分からないうち（zip を開く前・空のファイルだけの zip）は数字を付けない。
    /// 別の zip を続けて押すと並んで走るので、1つの帯に数と合計でまとめる（帯を足すと画面の下が何段も積み上がる）。
    /// </summary>
    /// <param name="count">展開している zip の数。</param>
    /// <param name="stopping">中止を押した後か。片付けが済むまでの間に出す。</param>
    public static string UnpackingLine(int count, long doneBytes, long totalBytes, bool stopping)
    {
        if (stopping)
        {
            return "展開を中止しています…";
        }

        var head = count > 1 ? $"{count} 件のzipを展開しています…" : "展開しています…";
        return totalBytes > 0 ? $"{head} {Size(doneBytes)} / {Size(totalBytes)}" : head;
    }

    /// <summary>
    /// Unity へ送る前に zip から取り出している間、送る帯に出す1行（ユーザ判断 2026-09-30）。
    ///
    /// 数と大きさの書き方は一時展開の帯（<see cref="UnpackingLine"/>）と同じ。語は「取り出す」——
    /// 失敗の文が「zipから取り出せませんでした」で、「展開」はフォルダに広げてエクスプローラで開く方を指している。
    /// </summary>
    /// <param name="index">何件目か（1から）。</param>
    public static string UnityExtractingLine(int index, int total, string name, long doneBytes, long totalBytes)
        => $"{index}/{total}：「{name}」をzipから取り出しています… {Size(doneBytes)} / {Size(totalBytes)}";

    /// <summary>
    /// 取り出しの進み具合を出し始めるまでの時間。**1秒**——これより短い待ちは、文を替えなくても待たされたと感じにくい。
    /// 手元の速いディスクでは 2.3GB の書き出しが約1秒だった（大容量の確かめ 2026-09-30）ので、
    /// 普通の unitypackage（数十〜数百MB）では文は替わらず、遅いディスクの数GBでだけ出る
    /// </summary>
    public static readonly TimeSpan UnityExtractingDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 送る帯の文を、取り出しの進み具合（<see cref="UnityExtractingLine"/>）に替えるか。
    ///
    /// **すぐ終わる取り出しで文をちらつかせない。**取り出しは小さい物なら一瞬で、その間だけ文を替えると
    /// 「送っています…」→「取り出しています…」→「送っています…」と読めない速さで入れ替わる。
    /// 始まって <see cref="UnityExtractingDelay"/> 経ち、そこまでの速さで見て残りも同じだけ掛かりそうなときだけ替える
    /// （経った時間だけで決めると、ちょうど過ぎた所で終わる取り出しが一瞬だけ出る）。
    /// 一度替えたら、取り出しが終わるまで出し続けるのは呼ぶ側の役目。
    /// </summary>
    /// <param name="elapsed">取り出しを始めてからの時間。</param>
    public static bool ShowsUnityExtracting(TimeSpan elapsed, long doneBytes, long totalBytes)
    {
        if (elapsed < UnityExtractingDelay || totalBytes <= 0 || doneBytes >= totalBytes)
        {
            return false;
        }

        // まだ1バイトも書けていないのに時間だけ経っているなら、残りは見積もれないほど長い
        if (doneBytes <= 0)
        {
            return true;
        }

        var remaining = elapsed.TotalSeconds * (totalBytes - doneBytes) / doneBytes;
        return remaining >= UnityExtractingDelay.TotalSeconds;
    }

    /// <summary>
    /// 購入の種類を**名詞として**出す（「自分用」）。
    /// JSONに書いてある語と同じにする——人がJSONを開いて読む前提なので、
    /// 画面と保存で語が違うと同じものだと分からなくなる。
    /// </summary>
    public static string PurchaseKindLabel(PurchaseKind kind) => kind switch
    {
        PurchaseKind.Received => "貰った",
        PurchaseKind.Given => "贈った",
        _ => "自分用",
    };

    /// <summary>
    /// 購入の種類を**文の中に入る形**で出す（「¥480 で買った」）。
    ///
    /// 「自分用」は文に入らないので動詞にする。3つとも過去形で揃える——
    /// 「¥480 で購入」と「¥0 で貰った」が混ざると、同じ欄なのに語調が変わる。
    /// </summary>
    public static string PurchaseKindVerb(PurchaseKind kind) => kind switch
    {
        PurchaseKind.Received => "貰った",
        PurchaseKind.Given => "贈った",
        _ => "買った",
    };

    /// <summary>
    /// 種類の行の名前。**null は「どの種類も指していない」購入。**
    /// BOOTHから取れない商品には種類が1件も無く、
    /// 種類ごとの販売終了でも指す先が消える。
    ///
    /// **番号をそのまま出さない。**以前は <c>variation {id}</c> を返していて、
    /// 英語の内部の言葉が画面に出ていた。番号は人が読んで意味が取れないので、
    /// 名前が引けないことだけを言う（BOOTH側から消えた種類がこれになる）。
    /// </summary>
    public static string VariationLabel(long? variationId)
        => variationId is not null ? "名前の分からないバリエーション" : "バリエーションを選ばない購入";

    /// <summary>
    /// BOOTH が名前を持たせていないバリエーション（種類が1つだけの商品に多い）の呼び方（ユーザ判断 2026-09-29）。
    /// 「（名前のないバリエーション）」は、名前を付け忘れた物のようで分かりにくかった。
    /// 種類を指さない購入（<see cref="VariationLabel"/> の null）とも、BOOTH から消えて名前が引けない種類とも別物なので混ぜない
    /// </summary>
    public const string NoVariationName = "バリエーション選択なし";

    /// <summary>今あるバリエーションの行の名前。名前が無ければ <see cref="NoVariationName"/>。</summary>
    public static string VariationName(string? name)
        => string.IsNullOrWhiteSpace(name) ? NoVariationName : name;

    /// <summary>
    /// **ユーザが入れたものを優先する**という決まり。名前・ショップ・分類で共通。
    /// 空白だけの入力は「入れていない」として扱う。
    /// </summary>
    public static string? Prefer(string? userValue, string? observed)
        => string.IsNullOrWhiteSpace(userValue) ? observed : userValue.Trim();

    /// <summary>
    /// 画面に出す商品名。ユーザの入力 → BOOTHの名前 → 商品ID の順。
    ///
    /// 最後が商品IDなのは、名前の場所を空にしないため。
    /// 仮IDの商品で名前を消すと <c>local-3f9c1b7e</c> が出るが、
    /// それは「名前が無い」ことが見えている方が正しい。
    /// </summary>
    public static string ItemName(string? userValue, string? boothName, string itemId)
        => Prefer(userValue, boothName) is { Length: > 0 } name ? name : itemId;

    /// <summary>ショップ名。ユーザの入力 → BOOTHのショップ名。</summary>
    public static string ShopName(string? userValue, string? boothShopName)
        => Prefer(userValue, boothShopName) ?? string.Empty;

    /// <summary>
    /// 分類。親が分かれば「親 / 子」、分からなければ子だけ。
    /// ユーザには子の名前しか入れさせないので、親は呼ぶ側が表から引いて渡す。
    /// </summary>
    public static string CategoryText(string? childName, string? parentName)
    {
        if (string.IsNullOrWhiteSpace(childName))
        {
            return string.Empty;
        }

        var child = childName.Trim();
        return string.IsNullOrWhiteSpace(parentName) ? child : $"{parentName.Trim()} / {child}";
    }
}
