using System.IO;
using Chmonos.App.ViewModels;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 初回の窓で選んだ場所を、何も書く前に版で確かめる（2026-11-10）。前は場所と設定を書いてから次の起動で止まり、選び直せなかった
/// </summary>
public sealed class FirstRunStoreCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-first-run-check-" + Guid.NewGuid().ToString("N"));

    public FirstRunStoreCheckTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void WriteMarker(string json) => File.WriteAllText(Path.Combine(_root, StoreFormat.MarkerFileName), json);

    [Fact]
    public void 印の無い場所は_開ける()
        => Assert.Null(FirstRunStoreCheck.RefusalFor(_root));

    [Fact]
    public void 同じ版と古い版の場所は_開ける()
    {
        WriteMarker($$"""{ "formatVersion": {{StoreFormat.Current}} }""");
        Assert.Null(FirstRunStoreCheck.RefusalFor(_root));

        WriteMarker("""{ "formatVersion": 1 }""");
        Assert.Null(FirstRunStoreCheck.RefusalFor(_root));
    }

    [Fact]
    public void 新しい版の場所は_選んだ時点で断る()
    {
        WriteMarker($$"""{ "formatVersion": {{StoreFormat.Current + 1}} }""");

        Assert.StartsWith("選んだ場所のデータは、新しいバージョンのChmonosで使われています。", FirstRunStoreCheck.RefusalFor(_root), StringComparison.Ordinal);
    }

    [Fact]
    public void 読めない印の場所は_断る()
    {
        WriteMarker("壊れた印");

        Assert.StartsWith("選んだ場所のデータの形式を確かめられませんでした。", FirstRunStoreCheck.RefusalFor(_root), StringComparison.Ordinal);
    }
}
