using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests.Robustness;

public sealed class ModificationSnapshotAdversarialTests
{
    // 時計が戻ったときに写しが増える件（2026-11-13 の点検 件6）は、ユーザ判断で記録だけにしたので、その試験はここに置かない

    [Fact]
    public void 手編集した写しのnull配列とnull行を_保存し直しても有効な記録を保つ()
    {
        using var directory = new RobustnessDirectory();
        var path = Path.Combine(directory.Root, "modification.json");
        var record = JsonSerializer.Deserialize<ModificationRecord>("""
            {"id":"mod-01234567","avatarItemId":"9900001","name":"作り物","members":null,
             "vpmSnapshots":[null,{"at":"2026-11-11T00:00:00Z","packages":null},
               {"at":"2026-11-12T00:00:00Z","packages":[null,{"name":"com.example.tool","version":"1"}]}]}
            """, JsonStore.Options)!;
        JsonStore.Write(path, record);
        var loaded = JsonStore.Read<ModificationRecord>(path)!;
        Assert.Empty(loaded.Members);
        Assert.Equal(2, loaded.RecordedVpm.Count);
        Assert.Empty(loaded.RecordedVpm[0].Items);
        Assert.Equal("1", Assert.Single(loaded.RecordedVpm[1].Items).Version);
    }
}
