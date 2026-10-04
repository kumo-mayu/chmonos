namespace Chmonos.App.ViewModels;

/// <summary>
/// 欄やボタンのすぐ下に出す知らせ1行の中身（<c>Controls/AreaNoticeLine</c> が出す）。
/// 画面の上の1行にまとめて出していた結果を、押した所の近くへ移すための入れ物
/// （2026-10-03 のユーザの方針。docs/feedback/notice-placement-2026-10-03.md）。
/// 済んだこと（<see cref="Show"/>）と、打ち直しや別の操作が要ること（<see cref="Warn"/>）を色で分ける。
/// </summary>
public sealed class AreaNotice : ViewModelBase
{
    private string _text = string.Empty;
    private bool _isWarning;

    public string Text
    {
        get => _text;
        private set => SetField(ref _text, value);
    }

    public bool IsWarning
    {
        get => _isWarning;
        private set => SetField(ref _isWarning, value);
    }

    /// <summary>済んだことを出す。</summary>
    public void Show(string text)
    {
        IsWarning = false;
        Text = text;
    }

    /// <summary>うまくいかなかったこと・打ち直しが要ることを出す。</summary>
    public void Warn(string text)
    {
        IsWarning = true;
        Text = text;
    }

    public void Clear()
    {
        Text = string.Empty;
        IsWarning = false;
    }
}
