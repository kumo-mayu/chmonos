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
/// 人が積んだ札があればそれが向き先。無ければ、検出した向き先（バリエーションの名前から）を薄い字で見せ、そのまま効いていることを言う
/// （設定で検出を使わないときは見せない）。「この内容で決める」で、検出の向き先を人が決めた物として写せる。
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
        AcceptDetectedCommand = new RelayCommand(AcceptDetected);

        foreach (var choice in decided)
        {
            Push(choice);
        }

        RefreshSuggestions();
    }

    public ObservableCollection<VariationTargetChip> Chips { get; } = [];

    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasChips => Chips.Count > 0;

    /// <summary>人が決めていない間に効いている、名前から見つけた向き先（「くうた・まめふれんず」）。</summary>
    public string DetectedText => string.Join("・", _detected.Select(choice => choice.Text));

    public bool ShowsDetected => Chips.Count == 0 && _detected.Count > 0;

    public RelayCommand AddCommand { get; }

    public RelayCommand AcceptDetectedCommand { get; }

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

    private void AcceptDetected()
    {
        foreach (var choice in _detected.Where(choice => Chips.All(chip => chip.Choice != choice)))
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
        OnPropertyChanged(nameof(ShowsDetected));
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        foreach (var choice in _choices.Where(choice => Chips.All(chip => chip.Choice != choice)))
        {
            Suggestions.Add(choice.Text);
        }
    }

    /// <summary>人が決めた向き先の行。札が無ければ null（行を持たない＝検出に任せる）。</summary>
    public VariationTarget? ToTarget(long variationId)
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
