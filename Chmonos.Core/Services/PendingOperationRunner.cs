using Chmonos.Core.Diagnostics;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 何か所も順に書く操作（IDの変更・タグや属性の名前の変更）を、やりかけの記録（<c>pending-operations.json</c>）で囲んで走らせ、
/// 次の起動で残っていた記録の続きを済ませる（ユーザ判断 2026-10-06「A」）。
///
/// 一覧・商品・ほかの参照を順に書くので、途中で落ちると一部だけが新しい名前（ID）を指したまま残っていた。
/// IDの変更は元を消した後で落ちると、もう一度押しても元が無いので始められない。
/// **記録を書けなければ始めない**（落ちたときに続きを当てる手掛かりが無くなるため）。
/// 続きは、どの段も2回当てても同じ結果になる作りに頼る（タグ・属性は操作をそのままもう一度、IDの変更は <see cref="IItemService.ResumeItemIdChangeAsync"/>）。
///
/// 命令の層（<see cref="Commands.CommandHandler"/>）が1つ持つ。
/// </summary>
public sealed class PendingOperationRunner
{
    /// <summary>続きを済ませられなかった知らせの ID の頭。続きが記録の ID。</summary>
    public const string NotificationPrefix = "pending-operation:";

    private readonly JsonFileStore<List<PendingOperation>>? _journal;
    private readonly IItemService _items;
    private readonly IUserTagService? _userTags;
    private readonly IAttributeService? _attributes;
    private readonly ISettingsService? _settings;
    private readonly INotificationService? _notifications;

    /// <summary>
    /// この起動で始めて、まだ走っている記録。続きを当てる所は飛ばす——起動の裏で続きを読む間に人が押した操作は、
    /// 記録だけ見ると前の起動の残りと見分けが付かず、同じ操作を2本並べて走らせてしまう。
    /// </summary>
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);

    /// <param name="journal">やりかけの記録。null なら記録せずに走らせる（保存先を持たない一部の試験の組み立て）。</param>
    public PendingOperationRunner(
        JsonFileStore<List<PendingOperation>>? journal,
        IItemService items,
        IUserTagService? userTags,
        IAttributeService? attributes,
        ISettingsService? settings,
        INotificationService? notifications)
    {
        _journal = journal;
        _items = items;
        _userTags = userTags;
        _attributes = attributes;
        _settings = settings;
        _notifications = notifications;
    }

    /// <summary>商品まるごとを別のIDへ移す。記録を書けなければ <see cref="ItemIdChangeOutcome.NotRecorded"/>。</summary>
    public async Task<ItemIdChangeOutcome> ChangeItemIdAsync(
        string fromId,
        string toId,
        IReadOnlySet<int>? skippedPurchases,
        CancellationToken cancellationToken)
    {
        var operation = New(PendingOperationKind.ChangeItemId) with
        {
            FromId = fromId,
            ToId = toId,
            SkippedPurchases = skippedPurchases is { Count: > 0 } ? [.. skippedPurchases.Order()] : null,
        };

        return await RecordedAsync(
            operation,
            () => _items.ChangeItemIdAsync(fromId, toId, skippedPurchases, cancellationToken),
            cancellationToken) is (true, var outcome)
            ? outcome
            : ItemIdChangeOutcome.NotRecorded;
    }

    /// <summary>ユーザータグの名前の変更・統合（保存した検索の条件まで）。記録を書けなければ null。</summary>
    public async Task<UserTagEditResult?> RenameUserTagAsync(
        string top,
        string? sub,
        string newName,
        CancellationToken cancellationToken)
    {
        var operation = New(PendingOperationKind.RenameUserTag) with { Top = top, Sub = sub, NewName = newName };
        return await RecordedAsync(operation, () => RunRenameUserTagAsync(top, sub, newName, cancellationToken), cancellationToken)
            is (true, var result)
            ? result
            : null;
    }

    /// <summary>属性の名前の変更・統合（設定のカードの属性・保存した検索の条件まで）。記録を書けなければ null。</summary>
    public async Task<AttributeEditResult?> RenameAttributeAsync(
        string oldName,
        string newName,
        AttributeMergeValue keep,
        CancellationToken cancellationToken)
    {
        var operation = New(PendingOperationKind.RenameAttribute) with { OldName = oldName, NewName = newName, Keep = keep };
        return await RecordedAsync(operation, () => RunRenameAttributeAsync(oldName, newName, keep, cancellationToken), cancellationToken)
            is (true, var result)
            ? result
            : null;
    }

    /// <summary>
    /// 前の起動で残った記録の続きを、始めた順に済ませる。済んだ記録は消し、前に出した「続けられなかった」知らせは解消済みにする。
    /// **続けられなかった記録は残し**（次の起動でまた試す）、知らせに出す。BOOTHへは問い合わせない。
    /// </summary>
    /// <returns>扱った記録の数（済んだ物と、続けられなかった物）。</returns>
    public async Task<int> ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_journal is null)
        {
            return 0;
        }

        List<PendingOperation> pending;
        try
        {
            pending = await _journal.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // 手で壊した記録は読めないまま置く（直し方はこちらで決めない）。読めない間は続きを当てない
            AppLog.Error("やりかけの記録を読む", exception);
            return 0;
        }

        var handled = 0;
        foreach (var operation in pending.OrderBy(entry => entry.StartedAt))
        {
            lock (_running)
            {
                if (_running.Contains(operation.Id))
                {
                    continue;
                }
            }

            handled++;
            try
            {
                await ResumeOneAsync(operation, cancellationToken);
                await RemoveAsync(operation, cancellationToken);
                if (_notifications is not null)
                {
                    await _notifications.ResolveAsync([NotificationPrefix + operation.Id], cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AppLog.Error($"やりかけの操作の続き（{operation.Kind}）", exception);
                await NotifyFailedAsync(operation, cancellationToken);
            }
        }

        return handled;
    }

    private async Task ResumeOneAsync(PendingOperation operation, CancellationToken cancellationToken)
    {
        switch (operation.Kind)
        {
            case PendingOperationKind.ChangeItemId:
                var outcome = await _items.ResumeItemIdChangeAsync(
                    Required(operation.FromId),
                    Required(operation.ToId),
                    (operation.SkippedPurchases ?? []).ToHashSet(),
                    cancellationToken);

                // 移す先へまだ何も書いていなかった物は、元のまま（押す前と同じ）なので記録を消すだけにする。
                // 自分で足した画像を写せなかった物は、元も残っているので次の起動でまた試す
                if (outcome is ItemIdChangeOutcome.ImagesNotMoved or ItemIdChangeOutcome.TargetUnavailable)
                {
                    throw new IOException($"IDの変更の続きを済ませられませんでした（{outcome}）。");
                }

                if (outcome == ItemIdChangeOutcome.NotStarted)
                {
                    AppLog.Warn("やりかけの操作の続き", $"IDの変更 {operation.FromId} → {operation.ToId} は書き始める前に止まっていた。元のまま記録を消す");
                }

                return;

            case PendingOperationKind.RenameUserTag:
                await RunRenameUserTagAsync(Required(operation.Top), operation.Sub, Required(operation.NewName), cancellationToken);
                return;

            case PendingOperationKind.RenameAttribute:
                await RunRenameAttributeAsync(
                    Required(operation.OldName),
                    Required(operation.NewName),
                    operation.Keep ?? AttributeMergeValue.KeepTarget,
                    cancellationToken);
                return;

            default:
                throw new InvalidDataException($"やりかけの記録の種類が読めません（{operation.Kind}）。");
        }
    }

    private static string Required(string? value)
        => value ?? throw new InvalidDataException("やりかけの記録に要る欄がありません。");

    private async Task<UserTagEditResult> RunRenameUserTagAsync(
        string top,
        string? sub,
        string newName,
        CancellationToken cancellationToken)
    {
        if (_userTags is null)
        {
            throw new InvalidOperationException("ユーザータグの編集が組み立てのときに渡されていません（アプリの不具合）。");
        }

        var renamed = sub is null
            ? await _userTags.RenameTopAsync(top, newName, cancellationToken)
            : await _userTags.RenameSubAsync(top, sub, newName, cancellationToken);

        // 保存した検索の条件の名前も付いていかせる。統合のときは、残る側の綴り（一覧にある名前）へ寄せる
        var target = newName.Trim();
        if (target.Length > 0)
        {
            var tops = renamed.Master.Tops;
            var topSpelling = sub is null
                ? tops.FirstOrDefault(entry => Same(entry.Name, target))?.Name ?? target
                : target;
            var subSpelling = sub is null
                ? target
                : tops.FirstOrDefault(entry => Same(entry.Name, top))
                    ?.Subs.FirstOrDefault(entry => Same(entry.Name, target))?.Name ?? target;
            await FollowInSavedSearchesAsync(
                entries => sub is null
                    ? SavedSearches.RenameUserTagTop(entries, top, topSpelling)
                    : SavedSearches.RenameUserTagSub(entries, top, sub, subSpelling),
                cancellationToken);
        }

        return renamed;
    }

    private async Task<AttributeEditResult> RunRenameAttributeAsync(
        string oldName,
        string newName,
        AttributeMergeValue keep,
        CancellationToken cancellationToken)
    {
        if (_attributes is null)
        {
            throw new InvalidOperationException("属性の編集が組み立てのときに渡されていません（アプリの不具合）。");
        }

        var renamed = await _attributes.RenameAsync(oldName, newName, keep, cancellationToken);

        // 設定で選んだ属性の名前も付いていかせる（設定の書き込みの錠の中で今の値に当てる）。
        // 統合のときは、残る側の綴り（一覧にある名前）へ寄せる
        var trimmed = newName.Trim();
        if (_settings is not null && trimmed.Length > 0)
        {
            var kept = renamed.Master.Attributes
                .Select(definition => definition.Name)
                .FirstOrDefault(name => Same(name, trimmed)) ?? trimmed;
            await _settings.UpdateAsync(current => current.WithCardAttributeRenamed(oldName, kept), cancellationToken);
            await FollowInSavedSearchesAsync(entries => SavedSearches.RenameAttribute(entries, oldName, kept), cancellationToken);
        }

        return renamed;
    }

    /// <summary>保存した検索の条件の名前も同じ名前へ寄せる（錠の中で今の並びに当てる）。設定の保存先が無い組み立てでは何もしない。</summary>
    private async Task FollowInSavedSearchesAsync(
        Func<IReadOnlyList<SearchHistoryEntry>, IReadOnlyList<SearchHistoryEntry>> rewrite,
        CancellationToken cancellationToken)
    {
        if (_settings is null)
        {
            return;
        }

        await _settings.ChangeSavedSearchesAsync(list => new SavedSearchList { Entries = rewrite(list.Entries) }, cancellationToken);
    }

    private static PendingOperation New(PendingOperationKind kind) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Kind = kind,
        StartedAt = DateTimeOffset.Now,
    };

    /// <summary>
    /// 記録を書いてから走らせ、終わったら記録を消す。**書けなければ走らせない**（偽を返す）。
    /// 走らせた中で例外が出たら記録は残す（次の起動で続ける）。
    /// </summary>
    private async Task<(bool Started, T Result)> RecordedAsync<T>(
        PendingOperation operation,
        Func<Task<T>> run,
        CancellationToken cancellationToken)
    {
        if (_journal is null)
        {
            return (true, await run());
        }

        lock (_running)
        {
            _running.Add(operation.Id);
        }

        try
        {
            try
            {
                await _journal.UpdateAsync(list => [.. list, operation], cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                AppLog.Error($"やりかけの記録を書く（{operation.Kind}）", exception);
                return (false, default!);
            }

            var result = await run();
            await RemoveAsync(operation, cancellationToken);
            return (true, result);
        }
        finally
        {
            lock (_running)
            {
                _running.Remove(operation.Id);
            }
        }
    }

    /// <summary>
    /// 済んだ記録を消す。消せなくても操作は済んでいるので失敗にしない——次の起動で同じ操作をもう一度当てるだけで、2回当てても変わらない。
    /// </summary>
    private async Task RemoveAsync(PendingOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            await _journal!.TryUpdateAsync(
                list => list.Any(entry => entry.Id == operation.Id)
                    ? [.. list.Where(entry => entry.Id != operation.Id)]
                    : null,
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AppLog.Error($"済んだやりかけの記録を消す（{operation.Kind}）", exception);
        }
    }

    private async Task NotifyFailedAsync(PendingOperation operation, CancellationToken cancellationToken)
    {
        if (_notifications is null)
        {
            return;
        }

        try
        {
            await _notifications.AddAsync(
                new NotificationRecord
                {
                    Id = NotificationPrefix + operation.Id,
                    Kind = NotificationKind.UnfinishedOperation,
                    ItemId = operation.Kind == PendingOperationKind.ChangeItemId ? operation.FromId : null,
                    Title = TitleOf(operation),
                    Detail = "途中で止まっていた操作を続けられませんでした。次に起動したときに、もう一度続けます。",
                    CreatedAt = DateTimeOffset.Now,
                    IsStrong = true,
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AppLog.Error("やりかけの操作の続きの失敗を知らせる", exception);
        }
    }

    /// <summary>知らせの見出し。どの操作かを言う。</summary>
    public static string TitleOf(PendingOperation operation) => operation.Kind switch
    {
        PendingOperationKind.ChangeItemId => $"商品IDの変更（{operation.FromId} → {operation.ToId}）",
        PendingOperationKind.RenameUserTag => operation.Sub is null
            ? $"タグ「{operation.Top}」の名前の変更（→「{operation.NewName}」）"
            : $"タグ「{operation.Top}／{operation.Sub}」の名前の変更（→「{operation.NewName}」）",
        PendingOperationKind.RenameAttribute => $"属性「{operation.OldName}」の名前の変更（→「{operation.NewName}」）",
        _ => "途中で止まった操作",
    };

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.CurrentCultureIgnoreCase);
}
