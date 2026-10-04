namespace Chmonos.App.ViewModels;

/// <summary>
/// 欄やボタンのすぐ下に出す知らせの1行分（<c>FieldNotice</c> の行。2026-10-03 の方針：docs/feedback/notice-placement-2026-10-03.md）。
/// 文と、打ち直しが要る注意か済んだ知らせかの2つだけを持つ。出し先ごとに1つ作り、別の欄を触ったら呼び手が <see cref="Clear"/> する。
/// 注意と済んだ知らせを2つの TextBlock に分けると、1行ぶんの場所が2つ要る。1つの行で色だけ切り替えるためにこの形にした
/// </summary>
public sealed class NoticeSlot : ViewModelBase
{
    private string _text = string.Empty;
    private bool _isWarning;

    public string Text
    {
        get => _text;
        private set => SetField(ref _text, value);
    }

    /// <summary>打ち直しや別の操作が要る知らせか（<c>FieldWarning</c> の色）。済んだことなら false</summary>
    public bool IsWarning
    {
        get => _isWarning;
        private set => SetField(ref _isWarning, value);
    }

    public void Notice(string text)
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
