namespace Chmonos.Core.Models;

/// <summary>
/// VCC と ALCOM の両方があるとき、改変の画面から開く方（設定の <c>projectManager</c>・ユーザ指示 2026-09-29）。
/// 片方しか無ければ、この値によらずその方を開く。
/// </summary>
public enum ProjectManagerChoice
{
    /// <summary>
    /// <c>vcc://</c> のリンクを引き受けている方。ALCOM の設定1つで ALCOM に移る（alcom.md §3-1）ので、
    /// 普段使う方を利用者が既に選んでいることが多い。既定にした
    /// </summary>
    VccLink,

    Vcc,

    Alcom,
}
