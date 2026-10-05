namespace Chmonos.Core.Tests;

/// <summary>試験でジャンクションを作る（mklink /J は管理者の権限が要らない。シンボリックリンクは要ることがある）。</summary>
internal static class TestJunction
{
    /// <returns>作れたか（FAT など作れない環境では false）。</returns>
    public static bool TryCreate(string link, string target)
    {
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!)
        {
            mklink.WaitForExit();
        }

        return Directory.Exists(link);
    }
}
