using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>アンインストール情報の1件。Windows の記録を読むのは App の <c>InstalledApps</c>。</summary>
/// <param name="KeyName">記録の鍵の名前（Inno Setup なら <c>{AppId}_is1</c>）。</param>
public sealed record UninstallRecord(
    string KeyName, string DisplayName, string? Publisher, string? InstallLocation, string? DisplayIcon);

/// <summary>「VCCを開く」「ALCOMを開く」のどちらを出すか。出すのはいつも1つ。</summary>
/// <param name="ShowVcc">「VCCを開く」を出すか。</param>
/// <param name="CanOpenVcc">「VCCを開く」を押せるか。どちらも無いときは押せない形で出して、入れ方を吹き出しに書く。</param>
/// <param name="ShowAlcom">「ALCOMを開く」を出すか。</param>
public sealed record ProjectManagerButtons(bool ShowVcc, bool CanOpenVcc, bool ShowAlcom);

/// <summary>
/// VCC と ALCOM（どちらも VRChat のプロジェクトを管理するアプリ）の実行ファイルの候補を、見る順に並べる。
/// **起動するだけで中身には触らない**（ユーザ仕様。`docs/research/alcom.md` §6 案 A）。
///
/// 並べるだけにして、実際にあるかの確かめ・レジストリ・プロセスは呼ぶ側（App の <c>VccLaunch</c>・<c>AlcomLaunch</c>）に置く——
/// 手掛かりの優先の決まりを、この PC の入り方に左右されずに試験できるように。
/// </summary>
public static class ProjectManagerApps
{
    /// <summary>
    /// ALCOM のインストーラ（Inno Setup）の <c>AppId</c> に付く鍵の名前。
    /// **<c>DisplayName</c> は「ALCOM バージョン 1.1.8」のように言語で変わる**ので、見分けには使わない（alcom.md §2-1）。
    /// </summary>
    public const string AlcomUninstallKey = "{4C3D0631-AE29-4D20-A231-678D9CF8D6DB}_is1";

    /// <summary>ALCOM の作者。古い版（NSIS のころ）は鍵の名前が違い得るので、こちらでも見分ける。</summary>
    public const string AlcomPublisher = "anatawa12";

    public const string AlcomExeName = "ALCOM.exe";

    public const string VccExeName = "CreatorCompanion.exe";

    /// <summary>
    /// VCC の候補。アンインストール情報の場所 → アイコンの欄 → <c>vcc://</c> の関連付け。
    ///
    /// <c>vcc://</c> は ALCOM の設定1つで ALCOM が引き受ける（alcom.md §3-1）。その先が ALCOM なら VCC の候補にしない——
    /// 前は「利用者が選んだ代わりのアプリ」として開いていたが、今は「ALCOMを開く」が別にあり、
    /// 両方のボタンが ALCOM を開くことになる。
    /// </summary>
    public static IEnumerable<string> VccCandidates(IEnumerable<UninstallRecord> records, string? vccLinkCommand)
    {
        foreach (var record in records.Where(record =>
            record.DisplayName.StartsWith("VRChat Creator Companion", StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrWhiteSpace(record.InstallLocation))
            {
                yield return Path.Combine(record.InstallLocation, VccExeName);
            }

            if (UnityEditorLocator.ExeFromCommand(record.DisplayIcon) is { } icon)
            {
                yield return icon;
            }
        }

        if (UnityEditorLocator.ExeFromCommand(vccLinkCommand) is { } linked && !IsAlcomExe(linked))
        {
            yield return linked;
        }
    }

    /// <summary>
    /// ALCOM の候補。
    /// 1. アンインストール情報のうち、鍵が <see cref="AlcomUninstallKey"/> の物の場所 → アイコンの欄（入れ先はインストーラで変えられる）
    /// 2. 作者が <see cref="AlcomPublisher"/> の物。同じ作者の別のアプリを掴まないよう、名前が <c>ALCOM.exe</c> の物だけ
    /// 3. 既定の入れ先 <c>%LOCALAPPDATA%\Programs\ALCOM\</c>、古い入れ先 <c>%LOCALAPPDATA%\ALCOM\</c>
    ///    （NSIS から替えたとき前の場所へ上書きするようにしたので、両方あり得る。alcom.md §2-1）
    /// 4. <c>vcc://</c> を ALCOM が引き受けていれば、その先（記録の無い入れ方でも場所が分かる）
    /// </summary>
    public static IEnumerable<string> AlcomCandidates(
        IEnumerable<UninstallRecord> records, string? localAppData, string? vccLinkCommand)
    {
        var list = records.ToList();

        foreach (var record in list.Where(record =>
            string.Equals(record.KeyName, AlcomUninstallKey, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var candidate in FromRecord(record, anyIcon: true))
            {
                yield return candidate;
            }
        }

        foreach (var record in list.Where(record =>
            !string.Equals(record.KeyName, AlcomUninstallKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.Publisher?.Trim(), AlcomPublisher, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var candidate in FromRecord(record, anyIcon: false))
            {
                yield return candidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "ALCOM", AlcomExeName);
            yield return Path.Combine(localAppData, "ALCOM", AlcomExeName);
        }

        if (UnityEditorLocator.ExeFromCommand(vccLinkCommand) is { } linked && IsAlcomExe(linked))
        {
            yield return linked;
        }

        static IEnumerable<string> FromRecord(UninstallRecord record, bool anyIcon)
        {
            if (!string.IsNullOrWhiteSpace(record.InstallLocation))
            {
                yield return Path.Combine(record.InstallLocation, AlcomExeName);
            }

            if (UnityEditorLocator.ExeFromCommand(record.DisplayIcon) is { } icon && (anyIcon || IsAlcomExe(icon)))
            {
                yield return icon;
            }
        }
    }

    /// <summary>候補のうち、最初に在る物。無ければ null。</summary>
    public static string? FirstExisting(IEnumerable<string> candidates, Func<string, bool> exists)
        => candidates.FirstOrDefault(exists);

    /// <summary>
    /// ボタンの出し分け（ユーザ指示 2026-09-29）。出すのはいつも1つ。
    /// 片方だけならその方。両方あれば設定の <paramref name="choice"/> の方で、既定は <c>vcc://</c> を引き受けている方
    /// ——2つ並べると、どちらで開くかを毎回選ばせることになる。
    /// どちらも無いときは今までどおり「VCCを開く」を押せない形で出す（ボタンごと消すと、ここから開けることに気付けない）。
    /// </summary>
    public static ProjectManagerButtons Buttons(bool hasVcc, bool hasAlcom, bool linkOpensAlcom, ProjectManagerChoice choice)
    {
        var alcom = hasVcc && hasAlcom
            ? choice switch
            {
                ProjectManagerChoice.Vcc => false,
                ProjectManagerChoice.Alcom => true,
                _ => linkOpensAlcom,
            }
            : hasAlcom;
        return new(ShowVcc: !alcom, CanOpenVcc: hasVcc, ShowAlcom: alcom);
    }

    /// <summary><c>vcc://</c> を ALCOM が引き受けているか。関連付けが無い・VCC のままなら false。</summary>
    public static bool LinkOpensAlcom(string? vccLinkCommand)
        => UnityEditorLocator.ExeFromCommand(vccLinkCommand) is { } linked && IsAlcomExe(linked);

    private static bool IsAlcomExe(string path)
        => string.Equals(Path.GetFileName(path), AlcomExeName, StringComparison.OrdinalIgnoreCase);
}
