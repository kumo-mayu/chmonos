namespace Chmonos.Core.Models;

/// <summary>
/// 商品ページと編集画面の「手元のファイル」の表示順（設定の <c>fileOrder</c>・ユーザ指示 2026-11-11）。
/// 並べ方そのものは <see cref="Services.LocalFileOrder"/>。どの順でも、外したファイルは後ろに置く。
/// </summary>
public enum FileOrder
{
    /// <summary>記録の順（ファイルを記録に足した順。前からの並び）。既定。</summary>
    Added,

    /// <summary>ファイル名の順。数字も1字ずつ比べる（file10 が file2 より前）。</summary>
    Name,

    /// <summary>ファイル名の順で、名前の中の数を数として比べる（v1.9 → v1.10、Ver2.9.0 → Ver2.19.0）。</summary>
    NameWithVersion,
}
