using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>拡張データの書き込みを断る理由（API の誤りの答えにそのまま使う。状態・名前・文）。</summary>
public sealed record ExtensionProblem(int Status, string Code, string Message);

/// <summary>
/// 欄の宣言の確かめと、宣言を置き換える時の扱い（docs/spec/local-api-extensions.md「中身の形」「宣言を変えた時」・local-api-extensions-search.md「検索の条件」「欄をまとめる」）。
/// どれも錠の中で今の宣言に当てる関数として使う（<c>UiCommand.ChangeExtensionSchema</c>）
/// </summary>
public static class ExtensionSchemas
{
    /// <summary>送られた宣言を確かめ、保存する形（前後の空白を除いた物）にする。合わなければ理由を返す。</summary>
    public static (IReadOnlyList<ExtensionField>? Fields, string Label, ExtensionProblem? Problem) Check(string? label, IReadOnlyList<ExtensionField?>? fields)
    {
        var name = label?.Trim() ?? string.Empty;
        if (BadText(name, required: true))
        {
            return (null, name, Invalid("invalidSchema", $"label に宣言の表示名を{ExtensionLimits.LabelLength}字までで書いてください（改行などの制御文字は使えません）。"));
        }

        if (fields is null)
        {
            return (null, name, Invalid("invalidSchema", "fields に欄の並びを書いてください（欄が無ければ空の並び）。"));
        }

        var (checkedFields, problem) = CheckFields(fields, isColumn: false);
        return (checkedFields, name, problem);
    }

    private static (IReadOnlyList<ExtensionField>? Fields, ExtensionProblem? Problem) CheckFields(IReadOnlyList<ExtensionField?> fields, bool isColumn)
    {
        var where = isColumn ? "表の列" : "欄";
        if (fields.Count > ExtensionLimits.Fields)
        {
            return (null, Invalid("invalidSchema", $"{where}は{ExtensionLimits.Fields}個までです。"));
        }

        var result = new List<ExtensionField>();

        // 大文字と小文字だけ違う欄は重ねない（読み手のソフトや検索の条件で取り違えるので）
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in fields)
        {
            if (raw is null)
            {
                return (null, Invalid("invalidField", $"{where}に null があります。"));
            }

            var (field, problem) = CheckField(raw, isColumn);
            if (problem is not null)
            {
                return (null, problem);
            }

            if (!keys.Add(field!.Key))
            {
                return (null, Invalid("duplicateField", $"{where}「{field.Key}」が2つあります。"));
            }

            result.Add(field);
        }

        return (result, null);
    }

    private static (ExtensionField? Field, ExtensionProblem? Problem) CheckField(ExtensionField raw, bool isColumn)
    {
        var key = raw.Key?.Trim() ?? string.Empty;
        if (!IsKey(key))
        {
            return (null, Invalid("invalidField", $"key は英字で始まる英数字・「_」・「-」の{ExtensionLimits.KeyLength}字までです（「{Short(key)}」）。"));
        }

        var label = raw.Label?.Trim() ?? string.Empty;
        if (BadText(label, required: true))
        {
            return (null, Invalid("invalidField", $"欄「{key}」の label に表示名を{ExtensionLimits.LabelLength}字までで書いてください。"));
        }

        var type = raw.Type ?? string.Empty;
        if (!ExtensionFieldTypes.All.Contains(type, StringComparer.Ordinal))
        {
            return (null, Invalid("invalidField", $"欄「{key}」の type「{Short(type)}」は使えません。使えるのは {string.Join("・", ExtensionFieldTypes.All)} です。"));
        }

        var isNumber = type == ExtensionFieldTypes.Number;
        if (!isNumber && (raw.Unit is not null || raw.Min is not null || raw.Max is not null || raw.Step is not null
                || raw.Scale is not null || raw.FoldBelow is not null || raw.IgnoreOutliers is not null))
        {
            return (null, Invalid("invalidField", $"欄「{key}」：unit・min・max・step・scale・foldBelow・ignoreOutliers は number の欄だけに書けます。"));
        }

        var unit = raw.Unit?.Trim();
        if (unit is not null && BadText(unit, required: false))
        {
            return (null, Invalid("invalidField", $"欄「{key}」の unit は{ExtensionLimits.LabelLength}字までです。"));
        }

        if (raw.Min is { } min && raw.Max is { } max && min > max)
        {
            return (null, Invalid("invalidField", $"欄「{key}」の min が max より大きくなっています。"));
        }

        if (raw.Step is { } step && !(step > 0))
        {
            return (null, Invalid("invalidField", $"欄「{key}」の step は 0 より大きい数にしてください。"));
        }

        if (raw.Scale is not null and not ("linear" or "log"))
        {
            return (null, Invalid("invalidScale", $"欄「{key}」の scale は linear か log です。"));
        }

        // 対数の目盛りは 0 から上しか配れない（今のスキ数のスライダと同じ。local-api-extensions-search.md）
        if (raw.Scale == "log" && raw.Min is < 0)
        {
            return (null, Invalid("invalidScale", $"欄「{key}」：負の値を持つ欄に log の目盛りは使えません。"));
        }

        if (raw.FoldBelow is { } fold && (raw.Scale != "log" || !(fold > 0)))
        {
            return (null, Invalid("invalidScale", $"欄「{key}」の foldBelow は scale が log の欄に、0 より大きい数で書いてください。"));
        }

        IReadOnlyList<ExtensionChoice>? options = null;
        if (type == ExtensionFieldTypes.Choice)
        {
            var (checkedOptions, problem) = CheckOptions(key, raw.Options);
            if (problem is not null)
            {
                return (null, problem);
            }

            options = checkedOptions;
        }
        else if (raw.Options is not null)
        {
            return (null, Invalid("invalidField", $"欄「{key}」：options は choice の欄だけに書けます。"));
        }

        IReadOnlyList<ExtensionField>? columns = null;
        if (type == ExtensionFieldTypes.Table)
        {
            // 表の中に表は置かない（行の入れ子は画面に出せず、探せない）
            if (isColumn)
            {
                return (null, Invalid("invalidField", $"表の列「{key}」に table の型は使えません。"));
            }

            if (raw.Columns is not { Count: > 0 })
            {
                return (null, Invalid("invalidField", $"表「{key}」の columns に列を1つ以上書いてください。"));
            }

            var (checkedColumns, problem) = CheckFields(raw.Columns, isColumn: true);
            if (problem is not null)
            {
                return (null, problem);
            }

            columns = checkedColumns;
        }
        else if (raw.Columns is not null)
        {
            return (null, Invalid("invalidField", $"欄「{key}」：columns は table の欄だけに書けます。"));
        }

        if (isColumn && (raw.Search is not null || raw.Editable is not null || raw.Group is not null))
        {
            return (null, Invalid("invalidField", $"表の列「{key}」に search・editable・group は書けません（表の欄に書きます）。"));
        }

        // 表は検索の条件にも編集画面の部品にもしない（spec の「仕様として出来ない事」と「画面」）
        if (type == ExtensionFieldTypes.Table && (raw.Search == true || raw.Editable == true))
        {
            return (null, Invalid("invalidField", $"表「{key}」に search・editable は付けられません。"));
        }

        var group = raw.Group?.Trim();
        if (group is not null)
        {
            if (type is not (ExtensionFieldTypes.Choice or ExtensionFieldTypes.Bool))
            {
                return (null, Invalid("invalidGroup", $"欄「{key}」：group にまとめられるのは choice と bool の欄だけです。"));
            }

            if (raw.Search != true)
            {
                return (null, Invalid("invalidGroup", $"欄「{key}」：group に入れる欄には search: true も付けてください。"));
            }

            if (BadText(group, required: true))
            {
                return (null, Invalid("invalidGroup", $"欄「{key}」の group は{ExtensionLimits.LabelLength}字までの名前です。"));
            }
        }

        return (new ExtensionField
        {
            Key = key,
            Label = label,
            Type = type,
            Unit = string.IsNullOrEmpty(unit) ? null : unit,
            Min = raw.Min,
            Max = raw.Max,
            Step = raw.Step,
            Scale = raw.Scale,
            FoldBelow = raw.FoldBelow,
            IgnoreOutliers = raw.IgnoreOutliers,
            Options = options,
            Columns = columns,

            // 偽はファイルに書かない（既定と同じ。人が読む時に余計な行を出さない）
            Search = raw.Search == true ? true : null,
            Editable = raw.Editable == true ? true : null,
            Group = group,
        }, null);
    }

    private static (IReadOnlyList<ExtensionChoice>? Options, ExtensionProblem? Problem) CheckOptions(string key, IReadOnlyList<ExtensionChoice?>? options)
    {
        if (options is not { Count: > 0 })
        {
            return (null, Invalid("invalidField", $"選択肢の欄「{key}」の options に、値と表示名を1つ以上書いてください。"));
        }

        if (options.Count > ExtensionLimits.Choices)
        {
            return (null, Invalid("invalidField", $"欄「{key}」の選択肢は{ExtensionLimits.Choices}個までです。"));
        }

        var result = new List<ExtensionChoice>();
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            var value = option?.Value ?? string.Empty;
            var label = option?.Label?.Trim() ?? string.Empty;
            if (BadText(value, required: true) || value.Trim() != value || BadText(label, required: true))
            {
                return (null, Invalid("invalidField", $"欄「{key}」の選択肢に、value（前後に空白の無い文字）と label を{ExtensionLimits.LabelLength}字までで書いてください。"));
            }

            if (!values.Add(value))
            {
                return (null, Invalid("invalidField", $"欄「{key}」の選択肢「{value}」が2つあります。"));
            }

            result.Add(new ExtensionChoice { Value = value, Label = label });
        }

        return (result, null);
    }

    /// <summary>
    /// 宣言を丸ごと置き換える（spec「宣言を変えた時」）：足す・表示名や並びを変える・外す（値は残して隠す）は受け、
    /// 型を変える（隠した欄を違う型で宣言し直すのも）は 409 <c>fieldTypeChanged</c>。表の列も欄と同じ決まりで扱う
    /// </summary>
    public static (ExtensionSchemaFile? File, ExtensionProblem? Problem) Replace(
        ExtensionSchemaFile? current, string label, string software, IReadOnlyList<ExtensionField> fields)
    {
        var (active, hidden, problem) = Merge(current?.Fields ?? [], current?.Hidden ?? [], fields, where: "欄");
        return problem is not null
            ? (null, problem)
            : (new ExtensionSchemaFile { Label = label, Software = software, Fields = active!, Hidden = hidden! }, null);
    }

    private static (IReadOnlyList<ExtensionField>? Active, IReadOnlyList<ExtensionField>? Hidden, ExtensionProblem? Problem) Merge(
        IReadOnlyList<ExtensionField> oldActive, IReadOnlyList<ExtensionField> oldHidden, IReadOnlyList<ExtensionField> next, string where)
    {
        var known = new Dictionary<string, ExtensionField>(StringComparer.Ordinal);
        foreach (var field in oldHidden.Concat(oldActive))
        {
            if (field is not null && !string.IsNullOrEmpty(field.Key))
            {
                known[field.Key] = field;
            }
        }

        var active = new List<ExtensionField>();
        foreach (var field in next)
        {
            if (!known.TryGetValue(field.Key, out var before))
            {
                active.Add(field);
                continue;
            }

            if (before.Type != field.Type)
            {
                return (null, null, new ExtensionProblem(409, "fieldTypeChanged",
                    $"{where}「{field.Key}」は前に {before.Type} の型で宣言されています（外した欄も含む）。別の名前の欄にするか、値ごと消してから宣言し直してください。"));
            }

            if (field.Type == ExtensionFieldTypes.Table)
            {
                var (columns, hiddenColumns, problem) = Merge(before.Columns ?? [], before.HiddenColumns ?? [], field.Columns ?? [], where: $"表「{field.Key}」の列");
                if (problem is not null)
                {
                    return (null, null, problem);
                }

                active.Add(field with { Columns = columns, HiddenColumns = hiddenColumns is { Count: > 0 } ? hiddenColumns : null });
                continue;
            }

            active.Add(field);
        }

        var activeKeys = active.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        var hidden = known.Values.Where(field => !activeKeys.Contains(field.Key)).OrderBy(field => field.Key, StringComparer.Ordinal).ToList();
        return (active, hidden, null);
    }

    /// <summary>欄の名前の形か（英字で始まる英数字・「_」・「-」）。</summary>
    public static bool IsKey(string? key)
        => key is { Length: > 0 and <= ExtensionLimits.KeyLength }
            && char.IsAsciiLetter(key[0])
            && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static bool BadText(string text, bool required)
        => (required && text.Length == 0) || text.Length > ExtensionLimits.LabelLength || text.Any(char.IsControl);

    private static string Short(string text) => text.Length <= 20 ? text : text[..20] + "…";

    private static ExtensionProblem Invalid(string code, string message) => new(400, code, message);
}
