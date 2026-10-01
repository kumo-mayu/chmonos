using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>保存先の使用状況。設定画面に出す。</summary>
public sealed record StorageUsage
{
    public required string Root { get; init; }

    public required long ImageBytes { get; init; }

    public required int ImageCount { get; init; }

    public required long ItemBytes { get; init; }

    public required int ItemCount { get; init; }
}

/// <summary>非表示にした商品1件。設定画面から戻せるようにする。</summary>
public sealed record HiddenItem
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }
}

/// <summary>管理から外したファイル1件。</summary>
public sealed record ExcludedFile
{
    public required string Hash { get; init; }

    public required string Path { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset ExcludedAt { get; init; }
}

/// <summary>
/// 「この商品のものではない」と外したファイル1件。
/// 除外と違い、外したのはその商品への紐付けだけで、ファイル自体は管理下に残る。
/// </summary>
public sealed record DetachedRecord
{
    public required string Hash { get; init; }

    public required string ItemId { get; init; }

    /// <summary>外したときの商品名。今は消えているかもしれないので、引けなければIDのまま。</summary>
    public required string ItemName { get; init; }

    public required string Path { get; init; }
}

public interface ISettingsService
{
    /// <summary>今の設定。書くたびに差し替わる。</summary>
    AppSettings Current { get; }

    /// <summary>今の設定を変える。変え方を関数で渡す。</summary>
    Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default);

    /// <summary>画面が覚えている状態。書くたびに差し替わる。</summary>
    UiState UiState { get; }

    /// <summary>画面が覚えている状態を変える。変え方を関数で渡す。</summary>
    Task<UiState> UpdateUiStateAsync(Func<UiState, UiState> change, CancellationToken cancellationToken = default);

    /// <summary>検索の履歴を変える。変え方を関数で渡す。</summary>
    Task<SearchHistoryList> ChangeSearchHistoryAsync(
        Func<SearchHistoryList, SearchHistoryList> change,
        CancellationToken cancellationToken = default);

    Task<StorageUsage> LoadUsageAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HiddenItem>> LoadHiddenAsync(CancellationToken cancellationToken = default);

    Task UnhideAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>除外したファイルを新しい順に。**読むのは呼んだスレッドの外**（同期の読み口は持たない。設定の画面が開くたびに読むため）。</summary>
    Task<IReadOnlyList<ExcludedFile>> LoadExcludedAsync(CancellationToken cancellationToken = default);

    Task RestoreExcludedAsync(string hash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DetachedRecord>> LoadDetachedAsync(CancellationToken cancellationToken = default);

    Task ForgetDetachedAsync(string hash, string itemId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 設定の保存と、設定画面に出す集計。
///
/// 「非表示にした商品」と「管理から外したファイル」をここから戻せるようにしているのは、
/// どちらも普段の画面からは見えなくなる操作で、設定画面以外に取り消す場所が無いため。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly DataStore _store;

    public SettingsService(DataStore store)
    {
        _store = store;

        // 以前の版は取得の間隔を500msまで保存できた。約束（1.5秒以上）の範囲に戻してから使う
        Current = store.Settings.Load().Normalized();
        UiState = store.UiState.Load();
    }

    /// <summary>画面が覚えている状態（ui-state.json）。設定と同じく、持つのはここだけ。</summary>
    public UiState UiState { get; private set; }

    /// <summary>画面が覚えている状態を変える。設定と同じく、錠の中で今の値に当てる。</summary>
    public Task<UiState> UpdateUiStateAsync(Func<UiState, UiState> change, CancellationToken cancellationToken = default)
        // 設定と同じく、手元の値は錠の中で差し替える（錠の外だと古い方が後から代入され得る）
        => _store.UiState.UpdateAsync(change, written => UiState = written, cancellationToken);

    /// <summary>
    /// 今の設定。サービスには値ではなく「今の設定を読む関数」を渡しているので、書けばすぐ効く。
    /// **設定を持つのはここだけ**（技術的負債 1-1）。前は画面ごとに写しを持ち、取り込み画面だけディスクから読んで書いていたので、
    /// 取り込み画面で足した取り込み元が、別の画面の保存（古い写し）で消えていた。
    /// </summary>
    public AppSettings Current { get; private set; }

    /// <summary>
    /// 設定を変える。**丸ごと書かず、変え方を関数で渡す。**錠の中でディスクの今の設定に当てるので、
    /// 別の画面が同時に別の項目を書いても消し合わない（同じファイルへの書き込みも重ならない・技術的負債 1-4）。
    /// 画面からは <see cref="Commands.UiCommand.ChangeSettings"/> で呼ぶ。
    /// </summary>
    public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default)
        // 手元の値は錠の中で差し替える。錠の外だと、2本の保存が重なったときに先に書いた方が後から代入され、
        // ディスクより古い設定を持ち続けていた
        => _store.Settings.UpdateAsync(
            current => change(current).Normalized(),
            written => Current = written,
            cancellationToken);

    /// <summary>
    /// 検索の履歴を変える。前は検索画面と設定画面が読んだ写しを丸ごと書いていた（技術的負債 3-1）。
    /// 錠の中で今の履歴に当てる。画面からは <see cref="Commands.UiCommand.ChangeSearchHistory"/> で呼ぶ。
    /// </summary>
    public Task<SearchHistoryList> ChangeSearchHistoryAsync(
        Func<SearchHistoryList, SearchHistoryList> change,
        CancellationToken cancellationToken = default)
        => _store.SearchHistory.UpdateAsync(change, cancellationToken);

    /// <summary>保存先が何をどれだけ使っているか。画像は実ファイルを数える。</summary>
    public Task<StorageUsage> LoadUsageAsync(CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                var (imageBytes, imageCount) = Measure(_store.Paths.ImagesDir);
                var (itemBytes, itemCount) = Measure(_store.Paths.ItemsDir);

                return new StorageUsage
                {
                    Root = _store.Paths.Root,
                    ImageBytes = imageBytes,
                    ImageCount = imageCount,
                    ItemBytes = itemBytes,
                    ItemCount = itemCount,
                };
            },
            cancellationToken);

    private static (long Bytes, int Count) Measure(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return (0, 0);
            }

            long bytes = 0;
            var count = 0;

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    bytes += new FileInfo(path).Length;
                    count++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 触れないファイルは数えないだけ
                }
            }

            return (bytes, count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    public async Task<IReadOnlyList<HiddenItem>> LoadHiddenAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return loaded.Items
            .Where(item => item.Local.IsHidden)
            .Select(item => new HiddenItem
            {
                ItemId = item.Id,
                Name = item.DisplayName,
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task UnhideAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var item = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (item is null || !item.Local.IsHidden)
        {
            return;
        }

        await _store.Items.SaveLocalAsync(
            itemId,
            item.Local with { IsHidden = false },
            LocalOwners.Visibility,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 除外したファイルを新しい順に。
    /// 除外した日時は記録に必ずある（無い記録はファイルごと読めない。古い形に合わせる救済は足さない）。
    /// 同じ日時の物は、記録の後ろ（後から足した物）を上にする——まとめて除外すると日時の揃った物が並び得るので、
    /// 並びを決めておかないと、読み直すたびに上下が入れ替わって見えかねない。
    /// </summary>
    private IReadOnlyList<ExcludedFile> LoadExcluded()
        => _store.Excluded.Load()
            .Select((entry, index) => (Index: index, File: new ExcludedFile
            {
                Hash = entry.Hash,
                Path = entry.Paths.FirstOrDefault() ?? entry.Hash,
                Reason = entry.Reason,
                ExcludedAt = entry.ExcludedAt,
            }))
            .OrderByDescending(pair => pair.File.ExcludedAt)
            .ThenByDescending(pair => pair.Index)
            .Select(pair => pair.File)
            .ToList();

    /// <summary>
    /// <see cref="LoadExcluded"/> を呼んだスレッドの外で。記録は除外するほど大きくなる（上限なし。5,000 件で約 15ms・2万件で約 57ms。
    /// `docs/research/large-files-2026-09-30.md`）。設定の画面は開くたびに読むので、その間画面を止めない。
    /// 同期の読み口を外に出さないのは、画面から呼ばれて画面のスレッドで読む道を作らないため。
    /// </summary>
    public Task<IReadOnlyList<ExcludedFile>> LoadExcludedAsync(CancellationToken cancellationToken = default)
        => Task.Run(LoadExcluded, cancellationToken);

    /// <summary>
    /// 除外を解除する。次の取り込みでまた未確定として出てくる。
    /// ここで解除しないと、一度除外したファイルは二度と現れない。
    /// </summary>
    public async Task RestoreExcludedAsync(string hash, CancellationToken cancellationToken = default)
    {
        // 取り込みも同じファイルへ書くので、読み直してから消す（`docs/spec/data-model.md`）。
        // 錠の外で読むと、解除の最中に取り込みが足した除外が消える
        await _store.Excluded.UpdateAsync(
            entries =>
            [
                .. entries.Where(entry => !string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase)),
            ],
            cancellationToken);
    }

    /// <summary>
    /// 商品ページで外したファイルの一覧。
    ///
    /// 出すのは、**外した記録を全商品まとめて見られる場所がここしかない**ため
    /// （1件ずつなら商品ページで灰色の行として見え、「この商品に戻す」で戻せる）。
    /// 外した印は商品のJSONの中にあるので全商品から集める。日時は持たない（ユーザ判断）ので、商品名の順に並べる。
    /// </summary>
    public async Task<IReadOnlyList<DetachedRecord>> LoadDetachedAsync(
        CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return loaded.Items
            .SelectMany(item => item.Local.LocalFiles
                .Where(file => file.Detached)
                .Select(file => new DetachedRecord
                {
                    Hash = file.Hash,
                    ItemId = item.Id,
                    ItemName = item.DisplayName,
                    Path = file.Paths.FirstOrDefault() ?? file.Hash,
                }))
            .OrderBy(record => record.ItemName, StringComparer.CurrentCulture)
            .ThenBy(record => record.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 外した記録を捨てる（商品のJSONから、印の付いた行ごと消す）。
    /// 次の取り込みで、手掛かりが指すならまたその商品へ紐付く。
    /// </summary>
    public async Task ForgetDetachedAsync(
        string hash,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var item = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (item is null)
        {
            return;
        }

        var files = item.Local.LocalFiles
            .Where(file => !(file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (files.Count == item.Local.LocalFiles.Count)
        {
            return;
        }

        await _store.Items.SaveLocalAsync(
            itemId,
            item.Local with { LocalFiles = files },
            [LocalField.LocalFiles],
            cancellationToken: cancellationToken);
    }
}
