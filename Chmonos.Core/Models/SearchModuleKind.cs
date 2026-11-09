namespace Chmonos.Core.Models;

/// <summary>
/// 検索の絞り込みのモジュール（ユーザ案 2026-09-15・`docs/history/search-redesign.md`）。
/// 名前は ui-state.json と検索の履歴に書くので、変えると古い状態が読めなくなる（読めなかった物は黙って飛ばす）。
/// </summary>
public enum SearchModuleKind
{
    Category,
    BoothTag,
    Shop,
    WishList,
    Price,
    EndOfSale,
    PublishedAt,
    Adult,
    Owned,
    Gift,
    FreePaid,
    UserTag,
    Attribute,
    Avatar,
    Favorite,
    AcquiredAt,
    Hidden,
    Unedited,

    // 「対応アバターの確認」（AvatarUnconfirmed・2026-09-18）は、編集状況の項目の1つにした（ユーザ判断 2026-10-06）。
    // 種類ごと消したので、前に保存した状態のその条件は読めずに飛ばされる（公開前なので救済はしない）
    Modification,
    UnityProject,
    Path,
    Recent,

    /// <summary>壊れていて開けない zip を持つか（ユーザ判断 2026-09-30）。</summary>
    BrokenZip,

    /// <summary>記録の上では持っているが、置き場がどこにも無いファイルがあるか（ユーザ判断 2026-10-04。カードの印「見つかりません」と同じ数え方）。</summary>
    MissingFile,

    /// <summary>仮のIDで登録した「BOOTHに無い商品」か（ユーザ指示 2026-10-04 メモ31。判定は <see cref="ItemRecord.IsLocalOnly"/> と同じ）。</summary>
    NotOnBooth,

    /// <summary>要確認に未読の「商品の更新」の知らせがあるか（ユーザ指示 2026-10-02。カードの札「更新あり」と同じ数え方）。画面の名前は「更新通知あり」（2026-10-06）。</summary>
    Updated,

    /// <summary>
    /// 選んだ Unity プロジェクトの中に実際に入っているか（画面の名前は「Unityプロジェクト」。ユーザ判断 2026-11-01）。
    /// <see cref="UnityProject"/> は「紐付けた改変に記録した商品」で、中を見ない。保存した条件の意味を変えないよう、種類を分ける
    /// </summary>
    UnityProjectContents,

    /// <summary>
    /// 変換ツールのプロファイルの商品を、つなぎ先のアバター・共通素体で絞る（画面の名前は「プロファイル」。ユーザ判断 2026-11-09）。
    /// アバターを選ばないときは、プロファイルの商品すべて（扱わないにした物は除く）。docs/spec/conversion.md「検索」
    /// </summary>
    Profile,
}
