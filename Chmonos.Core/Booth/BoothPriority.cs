namespace Chmonos.Core.Booth;

/// <summary>
/// BOOTHへの取得の優先順位。**小さいほど先に通す。**
///
/// 取得は <see cref="BoothClient"/> のゲート1つで直列化されている。
/// 順番待ちが1本しかないので、順位もこの1本の並びで決まる。
///
/// 順位を分ける理由は1つだけで、**人を待たせないこと**。
/// 取り込みは待てるが、画面の前で結果を待っている人は待てない。
/// 100商品の取り込み中に「このIDで確認」を押すと、順位が無ければ5分待ちになる。
/// </summary>
public enum BoothPriority
{
    /// <summary>
    /// **いま画面が結果を待っている対象。**
    ///
    /// 人が押した操作（<see cref="User"/>）より更に先に通す。
    /// 押した操作は複数溜まりうるが、**人が見ている対象は常に1つ**——
    /// 未確定で確定を押すと次の1件へ自動で移るので、
    /// 前の件の後始末が終わるまで、目の前の候補検索が待たされていた。
    ///
    /// 使うのは「その画面が今まさに描くために要る取得」だけ。
    /// 何にでも付けると順位が1段増えただけになる。
    /// </summary>
    Foreground = -1,

    /// <summary>人が押した操作。「このIDで確認」「再取得」「IDを与えて確定」。</summary>
    User = 0,

    /// <summary>「この商品の画像取得を優先」で指名された画像。</summary>
    PinnedImage = 1,

    /// <summary>① 商品JSON・② 商品ページHTML。取り込みの本体。</summary>
    Metadata = 2,

    /// <summary>③ 対応アバターの検出。手元に無いアバターの種類を確かめる。</summary>
    Detection = 3,

    /// <summary>④ 1枚目の画像。</summary>
    Thumbnail = 4,

    /// <summary>⑤ 残りの画像。</summary>
    Gallery = 5,

    /// <summary>⑥ ショップのアイコン。</summary>
    ShopIcon = 6,

    /// <summary>⑦ 期限の来た商品の再取得。急ぐ理由が無い唯一の段。</summary>
    Background = 7,
}
