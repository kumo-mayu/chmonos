using System.Text.Json;

namespace Chmonos.Core.Models;

/// <summary>
/// 外のソフトの情報（拡張データ）の、名前空間ごとの欄の宣言（保存先の <c>extensions/&lt;名前空間&gt;/schema.json</c>。docs/spec/local-api-extensions.md）。
/// 新しいフォルダなので保存先の形式の版は上げない（古い版は知らないファイルを読まず、消しもしない。data-format.md）
/// </summary>
public sealed record ExtensionSchemaFile
{
    /// <summary>このファイルの形の版（保存先の形式の版とは別）。</summary>
    public int Version { get; init; } = 1;

    /// <summary>宣言の表示名（商品ページの節の見出しになる）。</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// 宣言したソフトの名前（宣言した時の物）。許しの控えからは引かない：許しを取り消しても値と宣言は残し、
    /// 設定の「つないでいるソフト」に名前で出すため（spec「画面」）
    /// </summary>
    public string Software { get; init; } = string.Empty;

    /// <summary>今の宣言の欄（並びはソフトが送った順）。</summary>
    public IReadOnlyList<ExtensionField> Fields { get; init; } = [];

    /// <summary>
    /// 宣言から外した欄（値は残して隠す）。型を覚えておき、違う型で宣言し直されたら 409 で断る（spec「宣言を変えた時」）。
    /// 外した欄を忘れると、値は残ったまま違う型の欄として出てしまう
    /// </summary>
    public IReadOnlyList<ExtensionField> Hidden { get; init; } = [];
}

/// <summary>欄の宣言1つ（表の列も同じ形。列は表・<see cref="Search"/>・<see cref="Group"/>・<see cref="Editable"/> を持たない）。</summary>
public sealed record ExtensionField
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    /// <summary>型の名前（<see cref="ExtensionFieldTypes"/>）。</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>数の単位（表示とスライダに使う）。</summary>
    public string? Unit { get; init; }

    /// <summary>数の範囲の目安（スライダの端。値を断る物ではない）。</summary>
    public double? Min { get; init; }

    public double? Max { get; init; }

    /// <summary>スライダの刻み。</summary>
    public double? Step { get; init; }

    /// <summary>目盛り（<c>linear</c>・<c>log</c>。省けば直線）。</summary>
    public string? Scale { get; init; }

    /// <summary><c>log</c> の目盛りで、この値までを左端に直線で畳む（価格の 100円と同じ）。</summary>
    public double? FoldBelow { get; init; }

    /// <summary>検索のスライダに「外れ値を無視」を出すか。</summary>
    public bool? IgnoreOutliers { get; init; }

    /// <summary>選択肢（<c>choice</c> の型だけ）。</summary>
    public IReadOnlyList<ExtensionChoice>? Options { get; init; }

    /// <summary>表の列（<c>table</c> の型だけ）。</summary>
    public IReadOnlyList<ExtensionField>? Columns { get; init; }

    /// <summary>表から外した列（値は行に残して隠す。欄と同じ決まり）。</summary>
    public IReadOnlyList<ExtensionField>? HiddenColumns { get; init; }

    /// <summary>メモリに持ち、検索の条件にするか（既定は持たない。local-api-extensions-search.md）。</summary>
    public bool? Search { get; init; }

    /// <summary>編集画面で人も直せるか（spec「画面」）。</summary>
    public bool? Editable { get; init; }

    /// <summary>検索の条件と画面の見出しをまとめる名前（選択肢と真偽の欄だけ）。</summary>
    public string? Group { get; init; }
}

/// <summary>選択肢1つ（値は保存する文字、表示名は画面の文字）。</summary>
public sealed record ExtensionChoice
{
    public string Value { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;
}

/// <summary>欄の型の名前。API と保存のファイルで変えない英字。</summary>
public static class ExtensionFieldTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Bool = "bool";
    public const string Date = "date";
    public const string Url = "url";
    public const string Choice = "choice";
    public const string TextList = "textList";

    /// <summary>アバターの ID（商品 ID）の並び。画面で名前の札にする（名前を値に入れると、名前が変わった時に追えない）。</summary>
    public const string AvatarList = "avatarList";

    public const string Table = "table";

    public static IReadOnlyList<string> All { get; } = [Text, Number, Bool, Date, Url, Choice, TextList, AvatarList, Table];
}

/// <summary>
/// 付け先1つ・名前空間1つの値（<c>extensions/&lt;名前空間&gt;/&lt;付け先&gt;/&lt;鍵&gt;.json</c>）。
/// 宣言から外した欄の値もここに残る（隠すだけ）。メモ欄は別のファイル（<see cref="ExtensionMemoFile"/>）
/// </summary>
public sealed record ExtensionValueFile
{
    public int Version { get; init; } = 1;

    /// <summary>欄 → 値。値は JSON のまま持つ（型は宣言で決まり、書く時に確かめる）。</summary>
    public Dictionary<string, JsonElement> Values { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>メモ欄（持ち主のソフトだけが読む自由な JSON）。値と同じ付け先の横に別のファイルで置く。</summary>
public sealed record ExtensionMemoFile
{
    public int Version { get; init; } = 1;

    public JsonElement? Memo { get; init; }
}

/// <summary>
/// 上限（仮の数。spec「名前空間の決まり・画面・上限」。作り物の2万件で測ってから直すので、数はここ1か所に置く）。
/// 測るまでは spec の仮の数をそのまま使う
/// </summary>
public static class ExtensionLimits
{
    /// <summary>1つの付け先・1つのソフトの値（隠した欄の値も数える。外すだけで上限を超えて溜められないように）。</summary>
    public const int ValueBytes = 16 * 1024;

    /// <summary>メモ欄。</summary>
    public const int MemoBytes = 64 * 1024;

    /// <summary>1つの宣言の欄の数（表の列も1つの表につき同じ数まで）。</summary>
    public const int Fields = 64;

    /// <summary>表の行。</summary>
    public const int TableRows = 1000;

    /// <summary>選択肢の数。spec に数は無い。欄の数と同じにそろえた（検索の「候補から積む」で並べ切れる数）。</summary>
    public const int Choices = 64;

    /// <summary>表示名・単位・group・選択肢の文字の長さ。許しの窓のソフトの名前（64字）とそろえた。</summary>
    public const int LabelLength = 64;

    /// <summary>欄の名前の長さ。名前空間（40字）とそろえた。</summary>
    public const int KeyLength = 40;
}

/// <summary>
/// 名前空間の決まり（spec「名前空間の決まり」）：3〜40字・英小文字と数字とハイフン・英小文字で始める。大文字は受けた時に小文字にする。
/// <c>chmonos</c> と <c>chmonos-</c> で始まる名前は予約（Chmonos が自分で使う時のため）
/// </summary>
public static class ExtensionNamespace
{
    public const int MinLength = 3;
    public const int MaxLength = 40;

    /// <summary>
    /// Windows がファイル・フォルダの名前に使えない名前（<c>nul</c> など）。名前空間はそのままフォルダの名前になるので、決まりを通っても断る
    /// （書き出しの <c>BackupArchive</c> も同じ名前を断る）
    /// </summary>
    private static readonly HashSet<string> DeviceNames = new(StringComparer.Ordinal)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>受けた名前を決まりの形にする（小文字にし、前後の空白を除く）。決まりに合わなければ null。</summary>
    public static string? Normalize(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var value = name.Trim().ToLowerInvariant();
        return IsValid(value) ? value : null;
    }

    /// <summary>決まりの形か（小文字にした後の名前で見る）。</summary>
    public static bool IsValid(string? value)
        => value is { Length: >= MinLength and <= MaxLength }
            && value[0] is >= 'a' and <= 'z'
            && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            && !IsReserved(value)
            && !DeviceNames.Contains(value);

    /// <summary>Chmonos が自分で使うために取っておく名前か。</summary>
    public static bool IsReserved(string value)
        => value == "chmonos" || value.StartsWith("chmonos-", StringComparison.Ordinal);
}
