using System.Collections.ObjectModel;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 検索の左の条件で積んだ札（チップ）の並びを、2行を超えたら畳む状態。札の並び（<c>FoldingChipList</c>）が1つずつ持つ。
///
/// 札が多いと条件の枠が縦に伸び、下の条件が見えなくなる。札のまま一覧できることも大切なので、表に変えずに2行で切り、
/// 末尾の「ほか n 件」で全部を広げる（ユーザ判断 2026-11-20「チップのままで、折り畳めるようにしよう。一覧できることも大切だ。」）。
///
/// 広げたかは条件ごとにアプリを閉じるまで覚える。画面の側に持つと、枠を畳んだ時・並べ替えた時に部品が作り直されて消える。
/// 保存した検索と状態の JSON には書かない（見せ方で、条件ではない）。
/// 隠した数と「全部を並べると2行を超えるか」は、並べた高さで決まるので、並べる部品（<see cref="Controls.ChipWrapPanel"/>）が書く
/// </summary>
public sealed class ChipFold : ViewModelBase
{
    private bool _isExpanded;
    private int _hiddenCount;
    private bool _overflows;
    private bool _revealRequested;
    private RelayCommand? _toggle;

    public ChipFold(ObservableCollection<ListChip> chips)
    {
        Chips = chips;
    }

    public ObservableCollection<ListChip> Chips { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ToggleText));
                OnPropertyChanged(nameof(ToggleName));
            }
        }
    }

    /// <summary>畳んで隠した札の数（並べる部品が書く）。</summary>
    public int HiddenCount
    {
        get => _hiddenCount;
        set
        {
            if (SetField(ref _hiddenCount, value))
            {
                OnPropertyChanged(nameof(ToggleText));
                OnPropertyChanged(nameof(ToggleName));
            }
        }
    }

    /// <summary>
    /// 全部を並べると2行を超えるか（並べる部品が書く）。広げた後も true のまま——隠した数だけで出し分けると、
    /// 広げた途端に0になり「折りたたむ」が消える
    /// </summary>
    public bool Overflows
    {
        get => _overflows;
        set => SetField(ref _overflows, value);
    }

    /// <summary>人が札を足した。次に並べた時に足した札が隠れるなら広げる（並べる部品が見て、済んだら false に戻す）。</summary>
    public bool RevealRequested
    {
        get => _revealRequested;
        set => SetField(ref _revealRequested, value);
    }

    public string ToggleText => TextOf(_isExpanded, _hiddenCount);

    /// <summary>読み上げの名前。「ほか 3 件」だけでは何が起きるか分からない。</summary>
    public string ToggleName => _isExpanded ? "選んだ値を折りたたむ" : $"選んだ値をほか {_hiddenCount} 件表示";

    public RelayCommand ToggleCommand => _toggle ??= new RelayCommand(() => IsExpanded = !IsExpanded);

    internal static string TextOf(bool expanded, int hidden) => expanded ? "折りたたむ" : $"ほか {hidden} 件";

    /// <summary>
    /// 人が足した（候補から選んだ）。畳んだまま足すと、足した札が「ほか n 件」の中に入って見えない。
    /// 状態を戻す道（保存した検索・前回の状態）では呼ばない——起動のたびに全部が広がる
    /// </summary>
    public void RevealAdded() => RevealRequested = true;

    /// <summary>条件を空にした。札が無くなるので畳んだ姿へ戻す。</summary>
    public void Reset()
    {
        IsExpanded = false;
        RevealRequested = false;
    }
}
