using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

/// <summary>確認の窓のボタンの並び・既定・Esc の答えが MessageBox と同じになるか（NoticeLayout）。</summary>
public class NoticeLayoutTests
{
    [Theory]
    [InlineData(NoticeButtonSet.Ok, "OK")]
    [InlineData(NoticeButtonSet.OkCancel, "OK|キャンセル")]
    [InlineData(NoticeButtonSet.YesNo, "はい|いいえ")]
    [InlineData(NoticeButtonSet.YesNoCancel, "はい|いいえ|キャンセル")]
    public void ボタンは左から主の答えの順でキャンセルが右端(NoticeButtonSet set, string labels)
    {
        var layout = NoticeLayout.For(set);

        Assert.Equal(labels, string.Join('|', layout.Choices.Select(choice => choice.Label)));
    }

    [Theory]
    [InlineData(NoticeButtonSet.Ok, NoticeAnswer.Ok)]
    [InlineData(NoticeButtonSet.OkCancel, NoticeAnswer.Cancel)]
    [InlineData(NoticeButtonSet.YesNoCancel, NoticeAnswer.Cancel)]
    public void Escと閉じるはキャンセルでキャンセルが無ければOK(NoticeButtonSet set, NoticeAnswer dismiss)
    {
        var layout = NoticeLayout.For(set);

        Assert.True(layout.CanDismiss);
        Assert.Equal(dismiss, layout.Dismiss);
    }

    [Fact]
    public void はいといいえだけの窓はEscでも閉じるでも閉じない()
    {
        // 「いいえ」が別の操作を選ぶ窓があるので、閉じる操作を「いいえ」に読み替えない（MessageBox と同じ）
        var layout = NoticeLayout.For(NoticeButtonSet.YesNo);

        Assert.False(layout.CanDismiss);
        Assert.Null(layout.Dismiss);
    }

    [Theory]
    [InlineData(NoticeButtonSet.Ok, NoticeAnswer.None, NoticeAnswer.Ok)]
    [InlineData(NoticeButtonSet.OkCancel, NoticeAnswer.None, NoticeAnswer.Ok)]
    [InlineData(NoticeButtonSet.OkCancel, NoticeAnswer.Cancel, NoticeAnswer.Cancel)]
    [InlineData(NoticeButtonSet.YesNo, NoticeAnswer.No, NoticeAnswer.No)]
    [InlineData(NoticeButtonSet.YesNo, NoticeAnswer.Yes, NoticeAnswer.Yes)]
    [InlineData(NoticeButtonSet.YesNoCancel, NoticeAnswer.Cancel, NoticeAnswer.Cancel)]
    public void 既定のボタンは頼まれた答えで無ければ左端(NoticeButtonSet set, NoticeAnswer requested, NoticeAnswer expected)
    {
        Assert.Equal(expected, NoticeLayout.For(set, requested).Default);
    }

    [Theory]
    [InlineData(NoticeButtonSet.Ok, NoticeAnswer.Cancel)]
    [InlineData(NoticeButtonSet.OkCancel, NoticeAnswer.Yes)]
    [InlineData(NoticeButtonSet.YesNo, NoticeAnswer.Cancel)]
    public void 組に無い答えを既定に頼まれたら左端にする(NoticeButtonSet set, NoticeAnswer requested)
    {
        var layout = NoticeLayout.For(set, requested);

        Assert.Equal(layout.Choices[0].Answer, layout.Default);
    }

    [Fact]
    public void 扱わない組は断る()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoticeLayout.For((NoticeButtonSet)2));
    }

    [Fact]
    public void 写す文はMessageBoxと同じ形()
    {
        var text = NoticeLayout.For(NoticeButtonSet.OkCancel).CopyText("題", "1行目\n2行目");

        Assert.Equal(
            "---------------------------\r\n題\r\n---------------------------\r\n1行目\r\n2行目\r\n"
            + "---------------------------\r\nOK   キャンセル   \r\n---------------------------\r\n",
            text);
    }
}
