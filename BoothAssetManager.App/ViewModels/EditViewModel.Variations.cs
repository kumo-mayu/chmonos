using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>編集画面：購入した種類とファイルの種類分け（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class EditViewModel
{
    private void BuildVariations(ItemRecord record)
    {
        Variations.Clear();

        // 同じ版を複数回買った記録がありうるので、版ごとにまとめる。
        // 1件目は版の行そのもの、2件目以降は行の下にぶら下げる
        // ToLookup は null の鍵を持てる（ToDictionary は持てない）。
        // 「どのバリエーションも指していない」記録がここに入る
        var ordered = record.Local.Purchases.ToLookup(purchase => purchase.VariationId);

        foreach (var variation in record.Booth.Variations)
        {
            var group = ordered[variation.Id].ToList();
            var first = group.FirstOrDefault();

            var row = new OrderedVariationInput
            {
                VariationId = variation.Id,
                Name = variation.Name ?? "（1種類のみ）",
                ListPrice = variation.Price,
                ListPriceText = $"¥{variation.Price:N0}",
                IsPurchased = first is not null,
                Price = first?.Price?.ToString() ?? string.Empty,
                Kind = first?.Kind ?? PurchaseKind.ForSelf,
            };

            AttachExtras(row, group.Skip(1));
            Variations.Add(row);
        }

        // BOOTH側から消えた購入記録も、支出の記録として残っているので出す。
        // バリエーションを指していない記録（null）もここへ落ちる——
        // 指す先が無いので「現存する」側には入らない
        var currentIds = record.Booth.Variations.Select(variation => (long?)variation.Id).ToHashSet();
        foreach (var group in ordered.Where(entry => !currentIds.Contains(entry.Key)))
        {
            var purchases = group.ToList();
            var first = purchases[0];
            var row = new OrderedVariationInput
            {
                VariationId = group.Key,
                Name = first.NameSnapshot ?? DisplayText.VariationLabel(group.Key),
                ListPriceText = "-",

                // 指していない記録は「消えた」わけではない。
                // 指す先が無いだけなので、現存しない印は付けない
                IsGone = group.Key is not null,
                IsPurchased = true,
                Price = first.Price?.ToString() ?? string.Empty,
                Kind = first.Kind,
            };

            AttachExtras(row, purchases.Skip(1));
            Variations.Add(row);
        }

        // どのバリエーションも指さない購入を、いつでも足せるようにする。
        //
        // BOOTHから取れない商品にはバリエーションが1件も無いので、これが無いと
        // **買った金額を記録する場所が存在しない**（統計の支出から丸ごと落ちる）。
        // 普通の商品にも出すのは、**バリエーション単位の販売終了があるため**——
        // 買ったあとにその版が消えると、後から記録を入れる行が無くなる。
        if (Variations.All(row => row.VariationId is not null))
        {
            Variations.Add(new OrderedVariationInput
            {
                VariationId = null,
                Name = DisplayText.VariationLabel(null),
                ListPriceText = "-",
                IsPurchased = false,
            });
        }

        // ここから先の変更はユーザ操作。価格の自動入力を許可する
        foreach (var input in Variations)
        {
            input.IsInitialized = true;
        }
    }

    // ---- ファイルに種類を付ける（#40） ----
    //
    // 付ける操作がどこにも無く、友人のデータで327件中0件だった。
    // 他の入力と同じく「保存して次へ」で書く（スキップすれば捨てる）。
    // 書くのは編集画面の保存とは別の命令——種類は LocalFiles の中の項目で、
    // 編集画面が LocalFiles を丸ごと持つと、開いている間に取り込みが足したファイルを消してしまう。

    /// <summary>手元のファイル（ハッシュ→表示名）。場所の分からないものは出さない。</summary>
    private List<(string Hash, string Name)> _files = [];

    /// <summary>今の画面上の紐付け。</summary>
    private Dictionary<string, long?> _fileVariations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>開いた時点の紐付け。保存のときに差だけを書く。</summary>
    private Dictionary<string, long?> _savedFileVariations = new(StringComparer.OrdinalIgnoreCase);

    private void BuildFileLinks(ItemRecord record)
    {
        _files = record.Local.OwnedFiles
            .Where(file => file.Paths.Count > 0)
            .Select(file => (file.Hash, System.IO.Path.GetFileName(file.Paths[0])))
            .ToList();

        _fileVariations = record.Local.OwnedFiles.ToDictionary(
            file => file.Hash, file => file.VariationId, StringComparer.OrdinalIgnoreCase);
        _savedFileVariations = new Dictionary<string, long?>(_fileVariations, StringComparer.OrdinalIgnoreCase);

        _canLinkAny = record.Booth.Variations.Count >= 2 && _files.Count > 0;

        // 商品ごとに畳んだ状態から始める（既定は畳む・ユーザ指示）
        _isFileSortExpanded = false;

        foreach (var row in Variations)
        {
            row.LinkRequested = choice =>
            {
                _fileVariations[choice.Hash] = row.VariationId;
                RefreshFileLinks();
            };
            row.PurchasedChanged = OnPurchasedChanged;
        }

        _purchasedCount = PurchasedVariationCount();
        RefreshFileLinks();
    }

    // ---- ファイルの種類分け（ユーザ指示 2026-09-12） ----
    //
    // ファイルを主にした一覧を「購入した種類」とメモの間に置く。種類を主にした各行の選び欄は便利なので残し、
    // 同じ中身（_fileVariations）を直すので揃う。
    // 買った種類が1つなら、全部のファイルをその種類として**見せるだけ**で書かない（計算で出せる値なので・ユーザ判断）。
    // 2つ目の種類に印を付けると自動の見せ方が消え（全部解除）、ファイルごとに選べるようになる

    /// <summary>この商品で種類を選べるか（BOOTHの種類が2つ以上で、手元にファイルがある）。</summary>
    private bool _canLinkAny;

    /// <summary>買った種類の数（「種類を選ばない購入」は数えない。付け先の種類が無い）。</summary>
    private int _purchasedCount;

    private bool _isFileSortExpanded;

    private int PurchasedVariationCount() => Variations.Count(row => row.IsPurchased && row.VariationId is not null);

    /// <summary>買った種類が1つだけならその種類。全部のファイルをこれとして見せる。</summary>
    private long? AutoVariation()
    {
        if (_files.Count == 0)
        {
            return null;
        }

        var purchased = Variations.Where(row => row.IsPurchased && row.VariationId is not null).ToList();
        return purchased.Count == 1 ? purchased[0].VariationId : null;
    }

    private void OnPurchasedChanged()
    {
        _purchasedCount = PurchasedVariationCount();
        OnPropertyChanged(nameof(IsPurchaseUnselected));

        // 勝手には開かない（案A・ユーザ判断）。印を付けるたびに下の欄が開いたり閉じたりすると、画面が動いて分かりにくい。
        // 残りがあることは見出しの「未指定 n」で知らせる
        RefreshFileLinks();
    }

    /// <summary>ファイルを主にした一覧の行。</summary>
    public ObservableCollection<FileSortRow> FileSortRows { get; } = [];

    /// <summary>
    /// 欄を開けるか。種類を2つ以上買ったとき、またはこの商品にファイルが2つ以上付いているとき（ユーザ指示）。
    /// 開けないときも欄は消さず、灰色にして理由を書く（何も無い所へいきなり現れるのは変なので・ユーザ指示）。
    /// </summary>
    public bool CanSortFiles => _purchasedCount >= 2 || _files.Count >= 2;

    public bool IsFileSortExpanded
    {
        get => _isFileSortExpanded && CanSortFiles;
        set
        {
            if (_isFileSortExpanded != value)
            {
                _isFileSortExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 見出しの右の一言。開けないときは理由、開けるときはファイルの数と、まだ種類を付けていない数。
    /// 欄は勝手に開かないので、残りがあることはここで知らせる（案A）。
    /// </summary>
    public string FileSortHeaderNote => CanSortFiles
        ? $"（{_files.Count} ファイル・未指定 {FileSortRows.Count(row => row.IsUnassigned)}）"
        : "　複数の種類を購入するか、複数のファイルがこの商品に付いている場合にだけ開けます";

    /// <summary>
    /// 種類を選んでいないファイルがあるか。「購入した種類」と同じく、欄を畳んでいても見出しの札で分かるようにする
    /// （ユーザ指示 2026-09-13）。数え方は見出しの「未指定 n」と同じ。開けない欄（種類分けの要らない商品）では出さない
    /// </summary>
    public bool IsFileSortUnselected => CanSortFiles && FileSortRows.Any(row => row.IsUnassigned);

    public bool HasNoFilesToSort => _files.Count == 0;

    /// <summary>
    /// 欄の中の表示。ファイルごと（ファイルを主に種類を選ぶ）と種類ごと（種類を主にファイルを足す）。
    /// 種類ごとは、以前「購入した種類」の各行にあった選び方をこの欄へ移したもの（案A：入口を1か所にする）。
    /// 選んだ表示は次の商品へ進んでも保つ（アプリを閉じるまで）。
    /// </summary>
    private static bool s_isByVariationView;

    public bool IsByVariationView
    {
        get => s_isByVariationView;
        set
        {
            if (s_isByVariationView != value)
            {
                s_isByVariationView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsByFileView));
            }
        }
    }

    public bool IsByFileView
    {
        get => !s_isByVariationView;
        set => IsByVariationView = !value;
    }

    /// <summary>種類ごとの表示に並べる、買った種類の行。</summary>
    public ObservableCollection<OrderedVariationInput> PurchasedVariationRows { get; } = [];

    private void RebuildFileSortRows(long? auto)
    {
        FileSortRows.Clear();

        // 買った種類が1つなら、その種類を「（自動）」として1つだけ見せる（書かない）。案内の枠は出さない（案A）
        var choices = auto is { } autoId
            ? [new VariationChoice(autoId, $"{Variations.First(row => row.VariationId == autoId).Name}（自動）")]
            : new List<VariationChoice> { new(null, "指定しない") };
        if (auto is null)
        {
            choices.AddRange(Variations
                .Where(row => row.VariationId is not null && row.IsPurchased)
                .Select(row => new VariationChoice(row.VariationId, row.Name)));
        }

        foreach (var (hash, name) in _files.OrderBy(file => file.Name, StringComparer.CurrentCulture))
        {
            // 選べるのは買った種類と「指定しない」だけ（ユーザ指示）
            var current = auto ?? EffectiveVariation(hash);
            var rowChoices = choices.ToList();

            var row = new FileSortRow(rowChoices.First(choice => choice.VariationId == current))
            {
                Hash = hash,
                Name = name,
                Choices = rowChoices,
                IsAuto = auto is not null,
            };

            // 選び欄の選択の最中に一覧を作り直すと選び欄が迷うので、選び終えてから作り直す
            row.Changed = variationId =>
            {
                _fileVariations[hash] = variationId;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(RefreshFileLinks));
            };

            FileSortRows.Add(row);
        }
    }

    private void RefreshFileLinks()
    {
        var names = Variations
            .Where(row => row.VariationId is not null)
            .ToDictionary(row => row.VariationId!.Value, row => row.Name);

        var auto = AutoVariation();

        foreach (var row in Variations)
        {
            row.LinkedFiles.Clear();
            row.FileChoices.Clear();

            // 「種類を選ばない購入」の行には付けない（付け先の種類が無い）。
            // 買った種類が1つのときは全部その種類として見せるので、選ぶ欄は出さない
            // 結び付けられるのは買った種類だけ（ユーザ指示）。買っていない種類のファイルは手元に無いはず
            row.CanLinkFiles = auto is null && _canLinkAny && row.VariationId is not null && row.IsPurchased;

            if (auto is { } autoId)
            {
                if (row.VariationId == autoId)
                {
                    foreach (var (hash, name) in _files.OrderBy(file => file.Name, StringComparer.CurrentCulture))
                    {
                        row.LinkedFiles.Add(new FileLinkInput { Hash = hash, Name = name });
                    }
                }

                row.NoteLinkedFilesChanged();
                continue;
            }

            if (!row.CanLinkFiles)
            {
                row.NoteLinkedFilesChanged();
                continue;
            }

            foreach (var (hash, name) in _files.OrderBy(file => file.Name, StringComparer.CurrentCulture))
            {
                var current = EffectiveVariation(hash);
                if (current == row.VariationId)
                {
                    row.LinkedFiles.Add(new FileLinkInput
                    {
                        Hash = hash,
                        Name = name,
                        UnlinkCommand = new RelayCommand(() =>
                        {
                            _fileVariations[hash] = null;
                            RefreshFileLinks();
                        }),
                    });
                }
            }

            // 名前に種類名がそのまま入っているものを先に出す。友人のデータで当たるのは14%だけなので、
            // 自動では付けずに候補の順番にだけ使う
            var choices = _files
                .Where(file => EffectiveVariation(file.Hash) != row.VariationId)
                .Select(file =>
                {
                    var other = EffectiveVariation(file.Hash);
                    var looksLike = NameLooksLike(file.Name, row.Name);
                    return (File: file, LooksLike: looksLike, Note: other is { } otherId && names.TryGetValue(otherId, out var otherName)
                        ? $"「{otherName}」に付いています"
                        : looksLike ? "名前が似ています" : string.Empty);
                })
                .OrderByDescending(entry => entry.LooksLike)
                .ThenBy(entry => entry.File.Name, StringComparer.CurrentCulture);

            foreach (var entry in choices)
            {
                row.FileChoices.Add(new FileLinkInput { Hash = entry.File.Hash, Name = entry.File.Name, Note = entry.Note });
            }

            row.NoteLinkedFilesChanged();
        }

        RebuildFileSortRows(auto);

        PurchasedVariationRows.Clear();
        foreach (var row in Variations.Where(row => row.VariationId is not null && row.IsPurchased))
        {
            PurchasedVariationRows.Add(row);
        }

        foreach (var name in new[]
        {
            nameof(CanSortFiles), nameof(IsFileSortExpanded), nameof(FileSortHeaderNote), nameof(HasNoFilesToSort),
            nameof(IsFileSortUnselected),
        })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>ファイル名に種類名が丸ごと入っているか。空白・括弧・区切りは無視する。</summary>
    private static bool NameLooksLike(string fileName, string variationName)
    {
        static string Fold(string text) => new(text
            .ToLowerInvariant()
            .Where(character => !char.IsWhiteSpace(character) && !"_-.()[]【】（）「」『』・/\\".Contains(character))
            .ToArray());

        var file = Fold(System.IO.Path.GetFileNameWithoutExtension(fileName));
        var variation = Fold(variationName);
        return variation.Length >= 2 && file.Contains(variation, StringComparison.Ordinal);
    }

    /// <summary>
    /// 画面と保存に使う結び付け。**買っていない種類に付いていれば「指定しない」として扱う**（ユーザ指示）。
    /// 画面の中の控え（_fileVariations）は消さないので、印を外して付け直せば元の結び付けに戻る。
    /// </summary>
    private long? EffectiveVariation(string hash)
        => _fileVariations.GetValueOrDefault(hash) is { } id
            && Variations.Any(row => row.VariationId == id && row.IsPurchased)
                ? id
                : null;

    /// <summary>開いた時点から変わった紐付けだけ。買っていない種類への結び付けは外れた扱いで書く。</summary>
    private Dictionary<string, long?> ChangedFileVariations()
        => _fileVariations.Keys
            .Select(hash => (Hash: hash, Value: EffectiveVariation(hash)))
            .Where(pair => _savedFileVariations.GetValueOrDefault(pair.Hash) != pair.Value)
            .ToDictionary(pair => pair.Hash, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 2件目以降の購入記録を行にぶら下げ、足す／消すを配線する。
    /// 版の行と同じ形にしておくと、1件目と2件目で操作が変わらない。
    /// </summary>
    private void AttachExtras(OrderedVariationInput row, IEnumerable<Purchase>? existing)
    {
        foreach (var purchase in existing ?? [])
        {
            AddExtra(row, purchase.Price?.ToString(), purchase.Kind, purchase.NameSnapshot, purchase.Note);
        }

        row.AddPurchaseCommand = new RelayCommand(
            // 版の名前は引き継ぐ。BOOTH側から消えたときに何の版だったか分からなくなる
            () => AddExtra(row, row.Price, PurchaseKind.Given, row.Name, null),
            () => row.CanAddPurchase);

        row.NoteExtrasChanged();
    }

    private void AddExtra(
        OrderedVariationInput row,
        string? price,
        PurchaseKind kind,
        string? nameSnapshot,
        string? note)
    {
        var extra = new ExtraPurchaseInput
        {
            IsGone = row.IsGone,
            NameSnapshot = nameSnapshot,
            Note = note,
            Price = price ?? string.Empty,
            Kind = kind,
        };

        extra.RemoveCommand = new RelayCommand(() =>
        {
            row.Extras.Remove(extra);
            row.NoteExtrasChanged();
        });

        row.Extras.Add(extra);
        row.NoteExtrasChanged();
    }
}
