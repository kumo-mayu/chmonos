using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>三択などの選択肢1つ。件数は「選んだら何件になるか」。</summary>
public sealed class ChoiceOption : ViewModelBase
{
    private int _count = -1;

    public ChoiceOption(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }

    public int Count
    {
        get => _count;
        set
        {
            if (SetField(ref _count, value))
            {
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    public string Display => _count < 0 ? Label : $"{Label}（{_count}）";

    public override string ToString() => Display;
}

/// <summary>
/// 選ぶ条件の補助の切り替え1つ（見つからないファイルの「未所持も含める」・ギフトの「購入記録の無い商品も含める」）。
/// </summary>
/// <param name="Label">チェックの文。</param>
/// <param name="Default">足したときと「条件をクリア」の後の値。</param>
/// <param name="ChangedSummary">既定から変えたときに要約へ添える言い方。</param>
/// <param name="Keys">
/// 切り替えが効く選択肢。null ならどれでも効く。効かない選択肢を選んでいる間は隠す（押しても何も変わらない印を出さない）。
/// </param>
/// <param name="StateKey">状態に書く名前（<see cref="SearchModuleState.Toggles"/>）。使い回しの Flag には書かない。</param>
public sealed record ChoiceFlag(string Label, bool Default, string ChangedSummary, string StateKey, IReadOnlySet<string>? Keys = null);

/// <summary>
/// プルダウンで選ぶ条件（ユーザ案「三項」）。
///
/// 何も絞らない選択肢（「両方」）を持つ物は、その選択肢で条件を残したまま無効にできる（トグル拡張）。
/// 持たない物（ギフト）は、条件自体の切り替えで無効にする（純三項）。
/// 「両方」は並びの最後に置く（ユーザ判断 2026-10-06・メモ82。全部の条件で同じ位置）。先頭が足したときの既定。
/// </summary>
public sealed class ChoiceModule : SearchModule
{
    private readonly Func<ItemRecord, string, bool, bool> _matches;
    private readonly string? _neutralKey;
    private readonly ChoiceFlag? _flagSpec;
    private ChoiceOption _selected;
    private bool _flag;

    /// <param name="options">先頭が追加したときの既定（ユーザ案の def）。</param>
    /// <param name="neutralKey">何も絞らない選択肢の鍵。純三項は null。</param>
    /// <param name="matches">商品・選んだ鍵・補助の切り替え → 通すか。</param>
    public ChoiceModule(
        SearchModuleKind kind,
        IReadOnlyList<ChoiceOption> options,
        string? neutralKey,
        Func<ItemRecord, string, bool, bool> matches,
        ChoiceFlag? flag = null)
        : base(kind)
    {
        Options = options;
        _selected = options[0];
        _neutralKey = neutralKey;
        _matches = matches;
        _flagSpec = flag;
        _flag = flag?.Default ?? false;
    }

    public IReadOnlyList<ChoiceOption> Options { get; }

    public ChoiceOption Selected
    {
        get => _selected;
        set
        {
            if (value is not null && SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasFlag));
                NotifyChanged();
            }
        }
    }

    public string SelectedKey => _selected.Key;

    /// <summary>補助の切り替えの文。</summary>
    public string? FlagLabel => _flagSpec?.Label;

    /// <summary>補助の切り替えを出すか。効かない選択肢（見つからないファイルの「両方」など）を選んでいる間は隠す。</summary>
    public bool HasFlag => _flagSpec is not null && (_flagSpec.Keys is null || _flagSpec.Keys.Contains(_selected.Key));

    public bool Flag
    {
        get => _flag;
        set
        {
            if (SetField(ref _flag, value))
            {
                NotifyChanged();
            }
        }
    }

    protected override bool HasCondition => _selected.Key != _neutralKey;

    public override bool Matches(ItemRecord item, SearchModuleContext context) => _matches(item, _selected.Key, _flag);

    protected override string SummaryBody
        => _selected.Label + (HasFlag && _flag != _flagSpec!.Default ? $"・{_flagSpec.ChangedSummary}" : string.Empty);

    public override void Clear()
    {
        _flag = _flagSpec?.Default ?? false;
        OnPropertyChanged(nameof(Flag));
        if (_neutralKey is null)
        {
            // 純三項は「何も絞らない」選択肢を持たないので、条件ごと切る
            SetEnabledQuietly(false);
            return;
        }

        _selected = Options.First(option => option.Key == _neutralKey);
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasFlag));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    public override void RefreshCounts(IReadOnlyList<ItemRecord> items, SearchModuleContext context)
    {
        foreach (var option in Options)
        {
            option.Count = items.Count(item => _matches(item, option.Key, _flag));
        }
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with
        {
            Choice = _selected.Key,
            Flag = false,
            Toggles = _flagSpec is null ? null : new Dictionary<string, bool> { [_flagSpec.StateKey] = _flag },
        };

    protected override void Read(SearchModuleState state)
    {
        _selected = Options.FirstOrDefault(option => option.Key == state.Choice) ?? Options[0];
        // 名前で持つ切り替えだけを読む。欠けていれば既定（前の版の使い回しの Flag は読まない）
        _flag = _flagSpec is not null
            && (state.Toggles is { } toggles && toggles.TryGetValue(_flagSpec.StateKey, out var on) ? on : _flagSpec.Default);
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(Flag));
        OnPropertyChanged(nameof(HasFlag));
    }

    /// <summary>選ぶ（他の画面から条件を渡すとき）。通知だけ出し、絞り直しは呼ぶ側。</summary>
    public void Select(string key)
    {
        _selected = Options.FirstOrDefault(option => option.Key == key) ?? _selected;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasFlag));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }
}
