using System.Collections.ObjectModel;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>設定の「カードに表示する属性」の1つ（札の形で並べ、× で外す）。</summary>
public sealed class CardAttributeChoice
{
    public required string Name { get; init; }

    public required RelayCommand RemoveCommand { get; init; }
}

/// <summary>
/// 設定：一覧と検索の「カードに表示する属性」（ユーザ判断 2026-10-04：出す属性は設定で選ぶ。既定は属性の管理の並びの上から）。
/// 候補を全部並べてチェックさせる形にはしない（属性の数に比例して縦に伸びる・<c>.claude/rules/screen-and-wording.md</c>）。
/// 候補付きの欄から1つずつ足し、札の × で外す。出す順は設定で選んだ順ではなく、商品に付いている順
/// </summary>
public sealed partial class SettingsViewModel
{
    public ObservableCollection<CardAttributeChoice> CardAttributes { get; } = [];

    /// <summary>足せる属性（属性の管理の並び。足した物は除く）。</summary>
    public IReadOnlyList<string> CardAttributeCandidates { get; private set; } = [];

    public bool HasCardAttributes => CardAttributes.Count > 0;

    /// <summary>選んでいないときは、何が出るのかを言う（空の欄だけだと、何も出ないように読める）。</summary>
    public string CardAttributesNote => CardAttributes.Count > 0
        ? "選んだ属性のうち、商品に付いている順に2つまでカードに表示します。"
        : "選んでいないときは、属性の管理の並びの上から表示します。";

    private RelayCommand? _addCardAttribute;

    /// <summary>候補の欄で選んだ属性を足す（引数は属性の名前）。</summary>
    public RelayCommand AddCardAttributeCommand => _addCardAttribute ??= new RelayCommand(parameter =>
    {
        if (parameter is string name)
        {
            AddCardAttribute(name);
        }
    });

    private List<string> _masterAttributeNames = [];

    private void LoadCardAttributes(AppSettings settings)
    {
        _masterAttributeNames = _services.CachedAttributes.Load().Attributes.Select(definition => definition.Name).ToList();
        CardAttributes.Clear();
        foreach (var name in settings.CardAttributes ?? [])
        {
            CardAttributes.Add(ChoiceFor(name));
        }

        RefreshCardAttributeTexts();
    }

    internal void AddCardAttribute(string name)
    {
        name = name.Trim();

        // 候補は属性の管理にある物だけ（無い名前を選んでも、どの商品にも値が無く札は出ない）
        if (name.Length == 0
            || !_masterAttributeNames.Contains(name, StringComparer.Ordinal)
            || CardAttributes.Any(choice => choice.Name == name))
        {
            return;
        }

        CardAttributes.Add(ChoiceFor(name));
        SaveCardAttributes();
    }

    internal void RemoveCardAttribute(string name)
    {
        var choice = CardAttributes.FirstOrDefault(entry => entry.Name == name);
        if (choice is null)
        {
            return;
        }

        CardAttributes.Remove(choice);
        SaveCardAttributes();
    }

    private CardAttributeChoice ChoiceFor(string name)
        => new() { Name = name, RemoveCommand = new RelayCommand(() => RemoveCardAttribute(name)) };

    private void SaveCardAttributes()
    {
        RefreshCardAttributeTexts();

        // ほかの項目と別に書く（画面が持つ他の値を一緒に当て直さない）。保存の後に一覧を組み直す（カードの札が変わる）
        var names = CardAttributes.Select(choice => choice.Name).ToList();
        ThenAsync(SaveAsync(current => current with { CardAttributes = names }), _main.ReloadLibraryAsync).Forget();
    }

    private void RefreshCardAttributeTexts()
    {
        CardAttributeCandidates = _masterAttributeNames
            .Where(name => CardAttributes.All(choice => choice.Name != name))
            .ToList();
        OnPropertyChanged(nameof(CardAttributeCandidates));
        OnPropertyChanged(nameof(HasCardAttributes));
        OnPropertyChanged(nameof(CardAttributesNote));
    }
}
