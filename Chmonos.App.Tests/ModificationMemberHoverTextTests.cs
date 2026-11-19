using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>使ったものの吹き出しは、名前とファイルを1つにまとめて出す（ユーザ指示 2026-11-18）。作り物の名前</summary>
public class ModificationMemberHoverTextTests
{
    private static ModificationMemberRowViewModel Row(string name, string sourceText) => new()
    {
        Index = 0,
        Total = 1,
        Member = new ModificationMember { ItemId = "9900001" },
        Name = name,
        SourceText = sourceText,
        IsMissing = false,
        VariationText = string.Empty,
    };

    [Fact]
    public void 名前とファイルが1つの吹き出しに入る()
        => Assert.Equal("作り物の衣装\ncostume.zip の Body.unitypackage", Row("作り物の衣装", "costume.zip の Body.unitypackage").HoverText);

    [Fact]
    public void ファイルの文が空なら名前だけ()
        => Assert.Equal("作り物の衣装", Row("作り物の衣装", string.Empty).HoverText);
}
