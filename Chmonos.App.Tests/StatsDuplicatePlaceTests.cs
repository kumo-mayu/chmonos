using System.IO;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>統計「空けられる場所」の置き場所の行：押すとその場所をエクスプローラで選んで開き、外したドライブの上は押せない。</summary>
public sealed class StatsDuplicatePlaceTests
{
    private static string UnusedDriveRoot()
    {
        var used = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        return $"{Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).First(c => !used.Contains(c))}:\\";
    }

    [Fact]
    public async Task 押すと_その場所を開く口に渡す()
    {
        var opened = new List<string>();
        var place = new DuplicatePlaceViewModel(@"C:\Windows\notepad.exe", path =>
        {
            opened.Add(path);
            return Task.CompletedTask;
        });

        Assert.True(place.IsAvailable);
        place.OpenCommand.Execute(null);
        await Task.Yield();

        Assert.Equal([@"C:\Windows\notepad.exe"], opened);
        Assert.Equal("エクスプローラで、この場所を選んで開きます。", place.Tooltip);
    }

    [Fact]
    public void 外したドライブの上の場所は押せない()
    {
        var place = new DuplicatePlaceViewModel(UnusedDriveRoot() + @"files\a.zip", _ => Task.CompletedTask);

        Assert.False(place.IsAvailable);
        Assert.Equal("ドライブがつながっていません。つなぐと開けます。", place.Tooltip);
    }

    [Fact]
    public void ネットワークの場所は見に行かず押せる扱いにする()
    {
        Assert.True(new DuplicatePlaceViewModel(@"\\no-such-host\share\a.zip", _ => Task.CompletedTask).IsAvailable);
    }
}
