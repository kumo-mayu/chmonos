namespace Chmonos.App.ViewModels;

/// <summary>
/// まとめて操作するために選べる画面（検索・フォルダ・未確定）。
///
/// **Esc で選択を解除するため**の共通の入口（ユーザ指示 2026-09-20・M6）。
/// 前は解除がボタンだけで、しかも位置が画面ごとに違った（検索は下の帯、未確定は一覧の上）。
/// </summary>
internal interface ISelectionScreen
{
    /// <summary>1件でも選んでいるか。Esc を横取りしてよいかの判断に使う（選んでいなければ他へ譲る）。</summary>
    bool HasSelection { get; }

    void ClearSelection();
}
