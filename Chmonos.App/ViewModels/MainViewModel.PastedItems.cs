namespace Chmonos.App.ViewModels;

/// <summary>
/// 主画面：コピーした物の中の商品を、まとめて情報だけの商品（ファイルを持たない商品）として登録する（計画 26・ユーザ判断 2026-11-11）。
///
/// きっかけは、何着かをまとめたセットを買ったが、中身の各商品が手元に無かったこと。説明文・ショップの一覧・購入履歴を
/// 何行か選んでコピーし、貼るかドロップすると、見つけた商品を一覧で見せて選ばせる。
/// 登録は1件のときと同じ <see cref="Core.Commands.UiCommand.RegisterItem"/> を1件ずつ待って流す（門は1本ずつ・人が押した優先度は入口で掛かる）。
/// セットとつなぐ新しいデータは作らない。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// 動いているまとめての登録。続けて貼った分は同じ列の後ろに足す（列を2本にすると、帯の n/N が2つの数を混ぜる）。
    /// 記録には書かない：途中で閉じると残りは消える（もう一度貼れば、登録済みの物はライブラリにある印で外れて並ぶ）。
    /// </summary>
    private sealed class PastedBatch
    {
        public Queue<string> Waiting { get; } = new();

        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);

        public int Total { get; set; }

        public int Done { get; set; }

        public bool Stopping { get; set; }

        public List<Core.Models.ItemRecord> Registered { get; } = [];

        public List<string> NotOnBooth { get; } = [];

        public List<string> Unreachable { get; } = [];

        public int Stopped { get; set; }
    }

    private PastedBatch? _batch;
    private RelayCommand? _stopBatch;

    /// <summary>帯の「中止」を出すか。まとめての登録の間だけ（1件の登録は待つだけで済む）。</summary>
    public bool CanStopBatchRegistering => _batch is { Stopping: false };

    /// <summary>残りをやめる。今問い合わせている1件は最後まで待つ（途中で切ると、BOOTHの返事を捨てるだけになる）。</summary>
    public RelayCommand StopBatchRegisteringCommand => _stopBatch ??= new RelayCommand(() =>
    {
        if (_batch is { } batch)
        {
            batch.Stopping = true;
            NotifyDroppedRegistering();
        }
    });

    /// <summary>
    /// 見つけた商品を一覧で見せ、選んだ物を登録する。
    /// ライブラリにある物は、チェックを外して並べる（見つかったことは見せる。登録しても何も変わらない）。
    /// </summary>
    private async Task ChooseItemsToRegisterAsync(IReadOnlyList<string> itemIds)
    {
        var rows = new List<(string ItemId, string? KnownName)>();
        foreach (var itemId in itemIds)
        {
            var known = _services.Store.Items.Exists(itemId) ? await _services.Store.Items.LoadAsync(itemId) : null;
            rows.Add((itemId, known?.DisplayName));
        }

        var model = new RegisterPastedItemsDialogViewModel(rows);
        if (!RegisterPastedItemsDialogViewModel.Ask(model) || model.CheckedIds is not { Count: > 0 } chosen)
        {
            return;
        }

        if (_batch is { } running)
        {
            // 動いている列の後ろに足す。待っている物・済んだ物と同じ商品は足さない
            foreach (var itemId in chosen.Where(running.Seen.Add))
            {
                running.Waiting.Enqueue(itemId);
                running.Total++;
            }

            NotifyDroppedRegistering();
            return;
        }

        await RegisterBatchAsync(chosen);
    }

    private async Task RegisterBatchAsync(IReadOnlyList<string> itemIds)
    {
        var batch = new PastedBatch();
        foreach (var itemId in itemIds.Where(batch.Seen.Add))
        {
            batch.Waiting.Enqueue(itemId);
        }

        batch.Total = batch.Waiting.Count;
        _batch = batch;
        BeginDroppedRegistering();

        try
        {
            // 列が動いている間は、登録の後に続く問い合わせ（残りの画像・対応アバターの検出）を待たせる。
            // 待たせないと、2件目からの登録の問い合わせの合間に1本ずつ入り、遅くなる（未確定の列と同じ。CommandHandler.HoldAfterRegistration）
            using (_services.Commands.HoldAfterRegistration())
            {
                while (batch.Waiting.TryDequeue(out var itemId))
                {
                    if (batch.Stopping)
                    {
                        batch.Stopped = batch.Waiting.Count + 1;
                        break;
                    }

                    await RegisterOneOfBatchAsync(batch, itemId);
                    batch.Done++;
                    NotifyDroppedRegistering();
                }
            }
        }
        finally
        {
            _batch = null;
            EndDroppedRegistering();
        }

        if (batch.Registered.Count > 0)
        {
            await ReloadLibraryAsync();
        }

        ReportBatch(batch);
    }

    /// <summary>1件を登録する。失敗は覚えて次へ進む（1件ごとに窓を出すと、何十件の途中で何度も止まる）。</summary>
    private async Task RegisterOneOfBatchAsync(PastedBatch batch, string itemId)
    {
        Core.Commands.CommandResult result;
        try
        {
            result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.RegisterItem(itemId));
        }
        catch (Exception exception)
        {
            Core.Diagnostics.AppLog.Error("まとめての登録", exception);
            batch.Unreachable.Add(itemId);
            return;
        }

        switch (result)
        {
            case Core.Commands.CommandResult.RegisterFailed { NotOnBooth: true }:
                batch.NotOnBooth.Add(itemId);
                return;

            case Core.Commands.CommandResult.Failed:
                batch.Unreachable.Add(itemId);
                return;
        }

        if (await _services.Store.Items.LoadAsync(itemId) is { } added)
        {
            batch.Registered.Add(added);
        }
    }

    /// <summary>
    /// 済んだことを知らせる。登録できた物は下の帯（画面は移さない。1件の登録と同じ）、できなかった物は1つの窓にまとめる。
    /// </summary>
    private void ReportBatch(PastedBatch batch)
    {
        switch (batch.Registered.Count)
        {
            case 1:
                NoteRegistered(batch.Registered[0]);
                break;

            case > 1:
                NoteRegisteredMany(batch.Registered[0], batch.Registered.Count);
                break;
        }

        if (BatchFailureText(batch.Registered.Count, batch.NotOnBooth, batch.Unreachable, batch.Stopped) is { } failure)
        {
            Services.Notice.Show(
                failure,
                "登録できなかった商品があります",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 登録できなかった物の窓の本文。無ければ null（中止しただけのときも窓は出さない：押した本人が分かっている）。
    /// BOOTHに無い物と届かなかった物は次の一手が違うので分けて書く（E3）。商品名は取る前なので分からず、商品IDで言う。
    /// </summary>
    internal static string? BatchFailureText(int registered, IReadOnlyList<string> notOnBooth, IReadOnlyList<string> unreachable, int stopped)
    {
        var failed = notOnBooth.Count + unreachable.Count;
        if (failed == 0)
        {
            return null;
        }

        var lines = new List<string>
        {
            registered > 0
                ? $"{registered} 件を登録しました。{failed} 件は登録できませんでした。"
                : $"選んだ {failed} 件を登録できませんでした。",
        };

        if (notOnBooth.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("「BOOTHに見つからない商品」");
            lines.Add("商品ID " + string.Join("、", notOnBooth));
            lines.Add("IDが違うか、非公開か削除されています。");
        }

        if (unreachable.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("「取得できなかった商品」");
            lines.Add("商品ID " + string.Join("、", unreachable));
            lines.Add("通信を確かめて、少し待ってからもう一度貼り付けてください。");
        }

        if (stopped > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"残りの {stopped} 件は中止しました。");
        }

        return string.Join("\n", lines);
    }
}
