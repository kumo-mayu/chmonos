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

/// <summary>名前から見つけた、まだ確認していない向き先の札1枚（商品ページの対応アバターの確認待ちと同じ形）。</summary>
public sealed class VariationTargetCandidate
{
    public required VariationTargetChoice Choice { get; init; }

    public string Text => Choice.Text;

    /// <summary>確認済みの札が無く、この札がそのまま効いているか。</summary>
    public bool IsEffective { get; init; }

    public string Tooltip => IsEffective
        ? "名前から見つけました（未確認）。押すと確認済みにします。"
        : "名前から見つけました。押すと追加します。";

    public required RelayCommand ConfirmCommand { get; init; }
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
/// 最初から並べる（頭に点・乗せると ✓。ユーザ指摘 2026-11-07：前の「〇〇（名前から）」の字と「この内容で決める」が分かりにくく、ボタンが押せる見た目でなかった）。
/// 押すと確認済みの札になる。確認済みの札が無い間は、確認待ちの札がそのまま効く（設定で検出を使わないときは並べない）。
/// 候補は商品の対応アバター・その共通素体・「対応アバターすべて」だけ——対応に無いアバターを向き先にしても、見分けに効かない。
/// </summary>
public sealed class VariationTargetsInput : ViewModelBase
{
    private readonly IReadOnlyList<VariationTargetChoice> _choices;
    private readonly IReadOnlyList<VariationTargetChoice> _detected;

    public VariationTargetsInput(
        IReadOnlyList<VariationTargetChoice> choices,
        IReadOnlyList<VariationTargetChoice> decided,
        IReadOnlyList<VariationTargetChoice> detected)
    {
        _choices = choices;
        _detected = detected;
        AddCommand = new RelayCommand(parameter => Add(parameter as string));

        foreach (var choice in decided)
        {
            Push(choice);
        }

        RefreshSuggestions();
    }

    public ObservableCollection<VariationTargetChip> Chips { get; } = [];

    /// <summary>名前から見つけた、まだ確認していない向き先（確認済みの札に無い物）。</summary>
    public ObservableCollection<VariationTargetCandidate> Candidates { get; } = [];

    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasChips => Chips.Count > 0;

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
            }
        }
    }

    /// <summary>
    /// まだ決めていないことの印（ユーザ判断 2026-11-07・案A：ファイルのバリエーション分けの「未指定」と同じオレンジの札）。
    /// 確認済みの札も確認待ちの札も無ければ「未選択」、確認待ちの札だけなら「未確認」。買っていない行と、決めた行は null
    /// </summary>
    public string? StateText => !_isPurchased || Chips.Count > 0 ? null : Candidates.Count > 0 ? "未確認" : "未選択";

    public string StateTip => Candidates.Count > 0
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

        Push(choice);
        Changed();
    }

    private void Confirm(VariationTargetChoice choice)
    {
        if (Chips.All(chip => chip.Choice != choice))
        {
            Push(choice);
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
        // 確認待ちの札は、確認済みの札が無い間は効いている。吹き出しで言い分ける
        Candidates.Clear();
        var effective = Chips.Count == 0;
        foreach (var choice in _detected.Where(choice => Chips.All(chip => chip.Choice != choice)))
        {
            Candidates.Add(new VariationTargetCandidate
            {
                Choice = choice,
                IsEffective = effective,
                ConfirmCommand = new RelayCommand(() => Confirm(choice)),
            });
        }

        Suggestions.Clear();
        foreach (var choice in _choices.Where(choice => Chips.All(chip => chip.Choice != choice)))
        {
            Suggestions.Add(choice.Text);
        }
    }

    /// <summary>人が決めた向き先の行。札が無ければ null（行を持たない＝検出に任せる）。</summary>
    public VariationTarget? ToTarget(long? variationId)
        => Chips.Count == 0
            ? null
            : new VariationTarget
            {
                VariationId = variationId,
                All = Chips.Any(chip => chip.Choice.Kind == VariationTargetKind.All),
                Avatars = [.. Chips.Where(chip => chip.Choice.Kind == VariationTargetKind.Avatar).Select(chip => chip.Choice.Key)],
                Bases = [.. Chips.Where(chip => chip.Choice.Kind == VariationTargetKind.Base).Select(chip => chip.Choice.Key)],
            };
}
