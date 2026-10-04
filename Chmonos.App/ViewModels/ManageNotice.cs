namespace Chmonos.App.ViewModels;

/// <summary>
/// 管理の画面（タグ・属性）で、操作の結果を出す1行。出し先ごとに1つ持つ（欄の下・名前の近く・一覧の見出しの近く）。
/// 文と「打ち直しや別の操作が要るか」を1組にしたのは、済んだこと（ふつうの色）と失敗（警告の色）を同じ行で言い分けるため
/// </summary>
public sealed class ManageNotice : ViewModelBase
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

    public void Done(string text)
    {
        IsWarning = false;
        Text = text;
    }

    public void Warn(string text)
    {
        IsWarning = true;
        Text = text;
    }

    public void Clear() => Text = string.Empty;
}
