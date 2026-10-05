using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chmonos.App.Services;
using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

// EditViewModel の画面に並べる行と小さな入れ物（技術的負債 4-1：画面のクラスのファイルから分けた。中身は変えていない）

/// <summary>購入記録の入力行。</summary>
public sealed class OrderedVariationInput : ViewModelBase
{
    private bool _isPurchased;
    private string _price = string.Empty;
    private PurchaseKind _kind = PurchaseKind.ForSelf;

    /// <summary>
    /// どのバリエーションの行か。**null は「どのバリエーションも指していない」購入の行。**
    /// BOOTHから取れない商品にはバリエーションが1件も無く、
    /// バリエーション単位の販売終了でも後から記録を入れる行が無くなる。
    /// </summary>
    public long? VariationId { get; init; }

    public required string Name { get; init; }

    /// <summary>BOOTHの現在価格。未入力のときの目安として出す。</summary>
    public required string ListPriceText { get; init; }

    public int? ListPrice { get; init; }

    /// <summary>BOOTH の今の値段があるか。無い行（種類を指さない購入・消えた版）では「現在 -」を出さない。</summary>
    public bool HasListPrice => ListPrice is not null;

    /// <summary>BOOTH側に現存しない購入記録か。</summary>
    public bool IsGone { get; init; }

    /// <summary>
    /// 保存済みの値を流し込み終えたか。
    /// これが立つまで価格の自動入力はしない。読み込んだだけで
    /// 「未入力」だった過去の記録に勝手な金額が入るのを避けるため。
    /// </summary>
    public bool IsInitialized { get; set; }

    public bool IsPurchased
    {
        get => _isPurchased;
        set
        {
            if (!SetField(ref _isPurchased, value))
            {
                return;
            }

            // ユーザが印を付けた時点で、BOOTHの現在価格を初期値として入れておく。
            // 空欄のままだと「未入力」で保存され、統計の支出に乗らない。
            // セール等で実際の支払額が違うことはあるので、値は書き換えられるようにしておく。
            if (IsInitialized && value && _price.Length == 0 && ListPrice is { } listPrice)
            {
                Price = listPrice.ToString();
            }

            NotePurchasedChanged();
        }
    }

    /// <summary>購入価格。空欄は未入力（支出に数えない）、0は無料配布。</summary>
    public string Price
    {
        get => _price;
        set => SetField(ref _price, value);
    }

    /// <summary>
    /// この購入が誰のためのものだったか。
    /// 貰い物は支出に数えず、贈答は支出には入るが所持には入らない。
    /// </summary>
    public PurchaseKind Kind
    {
        get => _kind;
        set
        {
            if (SetField(ref _kind, value))
            {
                OnPropertyChanged(nameof(KindLabel));
                OnPropertyChanged(nameof(PurchasedAtLabel));
            }
        }
    }

    public string KindLabel => DisplayText.PurchaseKindLabel(Kind);

    private string _purchasedAt = string.Empty;

    /// <summary>
    /// この1回の日付（メモ45）。**空欄は入手日を使う**（保存時に null を書く）。1件目だけ入手日と違う日に買うこともあるので、種類の行にも出す（ユーザ判断 2026-10-05）。
    /// 読み方は入手日の欄と同じ <see cref="DateText"/>。
    /// </summary>
    public string PurchasedAt
    {
        get => _purchasedAt;
        set => SetField(ref _purchasedAt, value ?? string.Empty);
    }

    /// <summary>日付の欄の名前。行の種類に合わせる（買った日／贈った日／貰った日。ユーザ判断 2026-10-05）。</summary>
    public string PurchasedAtLabel => PurchaseDateLabel.Of(Kind);

    /// <summary>
    /// 1件目の購入記録のメモ。種類の行には欄が無いが、JSON を手で直して入れた物や、2件目だったメモが
    /// 前の1件目を消して繰り上がった物がある。保存で書き戻さないと黙って消えるので、読んだまま持って返す
    /// </summary>
    public string? FirstNote { get; init; }

    /// <summary>
    /// 同じ版の2件目以降の購入記録。
    ///
    /// **買った1回が1レコード**なので、同じ版を2回買った記録も持てる。
    /// 「自分用に1つ、ギフトに1つ」がこれにあたり、
    /// <see cref="PurchaseKind"/> を3種に割ったのはこの用途のため。
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<ExtraPurchaseInput> Extras { get; } = [];

    public bool HasExtras => Extras.Count > 0;

    /// <summary>この版をもう1回買った記録を足す。</summary>
    public RelayCommand? AddPurchaseCommand { get; set; }

    /// <summary>足せるのは購入に印を付けた版だけ。1件目が無いのに2件目は作れない。</summary>
    public bool CanAddPurchase => IsPurchased;

    /// <summary>買った印が変わった。ファイルの種類分け（買った種類が1つなら全部その種類として見せる）を作り直させる。</summary>
    public Action? PurchasedChanged { get; set; }

    internal void NotePurchasedChanged()
    {
        OnPropertyChanged(nameof(CanAddPurchase));
        RelayCommand.RaiseCanExecuteChanged();
        PurchasedChanged?.Invoke();
    }

    internal void NoteExtrasChanged() => OnPropertyChanged(nameof(HasExtras));

    // ---- この種類のファイル（#40） ----

    private bool _canLinkFiles;
    private FileLinkInput? _selectedFileChoice;

    /// <summary>
    /// ファイルを紐付けられる行か。種類が2つ以上ある商品で、手元にファイルがあるときだけ。
    /// 1種類しかない商品では、どのファイルもその種類なので選ぶ意味が無い。
    /// </summary>
    public bool CanLinkFiles
    {
        get => _canLinkFiles;
        set => SetField(ref _canLinkFiles, value);
    }

    /// <summary>この種類に紐付けたファイル。同じ種類に複数付けられる（別zipでも同じ種類由来のことがある）。</summary>
    public ObservableCollection<FileLinkInput> LinkedFiles { get; } = [];

    public bool HasLinkedFiles => LinkedFiles.Count > 0;

    /// <summary>プルダウンに出す、まだこの種類に付いていないファイル。名前が似ているものを先に並べる。</summary>
    public ObservableCollection<FileLinkInput> FileChoices { get; } = [];

    /// <summary>プルダウンで選ばれたら紐付ける。選んだファイルは一覧から抜けるので、選択は自然に空へ戻る。</summary>
    public Action<FileLinkInput>? LinkRequested { get; set; }

    public FileLinkInput? SelectedFileChoice
    {
        get => _selectedFileChoice;
        set
        {
            _selectedFileChoice = value;
            if (value is not null)
            {
                LinkRequested?.Invoke(value);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 種類の行の右端に出すファイルの数（案A・ユーザ判断）。結び付けそのものは「ファイルの種類分け」だけで行い、
    /// 種類の行には数だけ出す（行に選び欄や札を並べると、印を付けるたびに形が変わって分かりにくかった）。
    /// </summary>
    /// 「ファイル n」では何の数か分からなかった（ユーザ指摘）ので、紐付けた数だと読める言い方にする。
    public string FileCountText => LinkedFiles.Count > 0 ? $"{LinkedFiles.Count} ファイル紐付け済" : string.Empty;

    internal void NoteLinkedFilesChanged()
    {
        OnPropertyChanged(nameof(HasLinkedFiles));
        OnPropertyChanged(nameof(FileCountText));
    }
}

/// <summary>購入の日付の欄の名前と、読めなかったときの知らせの名前を1か所で決める。</summary>
public static class PurchaseDateLabel
{
    /// <summary>「買った日」「贈った日」「貰った日」。種類の動詞（札の「¥1,500 で贈った」と同じ）に「日」を付ける。</summary>
    public static string Of(PurchaseKind kind) => DisplayText.PurchaseKindVerb(kind) + "日";
}

/// <summary>種類に紐付ける／紐付いたファイル1件。</summary>
public sealed class FileLinkInput
{
    public required string Hash { get; init; }

    public required string Name { get; init; }

    /// <summary>「『〇〇』に付いています」「名前が似ています」。無ければ空。</summary>
    public string Note { get; init; } = string.Empty;

    public string Display => Note.Length == 0 ? Name : $"{Name}（{Note}）";

    public RelayCommand? UnlinkCommand { get; set; }

    /// <summary>✕を出すか。買った種類が1つで全部その種類として見せている札は、外す意味が無いので出さない。</summary>
    public bool CanUnlink => UnlinkCommand is not null;
}

/// <summary>ファイルの種類分けの選択肢1つ。null は「指定しない」。</summary>
public sealed record VariationChoice(long? VariationId, string Name);

/// <summary>
/// 「ファイルの種類分け」の1行（ユーザ指示 2026-09-12）。ファイルを主にして、どの種類のファイルかを選ぶ。
/// 「購入した種類」の各行の選び欄（種類を主にして、付けるファイルを選ぶ）と同じ中身を直すので、どちらで選んでも揃う。
/// </summary>
public sealed class FileSortRow : ViewModelBase
{
    private VariationChoice _selected;

    public FileSortRow(VariationChoice selected) => _selected = selected;

    public required string Hash { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<VariationChoice> Choices { get; init; }

    /// <summary>買った種類が1つなので、その種類として見せているだけ（書かない）。選び欄は触れない。</summary>
    public bool IsAuto { get; init; }

    /// <summary>まだどの種類にも付いていない。印を出して、残っているファイルが一目で分かるようにする。</summary>
    public bool IsUnassigned => !IsAuto && _selected.VariationId is null;

    /// <summary>選び直された。引数は新しい種類（null は指定しない）。</summary>
    public Action<long?>? Changed { get; set; }

    public VariationChoice Selected
    {
        get => _selected;
        set
        {
            if (value is null || value == _selected)
            {
                return;
            }

            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUnassigned));
            Changed?.Invoke(value.VariationId);
        }
    }
}

/// <summary>
/// 同じ版の2件目以降の購入記録1件。
///
/// 1件目（版の行そのもの）と同じ項目を持つが、印を外す代わりに行ごと消す。
/// 「買った回数」を減らす操作なので、外すより消す方が意味に合う。
/// </summary>
public sealed class ExtraPurchaseInput : ViewModelBase
{
    private string _price = string.Empty;
    private PurchaseKind _kind = PurchaseKind.Given;

    /// <summary>BOOTH側に現存しない版の記録か。1件目から引き継ぐ。</summary>
    public bool IsGone { get; init; }

    public string? NameSnapshot { get; init; }

    public string? Note { get; init; }

    public string Price
    {
        get => _price;
        set => SetField(ref _price, value);
    }

    /// <summary>
    /// 既定を「贈った」にしている。2件目を作る理由のほとんどが贈答だから
    /// （自分用を2つ買う場面はまれ）。違えばその場で変えられる。
    /// </summary>
    public PurchaseKind Kind
    {
        get => _kind;
        set
        {
            if (SetField(ref _kind, value))
            {
                OnPropertyChanged(nameof(PurchasedAtLabel));
            }
        }
    }

    private string _purchasedAt = string.Empty;

    /// <summary>この1回の日付。空欄は入手日を使う（1件目の行と同じ）。</summary>
    public string PurchasedAt
    {
        get => _purchasedAt;
        set => SetField(ref _purchasedAt, value ?? string.Empty);
    }

    public string PurchasedAtLabel => PurchaseDateLabel.Of(Kind);

    /// <summary>この記録を消す。</summary>
    public RelayCommand? RemoveCommand { get; set; }
}
