namespace Chmonos.App.ViewModels;

/// <summary>
/// 押さずに残る欄（メモ・名前）を持つ画面。**0.8 秒待ってから書く**ので、
/// 打ち終えてすぐ閉じる・画面を移ると、その待ちごと捨てられる。
///
/// 閉じる前と離れる前に <see cref="FlushPendingWritesAsync"/> を呼ぶ。
/// 2026-09-20 までは商品ページ1か所にしか配線されておらず、
/// ショップ・改変・分類の管理・属性の管理・アバターのメモだけが落ちていた。
/// </summary>
internal interface IPendingWrites
{
    /// <summary>待っている自動保存を今書く。</summary>
    /// <returns>書き終わりまでの待ち。待っている物が無ければ済んだ状態で返る。</returns>
    Task FlushPendingWritesAsync();
}
