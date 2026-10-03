namespace Chmonos.App.Controls;

/// <summary>
/// 本文の上に付ける印（<see cref="SelectableText.MarkedLinesProperty"/>）。足した行は本文の行の番号で、
/// 消えた行は本文に無いので、どの行の前へ差し込むかで持つ（メモ17：消えた行も元の位置に帯で並べる）
/// </summary>
public interface ILineMarks
{
    /// <summary>本文を改行で分けたときの、足された行の番号。</summary>
    IReadOnlySet<int> AddedLines { get; }

    /// <summary>差し込む消えた行。<see cref="InsertedLine.Before"/> の小さい順、同じ所は並べる順。</summary>
    IReadOnlyList<InsertedLine> RemovedLines { get; }
}

/// <summary>本文の <paramref name="Before"/> 番目の行の前に差し込む1行（本文の行の数なら末尾）。</summary>
public sealed record InsertedLine(int Before, string Text);
