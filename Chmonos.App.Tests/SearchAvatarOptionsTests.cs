using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 対応アバターの選択肢の並び（ユーザ判断 2026-11-27）：いつも開いている段（AND／OR・持っているバリエーションの分だけ）、
/// 畳める「範囲」（素体経由・変換経由）、畳める「表示するもの」。畳める2段は初めは畳み、見出しに今の値を書く。
/// 見出しの値は、押した時だけでなく、他の画面から渡した時・クリアした時・状態を戻した時にも出し直す
/// </summary>
public class SearchAvatarOptionsTests
{
    private static ListModule Avatar() => new(SearchModuleKind.Avatar, allowsAnd: true, "名前で絞り込む", "", (_, _, _, _) => true,
        ListModule.ViaBaseLabel, flagDefault: true, isUnspecified: (_, _) => false);

    [Fact]
    public void 範囲と表示するものは初めは畳み_見出しに既定の値を書く()
    {
        var module = Avatar();

        Assert.False(module.IsRangeExpanded);
        Assert.False(module.IsShownExpanded);
        Assert.Equal("範囲：素体経由", module.RangeHeader);
        Assert.Equal("表示するもの：対応している商品", module.ShownHeader);
    }

    [Theory]
    [InlineData(true, true, "範囲：素体経由・変換経由")]
    [InlineData(false, true, "範囲：変換経由")]
    [InlineData(false, false, "範囲：直接の対応のみ")]
    public void 範囲の見出しは今の値(bool viaBase, bool viaConversion, string expected)
    {
        var module = Avatar();
        var changed = new List<string?>();
        module.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        module.Flag = viaBase;
        module.ViaConversion = viaConversion;

        Assert.Equal(expected, module.RangeHeader);
        Assert.Contains(nameof(ListModule.RangeHeader), changed);
    }

    [Fact]
    public void 表示するものの見出しは今の値()
    {
        var module = Avatar();

        module.ShowUnspecified = true;
        Assert.Equal("表示するもの：対応している商品と指定が無い商品", module.ShownHeader);

        module.ShowMatched = false;
        Assert.Equal("表示するもの：対応の指定が無い商品", module.ShownHeader);
    }

    [Fact]
    public void 他の画面から渡した時も_範囲の見出しを出し直す()
    {
        var module = Avatar();
        var changed = new List<string?>();
        module.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        module.SetFlagQuietly(false);

        Assert.Equal("範囲：直接の対応のみ", module.RangeHeader);
        Assert.Contains(nameof(ListModule.RangeHeader), changed);
    }

    [Fact]
    public void 状態を戻すと見出しも戻り_クリアで既定に戻る() => UiThread.Run(() =>
    {
        var module = Avatar();
        var changed = new List<string?>();
        module.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        module.Load(new SearchModuleState
        {
            Kind = "Avatar",
            Items = ["A"],
            Flag = false,
            ShowMatched = true,
            ShowUnspecified = true,
            Toggles = new Dictionary<string, bool> { [SearchModuleConditions.ViaConversionToggle] = true },
        });

        Assert.Equal("範囲：変換経由", module.RangeHeader);
        Assert.Equal("表示するもの：対応している商品と指定が無い商品", module.ShownHeader);
        Assert.Contains(nameof(ListModule.RangeHeader), changed);
        Assert.Contains(nameof(ListModule.ShownHeader), changed);

        module.Clear();
        Assert.Equal("範囲：素体経由", module.RangeHeader);
        Assert.Equal("表示するもの：対応している商品", module.ShownHeader);
    });

    [Fact]
    public void 範囲の段はアバターを選んで初めて出し_素体経由は範囲の中にだけ出す()
    {
        var module = Avatar();
        module.SetSuggestions([("作り物のアバター（1）", "1")]);
        Assert.False(module.HasRange);

        module.AddCommand.Execute("作り物のアバター（1）");

        Assert.True(module.HasRange);
        Assert.True(module.HasFlag);
        Assert.False(module.ShowsPlainFlag);
    }

    [Fact]
    public void ほかの条件の補助の切り替えは_今までどおりいつも開いた所に出す()
    {
        var module = new ListModule(SearchModuleKind.UnityProjectContents, allowsAnd: true, "", "", (_, _, _, _) => true, "名前が同じファイルも含める");
        module.SetSuggestions([("作り物のプロジェクト", "p")]);
        module.AddCommand.Execute("作り物のプロジェクト");

        Assert.True(module.ShowsPlainFlag);
        Assert.False(module.HasRange);
    }

    [Fact]
    public void 要約は範囲の中の名前で言う()
    {
        var module = Avatar();
        module.SetSuggestions([("作り物のアバター（1）", "1")]);
        module.AddCommand.Execute("作り物のアバター（1）");

        module.Flag = false;
        module.ViaConversion = true;

        Assert.Contains("（素体経由を除く）", module.SummaryText, StringComparison.Ordinal);
        Assert.Contains("（変換経由を含める）", module.SummaryText, StringComparison.Ordinal);
    }
}
