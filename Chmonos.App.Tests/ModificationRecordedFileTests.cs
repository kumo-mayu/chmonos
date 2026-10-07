using System.IO;
using System.IO.Compression;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の使ったものを Unity へ送るとき、記録したファイルが手元に無ければ、別のファイルへ落とさない（外部の点検 2026-10-07）。
/// 前は商品の送れる物全部に落ちて、別の版・別の種類の包みを黙って送っていた（版まで同じにするための記録なのに）
/// </summary>
public sealed class ModificationRecordedFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chmonos-recorded-" + Guid.NewGuid().ToString("N")[..8]);

    public ModificationRecordedFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>中に包みを1つ入れた zip を作る。</summary>
    private string ZipWith(string name, string package)
    {
        var zip = Path.Combine(_dir, name);
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry(package).Open()))
        {
            writer.Write("x");
        }

        return zip;
    }

    [Fact]
    public void 記録したファイルが無ければ_別の版の包みを送らない()
    {
        var other = ZipWith("v2.zip", "Other_v2.unitypackage");
        var item = Make.Item("9900950", "作り物の衣装").WithFiles(Make.File(other), Make.File(Path.Combine(_dir, "v1.zip")) with { Paths = [] });
        var recorded = item.Local.LocalFiles[1].Hash;

        var places = ModificationViewModel.PlacesFor(item, new ModificationMember { ItemId = item.Id, FileHash = recorded, Package = "Mine_v1.unitypackage" });

        Assert.Empty(places);
    }

    /// <summary>包みの名前の無い記録（手で足すときに使ったファイルを選んだ物）は、その zip の中の物だけを送る。</summary>
    [Fact]
    public void 包みの名前の無い記録は_そのzipの中の物だけを送る()
    {
        var chosen = ZipWith("chosen.zip", "Chosen.unitypackage");
        var other = ZipWith("other.zip", "Other.unitypackage");
        var item = Make.Item("9900951", "作り物の髪").WithFiles(Make.File(chosen), Make.File(other));

        var places = ModificationViewModel.PlacesFor(item, new ModificationMember { ItemId = item.Id, FileHash = item.Local.LocalFiles[0].Hash });

        Assert.Equal(["Chosen.unitypackage"], places.Select(place => place.Entry.EntryPath));
    }
}
