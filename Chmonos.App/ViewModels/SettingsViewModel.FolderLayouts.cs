using System.Collections.ObjectModel;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 設定の「フォルダの置き方」（docs/spec/folder-layout.md）。未確定の自動検索が、フォルダ名を商品名・ショップ名の手掛かりに使う。
/// 書くのは <see cref="AppSettings.FolderLayouts"/> だけで、行ごとに変え方を関数で渡す（画面の写しで一覧を丸ごと書かない）。
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>段の選び方。null は「使わない」。</summary>
    public sealed record LevelOption(int? Level, string Label);

    public static IReadOnlyList<LevelOption> LevelOptions { get; } =
    [
        new(1, "1つ上"),
        new(2, "2つ上"),
        new(3, "3つ上"),
        new(null, "使わない"),
    ];

    public ObservableCollection<FolderLayoutRow> FolderLayouts { get; } = [];

    public bool HasFolderLayouts => FolderLayouts.Count > 0;

    private RelayCommand? _addFolderLayoutCommand;

    public RelayCommand AddFolderLayoutCommand => _addFolderLayoutCommand ??= new RelayCommand(() =>
    {
        if (PickFolder("置き方を指定するフォルダを選ぶ") is { } chosen)
        {
            AddFolderLayoutAsync(chosen).Forget();
        }
    });

    /// <summary>行を今の設定から並べ直す（開いたときと、変えた後）。当てはまりは裏で数える。</summary>
    private void LoadFolderLayouts(AppSettings settings)
    {
        FolderLayouts.Clear();
        foreach (var rule in settings.FolderLayouts)
        {
            FolderLayouts.Add(new FolderLayoutRow(this, rule));
        }

        OnPropertyChanged(nameof(HasFolderLayouts));
        MeasureFolderLayoutsAsync().Forget();
    }

    /// <summary>
    /// 紐付け済みのファイルで、どれだけ当てはまるかを数える。2万件でも画面を止めないよう裏で数える。
    /// 保存先の商品の記録から読む（検索の写しは起動の直後だとまだ空で、0件と言ってしまう）。読むだけなので命令を通さない
    /// </summary>
    internal async Task MeasureFolderLayoutsAsync()
    {
        var rows = FolderLayouts.ToList();
        if (rows.Count == 0)
        {
            return;
        }

        // 数える指定は画面のスレッドで写しておき、終わった時に行の指定がまだ同じときだけ出す。
        // 数えている間に段を変えると、前の計算が後から終わって、変えた後の段に古い件数を出した（点検35）
        var rules = rows.Select(row => row.Current).ToList();
        var items = (await _services.Store.Items.LoadAllAsync()).Items;
        var fits = await Task.Run(() => rules.Select(rule => FolderLayoutFit.Measure(items, rule)).ToList());
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Current == rules[index])
            {
                rows[index].Fit = fits[index];
            }
        }
    }

    /// <summary>フォルダを足す。既定は1つ上＝商品名・2つ上＝ショップ名・オン。同じフォルダが既にあれば足さない。試験はここから呼ぶ</summary>
    internal async Task AddFolderLayoutAsync(string root)
    {
        if (await SaveAsync(settings => settings.FolderLayouts.Any(rule => SameRoot(rule.Root, root))
                ? settings
                : settings with { FolderLayouts = [.. settings.FolderLayouts, FolderLayoutRule.DefaultFor(root)] }))
        {
            LoadFolderLayouts(_services.Settings);
        }
    }

    /// <summary>
    /// 1行を変える。今の設定の同じフォルダの行に当てる（画面の写しで丸ごと書かない）。
    /// **行は作り直さない**：続けて変えると、1つ目の保存が終わった所で作り直した行が、まだ保存していない2つ目の入力の前の値を出した（点検34）。
    /// 行は最後の入力を持ち続け、当てはまりだけ数え直す
    /// </summary>
    internal async Task ChangeFolderLayoutAsync(string root, Func<FolderLayoutRule, FolderLayoutRule> change)
    {
        if (await SaveAsync(settings => settings with
            {
                FolderLayouts = settings.FolderLayouts.Select(rule => SameRoot(rule.Root, root) ? change(rule) : rule).ToList(),
            }))
        {
            MeasureFolderLayoutsAsync().Forget();
        }
    }

    internal async Task RemoveFolderLayoutAsync(string root)
    {
        if (await SaveAsync(settings => settings with
            {
                FolderLayouts = settings.FolderLayouts.Where(rule => !SameRoot(rule.Root, root)).ToList(),
            }))
        {
            LoadFolderLayouts(_services.Settings);
        }
    }

    private static bool SameRoot(string left, string right)
        => string.Equals(left.TrimEnd('\\', '/'), right.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>「フォルダの置き方」の1行。</summary>
public sealed class FolderLayoutRow : ViewModelBase
{
    private readonly SettingsViewModel _owner;

    public FolderLayoutRow(SettingsViewModel owner, FolderLayoutRule rule)
    {
        _owner = owner;
        Rule = rule;
        _enabled = rule.Enabled;
        _itemNameLevel = rule.ItemNameLevel;
        _shopNameLevel = rule.ShopNameLevel;
        RemoveCommand = new RelayCommand(() => owner.RemoveFolderLayoutAsync(rule.Root).Forget());
    }

    // 最後の入力を覚えて、それと比べる。読んだときの設定（Rule）と比べると、保存が終わる前に元の値へ戻した操作を
    // 「変わっていない」と見て捨て、先の変更だけが残った（点検33）。保存は変え方を関数で渡すので、押した順に当たる
    private bool _enabled;
    private int? _itemNameLevel;
    private int? _shopNameLevel;

    public FolderLayoutRule Rule { get; }

    /// <summary>最後の入力を当てた指定（当てはまりと「効かない段」の文はこちらで見る）。</summary>
    public FolderLayoutRule Current => Rule with { Enabled = _enabled, ItemNameLevel = _itemNameLevel, ShopNameLevel = _shopNameLevel };

    public string Root => Rule.Root;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetField(ref _enabled, value))
            {
                _owner.ChangeFolderLayoutAsync(Rule.Root, rule => rule with { Enabled = value }).Forget();
            }
        }
    }

    public int? ItemNameLevel
    {
        get => _itemNameLevel;
        set
        {
            if (SetField(ref _itemNameLevel, value))
            {
                Fit = null;
                _owner.ChangeFolderLayoutAsync(Rule.Root, rule => rule with { ItemNameLevel = value }).Forget();
            }
        }
    }

    public int? ShopNameLevel
    {
        get => _shopNameLevel;
        set
        {
            if (SetField(ref _shopNameLevel, value))
            {
                Fit = null;
                _owner.ChangeFolderLayoutAsync(Rule.Root, rule => rule with { ShopNameLevel = value }).Forget();
            }
        }
    }

    public RelayCommand RemoveCommand { get; }

    private FolderLayoutFit? _fit;

    /// <summary>紐付け済みのファイルでの当てはまり。数え終えるまで null。</summary>
    public FolderLayoutFit? Fit
    {
        get => _fit;
        set
        {
            if (SetField(ref _fit, value))
            {
                OnPropertyChanged(nameof(FitText));
            }
        }
    }

    /// <summary>
    /// 行の下の1行。合わない置き方をオンにしたまま気付かない事故を防ぐ。
    /// 指定として効かない（段が無い・同じ段を2つに使う）なら、先にそれを言う
    /// </summary>
    public string FitText
        => !Current.IsUsable
            ? (Current.ItemNameLevel is not null && Current.ItemNameLevel == Current.ShopNameLevel
                ? "商品名とショップ名に同じ段は選べません。"
                : "商品名かショップ名のどちらかを選んでください。")
            : Fit is not { } fit
                ? "確かめています…"
                : fit.Files == 0
                    ? "この中に、紐付け済みのファイルはまだありません。"
                    : FitLine(fit);

    private string FitLine(FolderLayoutFit fit)
    {
        var parts = new List<string>();
        if (Current.ItemNameLevel is not null)
        {
            parts.Add($"商品名 {fit.ItemNameMatches:N0} 件");
        }

        if (Current.ShopNameLevel is not null)
        {
            parts.Add($"ショップ名 {fit.ShopNameMatches:N0} 件");
        }

        return $"紐付け済みのファイル {fit.Files:N0} 件のうち、合っているのは{string.Join("・", parts)}です。";
    }
}
