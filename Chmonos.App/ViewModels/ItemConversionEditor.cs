using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 「変換扱い」の欄（<c>docs/spec/conversion.md</c>「画面」）。商品を変換ツールのプロファイルとして扱うかと、中のプロファイルごとのつなぎ先を、人が決める・確かめる。
/// 商品ページとアバターの管理の「変換」で同じ部品を使う（ユーザ判断 2026-11-09「商品ページの欄を使い回す」）。
/// 書き込みは持ち主（画面）に渡す命令の道を通す：持ち主ごとに、書いた後の読み直しと知らせ方が違う（商品ページは対応アバターも組み直す）。
/// 変換経由の計算は確認したつなぎ先だけを使う（既定）ので、ここで確かめるまで衣装の「変換経由」は出ない。
/// </summary>
public sealed class ItemConversionEditor : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly Func<UiCommand, Task> _run;
    private readonly Func<bool> _isLocked;
    private ItemRecord? _item;

    /// <param name="run">命令を通して、書いた後に <see cref="Show"/> で今の記録を渡し直す道（持ち主が持つ）。</param>
    /// <param name="isLocked">書き込めない間（商品ページの編集の錠）。</param>
    public ItemConversionEditor(AppServiceContainer services, Func<UiCommand, Task> run, Func<bool> isLocked)
    {
        _services = services;
        _run = run;
        _isLocked = isLocked;
        SetModeCommand = new RelayCommand(
            parameter =>
            {
                ConversionMode? mode = parameter as string switch
                {
                    "auto" => ConversionMode.Auto,
                    "profile" => ConversionMode.Profile,
                    "notProfile" => ConversionMode.NotProfile,
                    _ => null,
                };
                if (mode is { } chosen && chosen != Mode)
                {
                    ChangeAsync(ConversionEdits.SetMode(chosen)).Forget();
                }
                else
                {
                    // 押した区画だけ先に点いたままにならないよう、今の値に合わせ直す
                    RaiseMode();
                }
            },
            _ => !_isLocked());
    }

    /// <summary>
    /// 欄を出すか。プロファイルのファイルを持っている商品と、変換扱いを既に持っている商品だけ
    /// （ユーザ判断 2026-11-06：手元にプロファイルが無いと意味を成さない。ほとんどの商品には要らない欄）。
    /// </summary>
    public bool ShowsConversion { get; private set; }

    public ConversionMode Mode => _item?.Local.Conversion?.Mode ?? ConversionMode.Auto;

    public bool IsAuto => Mode == ConversionMode.Auto;

    public bool IsProfile => Mode == ConversionMode.Profile;

    public bool IsNotProfile => Mode == ConversionMode.NotProfile;

    /// <summary>今の扱いを文で（ボタンは変える操作だけにする。アバターの管理の「アバターかどうか」と同じ作り）。</summary>
    public string StateText { get; private set; } = string.Empty;

    /// <summary>中のプロファイルごとの行（つなぎ先が決まっていない物も、選ぶ欄の付いた行として出す）。</summary>
    public IReadOnlyList<ConversionTargetRow> Rows { get; private set; } = [];

    /// <summary>つなぎ先を選ぶ欄の候補（アバターと、「（共通素体）」を添えた共通素体）。</summary>
    public IReadOnlyList<string> TargetSuggestions { get; private set; } = [];

    /// <summary>確認待ちのつなぎ先の数（人が外した物は数えない）。</summary>
    public int UnconfirmedCount => Rows.Count(row => row.IsUnconfirmed);

    /// <summary>つなぎ先の決まっていないプロファイルの数。</summary>
    public int UnsetCount => Rows.Count(row => !row.HasTarget);

    /// <summary>書けなかったときだけ出る1行。</summary>
    public AreaNotice Notice { get; } = new();

    /// <summary>扱いの3択。引数は "auto"・"profile"・"notProfile"。</summary>
    public RelayCommand SetModeCommand { get; }

    /// <summary>候補に出した共通素体の行から素体の名前を引く表（アバターと同じ名前の素体を取り違えない）。</summary>
    private Dictionary<string, string> _basesBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    private string? ItemId => _item?.Id;

    /// <summary>変え方を、今の商品の変換扱いに当てる命令として通す。</summary>
    public Task ChangeAsync(Func<ConversionSetting?, ConversionSetting?> change)
        => ItemId is { } id ? _run(new UiCommand.ChangeConversion(id, change)) : Task.CompletedTask;

    /// <summary>見せる商品を替える（書いた後の読み直し・一覧で選び直したとき）。登録簿は読むだけなので直に読む。</summary>
    public void Show(ItemRecord? item, AvatarRegistry? registry = null, IReadOnlyDictionary<string, string>? names = null)
    {
        _item = item;
        var setting = item?.Local.Conversion;
        var profiles = item is not null && ConversionScan.MayHoldProfiles(item) ? _services.Conversion.ProfilesOf(item) : [];
        ShowsConversion = item is not null && (profiles.Count > 0 || setting is not null);

        if (!ShowsConversion)
        {
            Rows = [];
            StateText = string.Empty;
            Raise();
            return;
        }

        registry ??= _services.CachedAvatars.Load();
        names ??= AvatarNames.Map(registry.Entries);

        string TargetName(ConversionTarget target)
            => target.BaseName is { } baseName
                ? $"{baseName}（共通素体）"
                : target.AvatarItemId is { } id ? names.TryGetValue(id, out var name) ? name : id : string.Empty;

        // 人が外したつなぎ先は出さない（そのプロファイルは選ぶ欄に戻る）。行は保存に残り、見回りが同じ物を推すのを止める
        var targets = (setting?.Targets ?? []).Where(target => !target.Rejected).ToList();
        var rows = new List<ConversionTargetRow>();
        foreach (var profile in profiles)
        {
            var mine = targets.Where(target => string.Equals(target.Slug, profile.Slug, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine.Count == 0)
            {
                rows.Add(EmptyRow(profile));
            }

            rows.AddRange(mine.Select(target => TargetRow(target, TargetName(target))));
        }

        // 中のファイルから見つからない（zip を外した・slug の分からない）つなぎ先も、外せるように出す
        rows.AddRange(targets
            .Where(target => !profiles.Any(profile => string.Equals(profile.Slug, target.Slug, StringComparison.OrdinalIgnoreCase)))
            .Select(target => TargetRow(target, TargetName(target))));

        Rows = rows;
        StateText = (setting?.Mode ?? ConversionMode.Auto) switch
        {
            ConversionMode.Profile => "今の扱い：プロファイルとして扱う（手動で指定）",
            ConversionMode.NotProfile => "今の扱い：プロファイルとして扱わない（手動で指定）",
            _ => profiles.Count > 0
                ? "今の扱い：プロファイル（自動の判定）"
                : "今の扱い：プロファイルではない（自動の判定）",
        };

        _basesBySuggestion = registry.BaseGroups
            .Where(group => !group.Rejected && group.Name.Trim().Length > 0)
            .Select(group => group.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(name => $"{name}（共通素体）", name => name, StringComparer.CurrentCultureIgnoreCase);
        TargetSuggestions = registry.Entries
            .Where(AvatarService.IsAvatar)
            .Select(entry => names[entry.ItemId])
            .Concat(_basesBySuggestion.Keys)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        Raise();
    }

    private ConversionTargetRow EmptyRow(ProfileSlug profile) => new()
    {
        ProfileText = profile.Slug,
        DirectionText = DirectionText(profile.Forward, profile.Reverse),
        ChooseCommand = new RelayCommand(
            parameter => ChooseAsync(profile, parameter as string).Forget(),
            _ => !_isLocked()),
    };

    private ConversionTargetRow TargetRow(ConversionTarget target, string name) => new()
    {
        ProfileText = target.Slug ?? string.Empty,
        DirectionText = DirectionText(target.Forward, target.Reverse),
        TargetText = name,
        HasTarget = true,
        IsUnconfirmed = !target.Confirmed,
        ConfirmCommand = new RelayCommand(
            () => ChangeAsync(ConversionEdits.Confirm(target)).Forget(),
            () => !_isLocked() && !target.Confirmed),
        RemoveCommand = new RelayCommand(
            () => ChangeAsync(ConversionEdits.Reject(target)).Forget(),
            () => !_isLocked()),
        Direction = (target.Forward, target.Reverse) switch
        {
            (true, true) => "both",
            (true, false) => "forward",
            (false, true) => "reverse",
            _ => string.Empty,
        },

        // 向きはふつう中のファイルの名前で決まるので、変える口は右クリックだけに置く（ユーザ判断 2026-11-09「普通は使わない」）
        SetDirectionCommand = new RelayCommand(
            parameter =>
            {
                (bool Forward, bool Reverse)? chosen = parameter as string switch
                {
                    "forward" => (true, false),
                    "reverse" => (false, true),
                    "both" => (true, true),
                    _ => null,
                };
                if (chosen is { } direction && (direction.Forward, direction.Reverse) != (target.Forward, target.Reverse))
                {
                    ChangeAsync(ConversionEdits.SetDirection(target, direction.Forward, direction.Reverse)).Forget();
                }
            },
            _ => !_isLocked()),
    };

    /// <summary>選ぶ欄で決めたつなぎ先を書く。候補に無い名前は何もしない（候補から選ぶ欄）。</summary>
    private Task ChooseAsync(ProfileSlug profile, string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (_basesBySuggestion.TryGetValue(trimmed, out var baseName))
        {
            return ChangeAsync(ConversionEdits.Choose(profile.Slug, profile.Forward, profile.Reverse, null, baseName));
        }

        return FindAvatarByName(trimmed) is { } avatar
            ? ChangeAsync(ConversionEdits.Choose(profile.Slug, profile.Forward, profile.Reverse, avatar.ItemId, null))
            : Task.CompletedTask;
    }

    /// <summary>候補に出した名前（同じ名前ならショップ名付き）と、正式名のどちらでも引けるようにする（対応アバターを足す欄と同じ引き方）。</summary>
    private AvatarRegistryEntry? FindAvatarByName(string name)
    {
        if (name.Length == 0)
        {
            return null;
        }

        var entries = _services.CachedAvatars.Load().Entries;
        var names = AvatarNames.Map(entries);
        return entries.FirstOrDefault(entry =>
            string.Equals(names[entry.ItemId], name, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(AvatarNames.ShownName(entry), name, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(entry.BoothName, name, StringComparison.CurrentCultureIgnoreCase));
    }

    private static string DirectionText(bool forward, bool reverse) => (forward, reverse) switch
    {
        (true, true) => "両方向",
        (true, false) => "順方向",
        (false, true) => "逆方向",
        _ => "向きが分かりません",
    };

    private void RaiseMode()
    {
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsAuto));
        OnPropertyChanged(nameof(IsProfile));
        OnPropertyChanged(nameof(IsNotProfile));
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(ShowsConversion));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(TargetSuggestions));
        OnPropertyChanged(nameof(UnconfirmedCount));
        OnPropertyChanged(nameof(UnsetCount));
        RaiseMode();
    }
}

/// <summary>「変換扱い」の欄の1行：中のプロファイル1つと、そのつなぎ先（決まっていなければ選ぶ欄）。</summary>
public sealed class ConversionTargetRow
{
    /// <summary>中のプロファイルの名前（ツールが付ける短い名前）。分からなければ空。</summary>
    public string ProfileText { get; init; } = string.Empty;

    public string DirectionText { get; init; } = string.Empty;

    /// <summary>つなぎ先の名前（アバター名か「〇〇（共通素体）」）。決まっていなければ空。</summary>
    public string TargetText { get; init; } = string.Empty;

    public bool HasTarget { get; init; }

    public bool HasProfile => ProfileText.Length > 0;

    /// <summary>自動で推して、まだ人が確かめていない。変換経由の計算には（既定では）使わない。</summary>
    public bool IsUnconfirmed { get; init; }

    public RelayCommand? ConfirmCommand { get; init; }

    public RelayCommand? RemoveCommand { get; init; }

    /// <summary>つなぎ先の決まっていない行の、選ぶ欄の確定（引数は選んだ候補の文字列）。</summary>
    public RelayCommand? ChooseCommand { get; init; }

    /// <summary>今の向き（"forward"・"reverse"・"both"）。右クリックの項目のどれに印を付けるかに使う。つなぎ先の無い行は空。</summary>
    public string Direction { get; init; } = string.Empty;

    public bool IsForward => Direction == "forward";

    public bool IsReverse => Direction == "reverse";

    public bool IsBoth => Direction == "both";

    /// <summary>向きの右クリックの項目（引数は "forward"・"reverse"・"both"）。つなぎ先の無い行は持たない（押せない）。</summary>
    public RelayCommand? SetDirectionCommand { get; init; }
}
