using BoothAssetManager.Core.Search;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索欄の右の「対象」の1項目。</summary>
public sealed class SearchTargetOption : ViewModelBase
{
    private readonly Action<SearchTargetOption> _changed;
    private bool _isOn;

    public SearchTargetOption(SearchField field, string label, bool isOn, Action<SearchTargetOption> changed)
    {
        Field = field;
        Label = label;
        _isOn = isOn;
        _changed = changed;
    }

    public SearchField Field { get; }

    public string Label { get; }

    /// <summary>メニューの1行。前置きの書き方も添える（覚えれば切り替えずに絞れる）。</summary>
    public string Header => $"{Label}　{SearchQuery.FieldName(Field)}:";

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetField(ref _isOn, value))
            {
                _changed(this);
            }
        }
    }

    internal void SetSilently(bool value)
    {
        _isOn = value;
        OnPropertyChanged(nameof(IsOn));
    }
}

/// <summary>
/// 検索画面：文字列で探すときの「対象」と「オプション」（ユーザ案 2026-09-15・`docs/history/search-redesign.md`）。
///
/// 前は「本文も探す」「ファイルのパスも探す」「別の表記も探す」の3つのチェックだった。対象を1つずつ選べるようにし、
/// 大文字と小文字・全角と半角・ひらがなとカタカナの区別と、別表記の道をそれぞれ切り替えられるようにした。
/// </summary>
public sealed partial class SearchViewModel
{
    /// <summary>対象の並びと見せる名前。既定でオンは商品名・ショップ名・メモ（<see cref="SearchOptions.DefaultTargets"/>）。</summary>
    private static readonly (SearchField Field, string Label)[] TargetLabels =
    [
        (SearchField.Name, "商品名"),
        (SearchField.Shop, "ショップ名"),
        (SearchField.Memo, "メモ"),
        (SearchField.Subdomain, "サブドメイン"),
        (SearchField.Main, "本文（説明とセクション）"),
        (SearchField.Tag, "BOOTHタグ"),
        (SearchField.Variation, "バリエーション名"),
        (SearchField.Id, "商品ID"),
        (SearchField.File, "ファイル名"),
        (SearchField.Path, "ファイルの場所（パス）"),
        (SearchField.Content, "zip の中のファイル名"),
    ];

    /// <summary>文字列で探す対象。</summary>
    private readonly HashSet<SearchField> _targets = [.. SearchOptions.DefaultTargets];

    private bool _caseSensitive;
    private bool _widthSensitive;
    private bool _kanaSensitive = true;
    private BridgeOptions _bridgeOptions = BridgeOptions.All;
    private bool _useCoined = true;

    /// <summary>照らすときの切り替え。変わったときだけ作り直す（1商品ごとに作ると件数ぶん無駄に作る）。</summary>
    private SearchOptions _searchOptions = SearchOptions.Default;

    private IReadOnlyList<SearchTargetOption>? _targetOptions;
    private RelayCommand? _widenCommand;

    public IReadOnlyList<SearchTargetOption> TargetOptions => _targetOptions ??= TargetLabels
        .Select(pair => new SearchTargetOption(pair.Field, pair.Label, _targets.Contains(pair.Field), OnTargetChanged))
        .ToList();

    /// <summary>検索欄の透かし。今探している対象を言う（「タグから探す」と書いてあるのに探さない、を避ける）。</summary>
    public string QueryPlaceholder
    {
        get
        {
            var labels = TargetLabels.Where(pair => _targets.Contains(pair.Field)).Select(pair => pair.Label).ToList();
            return labels.Count == 0
                ? "「対象」で探す所を選ぶか、name: のように前置きを付けて探す"
                : string.Join("・", labels.Take(4)) + (labels.Count > 4 ? " ほか" : string.Empty) + "から探す";
        }
    }

    public bool CaseSensitive
    {
        get => _caseSensitive;
        set
        {
            if (SetField(ref _caseSensitive, value))
            {
                OnSearchOptionsChanged();
            }
        }
    }

    public bool WidthSensitive
    {
        get => _widthSensitive;
        set
        {
            if (SetField(ref _widthSensitive, value))
            {
                OnSearchOptionsChanged();
            }
        }
    }

    public bool KanaSensitive
    {
        get => _kanaSensitive;
        set
        {
            if (SetField(ref _kanaSensitive, value))
            {
                OnSearchOptionsChanged();
            }
        }
    }

    /// <summary>
    /// 別の表記でも探すか（既定は切）。
    ///
    /// **0件でも自動では広げない**（ユーザ判断 2026-09-16）。切っているのに広げると、切っている意味が無くなる。
    /// 代わりに0件の所に「別表記でも検索する」ボタンを出す（<see cref="ShowsWidenOffer"/>）。
    /// 入にすると、当たっているときも一緒に広げる——「tori」で当たった商品があっても『鳥』の商品を見たい場面があるため。
    /// </summary>
    public bool SearchAlternates
    {
        get => _searchAlternates;
        set
        {
            if (SetField(ref _searchAlternates, value))
            {
                ClearWidening();
                OnPropertyChanged(nameof(ShowsWidenOffer));
                ApplyFilters();
            }
        }
    }

    public bool UseRomaji
    {
        get => _bridgeOptions.Romaji;
        set => SetRoute(_bridgeOptions with { Romaji = value });
    }

    public bool UseKanji
    {
        get => _bridgeOptions.Kanji;
        set => SetRoute(_bridgeOptions with { Kanji = value });
    }

    public bool UseEnglishToJapanese
    {
        get => _bridgeOptions.EnglishToJapanese;
        set => SetRoute(_bridgeOptions with { EnglishToJapanese = value });
    }

    public bool UseJapaneseToEnglish
    {
        get => _bridgeOptions.JapaneseToEnglish;
        set => SetRoute(_bridgeOptions with { JapaneseToEnglish = value });
    }

    /// <summary>造語変換：辞書に無い語は、商品名を漢字1字ごとの音訓で読んで照らす（撫で音 → なでおと）。</summary>
    public bool UseCoined
    {
        get => _useCoined;
        set
        {
            if (SetField(ref _useCoined, value) && _searchAlternates)
            {
                ClearWidening();
                ApplyFilters();
            }
        }
    }

    /// <summary>0件の所に「別表記でも検索する」を出すか。何か打っていて、別表記を切っていて、辞書があるときだけ。</summary>
    public bool ShowsWidenOffer => IsEmpty
        && !_searchAlternates
        && _queryNode is not SearchNode.All
        && _services.Bridge.IsAvailable;

    public RelayCommand WidenCommand => _widenCommand ??= new RelayCommand(() => SearchAlternates = true);

    private void SetRoute(BridgeOptions options)
    {
        if (options == _bridgeOptions)
        {
            return;
        }

        _bridgeOptions = options;
        OnPropertyChanged(nameof(UseRomaji));
        OnPropertyChanged(nameof(UseKanji));
        OnPropertyChanged(nameof(UseEnglishToJapanese));
        OnPropertyChanged(nameof(UseJapaneseToEnglish));

        if (_searchAlternates)
        {
            ClearWidening();
            ApplyFilters();
        }
    }

    private void OnTargetChanged(SearchTargetOption option)
    {
        if (option.IsOn)
        {
            _targets.Add(option.Field);
        }
        else
        {
            _targets.Remove(option.Field);
        }

        OnPropertyChanged(nameof(QueryPlaceholder));
        OnSearchOptionsChanged();
    }

    private void OnSearchOptionsChanged()
    {
        RefreshSearchOptions();
        ClearWidening();
        ApplyFilters();
    }

    /// <summary>今の切り替えで照らし方を作り直す。読み（造語）は、広げていて造語変換が入のときだけ見る。</summary>
    private void RefreshSearchOptions()
        => _searchOptions = new SearchOptions
        {
            Targets = new HashSet<SearchField>(_targets),
            CaseSensitive = _caseSensitive,
            WidthSensitive = _widthSensitive,
            KanaSensitive = _kanaSensitive,
            IncludeReadings = _widenedNode is not null && _useCoined,
        };

    /// <summary>履歴から戻すとき。知らない名前は読み飛ばす（前置きの名前を変えたときの古い履歴）。</summary>
    private void RestoreTextOptions(IReadOnlyList<string> targets, bool caseSensitive, bool widthSensitive, bool kanaSensitive)
    {
        _targets.Clear();
        var restored = targets
            .Select(name => SearchQuery.FieldNames.TryGetValue(name, out var field) ? field : (SearchField?)null)
            .OfType<SearchField>()
            .ToList();
        _targets.UnionWith(restored.Count > 0 ? restored : SearchOptions.DefaultTargets);

        foreach (var option in TargetOptions)
        {
            option.SetSilently(_targets.Contains(option.Field));
        }

        _caseSensitive = caseSensitive;
        _widthSensitive = widthSensitive;
        _kanaSensitive = kanaSensitive;
        OnPropertyChanged(nameof(CaseSensitive));
        OnPropertyChanged(nameof(WidthSensitive));
        OnPropertyChanged(nameof(KanaSensitive));
        OnPropertyChanged(nameof(QueryPlaceholder));
        RefreshSearchOptions();
    }

    /// <summary>履歴に書く対象。既定のままなら空（既定を変えても古い履歴が既定に追従する）。</summary>
    private IReadOnlyList<string> TargetsForHistory()
        => _targets.SetEquals(SearchOptions.DefaultTargets)
            ? []
            : _targets.Select(SearchQuery.FieldName).OrderBy(name => name, StringComparer.Ordinal).ToList();
}
