using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Chmonos.App.Controls;
using Chmonos.App.Services;
using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using BoothZipInspector;

namespace Chmonos.App.ViewModels;

/// <summary>商品ページ：対応アバター（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class ItemViewModel
{
    /// <summary>出品者が宣言している対応アバター。こちらは編集しない。</summary>
    public IReadOnlyList<AvatarRow> Avatars { get; private set; } = [];

    /// <summary>
    /// ユーザが消した対応アバター（ユーザ判断 2026-09-12）。以前は消すと画面のどこにも出ず、
    /// 消したことも戻せることも分からなかった（戻す道は名前を打ち直すことだけだった）。
    /// </summary>
    public IReadOnlyList<RejectedAvatarRow> RejectedAvatars { get; private set; } = [];

    public bool HasRejectedAvatars => RejectedAvatars.Count > 0;

    public string RejectedAvatarsHeader => $"消したもの {RejectedAvatars.Count} 件";

    private static bool s_rejectedAvatarsExpanded;

    /// <summary>「消したもの」を開いているか。既定は畳む。商品を移っても保つ（アプリを閉じるまで）。</summary>
    public bool IsRejectedAvatarsExpanded
    {
        get => s_rejectedAvatarsExpanded;
        set
        {
            if (s_rejectedAvatarsExpanded != value)
            {
                s_rejectedAvatarsExpanded = value;
                OnPropertyChanged(nameof(IsRejectedAvatarsExpanded));
            }
        }
    }

    public bool HasAvatars => Avatars.Count > 0;

    /// <summary>
    /// 札を絞り込む欄を出す境目（U26）。
    /// 20体までなら札は4〜5行に収まり、目で追える。それを超えると探す手間の方が大きい
    /// （友人のデータには248体を宣言している商品がある）。
    /// </summary>
    private const int AvatarFilterThreshold = 20;

    private string _avatarFilter = string.Empty;

    /// <summary>アバター名で札を絞る（U26）。ひらがな・カタカナ、全角・半角、大文字・小文字は区別しない。</summary>
    public string AvatarFilter
    {
        get => _avatarFilter;
        set
        {
            if (SetField(ref _avatarFilter, value ?? string.Empty))
            {
                ApplyAvatarFilter();
            }
        }
    }

    /// <summary>画面に出す札。絞り込み欄に何か入っていれば、名前が一致するものだけ。</summary>
    public IReadOnlyList<AvatarRow> VisibleAvatars { get; private set; } = [];

    /// <summary>
    /// 並べる札に、末尾の「＋ 追加」を混ぜたもの（ユーザ指示 2026-09-12）。画像の一覧の「足す」枠と同じく、
    /// 同じ並びに混ぜると折り返しても末尾に付いてくる。札が0件でも「＋ 追加」は出る。
    /// 作った札は隠すだけで捨てない（<see cref="ChipStrip{TSource}"/>・2回目以降に全部並べるのを速くする）
    /// </summary>
    public ObservableCollection<object> AvatarTiles => _avatarStrip.Tiles;

    private readonly ChipStrip<AvatarRow> _avatarStrip = new(row => row, "体", "対応アバター", AvatarAddTile.Instance);

    /// <summary>対応アバターの見出しに添える件数。畳んでいても何体あるかは分かるように。</summary>
    public string AvatarsCountText => Avatars.Count == 0 ? string.Empty : $"（{Avatars.Count} 体）";

    /// <summary>
    /// 説明文から読み取っただけで、まだ確かめていない推定の数（ユーザ判断 2026-09-18）。
    /// 対応とは数えていないので、確かめるまで検索にも統計にも出てこない。畳んでいても気付けるように見出しに出す
    /// </summary>
    public int UnconfirmedAvatarCount => Avatars.Count(row => row.IsUnconfirmed);

    public bool HasUnconfirmedAvatars => UnconfirmedAvatarCount > 0;

    public string UnconfirmedAvatarText => $"確認待ち {UnconfirmedAvatarCount}";

    /// <summary>
    /// 対応アバターの欄を開いているか（ユーザ指示 2026-09-12：200体を超える商品があるので畳める）。
    /// 商品ページと編集画面で共通で、商品を移っても保つ（アプリを閉じるまで）。
    /// </summary>
    public bool IsAvatarsExpanded
    {
        get => SectionFolds.AvatarsExpanded;
        set
        {
            if (SectionFolds.AvatarsExpanded != value)
            {
                SectionFolds.AvatarsExpanded = value;
                OnPropertyChanged(nameof(IsAvatarsExpanded));

                // 畳んでいる間は札を作らない。開いたときに作る。畳んでも作った札は捨てない
                _avatarStrip.SetExpanded(value);
            }
        }
    }

    private bool _isAddingAvatar;

    /// <summary>「＋ 追加」を押して、足す入力欄を出しているか。商品を移ると畳んだ状態に戻る。</summary>
    public bool IsAddingAvatar
    {
        get => _isAddingAvatar;
        private set => SetField(ref _isAddingAvatar, value);
    }

    private RelayCommand? _startAddAvatarCommand;
    private RelayCommand? _stopAddAvatarCommand;

    public RelayCommand StartAddAvatarCommand => _startAddAvatarCommand ??= new RelayCommand(
        () => IsAddingAvatar = true,
        () => !IsEditLocked);

    public RelayCommand StopAddAvatarCommand => _stopAddAvatarCommand ??= new RelayCommand(() => IsAddingAvatar = false);

    public bool ShowsAvatarFilter => Avatars.Count > AvatarFilterThreshold;

    public string AvatarFilterPlaceholder => $"アバター名で絞る（{Avatars.Count} 体）";

    /// <summary>絞った結果の件数。0件のときに「無い」のか「絞り過ぎ」なのかを分ける。</summary>
    public string AvatarFilterResultText => _avatarFilter.Trim().Length == 0
        ? string.Empty
        : VisibleAvatars.Count == 0
            ? "一致するアバターはありません"
            : $"{Avatars.Count} 体中 {VisibleAvatars.Count} 体";

    private void ApplyAvatarFilter()
    {
        var needle = _avatarFilter.Trim();
        var compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
        const System.Globalization.CompareOptions options = System.Globalization.CompareOptions.IgnoreCase
            | System.Globalization.CompareOptions.IgnoreKanaType
            | System.Globalization.CompareOptions.IgnoreWidth;

        Func<AvatarRow, bool>? match = needle.Length == 0
            ? null
            : row => compare.IndexOf(row.Name, needle, options) >= 0;
        VisibleAvatars = match is null ? Avatars : Avatars.Where(match).ToList();

        // 札を並べるのは画面の中で重い（友人データの244体の商品で、札だけで約550ms・ユーザ指摘 2026-09-12）。
        // 畳んでいる間は作らず、多い商品は最初の一部と「残り n 体を表示」だけ（持っているアバターが先頭に来る並びのまま切る）。
        // 絞り込んでいるときは、探しているのだから一致したものを全部並べる。
        // 一致しない札も隠すだけなので、絞り込みを打ち替えても作り直さない
        _avatarStrip.Filter(match);

        OnPropertyChanged(nameof(VisibleAvatars));
        OnPropertyChanged(nameof(AvatarFilterResultText));
    }

    /// <summary>並べる対応アバターが同じか（札に出る中身で比べる）。</summary>
    private static bool SameAvatars(IReadOnlyList<AvatarRow> a, IReadOnlyList<AvatarRow> b)
        => a.Count == b.Count
            && a.Zip(b).All(pair => pair.First.ItemId == pair.Second.ItemId
                && pair.First.Name == pair.Second.Name
                && pair.First.SourceText == pair.Second.SourceText
                && pair.First.IsUnconfirmed == pair.Second.IsUnconfirmed
                && pair.First.IsOwned == pair.Second.IsOwned);

    /// <summary>この商品が名指ししている共通素体。</summary>
    public IReadOnlyList<AvatarBaseRow> AvatarBases { get; private set; } = [];

    public bool HasAvatarBases => AvatarBases.Count > 0;

    /// <summary>
    /// 説明文に「〇〇共通素体」と書かれていて、この商品にまだ付いていない素体（ユーザ判断 2026-09-29）。
    /// 否定の書き方の揺れを規則で吸収しきれないので、自動では入れずに候補として見せ、押したときだけ入れる
    /// </summary>
    public IReadOnlyList<BaseMentionRow> BaseMentions { get; private set; } = [];

    public bool HasBaseMentions => BaseMentions.Count > 0;

    private (string Id, string? Description, DateTime HtmlWritten, AvatarDetector.ParsedDescription Parsed)? _parsedDescription;

    /// <summary>
    /// 説明を見出しで区切った物。何か保存するたびに <see cref="BuildAvatars"/> を通るので、
    /// 説明が変わっていなければ前に区切った物を使う（h2.html を読んで正規表現を何本も当て直さない）
    /// </summary>
    private AvatarDetector.ParsedDescription ParsedDescription()
    {
        var path = _services.Paths.ItemHtmlFile(Item.Id);
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_parsedDescription is { } cached
            && cached.Id == Item.Id && cached.Description == Item.Booth.Description && cached.HtmlWritten == written)
        {
            return cached.Parsed;
        }

        string? html = null;
        try
        {
            html = written == DateTime.MinValue ? null : Core.Storage.JsonStore.ReadText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 読めなくても候補が出ないだけ。平文の説明からは拾う
        }

        var parsed = AvatarDetector.Parse(html, Item.Booth.Description);
        _parsedDescription = (Item.Id, Item.Booth.Description, written, parsed);
        return parsed;
    }

    /// <summary>
    /// 説明文の素体の候補を、この商品の共通素体に入れる。
    /// 一覧に無い素体は、先に一覧へ足す（一覧に無い素体を商品に付けても、検索でつなぐ先が無い）。
    /// </summary>
    private async Task AddMentionedBaseAsync(AvatarBaseMention mention)
    {
        if (!mention.IsRegistered)
        {
            try
            {
                var result = await _services.Commands.ExecuteAsync(new UiCommand.AddBase(mention.Name));
                if (result is CommandResult.Failed failed)
                {
                    AvatarsNotice.Warn(failed.Message);
                    return;
                }
            }
            catch (Exception exception)
            {
                Core.Diagnostics.AppLog.Error("商品ページ：説明文の共通素体を一覧に追加", exception);
                AvatarsNotice.Warn($"共通素体を追加できませんでした。{Core.Services.FailureText.Cause(exception)}");
                return;
            }
        }

        await SaveLocalAsync(
            Item.Local with { AvatarBases = AvatarService.WithManualBaseLink(Item.Local.AvatarBases, mention.Name) },
            LocalOwners.AvatarBases);
    }

    public string AvatarSectionNote => HasAvatars || HasAvatarBases
        ? "出品者が対応と書いているアバターです。"
        : "出品者の対応表明は見つかっていません。アバターの管理から検出できます。";

    /// <summary>
    /// 対応アバターまわりを組み立てる。
    ///
    /// **ここは出品者の宣言（Avatars / AvatarBases）だけ。**
    /// 自分が着せた記録は改変（着せ替え1つ）を単位に持つことにしたので、
    /// 「この商品を使った改変」のカードに分けてある。
    /// 混ぜると「誰が言っていることなのか」が分からなくなる。
    /// </summary>
    private void BuildAvatars()
    {
        // 変わっていなければ前に読んだ物（商品ページを開くたびに登録簿を読み直していた）
        var registry = _services.CachedAvatars.Load();
        var names = AvatarNames.Map(registry.Entries);

        string NameOf(string id, string? cached)
            => names.TryGetValue(id, out var name) ? name : cached ?? id;

        // 持っているアバターを先に（U25）。100体を超える商品では、自分のアバターが
        // 札の山のどこにあるかを探すことになっていた。持っていない分は出品者の並びのまま
        var ownedIds = _main.Search.OwnedItemIds();
        var manuallyOwned = registry.Entries
            .Where(entry => entry.IsOwnedManually)
            .Select(entry => entry.ItemId)
            .ToHashSet(StringComparer.Ordinal);

        var rows = Item.Local.Avatars
            .Where(link => !link.Rejected)
            .Select(link => new AvatarRow
            {
                ItemId = link.AvatarItemId,
                Name = NameOf(link.AvatarItemId, link.Name),
                SourceText = SourceLabel(link.Source),
                IsUnconfirmed = !link.Confirmed,
                IsOwned = ownedIds.Contains(link.AvatarItemId) || manuallyOwned.Contains(link.AvatarItemId),
                RejectCommand = new RelayCommand(() => RejectAvatarAsync(link.AvatarItemId).Forget(), () => !IsEditLocked),
                // ツールチップに出す絵（R3）。乗せたときに初めて読む——248体の商品で全部を先に読むと開くのが遅れる
                IconFactory = () => AvatarIcon(link.AvatarItemId, _thumbnails.LoadForCard),
                OpenCommand = new RelayCommand(() => OpenAvatarAsync(link.AvatarItemId).Forget()),
            })
            .OrderByDescending(row => row.IsOwned)
            .ToList();

        // 何か保存するたびにここを通る（お気に入りを付けただけでも）。並べる対応アバターが変わっていなければ、
        // 作った札（全部並べた分も）をそのまま使う。作り直すと244体の商品で全部並べ直すのに約800ms掛かる
        if (_avatarStrip.Tiles.Count == 0 || !SameAvatars(Avatars, rows))
        {
            Avatars = rows;
            _avatarStrip.Reset(Avatars, IsAvatarsExpanded, _avatarStrip.ShowsAll);
        }

        ApplyAvatarFilter();

        AvatarBases = Item.Local.AvatarBases
            .Where(link => !link.Rejected)
            .Select(link => new AvatarBaseRow
            {
                Name = link.BaseName,
                RejectCommand = new RelayCommand(() => RejectBaseAsync(link.BaseName).Forget(), () => !IsEditLocked),
            })
            .ToList();

        BaseMentions = AvatarBaseMentions.Find(
                ParsedDescription(), registry.BaseGroups, Item.Local.AvatarBases, _services.Settings.AvatarIgnoredHeadings)
            .Select(mention => new BaseMentionRow
            {
                Name = mention.Name,
                IsNew = !mention.IsRegistered,
                AddCommand = new RelayCommand(() => AddMentionedBaseAsync(mention).Forget(), () => !IsEditLocked),
                DismissCommand = new RelayCommand(
                    () => SaveLocalAsync(
                        Item.Local with { AvatarBases = AvatarService.WithDismissedBaseMention(Item.Local.AvatarBases, mention.Name) },
                        LocalOwners.AvatarBases).Forget(),
                    () => !IsEditLocked),
            })
            .ToList();

        // 消した対応は畳んだ欄に並べ、1件ずつ戻せるようにする（ユーザ判断 2026-09-12）。
        // 共通素体も同じ欄に混ぜる（ユーザ判断 2026-09-21・X4：同じ「消したもの」として扱う）
        RejectedAvatars = Item.Local.Avatars
            .Where(link => link.Rejected)
            .Select(link => new RejectedAvatarRow
            {
                Name = NameOf(link.AvatarItemId, link.Name),
                RestoreCommand = new RelayCommand(() => RestoreAvatarAsync(link.AvatarItemId).Forget(), () => !IsEditLocked),
            })
            .Concat(Item.Local.AvatarBases
                .Where(link => link.Rejected)
                .Select(link => new RejectedAvatarRow
                {
                    Name = BaseSuggestionText(link.BaseName),
                    RestoreCommand = new RelayCommand(() => RestoreBaseAsync(link.BaseName).Forget(), () => !IsEditLocked),
                }))
            .ToList();

        // 候補の名前から絵を引くための表（U18）
        _avatarIdsByName = new Dictionary<string, string>(StringComparer.CurrentCulture);
        foreach (var entry in registry.Entries)
        {
            _avatarIdsByName.TryAdd(names[entry.ItemId], entry.ItemId);
        }

        // 対応アバターの候補。既に宣言されているものは出さない
        var declared = Avatars.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);
        var avatarEntries = registry.Entries
            // その商品自身（アバターのとき）は、自分の対応アバターにはならない
            .Where(entry => AvatarService.IsAvatar(entry) && !declared.Contains(entry.ItemId)
                && !string.Equals(entry.ItemId, Item.Id, StringComparison.Ordinal))
            .ToList();

        // 所持／未所持の群と、呼び方でも当たる案内（メモ48・メモ58。検索の対応アバターと同じ分け方・照らし方）
        _supportSuggestInfo = new Dictionary<string, Controls.SuggestInfo>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in avatarEntries)
        {
            var group = ownedIds.Contains(entry.ItemId) || manuallyOwned.Contains(entry.ItemId)
                ? AvatarSuggestionText.OwnedGroup
                : AvatarSuggestionText.OtherGroup;
            _supportSuggestInfo.TryAdd(names[entry.ItemId], AvatarSuggestionText.InfoOf(entry, names[entry.ItemId], group));
        }

        var avatarSuggestions = avatarEntries.Select(entry => names[entry.ItemId]);

        // 共通素体も同じ欄から付ける（ユーザ判断 2026-09-28：素体は外す・戻すしかできず、付ける口が無かった）。
        // アバターと素体に同じ名前がありうるので、素体には「（共通素体）」を添えて別の行にし、
        // 選ばれた文字列を名前で引き直さず、この表で素体と分かった物だけを素体として足す
        _baseNamesBySuggestion = AvatarService.ItemBaseCandidates(registry, Item.Local.AvatarBases)
            .ToDictionary(BaseSuggestionText, name => name, StringComparer.CurrentCultureIgnoreCase);

        foreach (var baseText in _baseNamesBySuggestion.Keys)
        {
            _supportSuggestInfo[baseText] = new Controls.SuggestInfo(AvatarSuggestionText.BaseGroup, []);
        }

        SupportSuggestions = avatarSuggestions
            .Concat(_baseNamesBySuggestion.Keys)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        OnPropertyChanged(nameof(SupportSuggestions));

        foreach (var name in new[]
        {
            nameof(Avatars), nameof(HasAvatars), nameof(AvatarBases), nameof(HasAvatarBases),
            nameof(AvatarSectionNote), nameof(ShowsAvatarFilter), nameof(AvatarFilterPlaceholder),
            nameof(RejectedAvatars), nameof(HasRejectedAvatars), nameof(RejectedAvatarsHeader),
            nameof(AvatarsCountText), nameof(UnconfirmedAvatarCount), nameof(HasUnconfirmedAvatars),
            nameof(UnconfirmedAvatarText), nameof(BaseMentions), nameof(HasBaseMentions),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private static string SourceLabel(AvatarLinkSource source) => source switch
    {
        AvatarLinkSource.SupportSection => "対応アバター節",
        AvatarLinkSource.Tag => "タグ",
        AvatarLinkSource.Variation => "バリエーションの名前",
        AvatarLinkSource.H2Link => "説明文のリンク",
        AvatarLinkSource.SupportList => "説明文の対応一覧",
        AvatarLinkSource.Manual => "手入力",
        _ => string.Empty,
    };

    /// <summary>
    /// この対応は違う、と消す。
    ///
    /// 消しただけだと次の検出で復活するので、Manual に付け替えたうえで Rejected を立てる。
    /// 再検出のマージは Manual の宣言だけを残して他を作り直すので、
    /// Source を変えないと行ごと作り直されて Rejected が消える。
    ///
    /// 検出の適合率は実測で89%。1割は誤りが出るので、消せないと噛み合わない。
    /// </summary>
    private async Task RejectAvatarAsync(string avatarItemId)
    {
        var links = Item.Local.Avatars
            .Select(link => link.AvatarItemId == avatarItemId
                ? link with { Source = AvatarLinkSource.Manual, Rejected = true, Confirmed = true }
                : link)
            .ToList();

        await SaveLocalAsync(Item.Local with { Avatars = links }, LocalOwners.SupportedAvatars);
    }

    /// <summary>
    /// 対応している共通素体を、この商品から消す（ユーザ判断 2026-09-21・X3/X4）。
    ///
    /// 足す道（検出）しか無く、誤検出を消せなかった。対応アバターと同じ形にする——
    /// 行は残して出どころを「手入力」に付け替え、消した印を立てる
    /// （出どころを変えないと、次の検出で行ごと作り直されて印が消える）。
    /// </summary>
    private async Task RejectBaseAsync(string baseName)
    {
        var links = Item.Local.AvatarBases
            .Select(link => string.Equals(link.BaseName, baseName, StringComparison.CurrentCultureIgnoreCase)
                ? link with { Source = AvatarLinkSource.Manual, Rejected = true, Confirmed = true }
                : link)
            .ToList();

        await SaveLocalAsync(Item.Local with { AvatarBases = links }, LocalOwners.AvatarBases);
    }

    /// <summary>消した共通素体を戻す（「消したもの」の欄の［戻す］）。</summary>
    private async Task RestoreBaseAsync(string baseName)
    {
        // 説明文の候補を消した物は、一覧に無い素体のことがある。［追加］と同じく先に一覧へ足す
        // （一覧に無い素体を商品に付けても、検索でつなぐ先が無い）。
        // 一覧から削除した素体は足し直さない——削除は人が決めたことで、商品の行を戻しただけで一覧に復活させない
        var known = _services.CachedAvatars.Load().BaseGroups
            .Any(group => string.Equals(group.Name, baseName, StringComparison.CurrentCultureIgnoreCase));
        if (!known)
        {
            await AddMentionedBaseAsync(new AvatarBaseMention(baseName, IsRegistered: false));
            return;
        }

        var links = Item.Local.AvatarBases
            .Select(link => string.Equals(link.BaseName, baseName, StringComparison.CurrentCultureIgnoreCase)
                ? link with { Rejected = false, Confirmed = true }
                : link)
            .ToList();

        await SaveLocalAsync(Item.Local with { AvatarBases = links }, LocalOwners.AvatarBases);
    }

    /// <summary>
    /// 消した対応を戻す（「消したもの」の欄の［戻す］・ユーザ判断 2026-09-12）。
    /// 出どころは消したときに「手入力」へ付け替えてあるので、元の出どころ（対応アバター節・タグなど）には戻らない。
    /// 手入力のままにするのは、次の検出でも消えないようにするため（手で足したのと同じ扱い）
    /// </summary>
    private async Task RestoreAvatarAsync(string avatarItemId)
    {
        var links = Item.Local.Avatars
            .Select(link => link.AvatarItemId == avatarItemId
                ? link with { Rejected = false, Confirmed = true }
                : link)
            .ToList();

        await SaveLocalAsync(Item.Local with { Avatars = links }, LocalOwners.SupportedAvatars);
    }

    /// <summary>
    /// 対応アバターを手で足す。出品者が書き漏らしている場合や、検出が拾えなかった場合に使う。
    /// 足したものは Manual なので、次の検出でも消えない。
    /// </summary>
    private async Task AddAvatarAsync(string? name)
    {
        if (name is not null && _baseNamesBySuggestion.TryGetValue(name.Trim(), out var baseName))
        {
            await SaveLocalAsync(
                Item.Local with { AvatarBases = AvatarService.WithManualBaseLink(Item.Local.AvatarBases, baseName) },
                LocalOwners.AvatarBases);
            return;
        }

        if (FindAvatarByName(name) is not { } match || string.Equals(match.ItemId, Item.Id, StringComparison.Ordinal))
        {
            return;
        }

        var existing = Item.Local.Avatars.FirstOrDefault(link => link.AvatarItemId == match.ItemId);

        // 一度消したものを足し直す場合は、Rejected を下ろすだけ
        var links = existing is null
            ? [.. Item.Local.Avatars, new AvatarLink
            {
                AvatarItemId = match.ItemId,
                Name = AvatarNames.ShownName(match),
                Source = AvatarLinkSource.Manual,
                Confirmed = true,
            }]
            : Item.Local.Avatars
                .Select(link => link.AvatarItemId == match.ItemId
                    ? link with { Source = AvatarLinkSource.Manual, Rejected = false, Confirmed = true }
                    : link)
                .ToList();

        await SaveLocalAsync(Item.Local with { Avatars = links }, LocalOwners.SupportedAvatars);
    }

    /// <summary>「＋ 追加」の候補に出した素体の行から素体の名前を引く表。アバターの名前と取り違えないように分けて持つ。</summary>
    private Dictionary<string, string> _baseNamesBySuggestion = new(StringComparer.CurrentCultureIgnoreCase);

    private static string BaseSuggestionText(string baseName) => $"{baseName}（共通素体）";

    private AvatarRegistryEntry? FindAvatarByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // 候補に出した名前（同じ名前ならショップ名付き）と、正式名のどちらでも引けるようにする
        var entries = _services.CachedAvatars.Load().Entries;
        var names = AvatarNames.Map(entries);
        return entries.FirstOrDefault(entry =>
            string.Equals(names[entry.ItemId], name, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(AvatarNames.ShownName(entry), name, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(entry.BoothName, name, StringComparison.CurrentCultureIgnoreCase));
    }
}
