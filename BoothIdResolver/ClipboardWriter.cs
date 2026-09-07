using System.Diagnostics;

namespace BoothIdResolver;

/// <summary>
/// Windows標準の clip.exe にパイプしてクリップボードへ書き込む。
/// P/InvokeやWinFormsへの依存を避けるための最小実装。
/// </summary>
public static class ClipboardWriter
{
    public static bool TrySetText(string text)
    {
        try
        {
            var psi = new ProcessStartInfo("clip.exe")
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
