using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chmonos.App.Services;
using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

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
        var changed = LocalFields.Changed(_baseline, current, LocalOwners.EditScreen);
        var files = ChangedFileVariations();

        // 決める前の打ちかけも控える（I4）。ほかが変わっていなくても、打ちかけがあれば書きかけとして残す
        var draft = new EditDraft
        {
            Local = current,
            Changed = changed,
            FileVariations = files,
            TagInput = TagInput,
            AttributeInput = AttributeInput,
        };

        if (changed.Count == 0 && files.Count == 0 && !draft.HasTypedInput)
        {
            _main.Drafts.Remove(_item.Id);
            return;
        }

        _main.Drafts.Put(_item.Id, draft);
    }

    /// <summary>「編集途中 n件」のボタン。書きかけの置き場そのもの。</summary>
    public EditDraftStore Drafts => _main.Drafts;

    private RelayCommand? _openDraftsCommand;

    /// <summary>書きかけのある商品だけを並べて開く（ユーザ指示）。今の商品の入力も先に控える。</summary>
    public RelayCommand OpenDraftsCommand => _openDraftsCommand ??= new RelayCommand(
        () =>
        {
            CaptureDraft();
            _main.ShowEditAsync(_main.Drafts.ItemIds).Forget();
        },
        () => _main.Drafts.HasAny);
}
