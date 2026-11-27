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
