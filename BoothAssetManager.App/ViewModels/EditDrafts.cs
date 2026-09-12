using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 編集の書きかけ1件。**変えた項目だけ**を持つ（ユーザ判断 2026-09-12）。
///
/// 戻すときは、その時点で読み直した記録に、変えた項目だけを重ねる——開いていない間に取り込みや
/// 他の画面が書いた項目を、古い写しで戻さないため（保存と同じ <see cref="LocalFields.Merge"/> の考え方）。
/// </summary>
public sealed class EditDraft
{
    /// <summary>画面の入力から組んだ local。使うのは <see cref="Changed"/> の項目だけ。</summary>
    public required LocalBlock Local { get; init; }

    public required IReadOnlyCollection<LocalField> Changed { get; init; }

    /// <summary>ファイルの種類の付け替え（変えたものだけ）。ハッシュ → 種類。</summary>
    public required IReadOnlyDictionary<string, long?> FileVariations { get; init; }
}

/// <summary>
/// 編集の書きかけの置き場。アプリに1つ。
///
/// **メモリだけに持つ**（ユーザ判断：次に起動したときほどは要らない。閉じるときに残っていれば尋ねる）。
/// 商品IDをキーにし、未編集の順番と指定して入った順番で共有する——同じ商品の書きかけは、
/// どこから開いても同じ書きかけ（ユーザ判断）。保存したらそのIDの分を消す。
/// </summary>
public sealed class EditDraftStore : ViewModelBase
{
    private readonly Dictionary<string, EditDraft> _drafts = new(StringComparer.Ordinal);

    public int Count => _drafts.Count;

    public bool HasAny => _drafts.Count > 0;

    /// <summary>編集画面の上に出すボタンの文言（ユーザ指示「編集途中n件」）。</summary>
    public string ButtonText => $"編集途中 {_drafts.Count} 件";

    /// <summary>書きかけのある商品。</summary>
    public IReadOnlyList<string> ItemIds => _drafts.Keys.ToList();

    public bool Contains(string itemId) => _drafts.ContainsKey(itemId);

    public EditDraft? Get(string itemId) => _drafts.GetValueOrDefault(itemId);

    public void Put(string itemId, EditDraft draft)
    {
        _drafts[itemId] = draft;
        Notify();
    }

    public void Remove(string itemId)
    {
        if (_drafts.Remove(itemId))
        {
            Notify();
        }
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasAny));
        OnPropertyChanged(nameof(ButtonText));
    }
}

/// <summary>
/// 検索などで指定して入った編集の順番と位置。**ファイルには書かず、画面の履歴に預ける**（ユーザ判断）。
/// 履歴にある間は「戻る」で続きから開け、履歴から落ちたら一緒に捨てる。
///
/// 未編集の順番（edit-session.json）はこれで上書きしない——以前は同じ記録を上書きしていて、
/// 商品ページから1件編集しただけで未編集の順番・位置・保存した印が消えていた。
/// </summary>
public sealed class EditRun
{
    public required List<string> ItemIds { get; init; }

    public int Index { get; set; }

    public HashSet<string> Saved { get; } = new(StringComparer.Ordinal);
}
