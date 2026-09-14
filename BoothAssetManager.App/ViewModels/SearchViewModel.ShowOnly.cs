using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：ほかの画面から「これだけ出す」で入る口（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class SearchViewModel
{
    /// <summary>
    /// このuserTagだけで絞り込んだ状態にする。タグの管理から「この分類が付いているitem」を
    /// 見に来る導線。件数だけ見せられても、消していいか統合していいかは判断できない。
    /// </summary>
    public void ShowOnly(string top, string? sub = null)
    {
        ClearFilters();

        var filter = TagFilters.FirstOrDefault(entry =>
            string.Equals(entry.Name, top, StringComparison.CurrentCultureIgnoreCase));

        if (filter is null)
        {
            return;
        }

        filter.IsSelected = true;

        if (sub is not null)
        {
            filter.Subs
                .FirstOrDefault(entry => string.Equals(entry.Name, sub, StringComparison.CurrentCultureIgnoreCase))
                ?.SetSilently(true);
        }

        ApplyFilters();
    }

    /// <summary>
    /// この属性で評価済みのitemだけを出す。軸を 0〜100 で足すと、
    /// 「評価が入っているもの」がそのまま残る（未評価は軸を足した時点で外れる）。
    /// </summary>
    public void ShowOnlyAttribute(string name)
    {
        ClearFilters();
        AddAttributeFilter(name);
    }

    /// <summary>
    /// このアバターに対応している商品だけを出す。アバター管理からの導線。
    /// 既定では素体経由も含める（「対応が確認できていないもの」を既定で隠さない方針に合わせる）。
    /// </summary>
    public void ShowOnlyAvatar(string avatarItemId, string displayName, bool includeViaBase = true)
    {
        ClearFilters();
        _avatarFilterId = avatarItemId;
        _avatarFilterName = displayName;
        _includeViaBase = includeViaBase;
        _avatarFilterHasBase = HasBase(avatarItemId);
        _compatibility = null;
        RaiseAvatarFilterChanged();
        OnPropertyChanged(nameof(IncludeViaBase));
        ApplyFilters();
    }

    /// <summary>
    /// このショップの商品だけで絞り込む。ショップ画面からの導線（#55）。
    /// 他の条件は外してから絞る——前の条件が残っていると「このショップの商品」に見えない。
    /// </summary>
    public void ShowOnlyShop(string shopKey, string shopName)
    {
        ClearFilters();
        _shopFilterKey = shopKey;
        _shopFilterName = shopName;
        RaiseShopFilterChanged();
        ApplyFilters();
    }

    /// <summary>
    /// このフォルダの下にファイルを持つ商品だけを出す。フォルダビューの「このフォルダで絞り込んで検索」からの導線
    /// （ユーザ：「フォルダビューからそのフォルダで絞り込んで検索に入れても良いくらいだ」）。条件は検索の「フォルダ」と同じ物を使う
    /// </summary>
    public void ShowOnlyFolder(string path)
    {
        ClearFilters();
        AddExtraFilter(ExtraFilterKind.Folder);
        if (ExtraFilters.FirstOrDefault(filter => filter.Kind == ExtraFilterKind.Folder) is not { } folder)
        {
            return;
        }

        folder.IsOn = true;
        foreach (var existing in folder.Selected.ToList())
        {
            folder.Remove(existing);
        }

        folder.CurrentPath = System.IO.Path.GetDirectoryName(path.TrimEnd('\\'));
        folder.Add(path);

        // 選んだ印を行に出す。行は現在地を変えたところで作られていて、そのときはまだ選んでいなかった
        // （絞り込めているのに、左の欄のチェックが外れて見えた）
        RebuildFolderRows(folder);
        ApplyFilters();
    }

    /// <summary>このカテゴリだけで絞り込む。統計の容量内訳から中身を見に来る導線。</summary>
    public void ShowOnlyCategory(string category)
    {
        ClearFilters();
        _selectedCategory = category;
        OnPropertyChanged(nameof(SelectedCategory));
        ApplyFilters();
    }

    /// <summary>
    /// 記録はあるのに置き場所が分からなくなったitemだけを出す。統計の積み残しからの導線。
    /// 消したのか移動しただけなのかはユーザにしか分からないので、判断できる形で並べる。
    /// </summary>
    public void ShowOnlyMissing()
    {
        ClearFilters();
        _missingOnly = true;
        ApplyFilters();
    }

    /// <summary>
    /// 条件を外し、最近手元に入った順に並べる。取り込みの結果から、取り込んだ物を見に来る導線（動線の点検 D1）。
    /// 絞らずに並べるだけにするのは、取り込みの前からあった物も一緒に見えていた方が、何が増えたかが分かるため
    /// </summary>
    public void ShowRecentlyAddedFirst()
    {
        ClearFilters();
        Sort = SortOptions.FirstOrDefault(option => option.Kind == SortKind.RecentlyAdded) ?? Sort;
    }

    /// <summary>
    /// 「条件をクリア」。消す前の条件を検索の履歴に積んでから消す（U3・ユーザ判断）。
    ///
    /// クリアは検索の文字まで全部消し、取り返しがつかない。「元に戻す」は付けず、
    /// 押し間違えても履歴の欄から1回で戻れるようにする（履歴は商品を開いたときにしか積んでいなかった）。
    /// </summary>
    private async Task ClearFiltersKeepingHistoryAsync()
    {
        await RecordHistoryAsync();
        ClearFilters();
    }

    /// <summary>
    /// 商品ページで星を変えたことを知る（U21）。一覧は読み込んだ写しを持っているので、
    /// 知らせないと戻ったときに古い星が出る。一覧ごと読み直さないのは、スクロール位置や並びを崩さないため。
    /// </summary>
    public void NoteFavoriteChanged(string itemId, bool isFavorite)
    {
        var index = _allItems.FindIndex(item => item.Id == itemId);
        if (index >= 0)
        {
            _allItems[index] = _allItems[index] with { Local = _allItems[index].Local with { IsFavorite = isFavorite } };
        }

        foreach (var card in _cards.Values.Where(card => card.Item.Id == itemId))
        {
            card.IsFavorite = isFavorite;
        }

        if (ExtraFilters.Any(filter => filter.Kind == ExtraFilterKind.Favorite))
        {
            ApplyFilters();
        }
    }

    /// <summary>
    /// 1件を保存し直したことを知る（編集画面の「保存して次へ」）。写しの1件・検索用の文字列・カードだけを差し替え、
    /// 一覧ごとは読み直さない（全件の読み直しは2000件で重い）。
    /// これでナビの「未:」がその場で減る（ユーザ指示 2026-09-12：以前は編集を終えるまで減らなかった）
    /// </summary>
    public void NoteItemChanged(ItemRecord item)
    {
        var index = _allItems.FindIndex(entry => entry.Id == item.Id);
        if (index < 0)
        {
            return;
        }

        _allItems[index] = item;
        _haystacks[item.Id] = Core.Services.SearchText.Build(item, _services.KanjiReadings);

        if (_cards.TryGetValue(item.Id, out var old))
        {
            old.SelectionChanged -= OnCardSelectionChanged;
        }

        var card = ToCard(item);
        card.SelectionChanged += OnCardSelectionChanged;
        _cards[item.Id] = card;

        ApplyFilters();
        OnPropertyChanged(nameof(NeedsEditCount));
    }
}
