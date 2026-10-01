using System.Text;

namespace Chmonos.Core.Services;

/// <summary>
/// 確認の窓のボタンの組。値は WPF の <c>MessageBoxButton</c> と同じ（App は値のまま写す）。
/// Core は WPF を参照しないので、同じ形を持つ。
/// </summary>
public enum NoticeButtonSet
{
    Ok = 0,
    OkCancel = 1,
    YesNoCancel = 3,
    YesNo = 4,
}

/// <summary>確認の窓の答え。値は WPF の <c>MessageBoxResult</c> と同じ。</summary>
public enum NoticeAnswer
{
    None = 0,
    Ok = 1,
    Cancel = 2,
    Yes = 6,
    No = 7,
}

/// <summary>確認の窓の1つのボタン。</summary>
public sealed record NoticeChoice(NoticeAnswer Answer, string Label);

/// <summary>
/// 確認の窓（<c>Services.Notice</c>）のボタンの並び・既定のボタン・Esc と × の答え。
///
/// **Windows の MessageBox と同じに決める。**知らせの窓は自前の窓にした（暗い表で白いまま残っていた。ユーザ指示 2026-09-29）が、
/// 呼ぶ所は約70か所（2026-09-29）あり、どれも MessageBox の動きを前に書かれている（「既定をキャンセルに倒して止める」など）。
/// 並びや Esc の答えを変えると、呼ぶ所の前提が黙って崩れる
/// </summary>
public sealed record NoticeLayout(IReadOnlyList<NoticeChoice> Choices, NoticeAnswer Default, NoticeAnswer? Dismiss)
{
    /// <summary>Esc と × で閉じられるか。</summary>
    public bool CanDismiss => Dismiss is not null;

    /// <summary>
    /// ボタンの組から並びを決める。既定のボタンは、頼まれた答えが組の中にあればそれ、無ければ左端（MessageBox と同じ）。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">4つの組のほか（呼ぶ側は MessageBox に任せる）。</exception>
    public static NoticeLayout For(NoticeButtonSet set, NoticeAnswer requestedDefault = NoticeAnswer.None)
    {
        // 「はい・いいえ」だけの窓は、Esc も × も効かない（MessageBox と同じ）。
        // 「いいえ」が別の操作を選ぶ窓がある（展開先のファイルの窓の「いいえ」は、指定のファイルのまま取り込む）ので、
        // 閉じる操作を「いいえ」に読み替えると、やめたつもりで取り込みが進む
        (NoticeChoice[] choices, NoticeAnswer? dismiss) = set switch
        {
            NoticeButtonSet.Ok => (new[] { Ok }, NoticeAnswer.Ok),
            NoticeButtonSet.OkCancel => (new[] { Ok, Cancel }, NoticeAnswer.Cancel),
            NoticeButtonSet.YesNoCancel => (new[] { Yes, No, Cancel }, NoticeAnswer.Cancel),
            NoticeButtonSet.YesNo => (new[] { Yes, No }, (NoticeAnswer?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(set), set, "知らせの窓が扱わないボタンの組"),
        };

        var defaultAnswer = choices.Any(choice => choice.Answer == requestedDefault)
            ? requestedDefault
            : choices[0].Answer;
        return new NoticeLayout(choices, defaultAnswer, dismiss);
    }

    /// <summary>
    /// Ctrl+C で写す文。MessageBox が写していた形（題・本文・ボタンを線で区切る）に合わせる
    /// （本文を問い合わせや記録に貼る使い方が、自前の窓にしても同じにできるように）。
    /// </summary>
    public string CopyText(string caption, string text)
    {
        const string Rule = "---------------------------";
        var buttons = new StringBuilder();
        foreach (var choice in Choices)
        {
            buttons.Append(choice.Label).Append("   ");
        }

        return new StringBuilder()
            .Append(Rule).Append("\r\n")
            .Append(caption).Append("\r\n")
            .Append(Rule).Append("\r\n")
            .Append(text.ReplaceLineEndings("\r\n")).Append("\r\n")
            .Append(Rule).Append("\r\n")
            .Append(buttons).Append("\r\n")
            .Append(Rule).Append("\r\n")
            .ToString();
    }

    // ボタンの語は Windows の確認の窓と同じ（docs/spec/ui-terms.md「決めた呼び方」）
    private static readonly NoticeChoice Ok = new(NoticeAnswer.Ok, "OK");
    private static readonly NoticeChoice Cancel = new(NoticeAnswer.Cancel, "キャンセル");
    private static readonly NoticeChoice Yes = new(NoticeAnswer.Yes, "はい");
    private static readonly NoticeChoice No = new(NoticeAnswer.No, "いいえ");
}
