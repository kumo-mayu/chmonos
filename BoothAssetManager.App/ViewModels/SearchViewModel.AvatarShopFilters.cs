using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：対応アバター・ショップで絞る（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class SearchViewModel
{
    /// <summary>
    /// 対応アバターの候補。「名前（商品ID）」で1行にしてある。
    ///
    /// **IDも同じ1行に入れているのは、VRChatでは商品IDで探す習慣があるから。**
    /// 候補の絞り込みは部分一致なので、名前でもIDでも同じ欄から引ける。
    /// 名前だけにすると、名前を思い出せずIDなら分かる場面で手が無くなる。
    /// </summary>
    public ObservableCollection<string> AvatarSuggestions { get; } = [];

    public RelayCommand SetAvatarFilterCommand { get; }

    public RelayCommand ClearAvatarFilterCommand { get; }

    public RelayCommand ClearShopFilterCommand { get; }

    /// <summary>
    /// ショップで絞っているか。ショップ画面の「検索でこのショップの商品を絞る」から入る（#55）。
    /// ショップ画面に絞り込みを作り直すより、検索の絞り込みをそのまま使えた方が同じ操作で済む（ユーザ判断）。
    /// </summary>
    public bool HasShopFilter => _shopFilterKey is not null;

    public string ShopFilterName => _shopFilterName ?? string.Empty;

    private void ClearShopFilter()
    {
        _shopFilterKey = null;
        _shopFilterName = null;
        RaiseShopFilterChanged();
        ApplyFilters();
    }

    private void RaiseShopFilterChanged()
    {
        OnPropertyChanged(nameof(HasShopFilter));
        OnPropertyChanged(nameof(ShopFilterName));
    }

    /// <summary>今アバターで絞っているか。絞っているときだけ、外す手段と素体経由の切り替えを出す。</summary>
    public bool HasAvatarFilter => _avatarFilterId is not null;

    public string AvatarFilterName => _avatarFilterName ?? string.Empty;

    private bool _avatarFilterHasBase;

    /// <summary>
    /// 今絞っているアバターが共通素体に属しているか。
    ///
    /// **属していないときに素体経由の切り替えを出さない。**
    /// 素体が無ければ経由する先も無いので、どちらに倒しても結果が変わらない。
    /// 効かない選択肢を並べると、結果が変わらないのを見て「壊れている」と読まれる。
    /// </summary>
    public bool AvatarFilterHasBase => _avatarFilterHasBase;

    /// <summary>
    /// 素体経由の対応も含めるか。
    /// 既定で含めるのは「対応が確認できていないものを既定で隠さない」方針に合わせるため。
    /// </summary>
    public bool IncludeViaBase
    {
        get => _includeViaBase;
        set
        {
            if (SetField(ref _includeViaBase, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>
    /// 候補の1行から商品IDを取り出して絞る。
    /// 候補に無い語（打ち間違い）はそのままIDとして扱わない。
    /// </summary>
    private void SetAvatarFilter(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        var id = AvatarSuggestionText.IdOf(entry);
        if (id is null)
        {
            return;
        }

        _avatarFilterId = id;
        _avatarFilterName = AvatarSuggestionText.NameOf(entry);
        _avatarFilterHasBase = HasBase(id);
        _compatibility = null;
        RaiseAvatarFilterChanged();
        ApplyFilters();
    }

    private void ClearAvatarFilter()
    {
        _avatarFilterId = null;
        _avatarFilterName = null;
        _avatarFilterHasBase = false;
        RaiseAvatarFilterChanged();
        ApplyFilters();
    }

    /// <summary>このアバターが共通素体グループに属しているか。登録簿を引くだけで通信は要らない。</summary>
    private bool HasBase(string avatarItemId)
        => _services.Store.Avatars.Load().Entries
            .Any(entry => entry.ItemId == avatarItemId && !string.IsNullOrWhiteSpace(entry.BaseName));

    private void RaiseAvatarFilterChanged()
    {
        OnPropertyChanged(nameof(HasAvatarFilter));
        OnPropertyChanged(nameof(AvatarFilterName));
        OnPropertyChanged(nameof(AvatarFilterHasBase));
    }

    /// <summary>登録簿にあるアバターを候補に並べ直す。</summary>
    private void RefreshAvatarSuggestions()
    {
        AvatarSuggestions.Clear();

        var registry = _services.Store.Avatars.Load();
        var names = Core.Services.AvatarNames.Map(registry.Entries);
        foreach (var entry in registry.Entries.Where(entry => entry.AvatarOverride != false)
            .OrderBy(entry => names[entry.ItemId], StringComparer.CurrentCulture))
        {
            AvatarSuggestions.Add(AvatarSuggestionText.Format(names[entry.ItemId], entry.ItemId));
        }

        OnPropertyChanged(nameof(HasAvatarSuggestions));
        RefreshExtraSuggestions();
    }

    /// <summary>
    /// 候補から積む条件に、候補を入れる。
    ///
    /// **持ち主をこちらにする。**候補はアバターの登録簿から作るもので、
    /// 条件そのものではない。条件の側に持たせると、登録簿が変わっても古いまま残る。
    /// </summary>
    private void RefreshExtraSuggestions()
    {
        var registry = _services.Store.Avatars.Load();

        var baseNames = registry.Entries
            .Select(entry => entry.BaseName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        foreach (var filter in ExtraFilters.Where(filter => filter.IsSuggest))
        {
            var source = filter.Kind switch
            {
                ExtraFilterKind.UsedOn => AvatarSuggestions.ToList(),
                ExtraFilterKind.BaseAvatar => baseNames,
                _ => [],
            };

            filter.Suggestions.Clear();
            foreach (var value in source)
            {
                filter.Suggestions.Add(value);
            }

            filter.NoteSuggestionsChanged();
        }
    }

    public bool HasAvatarSuggestions => AvatarSuggestions.Count > 0;

    /// <summary>
    /// 「対応アバター」の候補の頭に出す絵（U18）。候補の行（名前（ID））からIDを取り出し、
    /// 持っていれば商品の1枚目、持っていなければ控えの1枚。候補は見えた行だけで読む
    /// </summary>
    public Func<string, System.Windows.Media.ImageSource?> AvatarIconSelector => entry =>
        AvatarSuggestionText.IdOf(entry) is { } id
        && Core.Services.AvatarImageSync.IconPath(_services.Paths, id, FindItem(id)) is { } path
            ? _thumbnails.LoadForTile(path)
            : null;
}
