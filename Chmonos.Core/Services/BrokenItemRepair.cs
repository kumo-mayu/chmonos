using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>読めない商品の記録を直した結果。</summary>
public enum BrokenItemRepairResult
{
    /// <summary>控えに戻した・BOOTH から作り直した。</summary>
    Repaired,

    /// <summary>押すまでの間に読めるようになっていた（手で直した）。</summary>
    NotBroken,

    /// <summary>記録がもう無い（外した・手で消した）。</summary>
    Missing,

    /// <summary>控えが無い。</summary>
    NoCopy,

    /// <summary>控えも読めない。</summary>
    CopyUnreadable,

    /// <summary>BOOTH に無い商品として登録した物（仮ID）。作り直す元が無い。</summary>
    NotOnBooth,

    /// <summary>BOOTH に見つからなかった（壊れた記録は元の場所に戻した）。</summary>
    NotFound,

    /// <summary>BOOTH から取れなかった（通信・不調。壊れた記録は元の場所に戻した）。</summary>
    FetchFailed,
}

/// <summary>
/// 読めない商品の記録を、通知の画面から直す（ユーザ判断 2026-10-05「前提と 1 と 3 の案」）。
///
/// ① 1つ前の版に戻す：アプリが最後に書いた版の控え（<c>items/.prev</c>）を据える。
/// ③ BOOTH から作り直す：控えが使えないとき。商品IDで「商品IDで登録」と同じ取得の道を通る。
/// どちらも**壊れた記録は消さずに <c>items/_broken</c> へよける**（壊れた中にしか無いタグ・メモを後から手で救えるように）。
/// 直したら知らせを見直し、読めるようになった物を解消済みにする。
/// </summary>
public sealed class BrokenItemRepair
{
    private readonly DataStore _store;
    private readonly IItemService _items;
    private readonly INotificationService _notifications;

    public BrokenItemRepair(DataStore store, IItemService items, INotificationService notifications)
    {
        _store = store;
        _items = items;
        _notifications = notifications;
    }

    /// <summary>① 控えを本体に据える。</summary>
    public async Task<BrokenItemRepairResult> RestoreCopyAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var result = await _store.Items.RestoreCopyAsync(itemId, cancellationToken) switch
        {
            BrokenItemOutcome.Done => BrokenItemRepairResult.Repaired,
            BrokenItemOutcome.NotBroken => BrokenItemRepairResult.NotBroken,
            BrokenItemOutcome.Missing => BrokenItemRepairResult.Missing,
            BrokenItemOutcome.NoCopy => BrokenItemRepairResult.NoCopy,
            _ => BrokenItemRepairResult.CopyUnreadable,
        };

        await _notifications.DetectUnreadableItemsAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// ③ 壊れた記録をよけ、BOOTH から取り直して新しく作る。
    ///
    /// **取れなかったら、よけた記録を元の場所へ戻す。**戻さないと、通知の行ごと商品の痕跡が消え、
    /// 次に押す口も無くなる（壊れた物は _broken に残っているが、人はそこを知らない）。
    /// 手元のファイルの記録は作り直した記録に無いので、取り込み直したときに手掛かりで付くか、未確定に出る。
    /// </summary>
    public async Task<BrokenItemRepairResult> RebuildFromBoothAsync(string itemId, CancellationToken cancellationToken = default)
    {
        if (LocalItemId.IsLocal(itemId))
        {
            return BrokenItemRepairResult.NotOnBooth;
        }

        var (outcome, movedTo) = await _store.Items.SetAsideBrokenAsync(itemId, cancellationToken);
        if (outcome != BrokenItemOutcome.Done || movedTo is null)
        {
            await _notifications.DetectUnreadableItemsAsync(cancellationToken);
            return outcome == BrokenItemOutcome.NotBroken ? BrokenItemRepairResult.NotBroken : BrokenItemRepairResult.Missing;
        }

        Booth.BoothFetchStatus status;
        try
        {
            status = await _items.RegisterItemAsync(itemId, cancellationToken);
        }
        catch
        {
            // 中止・想定外の失敗でも、よけたまま放さない（押し直せるように元へ戻す）
            await _store.Items.PutBackBrokenAsync(itemId, movedTo, CancellationToken.None);
            throw;
        }

        if (status != Booth.BoothFetchStatus.Success)
        {
            await _store.Items.PutBackBrokenAsync(itemId, movedTo, cancellationToken);
            return status == Booth.BoothFetchStatus.NotFound
                ? BrokenItemRepairResult.NotFound
                : BrokenItemRepairResult.FetchFailed;
        }

        await _notifications.DetectUnreadableItemsAsync(cancellationToken);
        return BrokenItemRepairResult.Repaired;
    }
}
