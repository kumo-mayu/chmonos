namespace Chmonos.App.ViewModels;

/// <summary>
/// 欄やボタンのすぐ下に出す知らせ1行の中身。画面ごとに別の型を持たず、これ1つを使う
/// （2026-10-03 のユーザの方針で、画面の上の1行に出していた結果を押した所の近くへ移したとき、
/// 担当ごとに同じ中身の型を作ってしまったのをまとめた。docs/feedback/notice-placement-2026-10-03.md）。
/// 見た目は App.xaml の <c>AreaNoticeText</c>（<c>Controls/AreaNoticeLine</c> も同じ）が、文と <see cref="IsWarning"/> を結んで出す。
/// 済んだこと（<see cref="Show"/>）と、打ち直しや別の操作が要ること（<see cref="Warn"/>）を、1つの行で色だけ切り替える。
/// 2つの TextBlock に分けると、1行ぶんの場所が2つ要るため
/// </summary>
public sealed class AreaNotice : ViewModelBase
{
    private string _text = string.Empty;
    private bool _isWarning;

    public string Text
    {
        get => _text;
        private set
        {
            if (SetField(ref _text, value))
            {
                OnPropertyChanged(nameof(HasText));
            }
        }
    }

    /// <summary>打ち直しや別の操作が要る知らせか（<c>FieldWarning</c> の色）。済んだことなら false</summary>
    public bool IsWarning
    {
        get => _isWarning;
        private set => SetField(ref _isWarning, value);
    }

    public bool HasText => _text.Length > 0;

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

    /// <summary>出すか注意かを、呼び手が持っている印で決めるとき（<see cref="Show"/>／<see cref="Warn"/> の分かれ目を呼び手に書かせない）。</summary>
    public void Set(string text, bool warning)
    {
        IsWarning = warning;
        Text = text;
    }

    public void Clear()
    {
        Text = string.Empty;
        IsWarning = false;
    }
}
