using Chmonos.Core.Models;
using Xunit;

namespace Chmonos.Api.Tests;

/// <summary>
/// 外から使う API の属性の書き込みと、付いているが値はまだの属性（ユーザ判断 2026-11-20）。
/// 画面のまとめて編集と同じく、値を書けば値はまだから外れ、null は値はまだも外す。
/// </summary>
public sealed class ApiPendingAttributeTests
{
    [Fact]
    public void 値を書くと値はまだから外れ_nullは値はまだも外す()
    {
        var local = new LocalBlock { PendingAttributes = ["可愛さ", "派手さ", "落ち着き"] };

        var written = ApiClassify.ApplyAttributes(local, [("可愛さ", 0), ("派手さ", null)]);

        Assert.Equal(0, written.Attributes["可愛さ"]);
        Assert.False(written.Attributes.ContainsKey("派手さ"));
        Assert.Equal(["落ち着き"], written.PendingAttributes!);
    }
}
