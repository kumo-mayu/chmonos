using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 左右に分かれた画面で、右の欄から開いた商品ページを右の欄の中に重ねる口（open.md 95 行・ユーザ判断 2026-11-18・11-19）。
///
/// 左の選びはそのまま、右の欄の中で商品ページを開く。**1段だけ**：組み込んだページからさらに別の商品・ショップ・改変へ行くと、
/// 主の窓ごと移る（ページの中の操作は今までどおり主画面を呼ぶ）。
/// 前の右の中身は捨てずに隠すだけなので、「← 一覧に戻る」（Alt+← も）で、組み直さずに前の中身と流した位置へ戻る。
///
/// 持ち主は <see cref="IRightPaneItemHost"/> を実装し、右の中身を出し替えた時（左で別の物を選んだ時）は <see cref="Close"/> で閉じる。
/// </summary>
public sealed class RightPaneItemPage : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly object _owner;
    private readonly Func<ItemRecord, ItemViewModel> _create;
    private ItemViewModel? _page;
    private RelayCommand? _back;

    /// <param name="owner">持ち主の画面。主画面の今の画面がこれの時だけ「出ている」とみなす。</param>
    /// <param name="create">組み込みの形の商品ページを作る（持ち主ごとに覚える左の幅の鍵が違う）。</param>
    public RightPaneItemPage(MainViewModel main, object owner, Func<ItemRecord, ItemViewModel> create)
    {
        _main = main;
        _owner = owner;
        _create = create;
    }

    /// <summary>右に重ねた商品ページ。開いていなければ null。</summary>
    public ItemViewModel? Page
    {
        get => _page;
        private set
        {
            var leaving = _page;
            if (SetField(ref _page, value))
            {
                // 閉じるページの待っている書き込み（メモの入力など）は今書く。単独の商品ページを離れる時と同じ
                leaving?.FlushPendingWritesAsync().Forget();
                OnPropertyChanged(nameof(IsOpen));
                Changed?.Invoke();
            }
        }
    }

    public bool IsOpen => _page is not null;

    /// <summary>開いた・閉じた（持ち主が右の幅の求めを知らせ直す）。</summary>
    public event Action? Changed;

    /// <summary>右の頭の「← 一覧に戻る」。</summary>
    public RelayCommand BackCommand => _back ??= new RelayCommand(() => Close());

    /// <summary>右の欄の中で商品ページを開く。</summary>
    public void Open(ItemRecord item)
    {
        var page = _create(item);

        // 取り直し・ファイルを外した後の開き直しは、右の中だけで行う（主画面ごと差し替えない）。
        // 待つ間に閉じた・別の画面へ移った時は差し替えない（フォルダの木の組み込みと同じ）
        page.IsShownByOwner = () => ReferenceEquals(_main.CurrentViewModel, _owner) && ReferenceEquals(_page, page);
        page.Replaced = updated =>
        {
            if (!page.IsShownByOwner())
            {
                return;
            }

            if (updated is null)
            {
                Close();
            }
            else
            {
                Open(updated);
            }
        };
        Page = page;
    }

    /// <summary>重ねた商品ページを閉じ、前の右の中身へ戻す。閉じた時は true（Alt+← が画面の履歴へ回らない）。</summary>
    public bool Close()
    {
        if (_page is null)
        {
            return false;
        }

        Page = null;
        return true;
    }
}

/// <summary>右の欄に商品ページを重ねられる画面（<see cref="RightPaneItemPage"/>）。主画面が戻る・画像の宛先で聞く。</summary>
internal interface IRightPaneItemHost
{
    RightPaneItemPage RightItem { get; }
}
