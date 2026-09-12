using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 札の並び（BOOTHのタグ・対応アバター）を、**作った札を捨てずに持ち続ける**形で並べる
/// （ユーザ判断 2026-09-12：数MBのメモリは受け入れて、2回目以降に全部並べる速さを取る）。
///
/// 札は1枚ごとに画面の部品を作るので、数百並べると体感できるほど掛かる（244体で約550ms）。
/// 以前は並べる一覧を丸ごと差し替えていたので、「残り n を表示」「最初の 40 だけにする」を
/// 押すたびに全部作り直していた（全部並べるのに約800ms、戻すのに約160ms・毎回）。ここでは一覧を差し替えず、
/// ・最初は先頭の一部だけ作る
/// ・初めて全部並べるときに、残りを末尾へ足す（作るのはこの1回だけ）
/// ・戻すときは隠すだけ。2回目以降の「残り n を表示」は隠したのを出すだけ
/// ・欄を畳んでも作った札は捨てない（畳んでいる間に新しく作ることもしない）
/// ・作り直すのは中身が変わったとき（別の商品・対応アバターを消した／足した）だけ
/// </summary>
public sealed class ChipStrip<TSource>
{
    private readonly Func<TSource, ChipTile> _make;
    private readonly ChipToggle _toggle;
    private readonly IReadOnlyList<object> _tail;
    private readonly List<ChipTile> _chips = [];
    private IReadOnlyList<TSource> _source = [];
    private Func<TSource, bool>? _match;
    private bool _loaded;

    /// <param name="make">札を1枚作る。</param>
    /// <param name="unit">数える単位（「件」「体」）。</param>
    /// <param name="noun">切り替えの札のツールチップで呼ぶ名前（「タグ」「対応アバター」）。</param>
    /// <param name="tail">札の後ろに常に置くもの（対応アバターの「＋ 追加」）。</param>
    public ChipStrip(Func<TSource, ChipTile> make, string unit, string noun, params object[] tail)
    {
        _make = make;
        _toggle = new ChipToggle(unit, noun, () =>
        {
            if (ShowsAll)
            {
                ShowFewer();
            }
            else
            {
                ShowAll();
            }
        });
        _tail = tail;
    }

    /// <summary>画面に並べるもの。**差し替えない**——差し替えると画面が札を全部作り直す。</summary>
    public ObservableCollection<object> Tiles { get; } = [];

    /// <summary>「残り n を表示」で全部並べているか。</summary>
    public bool ShowsAll { get; private set; }

    /// <summary>並べる中身を入れ替える。作った札はここでだけ捨てる。</summary>
    public void Reset(IReadOnlyList<TSource> source, bool expanded, bool showAll)
    {
        _source = source;
        ShowsAll = showAll;
        _chips.Clear();
        Tiles.Clear();
        _loaded = false;

        // 畳んでいる間は作らない。開いたときに作る
        if (expanded)
        {
            Load();
        }
    }

    /// <summary>欄を開いた・畳んだ。畳んでも作った札は捨てない（次に開くのを速くするため）。</summary>
    public void SetExpanded(bool expanded)
    {
        if (expanded && !_loaded)
        {
            Load();
        }
    }

    public void ShowAll()
    {
        ShowsAll = true;
        Refresh();
    }

    public void ShowFewer()
    {
        ShowsAll = false;
        Refresh();
    }

    /// <summary>
    /// 名前で絞る。絞っている間は、探しているのだから一致したものを全部並べる。
    /// 一致しない札も捨てずに隠すだけなので、絞り込みを打ち替えても作り直さない
    /// </summary>
    public void Filter(Func<TSource, bool>? match)
    {
        _match = match;
        Refresh();
    }

    private void Load()
    {
        _loaded = true;
        Tiles.Add(_toggle);
        foreach (var tile in _tail)
        {
            Tiles.Add(tile);
        }

        Refresh();
    }

    private void Refresh()
    {
        if (!_loaded)
        {
            return;
        }

        var isLong = _source.Count > ChipLists.ShowAllUpTo;
        var limited = _match is null && !ShowsAll && isLong;
        var needed = limited ? ChipLists.PreviewCount : _source.Count;

        // 足りない分だけ作って、切り替えの札と「＋ 追加」の前に差し込む
        for (var i = _chips.Count; i < needed; i++)
        {
            var chip = _make(_source[i]);
            _chips.Add(chip);
            Tiles.Insert(i, chip);
        }

        for (var i = 0; i < _chips.Count; i++)
        {
            _chips[i].IsShown = _match is not null
                ? _match(_source[i])
                : !limited || i < ChipLists.PreviewCount;
        }

        // 絞っている間は全部並べる決まりなので、切り替えの札は出さない
        _toggle.IsShown = _match is null && isLong;
        _toggle.Describe(ShowsAll, _source.Count - ChipLists.PreviewCount);
    }
}

/// <summary>隠すだけで捨てない札（<see cref="ChipStrip{TSource}"/>）。</summary>
public abstract class ChipTile : INotifyPropertyChanged
{
    private bool _isShown = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>今並んでいるか。隠している札も画面の部品は持ったまま。</summary>
    public bool IsShown
    {
        get => _isShown;
        set
        {
            if (_isShown != value)
            {
                _isShown = value;
                OnPropertyChanged();
            }
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// 「残り n を表示」と「最初の 40 だけにする」を1枚で切り替える札（ユーザ指示：可逆にする）。
/// 2枚を差し替えると、その札の部品もまた作ることになるので、文字だけ変える
/// </summary>
public sealed class ChipToggle : ChipTile
{
    private readonly string _unit;
    private readonly string _noun;

    public ChipToggle(string unit, string noun, Action toggle)
    {
        _unit = unit;
        _noun = noun;
        Command = new RelayCommand(toggle);
    }

    public RelayCommand Command { get; }

    public string Text { get; private set; } = string.Empty;

    public string Hint { get; private set; } = string.Empty;

    public void Describe(bool showsAll, int rest)
    {
        var text = showsAll
            ? $"最初の {ChipLists.PreviewCount} {_unit}だけにする"
            : $"残り {rest} {_unit}を表示";
        if (text != Text)
        {
            Text = text;
            Hint = showsAll
                ? "並べるのを最初の一部に戻します。"
                : $"残りの{_noun}も並べます。多い商品は、開くのを速くするため最初は一部だけ並べています。";
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(Hint));
        }
    }
}

/// <summary>BOOTHのタグの札1枚。</summary>
public sealed class TagTile : ChipTile
{
    public required string Text { get; init; }
}

/// <summary>対応アバターの札の並びの末尾に置く「＋ 追加」（アバターではない）。</summary>
public sealed class AvatarAddTile
{
    public static AvatarAddTile Instance { get; } = new();

    private AvatarAddTile()
    {
    }
}

/// <summary>
/// 札を並べる数の決め事（ユーザ指示 2026-09-12：タグや対応アバターが多い商品を開くのが遅い）。
/// 欄は既定で開いたまま（ユーザ判断：畳んでおくべき項目ではない）にし、最初に並べる数の方を絞る。
/// </summary>
public static class ChipLists
{
    /// <summary>多い商品で最初に並べる数。</summary>
    public const int PreviewCount = 40;

    /// <summary>この数までは全部並べる。「残り 3 件」のような切り方をしない。</summary>
    public const int ShowAllUpTo = 50;

    /// <summary>BOOTHのタグの並び（商品ページと編集画面で同じ）。</summary>
    public static ChipStrip<string> TagStrip() => new(tag => new TagTile { Text = tag }, "件", "タグ");
}
