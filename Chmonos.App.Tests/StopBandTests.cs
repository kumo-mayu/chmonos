using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 下の帯の「中止」の、画面の側の出し分け（メモ3-②・ユーザ判断 2026-10-02「zipが仮ファイルなので中止関係は試していない←あなた側で保証できるはず」）。
///
/// 展開と取り出しを止めたときに書きかけを消すことは Core の試験（<c>TemporaryUnpackerTests</c>）が見ている。
/// ここで見るのは、帯に何が出て、押した後どう残り、いつ消えるか。前は描く台の絵（<c>band-unpacking</c>）でしか見ていなかった。
/// </summary>
public class StopBandTests
{
    /// <summary>帯の1コマ。帯の文が替わるたびに控える。</summary>
    private sealed record BandFrame(bool IsUnpacking, string Text, string Target, bool HasProgress, double Progress, bool CanStop);

    /// <summary>
    /// 1回で読む量（81,920 バイト）を何十回も超える作り物の zip。中止を押すまでに書き終えてしまわないように大きくし、
    /// 縮まない中身（乱数）を縮めずに入れる
    /// </summary>
    private static byte[] BigZip()
    {
        var content = new byte[4 * 1024 * 1024];
        new Random(20261002).NextBytes(content);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("textures/sample.bin", CompressionLevel.NoCompression).Open();
            entry.Write(content);
        }

        return buffer.ToArray();
    }

    [Fact]
    public Task 一時展開の帯は_合計が分かるまで流れる棒_分かれば進み具合_中止の後は片付くまで中止していますで残る() => TestApp.Run(async app =>
    {
        var zip = app.NewFile("sample_textures.zip", BigZip());
        var item = Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(zip));
        await app.AddItemAsync(item);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        var row = Assert.Single(page.LocalFiles);
        await UiThread.Until(() => row.CanUnpack, "zip が手元にあると分かる");

        // 帯の文が替わるたびに1コマ控える。合計が届いた（進み具合が出た）コマで「中止」を押す。
        // 進み具合は画面のスレッドへ届くので、押すのは展開の途中（展開が戻る知らせはその後に積まれる）
        var frames = new List<BandFrame>();
        var stopped = false;
        PropertyChangedEventHandler onChanged = (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.UnpackText))
            {
                return;
            }

            frames.Add(new BandFrame(main.IsUnpacking, main.UnpackText, main.UnpackTargetText, main.HasUnpackProgress, main.UnpackProgress, main.CanStopUnpack));
            if (!stopped && main.HasUnpackProgress)
            {
                stopped = true;
                Assert.True(main.StopUnpackCommand.CanExecute(null));
                main.StopUnpackCommand.Execute(null);
            }
        };
        main.PropertyChanged += onChanged;
        try
        {
            page.UnpackCommand.Execute(row);
            await UiThread.Until(() => stopped && !main.IsUnpacking, "中止を押して、片付けが済むと帯が消える");
        }
        finally
        {
            main.PropertyChanged -= onChanged;
            new TemporaryUnpacker(TemporaryUnpacker.RootFor(app.Services.Paths.Root)).CleanUp();
        }

        // 押した直後：合計がまだ分からないので、数字の無い文と流れる棒。何を展開しているかは名前で言う
        var first = frames[0];
        Assert.Equal(new BandFrame(true, "展開しています…", "sample_textures.zip", false, 0, true), first);

        // 合計が届いた：棒に長さが付き、文に「済んだ量 / 合計」が付く
        var progress = Assert.Single(frames, frame => frame.HasProgress);
        Assert.True(progress.IsUnpacking);
        Assert.StartsWith("展開しています… ", progress.Text);
        Assert.Contains(" / 4 MB", progress.Text);
        Assert.InRange(progress.Progress, 0, 1);
        Assert.True(progress.CanStop);

        // 中止を押した後：片付けが済むまで帯は残り、「中止」は引っ込め、棒は長さの無い待ちに戻す
        var stopping = frames.SkipWhile(frame => !frame.HasProgress).Skip(1).First();
        Assert.Equal(new BandFrame(true, "展開を中止しています…", string.Empty, false, 0, false), stopping);

        // 片付けが済んだら帯を畳む。中止は人が押した結果なので、窓では知らせない
        Assert.Equal(new BandFrame(false, string.Empty, string.Empty, false, 0, false), frames[^1]);
        Assert.Empty(app.Notices);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task Unityへ送るを窓が出る前に止めたら_送るのを中止しましたとだけ言い_帯を畳む(int count) => TestApp.Run(async app =>
    {
        // 送り始めた瞬間に帯の「中止」を押す（Unity の窓を探す前）。Unity が無くても、この道は窓に触れずに止まる
        var zip = app.NewFile("sample_costume.zip", BigZip());
        var packages = Enumerable.Range(1, count)
            .Select(index => new UnityPackageEntry(zip, $"sample_{index}.unitypackage", 100))
            .ToList();
        var main = await app.StartAsync();

        var shown = new List<bool>();
        PropertyChangedEventHandler onChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSendingToUnity))
            {
                shown.Add(main.IsSendingToUnity);
                if (main.IsSendingToUnity)
                {
                    main.StopUnityCommand.Execute(null);
                }
            }
        };
        main.PropertyChanged += onChanged;
        IReadOnlyList<UnityQueueOutcome> outcomes;
        try
        {
            outcomes = await UnityImportQueue.RunAsync(Environment.ProcessId, packages, null, CancellationToken.None);
            await app.SettleAsync();
        }
        finally
        {
            main.PropertyChanged -= onChanged;
            new TemporaryUnpacker(TemporaryUnpacker.RootFor(app.Services.Paths.Root)).CleanUp();
        }

        // どれも送っていない。理由は「窓が出る前に止めた」の文で、閉じてもらう取り込み画面の話はしない
        Assert.Equal(count, outcomes.Count);
        Assert.All(outcomes, outcome =>
        {
            Assert.False(outcome.Opened);
            Assert.Equal(UnityImportQueue.StoppedBeforeWindowMessage, outcome.Problem);
        });

        var text = UnityQueueOutcome.Describe(outcomes);
        Assert.Equal($"0 件をUnityへ順に送りました。残り {count} 件は送っていません。送るのを中止しました。", text);
        Assert.DoesNotContain("Cancel", text);

        // 帯は出てから畳まれ、次の送信を受けられる
        Assert.Equal([true, false], shown);
        Assert.False(UnityImportQueue.IsRunning);
        Assert.Equal(string.Empty, main.UnitySendText);
    });
}
