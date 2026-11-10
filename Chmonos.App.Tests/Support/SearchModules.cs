using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests.Support;

/// <summary>
/// 検索の条件を足す手助け。多くの試験のクラスが使うので Support に置く
/// （試験は頭文字で3つのプロジェクトに分けて走らせる。試験のクラスに置くと、別のプロジェクトのクラスから見えない。Chmonos.App.Tests.csproj）。
/// </summary>
internal static class SearchModules
{
    /// <summary>「＋ 条件を追加」から足す（人が押すのと同じ口）。</summary>
    public static SearchModule Add(SearchViewModel search, SearchModuleKind kind)
    {
        var before = search.Modules.ToList();
        search.ModuleMenu.SelectMany(heading => heading.Entries).OfType<SearchModuleMenuEntry>()
            .First(entry => entry.Kind == kind).AddCommand.Execute(null);
        return search.Modules.Except(before).Single();
    }
}
