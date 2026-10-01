using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chmonos.App.Services;
using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>編集画面：タグ・属性・分類・ショップの候補（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class EditViewModel
{
    private UserTagRow CreateTagRow(string top)
    {
        var row = new UserTagRow { Top = top };

        row.RemoveCommand = new RelayCommand(() =>
        {
            Tags.Remove(row);
            RefreshSuggestions();
        });

        row.AddSubCommand = new RelayCommand(parameter => AddSubAsync(row, parameter as string).Forget());

        row.RemoveSubCommand = new RelayCommand(parameter =>
        {
            if (parameter is string sub)
            {
                row.Subs.Remove(sub);
                row.Raise();
                RefreshSubCandidates(row);
            }
        });

        return row;
    }

    private AttributeRow CreateAttributeRow(string name, int value)
    {
        var row = new AttributeRow { Name = name, Value = value };
        row.RemoveCommand = new RelayCommand(() =>
        {
            Attributes.Remove(row);
            RefreshSuggestions();
        });

        return row;
    }

    /// <summary>まだ使っていない候補だけを出す。既に付けたものを候補に残すと選び間違える。</summary>
    /// <summary>
    /// 入力からショップを組み立てる。
    ///
    /// **URLを貼れば本物のサブドメイン、貼らなければ手元だけの鍵。**
    /// ローマ字化はしない——「ほとぎ屋」→ hotogiya は実在するので、
    /// 手で作った鍵が本物と衝突すると本物のアイコンとバナーが出てしまう。
    /// </summary>
    /// <summary>手元の商品が持っているショップ名を集める。</summary>
    /// <summary>
    /// 打った分で絞り込む。139件あるので、打つほど絞れる形にしないと選べない。
    /// </summary>
    private void RefreshCategorySuggestions()
    {
        var typed = CategoryInput.Trim();

        var matched = _services.Categories.Suggestions()
            .Where(name => typed.Length == 0
                || (name.Contains(typed, StringComparison.CurrentCultureIgnoreCase)
                    && !string.Equals(name, typed, StringComparison.CurrentCultureIgnoreCase)))
            .Take(CategorySuggestionLimit)
            .ToList();

        CategorySuggestions.Clear();
        foreach (var name in matched)
        {
            CategorySuggestions.Add(name);
        }

        OnPropertyChanged(nameof(HasCategorySuggestions));
    }

    /// <summary>
    /// 打った分で絞り込む。14店あると全部並べても読めないので、
    /// 打つほど絞れる形にする。空欄のときは頭から数件だけ出す。
    /// </summary>
    private void RefreshShopSuggestions()
    {
        var typed = ShopNameInput.Trim();

        var matched = _shopNames
            .Where(name => typed.Length == 0
                || (name.Contains(typed, StringComparison.CurrentCultureIgnoreCase)
                    && !string.Equals(name, typed, StringComparison.CurrentCultureIgnoreCase)))
            .Take(ShopSuggestionLimit)
            .ToList();

        ShopSuggestions.Clear();
        foreach (var name in matched)
        {
            ShopSuggestions.Add(name);
        }

        OnPropertyChanged(nameof(HasShopSuggestions));
    }

    // 候補なので検索の写しで足りる（編集を開くたびに全件を読み直していた。保存した店名は下で足していく）
    private async Task<List<string>> LoadShopNamesAsync()
        => (await _main.Search.ItemsAsync())
            .Select(item => item.ShopName)
            .Where(name => name is { Length: > 0 })
            .Select(name => name!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

    private LocalShop? BuildShop()
    {
        var name = ShopNameInput.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var url = ShopUrlInput.Trim();
        var subdomain = LocalShopKey.SubdomainFromUrl(url);

        return new LocalShop
        {
            Name = name,
            Subdomain = subdomain ?? LocalShopKey.For(name),
            Url = subdomain is null ? null : url,
        };
    }

    private void RefreshSuggestions()
    {
        TagSuggestions.Clear();
        foreach (var top in _tagMaster.Tops
            .Select(top => top.Name)
            .Where(name => !Tags.Any(row => string.Equals(row.Top, name, StringComparison.CurrentCultureIgnoreCase))))
        {
            TagSuggestions.Add(top);
        }

        RefreshShopSuggestions();

        RefreshCategorySuggestions();

        AttributeSuggestions.Clear();
        foreach (var name in _attributeMaster.Attributes
            .Select(definition => definition.Name)
            .Where(name => !Attributes.Any(row => string.Equals(row.Name, name, StringComparison.CurrentCultureIgnoreCase))))
        {
            AttributeSuggestions.Add(name);
        }
    }

    private void RefreshSubCandidates(UserTagRow row)
    {
        var master = _tagMaster.Tops.FirstOrDefault(top =>
            string.Equals(top.Name, row.Top, StringComparison.CurrentCultureIgnoreCase));

        row.SubCandidates.Clear();
        foreach (var sub in (master?.Subs ?? [])
            .Select(sub => sub.Name)
            .Where(name => !row.Subs.Contains(name, StringComparer.CurrentCultureIgnoreCase)))
        {
            row.SubCandidates.Add(sub);
        }
    }

    private async Task AddTagAsync(string? name)
    {
        var top = name?.Trim();
        if (string.IsNullOrEmpty(top)
            || Tags.Any(row => string.Equals(row.Top, top, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        // 候補に無い語はマスタへの新規追加を兼ねる
        if (!_tagMaster.Tops.Any(entry => string.Equals(entry.Name, top, StringComparison.CurrentCultureIgnoreCase)))
        {
            if (await _services.Commands.ExecuteAsync(new UiCommand.AddUserTag(top)) is CommandResult.UserTagsChanged changed)
            {
                _tagMaster = changed.Master;
            }
        }

        var row = CreateTagRow(top);
        RefreshSubCandidates(row);
        Tags.Add(row);
        RefreshSuggestions();
    }

    private async Task AddSubAsync(UserTagRow row, string? name)
    {
        var sub = name?.Trim();
        if (string.IsNullOrEmpty(sub) || row.Subs.Contains(sub, StringComparer.CurrentCultureIgnoreCase))
        {
            return;
        }

        var master = _tagMaster.Tops.FirstOrDefault(top =>
            string.Equals(top.Name, row.Top, StringComparison.CurrentCultureIgnoreCase));

        if (master is null
            || !master.Subs.Any(entry => string.Equals(entry.Name, sub, StringComparison.CurrentCultureIgnoreCase)))
        {
            if (await _services.Commands.ExecuteAsync(new UiCommand.AddUserTag(row.Top, sub)) is CommandResult.UserTagsChanged changed)
            {
                _tagMaster = changed.Master;
            }
        }

        row.Subs.Add(sub);
        row.Raise();
        RefreshSubCandidates(row);
    }

    private async Task AddAttributeAsync(string? name)
    {
        var attribute = name?.Trim();
        if (string.IsNullOrEmpty(attribute)
            || Attributes.Any(row => string.Equals(row.Name, attribute, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        if (!_attributeMaster.Attributes.Any(entry =>
            string.Equals(entry.Name, attribute, StringComparison.CurrentCultureIgnoreCase)))
        {
            if (await _services.Commands.ExecuteAsync(new UiCommand.AddAttribute(attribute)) is CommandResult.AttributesChanged changed)
            {
                _attributeMaster = changed.Master;
            }
        }

        Attributes.Add(CreateAttributeRow(attribute, 50));
        RefreshSuggestions();
    }
}
