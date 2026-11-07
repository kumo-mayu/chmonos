using System.Collections.ObjectModel;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>バリエーションの向き先の種類。</summary>
public enum VariationTargetKind
{
    /// <summary>商品の対応アバター全部（全部入りのセットの行）。</summary>
    All,

    /// <summary>共通素体（名前）。その仲間全員に効く。</summary>
    Base,

    /// <summary>アバター（商品 ID）。</summary>
    Avatar,
}

/// <summary>向き先の候補1件（入力欄に出す字と、選んだときに積む物）。</summary>
public sealed record VariationTargetChoice(string Text, VariationTargetKind Kind, string Key);

/// <summary>
/// 名前から見つけた向き先の札1枚。確認済みの札に無い物を並べる。
/// 外していなければ商品ページの対応アバターの確認待ちと同じ形（頭に点・乗せると ✓ と ✕）、外した物はグレーの札（押すと戻す）。
/// </summary>
public sealed class VariationTargetCandidate
{
    public required VariationTargetChoice Choice { get; init; }

    public string Text => Choice.Text;

    /// <summary>まだ何も決めていない行で、この札がそのまま効いているか。</summary>
    public bool IsEffective { get; init; }

    /// <summary>人が「違う」と外したか。グレーで出し、効かせない。</summary>
    public bool IsRejected { get; init; }

    public bool IsPending => !IsRejected;

    public string Tooltip => IsRejected
        ? "外した対応です。押すと確認済みに戻します。"
        : IsEffective
        ? "名前から見つけました（未確認）。押すと確認済みにします。"
        : "名前から見つけました（未確認）。押すと追加します。";

    public required RelayCommand ConfirmCommand { get; init; }

    public required RelayCommand RejectCommand { get; init; }
}

/// <summary>積んだ向き先の札1枚。</summary>
public sealed class VariationTargetChip
{
    public required VariationTargetChoice Choice { get; init; }

    public string Text => Choice.Text;

    public required RelayCommand RemoveCommand { get; init; }
}

/// <summary>
/// 編集の画面の「購入したバリエーション」の1行の、向き先の欄（<c>docs/spec/conversion.md</c>「バリエーションとアバターの結び付き」）。
///
/// 人が積んだ札（確認済み）があればそれが向き先。検出した向き先（バリエーションの名前から）は、商品ページの対応アバターの確認待ちと同じ形の札で
/// 最初から並べる（頭に点・乗せると ✓ と ✕。ユーザ指摘 2026-11-07：前の「〇〇（名前から）」の字と「この内容で決める」が分かりにくかった）。
/// 何も決めていない間は、確認待ちの札がそのまま効く（設定で検出を使わないときは並べない）。
/// **一度決めた行（確認済みの札か、外した札がある）では、外した候補をグレーの札で残し、次に開いても候補に戻さない**
/// （ユーザ指摘 2026-11-07：確認してから外した候補が、また確認待ちで出ていた）。外したことは記録の <see cref="VariationTarget.RejectedAvatars"/> に持つ。
/// 候補は商品の対応アバター・その共通素体・「対応アバターすべて」だけ——対応に無いアバターを向き先にしても、見分けに効かない。
/// </summary>
public sealed class VariationTargetsInput : ViewModelBase
{
    private readonly IReadOnlyList<VariationTargetChoice> _choices;
    private readonly IReadOnlyList<VariationTargetChoice> _detected;
    private readonly List<VariationTargetChoice> _rejected;

    public VariationTargetsInput(
        IReadOnlyList<VariationTargetChoice> choices,
        IReadOnlyList<VariationTargetChoice> decided,
        IReadOnlyList<VariationTargetChoice> detected,
        IReadOnlyList<VariationTargetChoice>? rejected = null)
    {
        _choices = choices;
        _detected = detected;
        _rejected = [.. rejected ?? []];
        AddCommand = new RelayCommand(parameter => Add(parameter as string));

        foreach (var choice in decided)
        {
            Push(choice);
        }

        RefreshSuggestions();
    }

    public ObservableCollection<VariationTargetChip> Chips { get; } = [];

    /// <summary>名前から見つけた向き先のうち、確認済みの札に無い物（確認待ちと、外した物）。</summary>
    public ObservableCollection<VariationTargetCandidate> Candidates { get; } = [];

    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasChips => Chips.Count > 0;

    /// <summary>人が決めた行か（確認済みの札か、外した候補がある）。決めた行では、名前から見つけた札は効かない。</summary>
    private bool IsDecided => Chips.Count > 0 || _rejected.Count > 0;

    private bool _isPurchased;

    /// <summary>この行を買ったか。決めていない印は買った行だけに出す（買っていない行は持っている分の見分けに効かないので、騒がない）。</summary>
    public bool IsPurchased
    {
        get => _isPurchased;
        set
        {
            if (SetField(ref _isPurchased, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(IsShown));
            }
        }
    }

    /// <summary>買っていない行にも欄を見せるか（設定。既定は見せない・ユーザ指示 2026-11-07「画面がうるさいので購入品以外は出さなくても良い」）。</summary>
    public bool ShowsUnpurchased { get; init; }

    /// <summary>
    /// 欄を見せるか。見せないときも場所は残す（買った印を付けたときに欄が出て、下の行が縦にずれないように。ユーザ指示 2026-11-07）
    /// </summary>
    public bool IsShown => _isPurchased || ShowsUnpurchased;

    /// <summary>
    /// まだ決めていないことの印（ユーザ判断 2026-11-07・案A：ファイルのバリエーション分けの「未指定」と同じオレンジの札）。
    /// 確認済みの札が無く、確認待ちの札があれば「未確認」、それも無ければ「未選択」（外した札だけの行も、何も選んでいない）。買っていない行と、決めた行は null
    /// </summary>
    public string? StateText => !_isPurchased || Chips.Count > 0 ? null : Candidates.Any(candidate => candidate.IsPending) ? "未確認" : "未選択";

    public string StateTip => Candidates.Any(candidate => candidate.IsPending)
        ? "名前から見つけた対応を確認していません。札を押すと確認済みにします。"
        : "どのアバター向けかを決めていません。右の欄から追加します。";

    public RelayCommand AddCommand { get; }

    public void Add(string? text)
    {
        var choice = _choices.FirstOrDefault(candidate => string.Equals(candidate.Text, text?.Trim(), StringComparison.CurrentCulture));
        if (choice is null || Chips.Any(chip => chip.Choice == choice))
        {
            return;
        }

        _rejected.Remove(choice);
        Push(choice);
        Changed();
    }

    private void Confirm(VariationTargetChoice choice)
    {
        _rejected.Remove(choice);
        if (Chips.All(chip => chip.Choice != choice))
        {
            Push(choice);
        }

        Changed();
    }

    private void Reject(VariationTargetChoice choice)
    {
        if (!_rejected.Contains(choice))
        {
            _rejected.Add(choice);
        }

        Changed();
    }

    private void Push(VariationTargetChoice choice)
    {
        VariationTargetChip? chip = null;
        chip = new VariationTargetChip
        {
            Choice = choice,
            RemoveCommand = new RelayCommand(() =>
            {
                Chips.Remove(chip!);

                // 名前から見つけた物を外したら、外したと覚える（次に開いても確認待ちに戻さない）
                if (_detected.Contains(choice) && !_rejected.Contains(choice))
                {
                    _rejected.Add(choice);
                }

                Changed();
            }),
        };
        Chips.Add(chip);
    }

    private void Changed()
    {
        RefreshSuggestions();
        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateTip));
    }

    private void RefreshSuggestions()
    {
        // 何も決めていない行では、確認待ちの札がそのまま効いている。吹き出しで言い分ける
        Candidates.Clear();
        var effective = !IsDecided;
        foreach (var choice in _detected.Where(choice => Chips.All(chip => chip.Choice != choice)))
        {
            Candidates.Add(new VariationTargetCandidate
            {
                Choice = choice,
                IsEffective = effective,
                IsRejected = _rejected.Contains(choice),
                ConfirmCommand = new RelayCommand(() => Confirm(choice)),
                RejectCommand = new RelayCommand(() => Reject(choice)),
            });
        }

        Suggestions.Clear();
        foreach (var choice in _choices.Where(choice => Chips.All(chip => chip.Choice != choice)))
        {
            Suggestions.Add(choice.Text);
        }
    }

    /// <summary>人が決めた向き先の行。何も決めていなければ null（行を持たない＝検出に任せる）。</summary>
    public VariationTarget? ToTarget(long? variationId)
    {
        if (!IsDecided)
        {
            return null;
        }

        List<string>? RejectedOf(VariationTargetKind kind)
            => _rejected.Where(choice => choice.Kind == kind).Select(choice => choice.Key).ToList() is { Count: > 0 } keys ? keys : null;

        return new VariationTarget
        {
            VariationId = variationId,
            All = Chips.Any(chip => chip.Choice.Kind == VariationTargetKind.All),
            Avatars = [.. Chips.Where(chip => chip.Choice.Kind == VariationTargetKind.Avatar).Select(chip => chip.Choice.Key)],
            Bases = [.. Chips.Where(chip => chip.Choice.Kind == VariationTargetKind.Base).Select(chip => chip.Choice.Key)],
            RejectedAvatars = RejectedOf(VariationTargetKind.Avatar),
            RejectedBases = RejectedOf(VariationTargetKind.Base),
        };
    }
}
