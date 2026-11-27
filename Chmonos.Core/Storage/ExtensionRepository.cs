using Chmonos.Core.Models;

namespace Chmonos.Core.Storage;

/// <summary>
/// 外のソフトの情報（拡張データ）の置き場（保存先の <c>extensions/</c>。docs/spec/local-api-extensions.md「置き場」）。
///
/// 形：<c>extensions/&lt;名前空間&gt;/schema.json</c>（宣言）。値は付け先ごとに1ファイル（<see cref="ExtensionValueFile"/>）。
/// 商品の JSON に入れないのは、古い版（v1.0.0）が商品を保存すると知らない欄が消えるため・ソフトの書き込みが編集画面とぶつからないため（spec）。
///
/// **錠は置き場全体で1本。**値の確かめは今の宣言に当てるので、宣言の置き換えと値の書き込みが重なると、
/// 外した直後の欄へ値を書けたり、型を変えた欄に古い型の値が残ったりする。書き込みは小さなファイル1つで数 ms なので、1本で待たせても困らない
/// </summary>
public sealed class ExtensionRepository(AppPaths paths)
{
    public const string SchemaFileName = "schema.json";

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Root => paths.ExtensionsDir;

    /// <summary>名前空間のフォルダ。名前は決まりを通った物だけ受ける（フォルダの名前になるので、外へ出る名前を作らない）。</summary>
    public string NamespaceDir(string ns)
        => ExtensionNamespace.IsValid(ns)
            ? Path.Combine(Root, ns)
            : throw new ArgumentException($"名前空間の形が合いません: {ns}", nameof(ns));

    public string SchemaPath(string ns) => Path.Combine(NamespaceDir(ns), SchemaFileName);

    /// <summary>宣言（まだ無ければ null）。</summary>
    public ExtensionSchemaFile? LoadSchema(string ns)
    {
        var path = SchemaPath(ns);
        return File.Exists(path) ? Normalized(JsonStore.Read<ExtensionSchemaFile>(path)) : null;
    }

    /// <summary>
    /// 宣言のある名前空間を全部（名前の順）。名前の決まりに合わないフォルダ（手で作った物）と、読めない宣言は飛ばす
    /// （1つの壊れた宣言で、ほかのソフトの宣言まで読めなくしない）
    /// </summary>
    public IReadOnlyList<(string Namespace, ExtensionSchemaFile Schema)> LoadAllSchemas()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        var result = new List<(string, ExtensionSchemaFile)>();
        foreach (var directory in Directory.EnumerateDirectories(Root).Order(StringComparer.Ordinal))
        {
            var ns = Path.GetFileName(directory);
            if (!ExtensionNamespace.IsValid(ns))
            {
                continue;
            }

            try
            {
                if (LoadSchema(ns) is { } schema)
                {
                    result.Add((ns, schema));
                }
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
            {
                Diagnostics.AppLog.Error($"ソフトの情報の宣言を読む（{ns}）", exception);
            }
        }

        return result;
    }

    /// <summary>
    /// 錠の中で今の宣言に変え方を当てて書く。変え方が null を返したら書かない。書いた宣言（書かなければ null）を返す
    /// </summary>
    public async Task<ExtensionSchemaFile?> ChangeSchemaAsync(string ns, Func<ExtensionSchemaFile?, ExtensionSchemaFile?> change, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (change(LoadSchema(ns)) is not { } updated)
            {
                return null;
            }

            Directory.CreateDirectory(NamespaceDir(ns));
            await JsonStore.WriteAsync(SchemaPath(ns), updated, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 値のファイル（<c>&lt;名前空間&gt;/&lt;付け先&gt;/&lt;鍵&gt;.json</c>、全体は <c>&lt;名前空間&gt;/global.json</c>）。
    /// 鍵は <see cref="ExtensionTarget"/> の決まりを通った物だけなので外へ出ないが、念のため置き場の中かも見る
    /// </summary>
    public string ValuePath(string ns, ExtensionTarget target) => FileOf(ns, target, ".json");

    /// <summary>
    /// メモ欄のファイル（値の横の <c>&lt;鍵&gt;.memo.json</c>）。値と分けるのは、検索の欄を読むたびに大きなメモまで読まないため・
    /// 値とメモの ETag を別にするため（片方を書いても、もう片方の If-Match が外れない）
    /// </summary>
    public string MemoPath(string ns, ExtensionTarget target) => FileOf(ns, target, ".memo.json");

    private string FileOf(string ns, ExtensionTarget target, string suffix)
    {
        var path = target.Kind == ExtensionTarget.GlobalKind
            ? Path.Combine(NamespaceDir(ns), target.Key + suffix)
            : Path.Combine(NamespaceDir(ns), target.Kind, target.Key + suffix);
        StoreIds.EnsureInside(path, Root);
        return path;
    }

    /// <summary>値（無ければ null）。欠けた欄の並び（null）は空として受ける。</summary>
    public ExtensionValueFile? LoadValues(string ns, ExtensionTarget target)
    {
        var path = ValuePath(ns, target);
        return File.Exists(path) && JsonStore.Read<ExtensionValueFile>(path) is { } file
            ? file with { Values = new Dictionary<string, System.Text.Json.JsonElement>(file.Values ?? [], StringComparer.Ordinal) }
            : null;
    }

    /// <summary>メモ欄（無ければ null）。</summary>
    public ExtensionMemoFile? LoadMemo(string ns, ExtensionTarget target)
    {
        var path = MemoPath(ns, target);
        return File.Exists(path) ? JsonStore.Read<ExtensionMemoFile>(path) : null;
    }

    /// <summary>
    /// 商品・改変が今も在るか（記録のファイルで見る）。値を書く時に錠の中で呼ぶ：消す側は記録を消してから同じ錠で値を消すので、
    /// どちらが先でも、消えた商品に値が残らない（<see cref="DeleteTargetAsync"/>）。ファイル・ショップ・全体は写しで見るので、ここでは真
    /// </summary>
    public bool TargetExists(ExtensionTarget target) => target.Kind switch
    {
        ExtensionTarget.Items => File.Exists(paths.ItemFile(target.Key)),
        ExtensionTarget.Modifications => File.Exists(paths.ModificationFile(target.Key)),
        _ => true,
    };

    /// <summary>
    /// 錠の中で今の宣言と値に変え方を当てて書く。変え方が null を返したら書かない（null を返す）。
    /// 欄が1つも残らなければファイルを消す（空のファイルを溜めない）。書いた後の値を返す
    /// </summary>
    public async Task<ExtensionValueFile?> ChangeValuesAsync(
        string ns,
        ExtensionTarget target,
        Func<ExtensionSchemaFile?, ExtensionValueFile?, ExtensionValueFile?> change,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (change(LoadSchema(ns), LoadValues(ns, target)) is not { } updated)
            {
                return null;
            }

            var path = ValuePath(ns, target);
            if (updated.Values.Count == 0)
            {
                await DeleteFilesAsync([path], cancellationToken);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await JsonStore.WriteAsync(path, updated, cancellationToken);
            }

            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>メモ欄を錠の中で変える。<see cref="ExtensionMemoFile.Memo"/> が null ならファイルを消す。変え方が null を返したら書かない。</summary>
    public async Task<ExtensionMemoFile?> ChangeMemoAsync(
        string ns,
        ExtensionTarget target,
        Func<ExtensionMemoFile?, ExtensionMemoFile?> change,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (change(LoadMemo(ns, target)) is not { } updated)
            {
                return null;
            }

            var path = MemoPath(ns, target);
            if (updated.Memo is null or { ValueKind: System.Text.Json.JsonValueKind.Null })
            {
                await DeleteFilesAsync([path], cancellationToken);
                return updated with { Memo = null };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await JsonStore.WriteAsync(path, updated, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 付け先が消えた（商品・改変を外した）時に、全部の名前空間からその値とメモを消す（spec「付け先の変化」）。
    /// 呼ぶのは消す側の錠の中で、**保存先の門（<see cref="StoreWriteGate"/>）に数えられていない所から**。門の中から呼ぶと、
    /// この錠を持って門を待つ書き込みと待ち合う（錠の順は「商品・改変の錠 → この錠 → 門」）
    /// </summary>
    public async Task DeleteTargetAsync(ExtensionTarget target, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var files = Namespaces().SelectMany(ns => new[] { ValuePath(ns, target), MemoPath(ns, target) }).Where(File.Exists).ToList();
            await DeleteFilesAsync(files, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 付け先の鍵が変わった（商品 ID の付け替え・統合）時に、名前空間ごとに値とメモを移す（spec「付け先の変化」）。
    /// 移す先に値かメモが1つでもあれば移す先の組を残して元を消し、無ければ元の組を移す（属性の「移す先に1つでも付いていれば移す先の組」と同じ）。
    /// 値とメモを組で扱うのは、メモがソフトの同期の印など値と対の物を持つため（片方だけ移すと食い違う）。
    /// 何回当てても同じ結果になる（止まった付け替えの続きでも呼ぶ）
    /// </summary>
    public async Task MoveTargetAsync(ExtensionTarget from, ExtensionTarget to, CancellationToken cancellationToken = default)
    {
        if (from == to)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var writing = await StoreWriteGate.EnterAsync(cancellationToken);
            foreach (var ns in Namespaces())
            {
                var pairs = new[] { (ValuePath(ns, from), ValuePath(ns, to)), (MemoPath(ns, from), MemoPath(ns, to)) };
                if (!pairs.Any(pair => File.Exists(pair.Item1)))
                {
                    continue;
                }

                var keepTarget = pairs.Any(pair => File.Exists(pair.Item2));
                foreach (var (source, destination) in pairs.Where(pair => File.Exists(pair.Item1)))
                {
                    if (keepTarget)
                    {
                        File.Delete(source);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Move(source, destination);
                    }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>決まりに合う名前空間のフォルダ（宣言が無くても値が残っていれば拾う）。</summary>
    private IEnumerable<string> Namespaces()
        => Directory.Exists(Root)
            ? Directory.EnumerateDirectories(Root).Select(Path.GetFileName).OfType<string>().Where(ExtensionNamespace.IsValid).ToList()
            : [];

    private static async Task DeleteFilesAsync(IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        if (!files.Any(File.Exists))
        {
            return;
        }

        using var writing = await StoreWriteGate.EnterAsync(cancellationToken);
        foreach (var file in files.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    /// <summary>手で書いた宣言の欠けた並び（null）を空として受ける。</summary>
    private static ExtensionSchemaFile? Normalized(ExtensionSchemaFile? schema)
        => schema is null
            ? null
            : schema with
            {
                Label = schema.Label ?? string.Empty,
                Software = schema.Software ?? string.Empty,
                Fields = (schema.Fields ?? []).Where(field => field is not null).ToList(),
                Hidden = (schema.Hidden ?? []).Where(field => field is not null).ToList(),
            };
}
