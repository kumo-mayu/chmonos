namespace Chmonos.App.ViewModels;

/// <summary>
/// 主画面：保存していない入力を持って編集画面を離れた時の知らせ（ユーザ判断 2026-11-18）。
///
/// 離れる時に「保存しますか」と聞く窓は足さない。書きかけは黙って控える作りで、「← 前へ」・帯の絵で行き来しながら埋める使い方で
/// 毎回窓が出ると、気軽に移れる狙いが崩れる。ただ黙って控えるだけでは、入力が消えたのか残ったのかが分からないので、
/// 画面の外へ出た時だけ下の帯で一言知らせ、ナビの「編:n」が控えた数である事と、続きを開く口を見せる。
/// 編集の順番の中で商品を移る時（前へ・スキップ・帯の絵）は出さない（そこは「編集途中 n 件」が見えている）。
/// </summary>
public sealed partial class MainViewModel
{
    private bool _draftNotice;
    private RelayCommand? _dismissDraftNotice;
    private RelayCommand? _openDraftsFromNotice;

    /// <summary>控えが全部無くなった（保存した・元に戻した）後まで「控えました」と言い続けない。</summary>
    public bool HasDraftNotice => _draftNotice && Drafts.HasAny;

    public string DraftNoticeText => $"保存していない入力を控えました。ナビの「編:{Drafts.Count}」が控えている商品の数です。";

    public RelayCommand DismissDraftNoticeCommand => _dismissDraftNotice ??= new RelayCommand(() => SetDraftNotice(false));

    /// <summary>書きかけのある商品だけで編集を開く（編集画面の「編集途中 n 件」と同じ）。</summary>
    public RelayCommand OpenDraftsFromNoticeCommand => _openDraftsFromNotice ??= new RelayCommand(
        () =>
        {
            SetDraftNotice(false);
            ShowEditAsync(Drafts.ItemIds).Forget();
        });

    /// <summary>
    /// 画面が替わる時に呼ぶ。編集から編集の外へ出て、離れた商品に書きかけが残った時だけ知らせる。
    /// 編集へ入った（書きかけを開いた・戻るで編集の段へ戻った）なら、知らせはもう要らない
    /// </summary>
    private void NoteScreenChangeForDrafts(bool leftEditWithDraft, object? next)
    {
        if (next is EditViewModel)
        {
            SetDraftNotice(false);
        }
        else if (leftEditWithDraft)
        {
            SetDraftNotice(true);
        }
    }

    private void NoteDraftsChanged()
    {
        OnPropertyChanged(nameof(HasDraftNotice));
        OnPropertyChanged(nameof(DraftNoticeText));
    }

    private void SetDraftNotice(bool shown)
    {
        _draftNotice = shown;
        OnPropertyChanged(nameof(HasDraftNotice));
        OnPropertyChanged(nameof(DraftNoticeText));
    }
}
