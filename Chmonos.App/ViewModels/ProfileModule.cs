using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 検索の条件「プロファイル」（ユーザ判断 2026-11-09・docs/spec/conversion.md「検索」の案B）。
/// 「変換元」と「変換先」の2つの欄を持つ。A 用の衣装を B に着せるには A の逆方向と B の順方向が要るので、変換元は逆方向、変換先は順方向で当てる。
/// 両方に入れたら、どちらの向きが手元に無いかを1行で言う。照らし方は Core の <see cref="ProfileCondition"/>（保存した状態からも同じ物を作る）。
/// 2つの欄の候補・札は、対応アバターと同じ部品（<see cref="ListModule"/>）を中に持って使い回す。
/// </summary>
public sealed class ProfileModule : SearchModule
{
    private bool _viaBase = true;
    private bool _includeUnconfirmed = true;
    private bool _showCounterpart;
    private string _statusText = string.Empty;

    public ProfileModule(ListModule source, ListModule destination)
        : base(SearchModuleKind.Profile)
    {
        Source = source;
        Destination = destination;

        // 欄の札を足す・外すと、この条件の値が変わる
        Source.Changed += OnSideChanged;
        Destination.Changed += OnSideChanged;
    }

    /// <summary>変換元の欄（逆方向で当てる）。</summary>
    public ListModule Source { get; }

    /// <summary>変換先の欄（順方向で当てる）。</summary>
    public ListModule Destination { get; }

    /// <summary>「素体経由を含める」（既定は入・ユーザ判断 2026-11-09）。</summary>
    public bool ViaBase
    {
        get => _viaBase;
        set
        {
            if (SetField(ref _viaBase, value))
            {
                NotifyChanged();
            }
        }
    }

    /// <summary>「確認待ちも含める」（既定は入）。切ると、人が確認したつなぎ先だけで当てる。</summary>
    public bool IncludeUnconfirmed
    {
        get => _includeUnconfirmed;
        set
        {
            if (SetField(ref _includeUnconfirmed, value))
            {
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// 「もう一方の候補も含める」（既定は切・ユーザ判断 2026-11-09）。片方の欄だけ入れたときに、空の側を「持っている物全部」として
    /// 反対の向きのプロファイルも出す（変換先だけなら元として使える物、変換元だけなら先として使える物）。
    /// </summary>
    public bool ShowCounterpart
    {
        get => _showCounterpart;
        set
        {
            if (SetField(ref _showCounterpart, value))
            {
                NotifyChanged();
            }
        }
    }

    /// <summary>「もう一方の候補も含める」が効くのは、片方の欄だけ入れたとき。それ以外は押せなくする（場所は残して縦に揺らさない）。</summary>
    public bool CounterpartApplies => (Source.Chips.Count == 0) != (Destination.Chips.Count == 0);

    /// <summary>両方の欄に入れたときの「そろっているか」の1行。手元の全部のプロファイルで見る（絞り込みの結果ではない）。</summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetField(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => _statusText.Length > 0;

    private IReadOnlyList<string> SourceKeys => Source.Chips.Select(chip => chip.Key).ToList();

    private IReadOnlyList<string> DestinationKeys => Destination.Chips.Select(chip => chip.Key).ToList();

    private void OnSideChanged()
    {
        OnPropertyChanged(nameof(CounterpartApplies));
        NotifyChanged();
    }

    /// <summary>足しただけでも絞る（何も選ばないときは、プロファイルの商品すべて）。</summary>
    protected override bool HasCondition => true;

    protected override ItemCondition BuildCondition()
        => new ProfileCondition(SourceKeys, DestinationKeys, _viaBase, _includeUnconfirmed, _showCounterpart);

    /// <summary>
    /// 札の数（その札で当たるプロファイルの数。欄の向きで数える）と、そろっているかの1行を直す。
    /// そろっているかは絞り込みの結果ではなく手元の全部で見る：ほかの条件で隠れていても、持っていれば変換はできる
    /// </summary>
    public override void RefreshCounts(IReadOnlyList<ItemRecord> items, SearchModuleContext context)
    {
        var condition = (ProfileCondition)BuildCondition();
        foreach (var chip in Source.Chips)
        {
            chip.Count = items.Count(item => condition.Has([item], chip.Key, reverse: true, context));
        }

        foreach (var chip in Destination.Chips)
        {
            chip.Count = items.Count(item => condition.Has([item], chip.Key, reverse: false, context));
        }

        StatusText = Readiness(condition, context.Items, context);
    }

    private string Readiness(ProfileCondition condition, IReadOnlyList<ItemRecord> all, SearchModuleContext context)
    {
        // 両方の欄に入れたときだけ言う。同じ物を両方に入れた（アバターの管理から「このアバターのプロファイル」を開いた）ときは、変換の話ではないので言わない
        if (Source.Chips.Count == 0 || Destination.Chips.Count == 0 || SourceKeys.ToHashSet().SetEquals(DestinationKeys))
        {
            return string.Empty;
        }

        var missing = Source.Chips.Where(chip => !condition.Has(all, chip.Key, reverse: true, context))
            .Select(chip => $"変換元「{chip.Text}」の逆方向")
            .Concat(Destination.Chips.Where(chip => !condition.Has(all, chip.Key, reverse: false, context))
                .Select(chip => $"変換先「{chip.Text}」の順方向"))
            .ToList();
        return missing.Count == 0
            ? "変換に要るプロファイルはそろっています。"
            : string.Join("・", missing) + "のプロファイルがありません。";
    }

    protected override string SummaryBody
    {
        get
        {
            if (Source.Chips.Count == 0 && Destination.Chips.Count == 0)
            {
                return "プロファイルの商品すべて";
            }

            var parts = new List<string>();
            if (Source.Chips.Count > 0)
            {
                parts.Add("変換元 " + JoinValues(Source.Chips.Select(chip => chip.Text), all: false));
            }

            if (Destination.Chips.Count > 0)
            {
                parts.Add("変換先 " + JoinValues(Destination.Chips.Select(chip => chip.Text), all: false));
            }

            return string.Join("・", parts)
                + (_viaBase ? string.Empty : "（素体経由を除く）")
                + (_includeUnconfirmed ? string.Empty : "（確認したものだけ）")
                + (_showCounterpart && CounterpartApplies ? "（もう一方の候補も含める）" : string.Empty);
        }
    }

    public override void Clear()
    {
        Source.Clear();
        Destination.Clear();
        _viaBase = true;
        _includeUnconfirmed = true;
        _showCounterpart = false;
        OnPropertyChanged(nameof(ViaBase));
        OnPropertyChanged(nameof(IncludeUnconfirmed));
        OnPropertyChanged(nameof(ShowCounterpart));
        OnPropertyChanged(nameof(CounterpartApplies));
        NotifyChanged();
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with
        {
            Items = [.. SourceKeys.Select(key => SearchModuleConditions.SourceKey + key), .. DestinationKeys.Select(key => SearchModuleConditions.DestinationKey + key)],
            Flag = _viaBase,
            Toggles = new Dictionary<string, bool>
            {
                [SearchModuleConditions.IncludeUnconfirmedToggle] = _includeUnconfirmed,
                [SearchModuleConditions.CounterpartToggle] = _showCounterpart,
            },
        };

    protected override void Read(SearchModuleState state)
    {
        Source.Chips.Clear();
        Destination.Chips.Clear();
        foreach (var key in state.Items)
        {
            if (key.StartsWith(SearchModuleConditions.SourceKey, StringComparison.Ordinal))
            {
                Source.AddKey(key[SearchModuleConditions.SourceKey.Length..], notify: false);
            }
            else if (key.StartsWith(SearchModuleConditions.DestinationKey, StringComparison.Ordinal))
            {
                Destination.AddKey(key[SearchModuleConditions.DestinationKey.Length..], notify: false);
            }
        }

        _viaBase = state.Flag;
        _includeUnconfirmed = state.Toggles is not { } toggles
            || !toggles.TryGetValue(SearchModuleConditions.IncludeUnconfirmedToggle, out var unconfirmed)
            || unconfirmed;
        _showCounterpart = state.Toggles is { } counterpartToggles
            && counterpartToggles.TryGetValue(SearchModuleConditions.CounterpartToggle, out var counterpart)
            && counterpart;
        OnPropertyChanged(nameof(ViaBase));
        OnPropertyChanged(nameof(IncludeUnconfirmed));
        OnPropertyChanged(nameof(ShowCounterpart));
        OnPropertyChanged(nameof(CounterpartApplies));
    }

    /// <summary>アバターの管理・アバターの商品ページから開くとき：その欄に札を1つ入れる（絞り直しは呼び手がまとめて）。</summary>
    internal void AddQuietly(bool toSource, string key, string text)
        => (toSource ? Source : Destination).AddKey(key, notify: false, text: text);
}
