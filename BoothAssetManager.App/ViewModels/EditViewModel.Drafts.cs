using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>編集画面：書きかけ（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class EditViewModel
{
    // ---- 書きかけ（ユーザ判断 2026-09-12） ----
    //
    // 保存せずに商品を離れたとき（スキップ・前へ・帯で飛ぶ・別の画面へ移る・閉じる）の入力を、
    // 変えた項目だけ控える。控えはアプリに1つの置き場（EditDraftStore）にあり、保存したら消える

    /// <summary>
    /// 開いた時点の入力から組んだ local。変えた項目はこれと比べて見分ける。
    /// 記録そのものと比べると、開いただけで形が整う項目（購入の名前の控え・現存の印など）まで
    /// 変えたことになってしまう。
    /// </summary>
    private LocalBlock? _baseline;

    /// <summary>今の商品の入力を書きかけとして控える。変えた項目が無ければ控えを消す（元に戻した＝書きかけではない）。</summary>
    public void CaptureDraft()
    {
        if (_item is null || _baseline is null)
        {
            return;
        }

        var current = BuildLocal(_item);
        var changed = LocalOwners.EditScreen.Where(field => !SameField(_baseline, current, field)).ToList();
        var files = ChangedFileVariations();

        if (changed.Count == 0 && files.Count == 0)
        {
            _main.Drafts.Remove(_item.Id);
            return;
        }

        _main.Drafts.Put(_item.Id, new EditDraft { Local = current, Changed = changed, FileVariations = files });
    }

    /// <summary>その項目だけを比べる。項目の中身（一覧や入れ子）ごと比べたいので、書き出した形で比べる。</summary>
    private static bool SameField(LocalBlock before, LocalBlock after, LocalField field)
        => System.Text.Json.JsonSerializer.Serialize(LocalFields.Merge(new LocalBlock(), before, [field]))
            == System.Text.Json.JsonSerializer.Serialize(LocalFields.Merge(new LocalBlock(), after, [field]));

    /// <summary>「編集途中 n件」のボタン。書きかけの置き場そのもの。</summary>
    public EditDraftStore Drafts => _main.Drafts;

    private RelayCommand? _openDraftsCommand;

    /// <summary>書きかけのある商品だけを並べて開く（ユーザ指示）。今の商品の入力も先に控える。</summary>
    public RelayCommand OpenDraftsCommand => _openDraftsCommand ??= new RelayCommand(
        () =>
        {
            CaptureDraft();
            _ = _main.ShowEditAsync(_main.Drafts.ItemIds);
        },
        () => _main.Drafts.HasAny);
}
