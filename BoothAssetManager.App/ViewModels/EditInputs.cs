using System.Collections.ObjectModel;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 編集画面で付けたuserTag 1件（トップ＋サブ）。
///
/// マスタ全部を並べて選ばせるのではなく、候補入力から選んだものだけをここに積む。
/// 並べる方式だと、分類が増えるほど画面が縦に伸びて使えなくなるため。
/// </summary>
public sealed class UserTagRow : ViewModelBase
{
    public required string Top { get; init; }

    /// <summary>このトップに付けたサブ。</summary>
    public ObservableCollection<string> Subs { get; } = [];

    /// <summary>マスタ側にあるサブのうち、まだ付けていないもの（候補に出す）。</summary>
    public ObservableCollection<string> SubCandidates { get; } = [];

    public RelayCommand? RemoveCommand { get; set; }

    public RelayCommand? AddSubCommand { get; set; }

    public RelayCommand? RemoveSubCommand { get; set; }

    public bool HasSubs => Subs.Count > 0;

    public void Raise() => OnPropertyChanged(nameof(HasSubs));
}

/// <summary>
/// 評価した属性1件。評価していない属性はそもそもこのリストに載らない。
/// 「未評価」はトグルのオフではなく、行が無いことで表す。
/// </summary>
public sealed class AttributeRow : ViewModelBase
{
    private int _value = 50;
    private bool _isSuggested;

    public required string Name { get; init; }

    public RelayCommand? RemoveCommand { get; set; }

    public int Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value))
            {
                OnPropertyChanged(nameof(ValueText));

                // 触られたので、もう「並べてあるだけ」ではない
                IsSuggested = false;
            }
        }
    }

    public string ValueText => $"{Value}%";

    /// <summary>
    /// 既定として並べてあるだけで、まだ評価していない行。
    ///
    /// **この状態のまま保存しても書き出さない。**全itemに同じ値の行が並ぶと、
    /// 付けていないのかそう評価したのかが区別できなくなる。
    /// 値を動かした時点で普通の行になる。
    /// </summary>
    public bool IsSuggested
    {
        get => _isSuggested;
        set
        {
            if (SetField(ref _isSuggested, value))
            {
                OnPropertyChanged(nameof(SuggestedNote));
            }
        }
    }

    /// <summary>並べてあるだけだと分かるようにする。黙って0%で保存されたと思われないため。</summary>
    public string SuggestedNote => _isSuggested ? "未評価（動かすと付きます）" : string.Empty;
}
