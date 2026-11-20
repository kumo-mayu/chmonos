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
    private readonly Func<UiCommand, Task<bool>> _run;
    private readonly Func<bool> _isLocked;
    private readonly Services.ThumbnailLoader? _thumbnails;

    /// <summary>
    /// 商品 ID から手元の商品。絵を引く（持っていれば商品の絵）・候補の所持の群を分ける・推した理由の (b) で持っているアバターの中身を引く。
    /// 無ければどれも「持っていない」として扱う
    /// </summary>
    private readonly Func<string, ItemRecord?>? _findItem;
    private ItemRecord? _item;

    /// <param name="run">命令を通して、書いた後に <see cref="Show"/> で今の記録を渡し直す道（持ち主が持つ）。</param>
    /// <param name="isLocked">書き込めない間（商品ページの編集の錠）。</param>
    /// <param name="thumbnails">絵を読むもの（つなぎ先の札に乗せたときと、選ぶ欄の候補の頭。読むのは乗せたとき・見えた行だけ）。無ければ絵を出さない。</param>
    public ItemConversionEditor(
        AppServiceContainer services,
        Func<UiCommand, Task<bool>> run,
        Func<bool> isLocked,
        Services.ThumbnailLoader? thumbnails = null,
        Func<string, ItemRecord?>? findItem = null)
    {
        _services = services;
        _run = run;
        _isLocked = isLocked;
        _thumbnails = thumbnails;
        _findItem = findItem;
        SetModeCommand = new RelayCommand(
            parameter =>
            {
                ConversionMode? mode = (parameter as string) switch
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

    /// <summary>全部の行が同じファイルの物のとき、その1行（「入っているファイル：…」）。行ごとには出さない。</summary>
    public string CommonSourceText { get; private set; } = string.Empty;

    public bool HasCommonSource => CommonSourceText.Length > 0;

    /// <summary>今の扱いを文で（ボタンは変える操作だけにする。アバターの管理の「アバターかどうか」と同じ作り）。</summary>
    public string StateText { get; private set; } = string.Empty;

    /// <summary>中のプロファイルごとの行（つなぎ先が決まっていない物も、選ぶ欄の付いた行として出す）。</summary>
    public IReadOnlyList<ConversionTargetRow> Rows => _rows;

    /// <summary>
    /// 行は1つの一覧を持ち回し、書いた後の読み直しでは中身の変わらない行を同じ物のまま残す（ユーザ判断 2026-11-20・③：
    /// プロファイル数百の商品で、1つ確認済みにするたびに全部の行を作り直していた。画面は残った行の部品を作り直さない）
    /// </summary>
    private readonly System.Collections.ObjectModel.ObservableCollection<ConversionTargetRow> _rows = [];

    /// <summary>
    /// 近ごろ行を作った商品とその行（2つまで）。今の商品と同じ商品の控えだけを使い回し、別の商品へ替えたら一覧は空けてから作る。
    /// 2つ持つのは、アバターの管理で書いた後に左の一覧を差し替える間、ListBox が一度先頭の別の商品を選んでから元の商品へ戻るため
    /// （1つだけだと、その間に控えが別の商品の物になり、400行を全部作り直していた。2026-11-20 に測った）
    /// </summary>
    private readonly List<(string ItemId, List<ConversionTargetRow> Rows)> _recentRows = [];

    /// <summary>今の一覧の商品（替わったら一覧を空けてから寄せる）。</summary>
    private string? _rowsItemId;

    /// <summary>新しく作った行のうち、控えと中身が同じ物は控えの行に置き換えて一覧へ寄せる。</summary>
    private void ApplyRows(string? itemId, List<ConversionTargetRow> fresh)
    {
        if (!string.Equals(itemId, _rowsItemId, StringComparison.Ordinal))
        {
            _rows.Clear();
        }

        _rowsItemId = itemId;
        if (itemId is null)
        {
            return;
        }

        var cached = _recentRows.FindIndex(entry => string.Equals(entry.ItemId, itemId, StringComparison.Ordinal));
        if (cached >= 0)
        {
            var old = _recentRows[cached].Rows.GroupBy(row => row.Signature)
                .ToDictionary(group => group.Key, group => new Queue<ConversionTargetRow>(group), StringComparer.Ordinal);
            for (var index = 0; index < fresh.Count; index++)
            {
                if (old.TryGetValue(fresh[index].Signature, out var same) && same.Count > 0)
                {
                    fresh[index] = same.Dequeue();
                }
            }

            _recentRows.RemoveAt(cached);
        }

        _recentRows.Insert(0, (itemId, fresh));
        if (_recentRows.Count > 2)
        {
            _recentRows.RemoveAt(2);
        }

        CollectionSync.Apply(_rows, fresh);
    }

    /// <summary>つなぎ先を選ぶ欄の候補（アバターと、「（共通素体）」を添えた共通素体）。</summary>
    public IReadOnlyList<string> TargetSuggestions { get; private set; } = [];

    /// <summary>確認待ちのつなぎ先の数（人が外した物は数えない）。</summary>
    public int UnconfirmedCount => Rows.Count(row => row.IsUnconfirmed);

    /// <summary>つなぎ先の決まっていないプロファイルの数。</summary>
    public int UnsetCount => Rows.Count(row => !row.HasTarget);

    /// <summary>今見せている確認待ちのつなぎ先（「確認済みにして次へ」が名指しする行。見せた物だけを確かめる）。</summary>
    public IReadOnlyList<ConversionTarget> UnconfirmedTargets { get; private set; } = [];

    /// <summary>人が外したつなぎ先（畳んだ「外したつなぎ先」の一覧。行ごとに「戻す」）。</summary>
    public IReadOnlyList<RejectedConversionRow> RejectedRows { get; private set; } = [];

    public bool HasRejected => RejectedRows.Count > 0;

    public string RejectedHeader => $"外したつなぎ先（{RejectedRows.Count}）";

    /// <summary>書けなかったときだけ出る1行。</summary>
    public AreaNotice Notice { get; } = new();

    /// <summary>扱いの3択。引数は "auto"・"profile"・"notProfile"。</summary>
    public RelayCommand SetModeCommand { get; }

    /// <summary>候補に出した共通素体の行から素体の名前を引く表（アバターと同じ名前の素体を取り違えない）。</summary>
    private Dictionary<string, string> _basesBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>候補に出したアバターの名前から商品 ID を引く表（候補の頭の絵）。</summary>
    private Dictionary<string, string> _avatarIdsBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    private Dictionary<string, Controls.SuggestInfo> _targetInfo = new(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>つなぎ先の候補の群と、名前以外で当たる語（呼び方・正式名）。</summary>
    public Func<string, Controls.SuggestInfo?> TargetInfoSelector => text => _targetInfo.GetValueOrDefault(text);

    public IReadOnlyList<string> TargetGroupHeadings => AvatarSuggestionText.Headings;

    /// <summary>
    /// つなぎ先の候補の頭の絵（商品ページの「対応アバターを追加」の欄と同じ大きさ）。素体かは確定の時と同じ表で先に見る
    /// （同じ名前のアバターと取り違えない）
    /// </summary>
    public Func<string, System.Windows.Media.ImageSource?> TargetIconSelector => text =>
        _thumbnails is not { } thumbnails ? null
        : _basesBySuggestion.TryGetValue(text, out var baseName) ? AvatarBaseIcons.Load(_services, baseName, FindItem, thumbnails.LoadForTile)
        : _avatarIdsBySuggestion.TryGetValue(text, out var id) ? AvatarIcon(id, thumbnails.LoadForTile)
        : null;

    private ItemRecord? FindItem(string itemId) => _findItem?.Invoke(itemId);

    /// <summary>アバターの絵（持っていれば商品の1枚目、持っていなければ控えの1枚。ほかのアバターの候補と同じ）。</summary>
    private System.Windows.Media.Imaging.BitmapSource? AvatarIcon(
        string avatarItemId, Func<string, System.Windows.Media.Imaging.BitmapSource?> load)
        => AvatarImageSync.IconPath(_services.Paths, avatarItemId, FindItem(avatarItemId), _services.Settings.ShowRemovedBoothImages) is { } path
            ? load(path)
            : null;

    private string? ItemId => _item?.Id;

    /// <summary>
    /// 変え方を、今の商品の変換扱いに当てる命令として通す。書けたら true、保存に失敗したら false
    /// （呼び手が「次の商品へ進む」かを決めるため。失敗の知らせは <see cref="Notice"/> に出し済み）。見せている商品が無ければ書く物が無いので true。
    /// </summary>
    public Task<bool> ChangeAsync(Func<ConversionSetting?, ConversionSetting?> change)
        => ItemId is { } id ? _run(new UiCommand.ChangeConversion(id, change)) : Task.FromResult(true);

    /// <summary>見せる商品を替える（書いた後の読み直し・一覧で選び直したとき）。登録簿は読むだけなので直に読む。</summary>
    public void Show(ItemRecord? item, AvatarRegistry? registry = null, IReadOnlyDictionary<string, string>? names = null)
    {
        _item = item;
        var setting = item?.Local.Conversion;
        var profiles = item is not null && ConversionScan.MayHoldProfiles(item) ? _services.Conversion.ProfilesOf(item) : [];
        ShowsConversion = item is not null && (profiles.Count > 0 || setting is not null);

        if (!ShowsConversion)
        {
            ApplyRows(null, []);
            CommonSourceText = string.Empty;
            UnconfirmedTargets = [];
            RejectedRows = [];
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

        // 札に乗せたときの絵：アバターはその商品の絵、共通素体は素体の絵（付けた画像 → 素体の商品の1枚目。AvatarBaseIcons）。
        // 前は素体の商品が無いと仲間のアバターの1体目を出していたが、素体の絵の決め方は1つにする（152 の続き）。どちらも無ければ絵は出さない
        Func<System.Windows.Media.ImageSource?>? IconFactory(ConversionTarget target)
        {
            if (_thumbnails is not { } thumbnails)
            {
                return null;
            }

            return target.BaseName is { } baseName
                ? () => AvatarBaseIcons.Load(_services, baseName, FindItem, thumbnails.LoadForCard)
                : target.AvatarItemId is { } id ? () => AvatarIcon(id, thumbnails.LoadForCard) : null;
        }

        // 入っているファイル（slug ごと）。控えを読むのは、プロファイルを持つかもしれない商品だけ（ProfilesOf と同じふるい）
        var sources = item is not null && ConversionScan.MayHoldProfiles(item)
            ? _services.Conversion.ProfileSourcesOf(item)
                .GroupBy(source => source.Slug, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => SourceText(group.ToList()), StringComparer.OrdinalIgnoreCase)
            : [];

        // 推した理由（アバターの管理の「変換」だけ・ユーザ判断 2026-11-09）。見回りと同じ推し方をその場で回し、手掛かりを文にする
        var suggestions = ShowsReasons && item is not null
            ? ConversionInference.Suggest(item, profiles, registry, AvatarCompatibilityIndex.For(registry),
                _findItem is { } find ? _services.Conversion.FbxAvatarsOf(item, _services.Conversion.FbxIndex(registry, find)) : null)
                .ToDictionary(suggestion => suggestion.Profile.Slug, StringComparer.OrdinalIgnoreCase)
            : [];

        string ReasonOf(ConversionTarget target)
            => target.Slug is { } slug
                && suggestions.TryGetValue(slug, out var suggestion)
                && suggestion.Target is { } suggested
                && ConversionTarget.SameRow(suggested, target)
                    ? "見つけた理由：" + CluesText(suggestion.Clues, names)
                    : string.Empty;

        // つなぎ先の無い行の訳。推せる手掛かりがあっても、人が外した物なら外したと言う（見回りが推さないのはそのため）。
        // 外してもいないのに推せる物は、まだ見回りが通っていない（取り込みの途中など）ので、推せる先を言う
        string EmptyReason(ProfileSlug profile)
        {
            if (!ShowsReasons || !suggestions.TryGetValue(profile.Slug, out var suggestion))
            {
                return string.Empty;
            }

            if (suggestion.Conflicting)
            {
                return "つなぎ先は見つかりませんでした：読み取った情報が食い違います・" + CluesText(suggestion.Clues, names);
            }

            if (suggestion.Target is not { } suggested)
            {
                return "つなぎ先は見つかりませんでした：元になる情報がありません";
            }

            var rejected = (setting?.Targets ?? []).Any(target => target.Rejected && ConversionTarget.SameRow(target, suggested));
            return rejected
                ? $"外したつなぎ先：{TargetName(suggested)}・見つけた理由：{CluesText(suggestion.Clues, names)}"
                : $"見つけたつなぎ先：{TargetName(suggested)}・理由：{CluesText(suggestion.Clues, names)}";
        }

        // 人が外したつなぎ先は出さない（そのプロファイルは選ぶ欄に戻る）。行は保存に残り、見回りが同じ物を推すのを止める
        var targets = (setting?.Targets ?? []).Where(target => !target.Rejected).ToList();
        var rows = new List<ConversionTargetRow>();
        foreach (var profile in profiles)
        {
            var mine = targets.Where(target => string.Equals(target.Slug, profile.Slug, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine.Count == 0)
            {
                rows.Add(EmptyRow(profile, EmptyReason(profile)));
            }

            rows.AddRange(mine.Select(target => TargetRow(target, TargetName(target), ReasonOf(target), IconFactory(target), profile)));
        }

        // 中のファイルから見つからない（zip を外した・slug の分からない）つなぎ先も、外せるように出す
        rows.AddRange(targets
            .Where(target => !profiles.Any(profile => string.Equals(profile.Slug, target.Slug, StringComparison.OrdinalIgnoreCase)))
            .Select(target => TargetRow(target, TargetName(target), string.Empty, IconFactory(target), null)));

        foreach (var row in rows)
        {
            row.SourceText = sources.GetValueOrDefault(row.ProfileText) ?? string.Empty;
        }

        // 全部の行が同じファイルの物なら、見出しの下に1回だけ出す（同じ文が行ごとに並ぶと読みにくい。ほとんどの商品はファイル1つ）
        var distinctSources = rows.Select(row => row.SourceText).Where(text => text.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        CommonSourceText = distinctSources.Count == 1 && rows.All(row => row.SourceText.Length > 0) ? distinctSources[0] : string.Empty;
        if (CommonSourceText.Length > 0)
        {
            foreach (var row in rows)
            {
                row.SourceText = string.Empty;
            }
        }

        ApplyRows(item?.Id, rows);
        UnconfirmedTargets = targets.Where(target => !target.Confirmed).ToList();

        // 外したつなぎ先は、畳んだ一覧に出して戻せるようにする（ユーザ判断 2026-11-09。対応アバターの「消したもの」と同じ形）
        RejectedRows = (setting?.Targets ?? [])
            .Where(target => target.Rejected && !target.IsEmpty)
            .Select(target => new RejectedConversionRow
            {
                ProfileText = target.Slug ?? string.Empty,
                TargetText = TargetName(target),
                RestoreCommand = new RelayCommand(() => ChangeAsync(ConversionEdits.Restore(target)).Forget(), () => !_isLocked()),
            })
            .ToList();
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

        // 候補の群（所持アバター／共通素体／未所持アバター）・呼び方・絵は、商品ページの「対応アバターを追加」の欄と同じ作り
        // （ユーザ指示 2026-11-18「変換のアバターや共通素体を探す入力欄もそれぞれのアイコンを表示する形が良い」）。並びは欄が群の順に寄せる
        _avatarIdsBySuggestion = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        _targetInfo = new Dictionary<string, Controls.SuggestInfo>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in registry.Entries.Where(AvatarService.IsAvatar))
        {
            var name = names[entry.ItemId];
            if (_avatarIdsBySuggestion.TryAdd(name, entry.ItemId))
            {
                var group = entry.IsOwnedManually || FindItem(entry.ItemId)?.IsOwned == true
                    ? AvatarSuggestionText.OwnedGroup
                    : AvatarSuggestionText.OtherGroup;
                _targetInfo[name] = AvatarSuggestionText.InfoOf(entry, name, group);
            }
        }

        foreach (var baseText in _basesBySuggestion.Keys)
        {
            _targetInfo[baseText] = new Controls.SuggestInfo(AvatarSuggestionText.BaseGroup, []);
        }

        TargetSuggestions = _avatarIdsBySuggestion.Keys
            .Concat(_basesBySuggestion.Keys)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        Raise();
    }

    /// <summary>
    /// 推した理由を行の下に出すか（アバターの管理の「変換」で入れる。ユーザ判断 2026-11-09「こちらの画面には推した理由は出しておきましょう」）。
    /// 商品ページでは出さない（1件を見に来た所で、決める材料を並べる画面ではない）。
    /// </summary>
    public bool ShowsReasons { get; init; }

    /// <summary>商品の1枚目の絵を引く道（アバターの管理と同じ引き方。検索の写しに無い商品は絵が無い）。</summary>
    public static Func<string, System.Windows.Media.ImageSource?> IconsFrom(AppServiceContainer services, MainViewModel main)
        => itemId => AvatarImageSync.IconPath(services.Paths, itemId, main.Search.FindItem(itemId), services.Settings.ShowRemovedBoothImages) is { } path
            ? main.Thumbnails.LoadForCard(path)
            : null;

    /// <summary>
    /// 入っているファイルを1行に：「profile.zip の Profile.unitypackage」（unitypackage をそのまま置いた物は名前1つ）。
    /// 同じプロファイルが何個もの unitypackage に入っていれば、1つ目と「ほか n 個」（FBX の場所だけ違えて配る物で、全部並べると長い）。
    /// </summary>
    internal static string SourceText(IReadOnlyList<ProfileSource> sources)
    {
        var places = sources
            .Select(source => string.IsNullOrEmpty(source.FileName) || string.Equals(source.FileName, source.PackageName, StringComparison.OrdinalIgnoreCase)
                ? source.PackageName
                : $"{source.FileName} の {source.PackageName}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return places.Count switch
        {
            0 => string.Empty,
            1 => "入っているファイル：" + places[0],
            _ => $"入っているファイル：{places[0]} ほか {places.Count - 1} 個",
        };
    }

    /// <summary>手掛かりを「商品の対応素体「A」・この商品がアバター本体」の形で並べる（同じ文は1つにまとめる）。</summary>
    internal static string CluesText(IReadOnlyList<ConversionClue> clues, IReadOnlyDictionary<string, string> names)
    {
        string Avatar(string? id) => id is not null && names.TryGetValue(id, out var name) ? name : id ?? string.Empty;
        return string.Join("・", clues
            .Select(clue => clue.Kind switch
            {
                ConversionClueKind.ItemIsAvatar => "この商品がアバター本体",
                ConversionClueKind.FbxAvatar => $"FBX の場所が「{Avatar(clue.AvatarItemId)}」と同じ",
                ConversionClueKind.DetectedBase => $"商品の対応素体「{clue.BaseName}」",
                ConversionClueKind.DetectedAvatar => $"商品の対応アバター「{Avatar(clue.AvatarItemId)}」",
                ConversionClueKind.SlugIsBaseName => $"プロファイルの名前が素体「{clue.BaseName}」と同じ",
                _ => string.Empty,
            })
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.Ordinal));
    }

    private ConversionTargetRow EmptyRow(ProfileSlug profile, string reason) => new()
    {
        ProfileText = profile.Slug,
        DirectionText = DirectionText(profile.Forward, profile.Reverse),
        FileDirection = DirectionCode(profile.Forward, profile.Reverse),
        ReasonText = reason,
        ChooseCommand = new RelayCommand(
            parameter => ChooseAsync(profile, parameter as string).Forget(),
            _ => !_isLocked()),
    };

    /// <param name="fileProfile">中のファイルから読んだ同じ slug のプロファイル（既定の向き）。中のファイルから見つからない行は null。</param>
    private ConversionTargetRow TargetRow(
        ConversionTarget target, string name, string reason, Func<System.Windows.Media.ImageSource?>? iconFactory, ProfileSlug? fileProfile) => new()
    {
        ProfileText = target.Slug ?? string.Empty,
        FileDirection = fileProfile is null ? string.Empty : DirectionCode(fileProfile.Forward, fileProfile.Reverse),
        DirectionText = DirectionText(target.Forward, target.Reverse),
        TargetText = name,
        TargetKey = target.BaseName is { } baseKey ? "base:" + baseKey : "avatar:" + target.AvatarItemId,
        ReasonText = reason,
        IconFactory = iconFactory,
        TooltipText = target.Confirmed ? name : $"{name}：自動で見つけたつなぎ先です。確認すると変換経由に使います。",
        HasTarget = true,
        IsUnconfirmed = !target.Confirmed,
        ConfirmCommand = new RelayCommand(
            () => ChangeAsync(ConversionEdits.Confirm(target)).Forget(),
            () => !_isLocked() && !target.Confirmed),
        RemoveCommand = new RelayCommand(
            () => ChangeAsync(ConversionEdits.Reject(target)).Forget(),
            () => !_isLocked()),
        Direction = DirectionCode(target.Forward, target.Reverse),

        // 向きはふつう中のファイルの名前で決まるので、変える口は右クリックだけに置く（ユーザ判断 2026-11-09「普通は使わない」）
        SetDirectionCommand = new RelayCommand(
            parameter =>
            {
                (bool Forward, bool Reverse)? chosen = (parameter as string) switch
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
    private Task<bool> ChooseAsync(ProfileSlug profile, string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (_basesBySuggestion.TryGetValue(trimmed, out var baseName))
        {
            return ChangeAsync(ConversionEdits.Choose(profile.Slug, profile.Forward, profile.Reverse, null, baseName));
        }

        return FindAvatarByName(trimmed) is { } avatar
            ? ChangeAsync(ConversionEdits.Choose(profile.Slug, profile.Forward, profile.Reverse, avatar.ItemId, null))
            : Task.FromResult(true);
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

    /// <summary>右クリックの項目の引数と同じ字（"forward"・"reverse"・"both"）。向きが無ければ空。</summary>
    private static string DirectionCode(bool forward, bool reverse) => (forward, reverse) switch
    {
        (true, true) => "both",
        (true, false) => "forward",
        (false, true) => "reverse",
        _ => string.Empty,
    };

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
        OnPropertyChanged(nameof(CommonSourceText));
        OnPropertyChanged(nameof(HasCommonSource));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(TargetSuggestions));
        OnPropertyChanged(nameof(UnconfirmedCount));
        OnPropertyChanged(nameof(UnsetCount));
        OnPropertyChanged(nameof(RejectedRows));
        OnPropertyChanged(nameof(HasRejected));
        OnPropertyChanged(nameof(RejectedHeader));
        RaiseMode();

        // 持ち主のボタン（確認済みにして次へ）が、確認待ちの有無で押せるかを変えるため
        RelayCommand.RaiseCanExecuteChanged();
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

    /// <summary>推した理由（アバターの管理の「変換」だけ）。無ければ空。</summary>
    public string ReasonText { get; init; } = string.Empty;

    /// <summary>
    /// プロファイルが入っているファイル（「profile.zip の Profile.unitypackage」。ユーザ指示 2026-11-09：アバターとプロファイルのセット商品で、
    /// アバター扱いと変換扱いのどちらの話か分かりにくかった）。中のファイルから見つからない行は空。
    /// </summary>
    public string SourceText { get; internal set; } = string.Empty;

    public bool HasSource => SourceText.Length > 0;

    public bool HasReason => ReasonText.Length > 0;

    /// <summary>札に乗せたときの絵を読む（乗せたときだけ読む。行ごとに先に読むと、18 個入りの商品で開くのが遅れる）。</summary>
    public Func<System.Windows.Media.ImageSource?>? IconFactory { get; init; }

    /// <summary>札に乗せたときの字（名前と、確認待ちなら何をすれば使われるか）。</summary>
    public string TooltipText { get; init; } = string.Empty;

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

    /// <summary>
    /// 中のファイルの名前どおりの向き（既定。<c>conversion.md</c>「向き」）。中のファイルから見つからない行は空（既定が分からない）。
    /// 人が向きを変えた印は持たない。ファイルの向きは毎回読み直せるので、今の向きと比べれば変えたかが分かる（データの形を増やさずに済む）
    /// </summary>
    public string FileDirection { get; init; } = string.Empty;

    /// <summary>右クリックの項目の字。既定の向きに「（既定）」を添える（2026-11-18 のメモ：どれが既定か分からなかった）。</summary>
    public string ForwardHeader => Header("順方向", "forward");

    public string ReverseHeader => Header("逆方向", "reverse");

    public string BothHeader => Header("両方向", "both");

    /// <summary>今の向きが既定と違う（人が右クリックで変えた）。「既定に戻す」を出す。</summary>
    public bool IsDirectionChanged => FileDirection.Length > 0 && Direction.Length > 0 && Direction != FileDirection;

    private string Header(string text, string direction) => FileDirection == direction ? $"{text}（既定）" : text;

    /// <summary>向きの右クリックの項目（引数は "forward"・"reverse"・"both"）。つなぎ先の無い行は持たない（押せない）。</summary>
    public RelayCommand? SetDirectionCommand { get; init; }

    /// <summary>
    /// 中身の印。読み直しで同じ印の行は前の行を残す（<see cref="ItemConversionEditor"/>）。命令が掴むつなぎ先は、画面に出す値
    /// （名前・向き・確認待ちか）と同じなら同じ物なので、印は画面に出す値で作る。端（ID か素体の名前）は名前が同じ別の物を分けるために入れる
    /// </summary>
    internal string Signature => string.Join(
        "\u001f", ProfileText, DirectionText, TargetText, TargetKey, ReasonText, SourceText, TooltipText, Direction, FileDirection,
        HasTarget ? "1" : "0", IsUnconfirmed ? "1" : "0");

    /// <summary>つなぎ先の端（"avatar:ID"・"base:名前"）。決まっていない行は空。</summary>
    public string TargetKey { get; init; } = string.Empty;
}

/// <summary>「外したつなぎ先」の一覧の1行。</summary>
public sealed class RejectedConversionRow
{
    public string ProfileText { get; init; } = string.Empty;

    public string TargetText { get; init; } = string.Empty;

    public RelayCommand? RestoreCommand { get; init; }
}
