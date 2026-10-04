using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

namespace Chmonos.App.Services;

/// <summary>窓が受けた素の ← → を、商品ページの絵を送るのに使うか（<see cref="GalleryArrows.For"/>）。</summary>
internal enum GalleryArrow
{
    /// <summary>絵を送る。</summary>
    Gallery,

    /// <summary>止まっている部品に渡す（入力欄のカーソル・スライダーの値・並びの中を移る）。</summary>
    Yield,

    /// <summary>
    /// 受けたことにして何もしない。渡すと WPF の「矢印で近い部品へ移る」が働き、
    /// リンクから離れた列のボタンへ飛ぶ（ID のリンクで ← を押すと、ギャラリーの「次の画像」へ移った）
    /// </summary>
    Swallow,
}

/// <summary>
/// 商品ページと編集画面で、窓が素の ← → を受けたとき、止まっている所によって絵を送るかを決める（窓の受け口 <c>MainWindow.TryMoveGallery</c>）。
///
/// **Tab で止まるリンクの上では絵を送らない**（メモ27-③・ユーザ判断 2026-10-04「直す」）。ショップ名・ID のリンクに止まって ← → を押すと、
/// 離れた所のギャラリーの絵が変わり、止まっている所は動かなかった。リンクの上の ← → には使い道が無いので、何もしない。
///
/// 説明の本文（読み取り専用の RichTextBox）とその中のリンクは、今まで通り絵を送る。本文は Tab で止まらず（押したときだけ入る）、
/// カーソルが見えないので、本文に渡しても ← → は見た目に何も起きず、本文を押した後に絵を送れなくなるだけになる
/// </summary>
internal static class GalleryArrows
{
    public static GalleryArrow For(object? focused)
    {
        // スライダー（編集画面の属性）の左右は値を動かすキー（点検 2026-09-23）。入力欄はカーソル、選ぶ欄は候補を選ぶキー
        if (focused is TextBox or ComboBox or Slider)
        {
            return GalleryArrow.Yield;
        }

        // 並び（対応アバターの札・ローカルファイルの行・ギャラリーの小さな絵など）の中の左右は、並びの中を移るキー（ユーザ判断 2026-10-01）
        if (Controls.ArrowGroup.OwnsArrows(focused as DependencyObject))
        {
            return GalleryArrow.Yield;
        }

        return focused is Hyperlink link && !IsInsideText(link) ? GalleryArrow.Swallow : GalleryArrow.Gallery;
    }

    /// <summary>リンクが説明の本文（RichTextBox）の中にあるか。文の一部は画面の木に載らないので、論理の木を辿る。</summary>
    private static bool IsInsideText(Hyperlink link)
    {
        for (DependencyObject? node = link; node is not null; node = LogicalTreeHelper.GetParent(node))
        {
            if (node is TextBoxBase)
            {
                return true;
            }
        }

        return false;
    }
}
