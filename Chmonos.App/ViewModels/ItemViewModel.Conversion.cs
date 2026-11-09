using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品ページの「変換扱い」の欄（<c>docs/spec/conversion.md</c>「画面」・ユーザ判断 2026-11-09：アバターの管理の「変換」より先に作る）。
/// その商品を変換ツールのプロファイルとして扱うかと、中のプロファイルごとのつなぎ先を、人が決める・確かめる。
/// 変換経由の計算は確認したつなぎ先だけを使う（既定）ので、ここで確かめるまで衣装の「変換経由」は出ない。
/// </summary>
public partial class ItemViewModel
{
    /// <summary>
    /// 欄を出すか。プロファイルのファイルを持っている商品と、変換扱いを既に持っている商品だけ
    /// （ユーザ判断 2026-11-06：手元にプロファイルが無いと意味を成さない。ほとんどの商品には要らない欄）。
    /// </summary>
    public bool ShowsConversion { get; private set; }

    public ConversionMode ConversionMode => Item.Local.Conversion?.Mode ?? ConversionMode.Auto;

    public bool IsConversionAuto => ConversionMode == ConversionMode.Auto;

    public bool IsConversionProfile => ConversionMode == ConversionMode.Profile;

    public bool IsConversionNotProfile => ConversionMode == ConversionMode.NotProfile;

    /// <summary>今の扱いを文で（ボタンは変える操作だけにする。アバターの管理の「アバターかどうか」と同じ作り）。</summary>
    public string ConversionStateText { get; private set; } = string.Empty;

    /// <summary>中のプロファイルごとの行（つなぎ先が決まっていない物も、選ぶ欄の付いた行として出す）。</summary>
    public IReadOnlyList<ConversionTargetRow> ConversionRows { get; private set; } = [];

    /// <summary>つなぎ先を選ぶ欄の候補（アバターと、「（共通素体）」を添えた共通素体）。</summary>
    public IReadOnlyList<string> ConversionTargetSuggestions { get; private set; } = [];

    /// <summary>書けなかったときだけ出る1行。</summary>
    public AreaNotice ConversionNotice { get; } = new();

    /// <summary>扱いの3択。引数は "auto"・"profile"・"notProfile"。</summary>
    public RelayCommand SetConversionModeCommand => _setConversionModeCommand ??= new RelayCommand(
        parameter =>
        {
            ConversionMode? mode = parameter as string switch
            {
                "auto" => ConversionMode.Auto,
                "profile" => ConversionMode.Profile,
                "notProfile" => ConversionMode.NotProfile,
                _ => null,
            };
            if (mode is { } chosen && chosen != ConversionMode)
            {
                ChangeConversionAsync(ConversionEdits.SetMode(chosen)).Forget();
            }
            else
            {
                // 押した区画だけ先に点いたままにならないよう、今の値に合わせ直す
                RaiseConversionMode();
            }
        },
        _ => !IsEditLocked);

    private RelayCommand? _setConversionModeCommand;

    /// <summary>候補に出した共通素体の行から素体の名前を引く表（アバターと同じ名前の素体を取り違えない）。</summary>
    private Dictionary<string, string> _conversionBasesBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    private Task ChangeConversionAsync(Func<ConversionSetting?, ConversionSetting?> change)
        => RunItemCommandAsync(new UiCommand.ChangeConversion(Item.Id, change), ConversionNotice);

    private void BuildConversion(AvatarRegistry registry, IReadOnlyDictionary<string, string> names)
    {
        var setting = Item.Local.Conversion;
        var profiles = ConversionScan.MayHoldProfiles(Item) ? _services.Conversion.ProfilesOf(Item) : [];
        ShowsConversion = profiles.Count > 0 || setting is not null;

        if (!ShowsConversion)
        {
            ConversionRows = [];
            ConversionStateText = string.Empty;
            RaiseConversion();
            return;
        }

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

        ConversionRows = rows;
        ConversionStateText = (setting?.Mode ?? ConversionMode.Auto) switch
        {
            ConversionMode.Profile => "今の扱い：プロファイルとして扱う（手動で指定）",
            ConversionMode.NotProfile => "今の扱い：プロファイルとして扱わない（手動で指定）",
            _ => profiles.Count > 0
                ? "今の扱い：プロファイル（自動の判定）"
                : "今の扱い：プロファイルではない（自動の判定）",
        };

        _conversionBasesBySuggestion = registry.BaseGroups
            .Where(group => !group.Rejected && group.Name.Trim().Length > 0)
            .Select(group => group.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(name => $"{name}（共通素体）", name => name, StringComparer.CurrentCultureIgnoreCase);
        ConversionTargetSuggestions = registry.Entries
            .Where(AvatarService.IsAvatar)
            .Select(entry => names[entry.ItemId])
            .Concat(_conversionBasesBySuggestion.Keys)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        RaiseConversion();
    }

    private ConversionTargetRow EmptyRow(ProfileSlug profile) => new()
    {
        ProfileText = profile.Slug,
        DirectionText = DirectionText(profile.Forward, profile.Reverse),
        ChooseCommand = new RelayCommand(
            parameter => ChooseConversionTargetAsync(profile, parameter as string).Forget(),
            _ => !IsEditLocked),
    };

    private ConversionTargetRow TargetRow(ConversionTarget target, string name) => new()
    {
        ProfileText = target.Slug ?? string.Empty,
        DirectionText = DirectionText(target.Forward, target.Reverse),
        TargetText = name,
        HasTarget = true,
        IsUnconfirmed = !target.Confirmed,
        ConfirmCommand = new RelayCommand(
            () => ChangeConversionAsync(ConversionEdits.Confirm(target)).Forget(),
            () => !IsEditLocked && !target.Confirmed),
        RemoveCommand = new RelayCommand(
            () => ChangeConversionAsync(ConversionEdits.Reject(target)).Forget(),
            () => !IsEditLocked),
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
                    ChangeConversionAsync(ConversionEdits.SetDirection(target, direction.Forward, direction.Reverse)).Forget();
                }
            },
            _ => !IsEditLocked),
    };

    /// <summary>選ぶ欄で決めたつなぎ先を書く。候補に無い名前は何もしない（候補から選ぶ欄）。</summary>
    private Task ChooseConversionTargetAsync(ProfileSlug profile, string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (_conversionBasesBySuggestion.TryGetValue(trimmed, out var baseName))
        {
            return ChangeConversionAsync(ConversionEdits.Choose(profile.Slug, profile.Forward, profile.Reverse, null, baseName));
        }

        return FindAvatarByName(trimmed) is { } avatar
            ? ChangeConversionAsync(ConversionEdits.Choose(profile.Slug, profile.Forward, profile.Reverse, avatar.ItemId, null))
            : Task.CompletedTask;
    }

    private static string DirectionText(bool forward, bool reverse) => (forward, reverse) switch
    {
        (true, true) => "両方向",
        (true, false) => "順方向",
        (false, true) => "逆方向",
        _ => "向きが分かりません",
    };

    private void RaiseConversionMode()
    {
        OnPropertyChanged(nameof(ConversionMode));
        OnPropertyChanged(nameof(IsConversionAuto));
        OnPropertyChanged(nameof(IsConversionProfile));
        OnPropertyChanged(nameof(IsConversionNotProfile));
    }

    private void RaiseConversion()
    {
        OnPropertyChanged(nameof(ShowsConversion));
        OnPropertyChanged(nameof(ConversionStateText));
        OnPropertyChanged(nameof(ConversionRows));
        OnPropertyChanged(nameof(ConversionTargetSuggestions));
        RaiseConversionMode();
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
