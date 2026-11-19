using System.Globalization;
using Chmonos.App.Controls;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>履歴の1件に付ける、変わった項目の札（左の短い線の色は商品ページの帯の並びと同じ種類の色）。</summary>
public sealed record ChangeHistoryLabel(string Text, ChangeTone Tone);

/// <summary>
/// 差分の本文の印。今の本文に当てる <see cref="ChangedLineMarks"/> と違い、**差そのものから本文を組む**：
/// 過去の変化の行は今の本文にもう無いことが多く、今の本文に当てると印が付かずに何も出ない。
/// 本文は足した行だけ（全部に印）で、消えた行は差の並びの順にその位置へ差し込む。前の本文は保存しないので、変わっていない行は出せない
/// </summary>
public sealed class HistoryLineMarks : ILineMarks
{
    private HistoryLineMarks(IReadOnlySet<int> addedLines, IReadOnlyList<InsertedLine> removedLines)
    {
        AddedLines = addedLines;
        RemovedLines = removedLines;
    }

    public IReadOnlySet<int> AddedLines { get; }

    public IReadOnlyList<InsertedLine> RemovedLines { get; }

    /// <summary>差の行の並び（足した行と消えた行が今の本文の順に混ざる）から、本文と印を組む。</summary>
    public static (string Text, HistoryLineMarks Marks) From(IReadOnlyList<NotificationLine> lines)
    {
        var added = new List<string>();
        var removed = new List<InsertedLine>();
        foreach (var line in lines)
        {
            if (line.Kind == NotificationLineKind.Added)
            {
                added.Add(line.Text);
            }
            else
            {
                // 差の並びで手前にある足した行の後ろ（＝本文の何行目の前か）
                removed.Add(new InsertedLine(added.Count, line.Text));
            }
        }

        return (string.Join("\n", added), new HistoryLineMarks(Enumerable.Range(0, added.Count).ToHashSet(), removed));
    }

    /// <summary>前と後だけを持つ欄（商品名・数・行を持たない前の形の知らせ）：前を消えた行、後を足した行として1行ずつ。</summary>
    public static (string Text, HistoryLineMarks Marks) FromBeforeAfter(string? before, string? after)
    {
        var hasAfter = !string.IsNullOrEmpty(after);
        IReadOnlyList<InsertedLine> removed = string.IsNullOrEmpty(before) ? [] : [new InsertedLine(0, before)];
        return (hasAfter ? after! : string.Empty, new HistoryLineMarks(hasAfter ? new HashSet<int> { 0 } : [], removed));
    }
}

/// <summary>差分の1欄（項目の名前と、帯と線で示す本文）。</summary>
public sealed class ChangeHistoryField
{
    public required string Heading { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>本文の帯。値段のように前と後を1行で言う欄は null（帯を付けずに本文だけ）。</summary>
    public ILineMarks? Marks { get; init; }

    /// <summary>上限で残さなかった行の数の1行。無ければ空。</summary>
    public string MoreText { get; init; } = string.Empty;

    public bool HasMore => MoreText.Length > 0;
}

/// <summary>履歴の1件（知らせ1つ）。未読のうちに重ねた変化は1件にまとまっているので、日付は最初〜最後の幅で言う。</summary>
public sealed class ChangeHistoryEntry
{
    /// <summary>札に出す見出しの長さ。商品ページの帯の並びと同じ（長い見出し1つで行が埋まらないように）。</summary>
    private const int LabelHeadingLength = 16;

    private IReadOnlyList<ChangeHistoryField>? _fields;

    public ChangeHistoryEntry(NotificationRecord record)
    {
        Record = record;
        Labels = record.Diffs.Count > 0
            ? record.Diffs.Select(diff => new ChangeHistoryLabel(LabelOf(diff.Field), ToneOf(diff))).ToList()
            : [new ChangeHistoryLabel("変更", ChangeTone.Changed)];
    }

    public NotificationRecord Record { get; }

    /// <summary>未読（未解消）。商品ページの帯・ナビの数と同じ数え方。</summary>
    public bool IsUnread => !Record.IsRead && !Record.IsResolved;

    public string DateText => DateRange(Record.CreatedAt, Record.UpdatedAt);

    /// <summary>乗せると出す正確な日時。</summary>
    public string DateTip => Record.UpdatedAt is { } updated
        ? $"{Stamp(Record.CreatedAt)}〜{Stamp(updated)}"
        : Stamp(Record.CreatedAt);

    public IReadOnlyList<ChangeHistoryLabel> Labels { get; }

    /// <summary>読み上げ・自動操作の名前（日付と変わった項目）。</summary>
    public string AccessibleName => $"{DateText} {string.Join("、", Labels.Select(label => label.Text))}{(IsUnread ? "（未読）" : string.Empty)}";

    /// <summary>差分の欄。押したときに初めて組む（履歴が多い商品でも開くのを遅くしない）。</summary>
    public IReadOnlyList<ChangeHistoryField> Fields => _fields ??= BuildFields(Record);

    /// <summary>
    /// 日付。重ねた知らせは最初の日〜最後の日（同じ日なら1つ）。同じ年なら後ろの年は省く。
    /// 時刻は記録が持つ時差のまま出す（保管庫の月の分け方と同じ。読む PC の時刻帯で日付が変わらない）
    /// </summary>
    internal static string DateRange(DateTimeOffset created, DateTimeOffset? updated)
    {
        var first = created.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (updated is not { } last || last.Date == created.Date)
        {
            return first;
        }

        var format = last.Year == created.Year ? "MM-dd" : "yyyy-MM-dd";
        return $"{first}〜{last.ToString(format, CultureInfo.InvariantCulture)}";
    }

    private static string Stamp(DateTimeOffset at) => at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>決まった欄は名前のまま、それ以外（説明文の見出し）は「説明文：見出し」。</summary>
    internal static string LabelOf(string field)
    {
        if (field is BoothChanges.NameField or BoothChanges.PriceField or BoothChanges.VariationsField
            or BoothChanges.ImagesField or BoothChanges.DescriptionField or BoothChanges.SaleField)
        {
            return field;
        }

        var heading = field.Trim();
        return "説明文：" + (heading.Length > LabelHeadingLength ? heading[..LabelHeadingLength] + "…" : heading);
    }

    /// <summary>足しただけ＝緑・消しただけ＝赤・それ以外＝橙（商品ページの帯の並びと同じ考え）。</summary>
    internal static ChangeTone ToneOf(NotificationDiff diff)
    {
        if (diff.Lines is { Count: > 0 } lines)
        {
            return lines.All(line => line.Kind == NotificationLineKind.Added) && (diff.MoreRemoved ?? 0) == 0 ? ChangeTone.Added
                : lines.All(line => line.Kind == NotificationLineKind.Removed) && (diff.MoreAdded ?? 0) == 0 ? ChangeTone.Removed
                : ChangeTone.Changed;
        }

        return diff.Before is null && diff.After is not null ? ChangeTone.Added
            : diff.Before is not null && diff.After is null ? ChangeTone.Removed
            : ChangeTone.Changed;
    }

    internal static IReadOnlyList<ChangeHistoryField> BuildFields(NotificationRecord record)
    {
        if (record.Diffs.Count == 0)
        {
            // 差を書けなかった知らせ（前の形）は要約の1文だけを持つ
            return [new ChangeHistoryField { Heading = "変更", Text = record.Detail }];
        }

        return record.Diffs.Select(Field).ToList();
    }

    private static ChangeHistoryField Field(NotificationDiff diff)
    {
        var heading = diff.Field;
        if (diff.Lines is { Count: > 0 } lines)
        {
            var (text, marks) = HistoryLineMarks.From(lines);
            return new ChangeHistoryField { Heading = heading, Text = text, Marks = marks, MoreText = MoreText(diff.MoreAdded, diff.MoreRemoved) };
        }

        if (diff.MoreAdded is not null || diff.MoreRemoved is not null)
        {
            return new ChangeHistoryField { Heading = heading, MoreText = MoreText(diff.MoreAdded, diff.MoreRemoved) };
        }

        // 値段はバリエーションごとの「名前 ¥前 → ¥今」（通知の札と同じ形）。商品の価格の文字は一番安い値段しか言わない
        if (diff.Prices is { Count: > 0 } prices)
        {
            return new ChangeHistoryField { Heading = heading, Text = string.Join("\n", prices.Select(BoothChanges.PriceChangeText)) };
        }

        if (diff.Before is null && diff.After is null)
        {
            return new ChangeHistoryField { Heading = heading, Text = "変わった内容は記録されていません。" };
        }

        var (beforeAfter, beforeAfterMarks) = HistoryLineMarks.FromBeforeAfter(diff.Before, diff.After);
        return new ChangeHistoryField { Heading = heading, Text = beforeAfter, Marks = beforeAfterMarks };
    }

    internal static string MoreText(int? added, int? removed)
    {
        var parts = new List<string>();
        if (added is > 0)
        {
            parts.Add($"追加された行が {added} 行");
        }

        if (removed is > 0)
        {
            parts.Add($"削除された行が {removed} 行");
        }

        return parts.Count == 0 ? string.Empty : $"ほかに{string.Join("、", parts)}あります。";
    }
}

/// <summary>
/// 窓「BOOTHでの変化の履歴」（ユーザ判断 2026-11-20）。商品ページの「…」・上の帯の右クリックから開く。
/// 新しい順に日付・未読の印・変わった項目の札を並べ、選んだ件の差分を右に出す（商品ページの本文と同じ帯と線）。
/// 読むだけ：既読にはしない（既読にするのは商品ページの帯と通知の画面）
/// </summary>
public sealed class ChangeHistoryViewModel : ViewModelBase
{
    private ChangeHistoryEntry? _selected;

    public ChangeHistoryViewModel(string itemName, ItemChangeHistoryRead read)
    {
        ItemName = itemName;
        Entries = read.Records.Select(record => new ChangeHistoryEntry(record)).ToList();
        HasDamage = read.HasDamage;
        _selected = Entries.FirstOrDefault();
    }

    public string ItemName { get; }

    public IReadOnlyList<ChangeHistoryEntry> Entries { get; }

    public string CountText => $"{Entries.Count} 件";

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>保管庫に読めなかった所がある（その分は並んでいない）。</summary>
    public bool HasDamage { get; }

    public ChangeHistoryEntry? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(SelectedFields));
            }
        }
    }

    public IReadOnlyList<ChangeHistoryField> SelectedFields => _selected?.Fields ?? [];
}

/// <summary>窓を出す口（<see cref="ExclusionDialog"/> と同じ形）。試験と ViewShot は <see cref="Intercept"/> で窓を出さずに受ける。</summary>
public static class ChangeHistoryWindow
{
    /// <summary>窓を出す代わりに中身を受ける口。**アプリでは null のまま**（窓を出す）。</summary>
    internal static Func<ChangeHistoryViewModel, Task>? Intercept { get; set; }

    public static async Task ShowAsync(ChangeHistoryViewModel model)
    {
        if (Intercept is { } intercept)
        {
            await intercept(model);
            return;
        }

        new Views.ChangeHistoryDialog(model).ShowDialog();
    }
}
