using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 検索画面：ほかの画面から「これだけ出す」で入る口。
///
/// どれも**他の条件の値を戻してから**絞る——前の条件が残っていると「このショップの商品」などに見えない。
/// 渡す条件は絞り込みのモジュールそのもの（無ければ足す）なので、入った後もパネルでそのまま触れる。
/// </summary>
public sealed partial class SearchViewModel
{
    /// <summary>
    /// このユーザタグだけで絞り込んだ状態にする。タグの管理から「この分類が付いている商品」を
    /// 見に来る導線。件数だけ見せられても、消していいか統合していいかは判断できない。
    /// </summary>
    public void ShowOnly(string top, string? sub = null)
    {
        ClearFilters(apply: false);
        var row = EnsureModule<UserTagModule>(SearchModuleKind.UserTag).AddTop(top, notify: false);
        if (sub is not null)
        {
            row?.AddSubQuietly(sub);
        }
        FinishShowOnly();
    }

    /// <summary>
    /// この属性で評価済みの商品だけを出す。属性を 0〜100 で足すと、
    /// 「評価が入っているもの」がそのまま残る（未評価は属性を足した時点で外れる）。
    /// </summary>
    public void ShowOnlyAttribute(string name)
    {
        ClearFilters(apply: false);
        EnsureModule<AttributeModule>(SearchModuleKind.Attribute).AddRow(name, notify: false);
        FinishShowOnly();
    }

    /// <summary>
    /// このアバターに対応している商品だけを出す。アバター管理からの導線。
    /// 既定では素体経由も含める（「対応が確認できていないもの」を既定で隠さない方針に合わせる）。
    /// </summary>
    public void ShowOnlyAvatar(string avatarItemId, string displayName, bool includeViaBase = true)
    {
        ClearFilters(apply: false);
        var module = EnsureModule<ListModule>(SearchModuleKind.Avatar);
        module.AddKey(AvatarKey + avatarItemId, notify: false, text: AvatarSuggestionText.Format(displayName, avatarItemId));
        module.SetFlagQuietly(includeViaBase);
        FinishShowOnly();
    }

    /// <summary>このショップの商品だけで絞り込む。ショップ画面からの導線（#55）。</summary>
    public void ShowOnlyShop(string shopKey, string shopName)
    {
        ClearFilters(apply: false);
        EnsureModule<ListModule>(SearchModuleKind.Shop).AddKey(shopKey, notify: false, text: $"{shopName}（{shopKey}）");
        FinishShowOnly();
    }

    /// <summary>
    /// このフォルダの下にファイルを持つ商品だけを出す。フォルダビューの「このフォルダで絞り込んで検索」からの導線
    /// （ユーザ：「フォルダビューからそのフォルダで絞り込んで検索に入れても良いくらいだ」）。条件は検索の「ファイルの場所」と同じ物を使う
    /// </summary>
    public void ShowOnlyFolder(string path)
    {
        ClearFilters(apply: false);
        EnsureModule<ListModule>(SearchModuleKind.Path).AddKey(path, notify: false, text: path);
        FinishShowOnly();
    }

    /// <summary>このカテゴリだけで絞り込む。統計の容量内訳から中身を見に来る導線。</summary>
    public void ShowOnlyCategory(string category)
    {
        ClearFilters(apply: false);
        EnsureModule<ListModule>(SearchModuleKind.Category).AddKey(category, notify: false, text: category);
        FinishShowOnly();
    }

    /// <summary>
    /// 条件の値を戻し、最近取り込んだ順に並べる。取り込みの結果から、取り込んだ物を見に来る導線（動線の点検 D1）。
    /// 絞らずに並べるだけにするのは、取り込みの前からあった物も一緒に見えていた方が、何が増えたかが分かるため
    /// </summary>
    public void ShowRecentlyAddedFirst()
    {
        ClearFilters(apply: false);
        _sortField = SortFields.FirstOrDefault(field => field.Kind == SortKind.RecentlyAdded) ?? _sortField;
        _sort = _sortField.ToOption(descending: true);
        OnPropertyChanged(nameof(Sort));
        OnPropertyChanged(nameof(SortField));
        ApplyFilters();
    }

    private void FinishShowOnly()
    {
        RefreshModuleMenu();
        SaveModulesLater();
        ApplyFilters();
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
            _itemsById[itemId] = _allItems[index];
        }

        foreach (var card in _cards.Values.Where(card => card.Item.Id == itemId))
        {
            card.IsFavorite = isFavorite;
        }

        if (Modules.Any(module => module.Kind == SearchModuleKind.Favorite))
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
        _itemsById[item.Id] = item;
        _haystacks[item.Id] = Core.Services.SearchText.Build(item, _services.KanjiReadings);
        _fingerprints.Remove(item.Id);

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

    /// <summary>
    /// 未確定で登録した商品を知る（ユーザ判断 2026-09-29）。写しに無ければ1件だけ足し、あれば <see cref="NoteItemChanged"/> で差し替える
    /// （既にある商品へファイルを足しただけのとき）。
    ///
    /// 前は画面を離れるまで検索に出なかった。登録のたびに全件を読み直すと2000件で数秒待つので、1件だけを写しの並び
    /// （<see cref="Core.Services.ItemOrder.Library"/>。読み込みと同じ決まり）の位置へ足し、検索用の文字列とカードを作って絞り込みをかけ直す。
    /// 件数（ナビの「商品 n 件」）は <see cref="TotalCount"/> の知らせで主画面が合わせる。
    /// 読み直しの途中に足した分は、読み直しが読んだ時点の一覧で上書きされることがある。そのときは未確定の画面を離れるときの読み直しが拾う
    /// </summary>
    public void NoteItemSaved(ItemRecord item)
    {
        if (_itemsById.ContainsKey(item.Id))
        {
            NoteItemChanged(item);
            return;
        }

        _allItems.Insert(Core.Services.ItemOrder.LibraryInsertIndex(_allItems, item), item);
        _itemsById[item.Id] = item;
        _haystacks[item.Id] = Core.Services.SearchText.Build(item, _services.KanjiReadings);
        _fingerprints.Remove(item.Id);

        var card = ToCard(item);
        card.SelectionChanged += OnCardSelectionChanged;
        _cards[item.Id] = card;

        // 新しいショップ・カテゴリ・BOOTHタグを絞り込みの候補に出す（読み直しと同じく全件から組み直す）
        BuildFacets();
        ApplyFilters();
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ShopCount));
        OnPropertyChanged(nameof(NeedsEditCount));
    }
}
