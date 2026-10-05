using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Chmonos.App.Tests;

/// <summary>
/// 消す・初期化する操作は、戻せるかに関わらず赤にそろえる（ユーザ判断 2026-10-05・V4）。
/// 画面の元の文（XAML）を読み、名前に「削除」「消す」「既定に戻す」「ごみ箱」を含むボタン・右クリックの項目が
/// 赤の型（<c>DangerButton</c>／<c>DangerMenuItem</c>）を使っていることを確かめる。
/// 赤い字の小さな ×（行の端の削除）は、名前の付いたものを <see cref="赤い字の小さな削除ボタン"/> で確かめる。
/// 試験の中では画面の資源が無く View そのものは作れないので、元の文を読む（<c>FilterFieldDelayTests</c> と同じ）。
/// </summary>
public class DeleteIsRedTests
{
    private static readonly string[] DeleteWords = ["削除", "消す", "既定に戻す", "ごみ箱"];

    /// <summary>消す操作に見えても、入力欄を空にするだけの物（赤にしない）。</summary>
    private static readonly string[] NotDelete = ["絞り込みを消す", "条件をクリア"];

    [Fact]
    public void 削除や初期化の名前のボタンと項目は_赤の型を使う()
    {
        var wrong = new List<string>();
        var checkedCount = 0;
        foreach (var file in Directory.GetFiles(ViewsFolder(), "*.xaml").Append(Path.Combine(ViewsFolder(), "..", "MainWindow.xaml")))
        {
            var document = XDocument.Load(file);
            foreach (var element in document.Descendants().Where(e => e.Name.LocalName is "Button" or "MenuItem"))
            {
                var label = (string?)element.Attribute(element.Name.LocalName == "Button" ? "Content" : "Header") ?? "";
                if (label.Length == 0
                    || NotDelete.Contains(label)
                    || !DeleteWords.Any(label.Contains))
                {
                    continue;
                }

                checkedCount++;
                var style = (string?)element.Attribute("Style") ?? "";
                var expected = element.Name.LocalName == "Button" ? "DangerButton" : "DangerMenuItem";
                if (!style.Contains(expected))
                {
                    wrong.Add($"{Path.GetFileName(file)}: {element.Name.LocalName}「{label}」");
                }
            }
        }

        Assert.True(checkedCount >= 15, $"対象が少なすぎます（{checkedCount}）。調べ方が壊れています。");
        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("AvatarsView.xaml", "AvatarAliasRemove")]
    [InlineData("SearchView.xaml", "SearchHistoryRemove")]
    public void 赤い字の小さな削除ボタン(string view, string automationId)
    {
        var document = XDocument.Load(Path.Combine(ViewsFolder(), view));
        var button = Assert.Single(document.Descendants(), e => e.Name.LocalName == "Button"
            && (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "AutomationProperties.AutomationId") == automationId);
        Assert.Contains("DangerText", (string?)button.Attribute("Foreground"));
    }

    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views");
}
