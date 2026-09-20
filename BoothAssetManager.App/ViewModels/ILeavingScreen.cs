namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 画面を離れるときの後始末を自分で持つ画面。
///
/// 後始末は主画面の差し替えの所に手書きで並んでいたため、**画面ごとに当たり外れが出ていた**
/// （裏の取得を止めているのはショップ一覧だけ、知らせの購読を外している画面は無い）。
/// 離れる画面の側に置けば、画面を足したときに書き忘れても、その画面の中だけで完結する。
/// </summary>
internal interface ILeavingScreen
{
    /// <summary>もうこの画面は出ない。止める物を止め、外す購読を外す。</summary>
    void OnLeaving();
}
