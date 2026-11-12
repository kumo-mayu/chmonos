using System.Text.Json;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>
/// 使ったものを記録した時に、紐付けたプロジェクトの VPM を改変に控える（ユーザ判断 2026-11-12。docs/spec/unity-vpm.md「改変に控える」）。
/// 入口は命令の口から通す（画面と API が同じ道を通るので）。
/// </summary>
public sealed class ModificationVpmSnapshotTests : IDisposable
{
    private readonly string _root;
    private readonly string _project;
    private readonly DataStore _store;
    private readonly ModificationService _service;
    private readonly CommandHandler _commands;

    public ModificationVpmSnapshotTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-modvpm-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(Path.Combine(_root, "store"));
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new ModificationService(_store);
        _commands = new CommandHandler(null!, null!, modifications: _service);
        _project = Path.Combine(_root, "Project");
        Directory.CreateDirectory(Path.Combine(_project, "Assets"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>作り物の VPM を置く（名前・表示名・版）。manifest にも書く。</summary>
    private void WriteVpm(params (string Name, string? DisplayName, string Version)[] packages)
    {
        var folder = Path.Combine(_project, "Packages");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        var locked = string.Join(",", packages.Select(package => $"\"{package.Name}\": {{ \"version\": \"{package.Version}\" }}"));
        File.WriteAllText(Path.Combine(folder, "vpm-manifest.json"), $"{{ \"dependencies\": {{}}, \"locked\": {{ {locked} }} }}");
        foreach (var (name, displayName, version) in packages)
        {
            Directory.CreateDirectory(Path.Combine(folder, name));
            var display = displayName is null ? string.Empty : $", \"displayName\": \"{displayName}\"";
            File.WriteAllText(Path.Combine(folder, name, "package.json"), $"{{ \"name\": \"{name}\", \"version\": \"{version}\"{display} }}");
        }
    }

    private async Task<ModificationRecord> CreateAsync(string? project)
    {
        var record = (await _service.CreateAsync("7841391", "普段着"))!;
        if (project is not null)
        {
            Assert.True(await _service.SetProjectAsync(record.Id, project));
        }

        return record;
    }

    private static ModificationMember Member(string itemId, string? hash = null)
        => new() { ItemId = itemId, FileHash = hash, Package = hash is null ? null : "Outfit.unitypackage" };

    private async Task<ModificationRecord> LoadAsync(string id) => (await _service.LoadAsync(id))!;

    private static string Versions(ModificationVpmSnapshot snapshot)
        => string.Join(",", snapshot.Items.Select(package => $"{package.Name}={package.Version}"));

    [Fact]
    public async Task 使ったものを1つ足すと_その時のVPMを控える()
    {
        WriteVpm(("jp.lilxyzw.liltoon", "lilToon", "2.3.2"), ("nadena.dev.modular-avatar", "Modular Avatar", "1.10.0"));
        var record = await CreateAsync(_project);

        Assert.IsType<CommandResult.ModificationsChanged>(await _commands.ExecuteAsync(new UiCommand.AddModificationMember(record.Id, Member("1001"))));

        var snapshot = Assert.Single((await LoadAsync(record.Id)).RecordedVpm);
        Assert.Equal("jp.lilxyzw.liltoon=2.3.2,nadena.dev.modular-avatar=1.10.0", Versions(snapshot));
        Assert.Equal("lilToon", snapshot.Items[0].DisplayName);
    }

    [Fact]
    public async Task まとめて足すときも控える()
    {
        WriteVpm(("jp.lilxyzw.liltoon", "lilToon", "2.3.2"));
        var record = await CreateAsync(_project);

        await _commands.ExecuteAsync(new UiCommand.AddModificationMembers(record.Id, [Member("1001"), Member("1002")]));

        Assert.Equal("jp.lilxyzw.liltoon=2.3.2", Versions(Assert.Single((await LoadAsync(record.Id)).RecordedVpm)));
    }

    [Fact]
    public async Task 送る時に使ったファイルを記録すると控える()
    {
        // 手で足した時はプロジェクトが無く、後で紐付けてから送った
        var record = await CreateAsync(null);
        await _service.AddMemberAsync(record.Id, Member("1001"));
        var row = Assert.Single((await LoadAsync(record.Id)).Members);
        Assert.Empty((await LoadAsync(record.Id)).RecordedVpm);

        WriteVpm(("jp.lilxyzw.liltoon", "lilToon", "2.3.3"));
        await _service.SetProjectAsync(record.Id, _project);
        Assert.IsType<CommandResult.ModificationsChanged>(await _commands.ExecuteAsync(
            new UiCommand.RecordModificationMemberFiles(record.Id, row, [Member("1001", "abc")])));

        Assert.Equal("jp.lilxyzw.liltoon=2.3.3", Versions(Assert.Single((await LoadAsync(record.Id)).RecordedVpm)));
    }

    [Fact]
    public async Task 使ったファイルを選び直すと控え_同じ物を選んだだけなら控えない()
    {
        var record = await CreateAsync(null);
        await _service.AddMemberAsync(record.Id, Member("1001", "aaa"));
        var row = Assert.Single((await LoadAsync(record.Id)).Members);

        WriteVpm(("jp.lilxyzw.liltoon", "lilToon", "2.3.3"));
        await _service.SetProjectAsync(record.Id, _project);

        // 同じ物を選び直しただけ：使ったものは変わっていないので、記録した時の姿ではない
        await _commands.ExecuteAsync(new UiCommand.ChangeModificationMemberFiles(record.Id, row, [new ModificationMemberFile("aaa", "Outfit.unitypackage", null)]));
        Assert.Empty((await LoadAsync(record.Id)).RecordedVpm);

        Assert.IsType<CommandResult.ModificationsChanged>(await _commands.ExecuteAsync(
            new UiCommand.ChangeModificationMemberFiles(record.Id, row, [new ModificationMemberFile("bbb", "Outfit.unitypackage", null)])));
        Assert.Equal("jp.lilxyzw.liltoon=2.3.3", Versions(Assert.Single((await LoadAsync(record.Id)).RecordedVpm)));
    }

    [Fact]
    public async Task 外す_並べ替えでは控えない()
    {
        var record = await CreateAsync(null);
        await _service.AddMembersAsync(record.Id, [Member("1001"), Member("1002")]);
        WriteVpm(("jp.lilxyzw.liltoon", "lilToon", "2.3.3"));
        await _service.SetProjectAsync(record.Id, _project);
        var rows = (await LoadAsync(record.Id)).Members;

        await _commands.ExecuteAsync(new UiCommand.SetModificationMemberDetached(record.Id, rows[0], true));
        await _commands.ExecuteAsync(new UiCommand.MoveModificationMember(record.Id, rows[1], -1));

        Assert.Null((await LoadAsync(record.Id)).VpmSnapshots);
    }

    [Fact]
    public async Task プロジェクトを紐付けていなければ控えず_JSONにも欄を書かない()
    {
        var record = await CreateAsync(null);

        await _commands.ExecuteAsync(new UiCommand.AddModificationMember(record.Id, Member("1001")));

        Assert.Null((await LoadAsync(record.Id)).VpmSnapshots);
        Assert.DoesNotContain("vpmSnapshots", File.ReadAllText(_store.Paths.ModificationFile(record.Id)));
    }

    [Fact]
    public async Task プロジェクトが読めず調べた控えも無ければ控えない()
    {
        // Packages が無い（ドライブが外れた・プロジェクトでない）
        var record = await CreateAsync(_project);

        await _commands.ExecuteAsync(new UiCommand.AddModificationMember(record.Id, Member("1001")));

        Assert.Null((await LoadAsync(record.Id)).VpmSnapshots);
    }

    [Fact]
    public async Task プロジェクトが読めなければ調べた控えのVPMを使う()
    {
        new UnityScanStore(_store.Paths).Save(_project, UnityProjectScan.Empty with
        {
            Vpm = [new VpmPackage("jp.lilxyzw.liltoon", "lilToon", "2.3.1", "jp.lilxyzw.liltoon", InManifest: true)],
        });
        var record = await CreateAsync(_project);

        await _commands.ExecuteAsync(new UiCommand.AddModificationMember(record.Id, Member("1001")));

        Assert.Equal("jp.lilxyzw.liltoon=2.3.1", Versions(Assert.Single((await LoadAsync(record.Id)).RecordedVpm)));
    }

    [Fact]
    public async Task 紐付けの綴りが揃っていなくても調べた控えを引く()
    {
        // 控えは見張りと同じく揃えた綴りの場所で書く。紐付けは「/」区切り・末尾の区切り付きで持つことがある
        new UnityScanStore(_store.Paths).Save(UnityProjectPath.Normalize(_project)!, UnityProjectScan.Empty with
        {
            Vpm = [new VpmPackage("jp.lilxyzw.liltoon", "lilToon", "2.3.1", "jp.lilxyzw.liltoon", InManifest: true)],
        });
        var record = await CreateAsync(_project.Replace('\\', '/') + "/");

        await _commands.ExecuteAsync(new UiCommand.AddModificationMember(record.Id, Member("1001")));

        Assert.Equal("jp.lilxyzw.liltoon=2.3.1", Versions(Assert.Single((await LoadAsync(record.Id)).RecordedVpm)));
    }

    [Fact]
    public async Task 同じ中身なら足さず_版が変われば足し_表示名だけの違いでは足さない()
    {
        WriteVpm(("jp.lilxyzw.liltoon", "lilToon", "2.3.2"));
        var record = await CreateAsync(_project);
        await _service.AddMemberAsync(record.Id, Member("1001"));
        await _service.AddMemberAsync(record.Id, Member("1002"));
        Assert.Single((await LoadAsync(record.Id)).RecordedVpm);

        WriteVpm(("jp.lilxyzw.liltoon", "lilToon Shader", "2.3.2"));
        await _service.AddMemberAsync(record.Id, Member("1003"));
        Assert.Single((await LoadAsync(record.Id)).RecordedVpm);

        WriteVpm(("jp.lilxyzw.liltoon", "lilToon", "2.3.3"));
        await _service.AddMemberAsync(record.Id, Member("1004"));

        // 戻したのも戻した事として残す（比べるのは最後の写しだけ）
        WriteVpm(("jp.lilxyzw.liltoon", "lilToon", "2.3.2"));
        await _service.AddMemberAsync(record.Id, Member("1005"));

        var snapshots = (await LoadAsync(record.Id)).RecordedVpm;
        Assert.Equal(["jp.lilxyzw.liltoon=2.3.2", "jp.lilxyzw.liltoon=2.3.3", "jp.lilxyzw.liltoon=2.3.2"], snapshots.Select(Versions));
        Assert.True(snapshots[0].At <= snapshots[1].At && snapshots[1].At <= snapshots[2].At);
    }

    [Fact]
    public async Task 控えのJSONは人が読める形で_読めない版と表示名は書かない()
    {
        Directory.CreateDirectory(Path.Combine(_project, "Packages", "com.example.tool"));
        File.WriteAllText(Path.Combine(_project, "Packages", "com.example.tool", "package.json"), "{ \"name\": \"com.example.tool\" }");
        var record = await CreateAsync(_project);

        await _service.AddMemberAsync(record.Id, Member("1001"));

        using var document = JsonDocument.Parse(File.ReadAllText(_store.Paths.ModificationFile(record.Id)));
        var snapshot = Assert.Single(document.RootElement.GetProperty("vpmSnapshots").EnumerateArray().ToList());
        Assert.True(snapshot.TryGetProperty("at", out _));
        var package = Assert.Single(snapshot.GetProperty("packages").EnumerateArray().ToList());
        Assert.Equal(["name"], package.EnumerateObject().Select(property => property.Name));
        Assert.Equal("com.example.tool", package.GetProperty("name").GetString());
    }

    [Fact]
    public async Task 欄の無い前の改変と_手で直した欠けた行は控えなしとして読む()
    {
        var record = await CreateAsync(null);
        var path = _store.Paths.ModificationFile(record.Id);
        var json = File.ReadAllText(path);
        Assert.Empty((await LoadAsync(record.Id)).RecordedVpm);

        File.WriteAllText(path, json.TrimEnd().TrimEnd('}') + ", \"vpmSnapshots\": [null, { \"at\": \"2026-11-12T10:00:00+09:00\", \"packages\": [null, { \"name\": \"\" }, { \"name\": \"a.b\", \"version\": \"1\" }] }, { \"at\": \"2026-11-11T10:00:00+09:00\", \"packages\": null }] }");
        var snapshots = (await new ModificationService(new DataStore(_store.Paths)).LoadAsync(record.Id))!.RecordedVpm;

        Assert.Equal(2, snapshots.Count);
        Assert.Empty(snapshots[0].Items);
        Assert.Equal("a.b=1", Versions(snapshots[1]));
    }

    [Fact]
    public async Task 複製には控えも写す()
    {
        WriteVpm(("jp.lilxyzw.liltoon", "lilToon", "2.3.2"));
        var record = await CreateAsync(_project);
        await _service.AddMemberAsync(record.Id, Member("1001"));

        var copy = await _service.DuplicateAsync(record.Id);

        Assert.Equal("jp.lilxyzw.liltoon=2.3.2", Versions(Assert.Single(copy!.RecordedVpm)));
    }
}
