using System.IO;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>
/// 商品を使っている改変と Unity のプロジェクトを見張る（使っている商品の更新の促し・<c>docs/spec/used-updates.md</c>）。
///
/// 調べるプロジェクトは、改変に紐付けたプロジェクトと今開いている Unity のプロジェクト（ユーザ判断 2026-11-09）。
/// 起動の後と窓に戻ったときに裏で調べる。手元のディスクだけを読み、BOOTH には問い合わせない。
/// 1つ調べるのに 782件で 0.6〜4.7秒かかる [実測・unity.md] ので、窓に戻るたびに全部は調べ直さない：
/// このアプリで一度調べたプロジェクトは、Unity で開いている間だけ調べ直す（中身が変わるのは Unity で入れたとき）。
/// </summary>
public sealed class UsedItemsWatch
{
    private readonly AppServiceContainer _services;
    private readonly Func<IReadOnlyList<ItemRecord>> _items;
    private readonly CoalescedRun _refresh;

    /// <summary>このアプリで調べ終えたプロジェクトの結果（場所 → 中身）。控えから読んだだけの物も入る。</summary>
    private readonly Dictionary<string, UnityProjectScan> _scans = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>このアプリで実際に調べたプロジェクト。控えだけの物は入らない（一度は調べ直す）。</summary>
    private readonly HashSet<string> _scanned = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();

    /// <param name="items">今の全商品（検索の写し）。調べるのは持っている商品だけ。</param>
    public UsedItemsWatch(AppServiceContainer services, Func<IReadOnlyList<ItemRecord>> items)
    {
        _services = services;
        _items = items;
        _refresh = new CoalescedRun(RunAsync);
    }

    /// <summary>今の答え。調べ終わるたびに差し替わる。</summary>
    public UsedItems Current { get; private set; } = UsedItems.Empty;

    /// <summary>答えが変わったとき。裏のスレッドから来る。</summary>
    public event EventHandler? Changed;

    /// <summary>調べ直しを頼む（起動の後・窓に戻ったとき・改変を書いたとき）。重なった依頼は1回にまとめる。</summary>
    public Task RequestAsync(CancellationToken cancellationToken = default) => _refresh.RequestAsync(cancellationToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var modifications = (await _services.Store.Modifications.LoadAllAsync(cancellationToken)).Modifications;
        var open = await Task.Run(OpenProjects, cancellationToken);
        var targets = modifications
            .Select(modification => UnityProjectPath.Normalize(modification.UnityProject))
            .OfType<string>()
            .Concat(open)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 先に控えを見せる（調べ終わるまで促しが出ないと、開いた商品ページで帯が遅れて出る）
        lock (_sync)
        {
            foreach (var project in targets.Where(project => !_scans.ContainsKey(project)))
            {
                if (_services.UnityScans.Load(project) is { } saved)
                {
                    _scans[project] = saved;
                }
            }

            // 改変から外れ、開いてもいないプロジェクトは数えない
            foreach (var gone in _scans.Keys.Except(targets, StringComparer.OrdinalIgnoreCase).ToList())
            {
                _scans.Remove(gone);
                _scanned.Remove(gone);
            }
        }

        Publish(modifications);

        // 検索の写しを読み終える前は商品が無い。ここで調べると「何も入っていない」を調べ済みにしてしまうので、控えのまま待つ
        // （読み終えると検索が知らせて、また頼まれる）
        var owned = _items().Where(item => item.IsOwned).ToList();
        if (owned.Count == 0)
        {
            return;
        }

        foreach (var project in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool again;
            lock (_sync)
            {
                again = !_scanned.Contains(project) || open.Contains(project, StringComparer.OrdinalIgnoreCase);
            }

            if (!again)
            {
                continue;
            }

            var scan = await Task.Run(() => ScanOrNull(project, owned, cancellationToken), cancellationToken);
            lock (_sync)
            {
                _scanned.Add(project);
                if (scan is not null)
                {
                    _scans[project] = scan;
                }
            }

            Publish(modifications);
        }
    }

    /// <summary>調べる。フォルダが無い・読めないときは null（前の結果を残す。外付けをつなぎ直せばまた使える）。</summary>
    private UnityProjectScan? ScanOrNull(string project, IReadOnlyList<ItemRecord> owned, CancellationToken cancellationToken)
    {
        if (_services.ProjectFolderState(project) != DiskAnswer.Present)
        {
            return null;
        }

        try
        {
            var scan = _services.ScanUnityProject(project, owned, null, cancellationToken);
            _services.UnityScans.Save(project, scan);
            return scan;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private List<string> OpenProjects()
        => _services.OpenUnityEditors()
            .Select(editor => UnityEditors.PathOf(editor) is { } path ? UnityProjectPath.Normalize(path) ?? path : null)
            .OfType<string>()
            .ToList();

    private void Publish(IReadOnlyList<ModificationRecord> modifications)
    {
        Dictionary<string, UnityProjectScan> scans;
        lock (_sync)
        {
            scans = new Dictionary<string, UnityProjectScan>(_scans, StringComparer.OrdinalIgnoreCase);
        }

        Current = new UsedItems(modifications, scans);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
