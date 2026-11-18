namespace Chmonos.App.ViewModels;

/// <summary>
/// 取り込み元の履歴の一覧を開いているか。取り込み画面と設定の画面の同じ一覧で1つの値を使う
/// （ユーザ判断 2026-11-18：開閉は設定の「取り込み元の履歴」と同じ値を共有する。既定は閉じる）。
/// 履歴は取り込むたびに増え、全部を縦に並べると膨大になる（ユーザ判断 2026-09-28）ので既定は閉じる。
/// 起動の間だけ覚え、設定には書かない（前から設定の画面の一覧もそうだった）。
/// 画面は同時に1つしか出ないので、知らせの仕組みは持たない。取り込み画面は出し直すとき（NoteShown）に読み直す
/// </summary>
public static class ImportFolderHistoryFold
{
    public static bool IsExpanded { get; set; }
}
