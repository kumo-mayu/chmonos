using System.IO;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>主画面：取り込みの途中の商品（U8・U10）（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class MainViewModel
{
    // ---- 取り込みの途中の商品（U8・U10） ----
    //
    // ①で商品ができた時点で検索・ショップ・件数に出す（JSONだけで最低限の表示はできる）。
    // 編集は③（対応アバターの検出）が済むまで出さない（ユーザ判断）。
    // 判断は取り込みと同じ作業の集まり（ImportWorkSet）を見て行い、画面側に別の状態を持たない

    private Core.Scanning.ImportWorkSet? _importWork;
    private int _lastAwaitingCount;
    private int _reflectedAdded;
    private DateTime _lastReflectAt = DateTime.MinValue;

    /// <summary>
    /// 増えた商品を一覧へ入れる間隔の下限。
    /// 読み直しは全商品を読み、検索用の文字列を作り直す。①は1.5秒に1件進むので、
    /// 増えるたびに読み直すと取り込みの間じゅう読み直し続けることになる。
    /// 10秒に1回なら、増えた商品は遅くとも10秒で一覧に出る
    /// </summary>
    private static readonly TimeSpan ReflectInterval = TimeSpan.FromSeconds(10);

    /// <summary>取り込みが始まったときに、その作業の集まりを受け取る。</summary>
    public void AttachImportWork(Core.Scanning.ImportWorkSet work)
    {
        _importWork = work;
        _lastAwaitingCount = 0;
        _reflectedAdded = 0;
    }

    /// <summary>この商品が、取り込みの③を待っているか。待っている間は編集に出さない。</summary>
    public bool IsAwaitingDetection(string itemId)
        => IsImporting && _importWork?.IsAwaitingDetection(itemId) == true;

    /// <summary>
    /// 取り込みの進み具合が届くたびに呼ぶ。③待ちの数が変わったら編集の可否を知らせ直し、
    /// 増えた商品を一覧へ入れる（読んでいる途中なら「押すと反映」の1行にする）。
    /// </summary>
    public void NoteImportProgress()
    {
        if (_importWork is not { } work)
        {
            return;
        }

        var awaiting = work.AwaitingDetectionCount;
        var gateChanged = awaiting != _lastAwaitingCount;
        _lastAwaitingCount = awaiting;

        if (gateChanged)
        {
            OnEditGateChanged();
        }

        var unseen = work.AddedCount - _reflectedAdded;
        if (unseen <= 0 && !gateChanged)
        {
            return;
        }

        // 一覧を下へ読み進めている最中は足元を動かさない。件数だけ出して、反映は押してもらう（U10）
        if (CurrentViewModel is SearchViewModel && Search.IsScrolledDown)
        {
            if (unseen > 0)
            {
                PendingItemCount = unseen;
            }

            return;
        }

        // ③が済んで編集に出せるようになったときは待たせない。札（取り込み中）を早く外す方が要る
        if (!gateChanged && DateTime.UtcNow - _lastReflectAt < ReflectInterval)
        {
            return;
        }

        _ = ReloadLibraryAsync();
    }

    /// <summary>
    /// 編集に出せる商品が変わった。押せるボタンと件数を知らせ直す。
    /// 開いている商品ページには直接伝える——イベントで配ると、閉じた商品ページが購読したまま残る
    /// </summary>
    private void OnEditGateChanged()
    {
        RelayCommand.RaiseCanExecuteChanged();
        CurrentItemPage?.RefreshEditLock();

        RefreshCounts();
    }

    /// <summary>
    /// 1行を消す。反映したときと、その一覧から離れたときに呼ぶ。
    ///
    /// 離れたら消すのは、「押すと反映」が**その一覧を今読んでいる人のためのもの**だから。
    /// 離れた時点で守るものが無くなるので、戻ってきたら黙って最新にする。
    /// </summary>
    public void ClearPendingItems() => RunOnUiThread(() => PendingItemCount = 0);
}
