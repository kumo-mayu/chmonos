using System.IO;
using System.Security;
using BoothAssetManager.Core.Services;
using Microsoft.Win32;

namespace BoothAssetManager.App.Services;

/// <summary>
/// Windows に残っている、入れたアプリの記録を読む（アンインストール情報・関連付け・Unity の登録）。
///
/// **Unity や VCC の場所を、利用者の PC の置き場所に頼らずに探すため**（ユーザ指示 2026-09-13）。
/// 読むだけで書かない。読めない所（権限・壊れた登録）は黙って飛ばす——ほかの記録から探せる。
/// 記録の中身の読み解きは <see cref="Core.Services.UnityEditorLocator"/>（試験付き）。
/// </summary>
internal static class InstalledApps
{
    /// <summary>利用者ごとに入れた物と、PC 全体に入れた物（32bit の物は WOW6432Node の下）。</summary>
    private static readonly (RegistryKey Hive, string Path)[] UninstallRoots =
    [
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
    ];

    /// <summary>アンインストール情報を全部。</summary>
    public static IReadOnlyList<UninstallRecord> Uninstall()
    {
        var entries = new List<UninstallRecord>();
        foreach (var (hive, path) in UninstallRoots)
        {
            try
            {
                using var root = hive.OpenSubKey(path);
                if (root is null)
                {
                    continue;
                }

                foreach (var name in root.GetSubKeyNames())
                {
                    try
                    {
                        using var key = root.OpenSubKey(name);
                        if (key?.GetValue("DisplayName") is string display)
                        {
                            entries.Add(new UninstallRecord(
                                name,
                                display,
                                key.GetValue("Publisher") as string,
                                key.GetValue("InstallLocation") as string,
                                key.GetValue("DisplayIcon") as string));
                        }
                    }
                    catch (Exception exception) when (IsUnreadable(exception))
                    {
                    }
                }
            }
            catch (Exception exception) when (IsUnreadable(exception))
            {
            }
        }

        return entries;
    }

    /// <summary>
    /// 関連付けの開くコマンド（<c>unityhub</c>・<c>vcc</c> のようなリンクの形、または <c>.unitypackage</c> の中身の名前）。
    /// 利用者ごとの登録を先に見て、無ければ全体の登録を見る。
    /// </summary>
    public static string? OpenCommand(string className)
        => ReadDefault(Registry.CurrentUser, $@"Software\Classes\{className}\shell\open\command")
            ?? ReadDefault(Registry.ClassesRoot, $@"{className}\shell\open\command");

    /// <summary>拡張子（<c>.unitypackage</c>）を開くコマンド。拡張子 → 中身の名前 → 開くコマンドの順にたどる。</summary>
    public static string? FileAssociationCommand(string extension)
    {
        var className = ReadDefault(Registry.CurrentUser, $@"Software\Classes\{extension}")
            ?? ReadDefault(Registry.ClassesRoot, extension);
        return string.IsNullOrWhiteSpace(className) ? null : OpenCommand(className);
    }

    /// <summary>
    /// Unity の登録にあるエディタの場所（<c>Software\Unity Technologies\Installer\Unity &lt;版&gt;</c> の <c>Location x64</c>）。
    /// Hub で入れた物にも、Hub を使わずに入れた物にも残る（手元の実物で確かめた）。
    /// </summary>
    public static IReadOnlyList<string> UnityInstallerLocations(string version)
    {
        var found = new List<string>();
        foreach (var (hive, path) in new[]
        {
            (Registry.CurrentUser, @"Software\Unity Technologies\Installer"),
            (Registry.LocalMachine, @"Software\Unity Technologies\Installer"),
            (Registry.LocalMachine, @"Software\WOW6432Node\Unity Technologies\Installer"),
        })
        {
            try
            {
                using var key = hive.OpenSubKey($@"{path}\Unity {version}");
                foreach (var valueName in new[] { "Location x64", "Location" })
                {
                    if (key?.GetValue(valueName) is string location && location.Length > 0)
                    {
                        found.Add(location);
                    }
                }
            }
            catch (Exception exception) when (IsUnreadable(exception))
            {
            }
        }

        return found;
    }

    private static string? ReadDefault(RegistryKey hive, string path)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            return key?.GetValue(null) as string;
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return null;
        }
    }

    private static bool IsUnreadable(Exception exception)
        => exception is SecurityException or UnauthorizedAccessException or IOException or ObjectDisposedException;
}
