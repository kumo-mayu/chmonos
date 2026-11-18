using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>説明の見出しの右クリック「段落をコピー」（ユーザ指示 2026-11-18）。見出しを含めた中身を写す。</summary>
public class ItemSectionCopyTests
{
    [Fact]
    public void 段落の写しは_見出しと本文を改行でつなぐ()
    {
        var row = new SectionRow(new H2Section { Heading = "■ 同梱物", Text = "本体\nテクスチャ" });

        Assert.Equal("■ 同梱物\n本体\nテクスチャ", ItemViewModel.SectionCopyText(row));
    }

    [Fact]
    public void 本文の無い見出しは_見出しだけを写す()
    {
        var row = new SectionRow(new H2Section { Heading = "■ 注意", Text = string.Empty });

        Assert.Equal("■ 注意", ItemViewModel.SectionCopyText(row));
    }
}
