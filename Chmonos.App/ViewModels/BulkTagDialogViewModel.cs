using System.Collections.ObjectModel;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 選択の帯の「タグ・属性 ▾」の窓（ユーザ判断 2026-11-20）。足すときはタグか属性を、外すときはタグか属性を1つ選ぶ。
/// 書くのは窓を閉じた後で、呼ぶ側（<see cref="SelectionBulkActions"/>）が命令で行う。
/// </summary>
/// <remarks>
/// 属性は**値を付けずに**足す（付いているが値はまだ。ユーザ判断 2026-11-20 の (a)）。スライダーは出さない——
/// 選んだ商品に同じ値を入れると、付けただけか評価したかが分からなくなる。値は編集の画面の「未評価」の行から付ける。
/// 外す候補は選んだ商品に付いている物だけ（付いていない物を選ばせても何も起きない）。
/// </remarks>
public sealed class BulkTagDialogViewModel : ViewModelBase
{
    /// <summary>候補の小分類の並びの区切り（タグの管理の表示と同じ）。</summary>
    public const string SubSeparator = "／";

    private enum Mode
    {
        AddTag,
        AddAttribute,
        Remove,
    }

    private readonly Mode _mode;
    private readonly UserTagMaster _master;
    private readonly AttributeMaster _attributeMaster;
    private readonly IReadOnlyList<ItemRecord> _items;

    private BulkTagDialogViewModel(Mode mode, UserTagMaster master, AttributeMaster attributeMaster, IReadOnlyList<ItemRecord> items)
    {
        _mode = mode;
        _master = master;
        _attributeMaster = attributeMaster;
        _items = items;
        PickTopCommand = new RelayCommand(parameter => PickTop(parameter as string));
        PickSubCommand = new RelayCommand(parameter => PickSub(parameter as string));
        PickAttributeCommand = new RelayCommand(parameter => PickAttribute(parameter as string));
        PickRemovalCommand = new RelayCommand(parameter => PickRemoval(parameter as string));
        RefreshCandidates();
    }

    /// <summary>タグを足す窓。候補はタグの一覧（マスタ）の大分類・小分類。</summary>
    public static BulkTagDialogViewModel ForAdding(UserTagMaster master, IReadOnlyList<ItemRecord> items)
        => new(Mode.AddTag, master, new AttributeMaster(), items);

    /// <summary>属性を足す窓。候補は属性の一覧（マスタ）。</summary>
    public static BulkTagDialogViewModel ForAddingAttribute(AttributeMaster master, IReadOnlyList<ItemRecord> items)
        => new(Mode.AddAttribute, new UserTagMaster(), master, items);

    /// <summary>外す窓。候補は選んだ商品に付いているタグ・属性だけ（属性は値はまだの物も）。</summary>
    public static BulkTagDialogViewModel ForRemoving(IReadOnlyList<ItemRecord> items)
        => new(Mode.Remove, new UserTagMaster(), new AttributeMaster(), items);

    public bool IsRemoving => _mode == Mode.Remove;

    /// <summary>足す窓か（タグ・属性のどちらも。ボタンの形が同じ）。</summary>
    public bool IsAdding => !IsRemoving;

    public bool IsAddingTag => _mode == Mode.AddTag;

    public bool IsAddingAttribute => _mode == Mode.AddAttribute;

    public string DialogTitle => _mode switch
    {
        Mode.Remove => "タグ・属性を削除",
        Mode.AddAttribute => "属性を追加",
        _ => "タグを追加",
    };

    public string HeadingText => _mode switch
    {
        Mode.Remove => $"選んだ {_items.Count} 件から外すタグか属性を選んでください。",
        Mode.AddAttribute => $"選んだ {_items.Count} 件に追加する属性を選んでください。",
        _ => $"選んだ {_items.Count} 件に追加するタグを選んでください。",
    };

    // ---- 属性を足す ----

    public ObservableCollection<string> AttributeCandidates { get; } = [];

    public RelayCommand PickAttributeCommand { get; }

    private string? _attribute;

    /// <summary>選んだ属性。候補に無い語は、追加するときに属性の一覧へも足す（編集画面の欄と同じ）。</summary>
    public string? Attribute
    {
        get => _attribute;
        private set
        {
            if (SetField(ref _attribute, value))
            {
                RaiseChoice();
            }
        }
    }

    /// <summary>候補に無い属性か（追加の前に属性の一覧へ足す）。</summary>
    public bool IsNewAttribute => Attribute is not null && MasterAttributeName(Attribute) is null;

    private void PickAttribute(string? name)
        => Attribute = string.IsNullOrWhiteSpace(name) ? null : MasterAttributeName(name.Trim()) ?? name.Trim();

    private string? MasterAttributeName(string name)
        => _attributeMaster.Attributes.FirstOrDefault(entry => Same(entry.Name, name))?.Name;

    // ---- 足す ----

    public ObservableCollection<string> TopCandidates { get; } = [];

    public ObservableCollection<string> SubCandidates { get; } = [];

    public RelayCommand PickTopCommand { get; }

    public RelayCommand PickSubCommand { get; }

    private string? _top;

    /// <summary>選んだ大分類。候補に無い語は、追加するときにタグの一覧へも足す（編集画面の欄と同じ）。</summary>
    public string? Top
    {
        get => _top;
        private set
        {
            if (SetField(ref _top, value))
            {
                Sub = null;
                RefreshSubCandidates();
                RaiseChoice();
            }
        }
    }

    private string? _sub;

    public string? Sub
    {
        get => _sub;
        private set
        {
            if (SetField(ref _sub, value))
            {
                RaiseChoice();
            }
        }
    }

    public bool HasTop => Top is not null;

    private void PickTop(string? name)
        => Top = string.IsNullOrWhiteSpace(name) ? null : MasterTopName(name.Trim()) ?? name.Trim();

    private void PickSub(string? name)
        => Sub = Top is null || string.IsNullOrWhiteSpace(name) ? null : MasterSubName(Top, name.Trim()) ?? name.Trim();

    // ---- 外す ----

    private bool _removesAttribute;

    /// <summary>外す物が属性か（いいえならタグ）。切り替えると選んだ物を捨てる。</summary>
    public bool RemovesAttribute
    {
        get => _removesAttribute;
        set
        {
            if (SetField(ref _removesAttribute, value))
            {
                OnPropertyChanged(nameof(RemovesTag));
                Removal = null;
                RefreshCandidates();
            }
        }
    }

    public bool RemovesTag
    {
        get => !RemovesAttribute;
        set => RemovesAttribute = !value;
    }

    /// <summary>外す候補（タグは「大分類」と「大分類／小分類」、属性は名前）。</summary>
    public ObservableCollection<string> RemovalCandidates { get; } = [];

    public RelayCommand PickRemovalCommand { get; }

    private string? _removal;

    public string? Removal
    {
        get => _removal;
        private set
        {
            if (SetField(ref _removal, value))
            {
                RaiseChoice();
            }
        }
    }

    private void PickRemoval(string? text)
        => Removal = text is not null && RemovalCandidates.Contains(text) ? text : null;

    // ---- 決めた物 ----

    /// <summary>選び終えたか（「追加」「削除…」を押せるか）。</summary>
    public bool HasChoice => _mode switch
    {
        Mode.Remove => Removal is not null,
        Mode.AddAttribute => Attribute is not null,
        _ => Top is not null,
    };

    /// <summary>選んだ物の表示（窓の中の確かめ）。</summary>
    public string ChoiceText => _mode switch
    {
        Mode.Remove => Removal ?? string.Empty,
        Mode.AddAttribute => Attribute ?? string.Empty,
        _ => Top is null ? string.Empty : Sub is null ? Top : $"{Top}{SubSeparator}{Sub}",
    };

    public string CommitText => IsRemoving ? "削除…" : "追加";

    /// <summary>押せないときの理由（E9）。</summary>
    public string CommitHint => HasChoice ? string.Empty
        : _mode switch
        {
            Mode.Remove => RemovalCandidates.Count == 0
                ? RemovesAttribute ? "選んだ商品に属性は付いていません。" : "選んだ商品にタグは付いていません。"
                : "外す物を候補から選んでください。",
            Mode.AddAttribute => "属性を入力して Enter を押してください。",
            _ => "大分類を入力して Enter を押してください。",
        };

    /// <summary>窓で決めた変更。決めていなければ null。</summary>
    public Core.Services.BulkItemChange? Change
    {
        get
        {
            if (!HasChoice)
            {
                return null;
            }

            if (IsAddingAttribute)
            {
                return new Core.Services.BulkItemChange.AddAttribute(Attribute!);
            }

            if (IsAddingTag)
            {
                return new Core.Services.BulkItemChange.AddUserTag(Top!, Sub);
            }

            if (RemovesAttribute)
            {
                return new Core.Services.BulkItemChange.RemoveAttribute(Removal!);
            }

            var (top, sub) = _tagTargets[Removal!];
            return new Core.Services.BulkItemChange.RemoveUserTag(top, sub);
        }
    }

    /// <summary>候補に無い大分類・小分類か（追加の前にタグの一覧へ足す）。</summary>
    public bool IsNewTop => Top is not null && MasterTopName(Top) is null;

    public bool IsNewSub => Top is not null && Sub is not null && MasterSubName(Top, Sub) is null;

    /// <summary>外すときに、選んだ商品のうち何件に付いているか（確かめの窓に出す）。</summary>
    public int CountHaving() => Removal is null ? 0 : _items.Count(item => Has(item, Removal));

    private bool Has(ItemRecord item, string removal)
    {
        if (RemovesAttribute)
        {
            // 値はまだの物も付いている（外すと消える）
            return ItemAttributes.IsAttached(item.Local, removal.Trim());
        }

        var (top, sub) = _tagTargets[removal];
        return item.Local.UserTags.Any(tag => Same(tag.Top, top) && (sub is null || tag.Subs.Any(known => Same(known, sub))));
    }

    /// <summary>
    /// 外す候補の表示から、大分類・小分類への引き当て。表示を「／」で割り戻すと、名前に「／」を含むタグを取り違える
    /// </summary>
    private readonly Dictionary<string, (string Top, string? Sub)> _tagTargets = new(StringComparer.CurrentCultureIgnoreCase);

    private void RefreshCandidates()
    {
        TopCandidates.Clear();
        foreach (var top in _master.Tops)
        {
            TopCandidates.Add(top.Name);
        }

        AttributeCandidates.Clear();
        foreach (var attribute in _attributeMaster.Attributes)
        {
            AttributeCandidates.Add(attribute.Name);
        }

        RemovalCandidates.Clear();
        if (!IsRemoving)
        {
            return;
        }

        _tagTargets.Clear();
        foreach (var tag in _items.SelectMany(item => item.Local.UserTags))
        {
            _tagTargets.TryAdd(tag.Top.Trim(), (tag.Top, null));
            foreach (var sub in tag.Subs)
            {
                _tagTargets.TryAdd($"{tag.Top.Trim()}{SubSeparator}{sub.Trim()}", (tag.Top, sub));
            }
        }

        // 付いている商品の多い順にすると、よく使う物が先に出る。同じ数なら名前の順
        var names = RemovesAttribute
            ? _items.SelectMany(item => item.Local.Attributes.Keys.Concat(ItemAttributes.Pending(item.Local)))
            : _items.SelectMany(item => item.Local.UserTags.SelectMany(tag => (IEnumerable<string>)[tag.Top, .. tag.Subs.Select(sub => $"{tag.Top.Trim()}{SubSeparator}{sub.Trim()}")]));
        foreach (var name in names
            .GroupBy(name => name.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCulture)
            .Select(group => group.Key))
        {
            RemovalCandidates.Add(name);
        }

        OnPropertyChanged(nameof(CommitHint));
    }

    private void RefreshSubCandidates()
    {
        SubCandidates.Clear();
        foreach (var sub in _master.Tops.FirstOrDefault(top => Same(top.Name, Top))?.Subs ?? [])
        {
            SubCandidates.Add(sub.Name);
        }
    }

    private void RaiseChoice()
    {
        OnPropertyChanged(nameof(HasTop));
        OnPropertyChanged(nameof(HasChoice));
        OnPropertyChanged(nameof(ChoiceText));
        OnPropertyChanged(nameof(CommitHint));
    }

    private string? MasterTopName(string name) => _master.Tops.FirstOrDefault(top => Same(top.Name, name))?.Name;

    private string? MasterSubName(string top, string name)
        => _master.Tops.FirstOrDefault(entry => Same(entry.Name, top))?.Subs.FirstOrDefault(sub => Same(sub.Name, name))?.Name;

    // タグの名前は大文字と小文字を区別しない（BulkItemChange と同じ）
    private static bool Same(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// 窓を出す代わりに答える口。**アプリでは null のまま**（窓を出す）。試験と ViewShot が入れる
    /// （窓を出すと答える人がいないので止まる。<see cref="Services.Notice.Intercept"/> と同じ）
    /// </summary>
    internal static Func<BulkTagDialogViewModel, bool>? Intercept { get; set; }

    /// <summary>窓で選ばせる。「追加」「削除…」なら true。</summary>
    public static bool Ask(BulkTagDialogViewModel model)
        => Intercept is { } intercept
            ? intercept(model)
            : new Views.BulkTagDialog(model).ShowDialog() == true;

    /// <summary>試験と ViewShot から、欄に打って Enter を押したのと同じにする。</summary>
    internal void Type(string? top = null, string? sub = null, string? removal = null, string? attribute = null)
    {
        if (attribute is not null)
        {
            PickAttribute(attribute);
        }

        if (top is not null)
        {
            PickTop(top);
        }

        if (sub is not null)
        {
            PickSub(sub);
        }

        if (removal is not null)
        {
            PickRemoval(removal);
        }
    }
}
