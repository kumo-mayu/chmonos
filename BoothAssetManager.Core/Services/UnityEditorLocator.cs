using System.Text.Json;

namespace BoothAssetManager.Core.Services;

/// <summary>Unity エディタ1つの場所と版。</summary>
public sealed record UnityEditorEntry(string Version, string ExePath);

/// <summary>
/// Unity エディタや関連のアプリの場所を、Windows と Unity Hub の記録から読み解く。
///
/// **利用者の PC の置き場所に頼らない**（ユーザ指示 2026-09-13：配布するので）。エディタは Hub の既定の場所
/// （<c>%PROGRAMFILES%\Unity\Hub\Editor</c>）に入っているとは限らない——Hub で置き場所を変えた人、
/// Hub を使わずに入れた人、Hub の「場所を指定」で足した人がいる。どれも Windows か Hub のどこかに場所が残るので、
/// それらを読む。ここは文字の読み解きだけで、登録やファイルを読むのは呼ぶ側（Windows の登録はアプリの側）。
/// </summary>
public static class UnityEditorLocator
{
    /// <summary>
    /// Hub の「場所を指定」で足したエディタの一覧（<c>editors-v2.json</c>・古い版は <c>editors.json</c>）から、版と場所を取り出す。
    ///
    /// **形を決め打ちしない。**版によって、配列の下に並ぶ・版をキーにして並ぶ、場所が文字列・配列、と揺れる。
    /// JSON の中で <c>version</c>（文字列）と <c>location</c>（文字列か文字列の配列）を持つものを全部拾う。
    /// </summary>
    public static IReadOnlyList<UnityEditorEntry> EditorsFromHubJson(string json)
    {
        var found = new List<UnityEditorEntry>();
        try
        {
            using var document = JsonDocument.Parse(json);
            Walk(document.RootElement, found);
        }
        catch (JsonException)
        {
            // 壊れた一覧は読まない。ほかの記録から探せる
        }

        return found;
    }

    private static void Walk(JsonElement element, List<UnityEditorEntry> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
                    && element.TryGetProperty("location", out var location))
                {
                    foreach (var path in Strings(location))
                    {
                        found.Add(new UnityEditorEntry(version.GetString()!, ExeFromLocation(path)));
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, found);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, found);
                }

                break;
        }
    }

    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Array => element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!),
        _ => [],
    };

    /// <summary>
    /// 記録にある場所を <c>Unity.exe</c> の場所にそろえる。記録によって、実行ファイルそのもの・
    /// <c>Editor</c> のフォルダ・その上の版のフォルダ（Windows の登録の <c>Location x64</c>）と揺れる。
    /// </summary>
    public static string ExeFromLocation(string location)
    {
        var trimmed = location.Trim().Trim('"').TrimEnd('\\', '/');
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return string.Equals(Path.GetFileName(trimmed), "Editor", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(trimmed, "Unity.exe")
            : Path.Combine(trimmed, "Editor", "Unity.exe");
    }

    /// <summary>
    /// 起動のコマンド（関連付けの <c>"C:\…\Unity.exe" -openfile "%1"</c>）や、アイコンの欄
    /// （<c>C:\…\Unity.exe,0</c>）から、実行ファイルの場所を取り出す。取れなければ null。
    /// </summary>
    public static string? ExeFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();
        string head;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            head = end > 1 ? text[1..end] : text.Trim('"');
        }
        else
        {
            // 引用符の無い形は、.exe までを場所とみなす（場所に空白が入っていても切れないように）
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            head = exe > 0 ? text[..(exe + 4)] : text.Split(' ')[0];
        }

        // アイコンの欄の後ろの「,0」（何番目の絵か）を落とす
        var comma = head.LastIndexOf(',');
        if (comma > 0 && head[(comma + 1)..].All(char.IsDigit))
        {
            head = head[..comma];
        }

        return head.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? head : null;
    }

    /// <summary>アンインストール情報の名前（<c>Unity 2022.3.22f1</c>）から版を取り出す。エディタでなければ null。</summary>
    public static string? VersionFromUninstallName(string? displayName)
    {
        const string prefix = "Unity ";
        if (displayName is null || !displayName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = displayName[prefix.Length..].Trim();

        // 「Unity Hub」のような別のアプリを取り違えない。版は数字で始まる
        return rest.Length > 0 && char.IsDigit(rest[0]) && !rest.Contains(' ') ? rest : null;
    }

    /// <summary>
    /// 実行ファイルの版の欄（<c>2022.3.22f1_887be4894c44</c>）が、欲しい版か。
    /// 場所だけでは版が分からない記録（起動中のエディタ・関連付け）を見分けるのに使う。
    /// </summary>
    public static bool IsVersion(string? productVersion, string version)
    {
        if (string.IsNullOrWhiteSpace(productVersion))
        {
            return false;
        }

        var head = productVersion.Split('_', ' ')[0];
        return string.Equals(head, version, StringComparison.OrdinalIgnoreCase);
    }
}
