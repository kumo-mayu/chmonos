# 仮想の PC（VirtualBox）で、Unity Hub・VCC・ALCOM の見分けを確かめる道具（2026-10-06）。
#
# なぜ：見分けはレジストリの記録を読むだけの作りで、本物の PC の記録の形・消した後の残りかす・
# 入っていないリンクを開いたときに Windows が何をするか（失敗が返るのか、窓を出して「開けた」に見えるのか）は試験で作れない。
# この PC には Hub も VCC も入っていて、Windows 11 Home なのでサンドボックスも使えないので、まっさらな Windows を仮想の PC に入れる。
#
# 使い方（PowerShell）：
#   . .\experiments\ToolDetectProbe\vm-kit.ps1
#   New-ProbeVm -Iso D:\vm\Win11_Eval.iso            # 作って、Windows を自動で入れ始める（1時間ほど）
#   Wait-ProbeVm                                       # 入れ終わってログオンするまで待つ
#   Save-ProbeSnapshot clean                           # まっさらな状態の控え
#   Publish-Probe; Invoke-Probe                        # 見分けを書き出す（読むだけ）
#   Invoke-Probe -Open; Save-ProbeShot open.png        # リンクを開こうとして、画面を撮る
#   Restore-ProbeSnapshot clean                        # まっさらに戻す
#
# 守ること：仮想の PC の中だけで動かす。利用者の保存先（本番・写し）は渡さない。道具は保存先を持たない。

$ErrorActionPreference = 'Stop'

$script:VBox = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
$script:ProbeVmName = 'ChmonosProbe'
# 仮想の PC の中のアカウント。仮想の PC の中でしか使わない作り物（外へ出る物ではない）
$script:ProbeUser = 'probe'
$script:ProbePassword = 'probe-local-only'
$script:ProbeRoot = Join-Path $PSScriptRoot '..\..'
$script:ProbeShare = Join-Path $env:LOCALAPPDATA 'Chmonos-vm\share'

function Invoke-VBox {
    # VBoxManage の失敗は終了コードでしか分からないので、ここで例外にする
    & $script:VBox @args
    if ($LASTEXITCODE -ne 0) { throw "VBoxManage $($args -join ' ') が失敗した（$LASTEXITCODE）" }
}

function New-ProbeVm {
    param(
        [Parameter(Mandatory)][string]$Iso,
        [string]$Dir = (Join-Path $env:LOCALAPPDATA 'Chmonos-vm'),
        [int]$MemoryMB = 16384,
        [int]$Cpus = 8,
        [int]$DiskGB = 80,
        # 版の番号（VBoxManage unattended detect --iso で見る）。通常の ISO は 3 が Pro。
        # Home（1）は最初の立ち上げで Microsoft のアカウントとネットワークを求め、自動の入れ方のローカルのアカウントが通らず、
        # 黒い画面のまま入力を待った（2026-10-06・26300 の ISO）。見分けが読むレジストリは版で変わらないので Pro で確かめる
        [int]$ImageIndex = 3,
        # 通常の ISO は入れるときにプロダクトキーを求める。Microsoft が公開している「入れるための既定のキー」（認証はされない）を渡す。
        # 評価版の ISO ならキーは要らないので空にする
        [string]$Key = 'YTMG3-N6DKC-DKB77-7M9GH-8HVX7'
    )
    if (-not (Test-Path $Iso)) { throw "ISO が見つからない: $Iso" }
    New-Item -ItemType Directory -Force $Dir, $script:ProbeShare | Out-Null

    Invoke-VBox createvm --name $script:ProbeVmName --ostype Windows11_64 --register --basefolder $Dir
    # Windows 11 は TPM 2.0 とセキュアブートを求める。VirtualBox 7 は両方を作り物で持てる
    Invoke-VBox modifyvm $script:ProbeVmName --memory $MemoryMB --cpus $Cpus --firmware efi --tpm-type 2.0 `
        --graphicscontroller vboxsvga --vram 128 --nic1 nat --clipboard-mode disabled --drag-and-drop disabled
    Invoke-VBox modifynvram $script:ProbeVmName inituefivarstore
    Invoke-VBox modifynvram $script:ProbeVmName enrollmssignatures
    Invoke-VBox modifynvram $script:ProbeVmName enrollorclpk
    Invoke-VBox modifynvram $script:ProbeVmName secureboot --enable

    $disk = Join-Path $Dir "$($script:ProbeVmName)\disk.vdi"
    Invoke-VBox createmedium disk --filename $disk --size ($DiskGB * 1024) --format VDI
    Invoke-VBox storagectl $script:ProbeVmName --name SATA --add sata --controller IntelAhci
    Invoke-VBox storageattach $script:ProbeVmName --storagectl SATA --port 0 --device 0 --type hdd --medium $disk

    # 道具を渡す共有のフォルダ（読むだけで渡す。中から書かせない）
    Invoke-VBox sharedfolder add $script:ProbeVmName --name share --hostpath $script:ProbeShare --readonly --automount

    $keyArgs = if ($Key) { @('--key', $Key) } else { @() }
    Invoke-VBox unattended install $script:ProbeVmName --iso $Iso --image-index $ImageIndex @keyArgs `
        --user $script:ProbeUser --password $script:ProbePassword --full-user-name $script:ProbeUser `
        --locale ja_JP --country JP --time-zone 'Tokyo Standard Time' --install-additions
    Invoke-VBox startvm $script:ProbeVmName --type gui
    "作った：$($script:ProbeVmName)。Windows を自動で入れている（1時間ほど）。Wait-ProbeVm で待つ"
}

function Remove-ProbeVm {
    # 作り直すとき。止めてから、登録とディスクを消す（共有のフォルダの中身は残す）
    & $script:VBox controlvm $script:ProbeVmName poweroff 2>$null | Out-Null
    Start-Sleep -Seconds 5
    Invoke-VBox unregistervm $script:ProbeVmName --delete
    "消した：$($script:ProbeVmName)"
}

function Wait-ProbeVm {
    param([int]$TimeoutMinutes = 120)
    # 追加の部品（Guest Additions）が動き、利用者がログオンしていれば、中で命令を走らせられる
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ((Get-Date) -lt $deadline) {
        $users = & $script:VBox guestproperty get $script:ProbeVmName '/VirtualBox/GuestInfo/OS/LoggedInUsers' 2>$null
        if ($users -match 'Value:\s*[1-9]') { "ログオンした：$users"; return }
        Start-Sleep -Seconds 30
    }
    throw "$TimeoutMinutes 分待ってもログオンしなかった"
}

function Save-ProbeSnapshot([Parameter(Mandatory)][string]$Name) {
    Invoke-VBox snapshot $script:ProbeVmName take $Name --live
}

function Restore-ProbeSnapshot([Parameter(Mandatory)][string]$Name) {
    # 動いたままでは戻せないので、止めてから戻して起こす
    & $script:VBox controlvm $script:ProbeVmName poweroff 2>$null | Out-Null
    Start-Sleep -Seconds 3
    Invoke-VBox snapshot $script:ProbeVmName restore $Name
    Invoke-VBox startvm $script:ProbeVmName --type gui
}

function Publish-Probe {
    # 仮想の PC には .NET が無いので、全部入りの形で組んで共有のフォルダへ置く
    $out = Join-Path $script:ProbeShare 'ToolDetectProbe'
    dotnet publish (Join-Path $script:ProbeRoot 'experiments\ToolDetectProbe') -c Release -r win-x64 --self-contained -o $out -v q
    if ($LASTEXITCODE -ne 0) { throw '組めなかった' }
    "置いた：$out"
}

function Invoke-Probe {
    param([switch]$Open)
    # guestcontrol で直に走らせると、利用者の画面の外（対話のない場）で動き、Windows が出す窓が見えない。
    # 開こうとする回（-Open）は、利用者の画面で動くよう、ログオン中の利用者の予定の作業として走らせる
    $exe = '\\VBOXSVR\share\ToolDetectProbe\ToolDetectProbe.exe'
    $report = "C:\Users\$($script:ProbeUser)\probe-$(Get-Date -Format HHmmss).txt"
    $arguments = (@('--out', $report) + $(if ($Open) { @('--open') } else { @() })) -join ' '
    $task = "schtasks /Create /F /TN ChmonosProbe /SC ONCE /ST 00:00 /IT /RU $($script:ProbeUser) /TR `"$exe $arguments`" & schtasks /Run /TN ChmonosProbe"
    Invoke-VBox guestcontrol $script:ProbeVmName run --exe 'C:\Windows\System32\cmd.exe' --username $script:ProbeUser --password $script:ProbePassword `
        --wait-stdout -- cmd.exe /c $task
    # 書き終わるのを待って、中身を取り出す
    $deadline = (Get-Date).AddSeconds($(if ($Open) { 60 } else { 30 }))
    do {
        Start-Sleep -Seconds 3
        $text = & $script:VBox guestcontrol $script:ProbeVmName run --exe 'C:\Windows\System32\cmd.exe' --username $script:ProbeUser --password $script:ProbePassword `
            --wait-stdout -- cmd.exe /c "type $report" 2>$null
    } while (-not $text -and (Get-Date) -lt $deadline)
    if (-not $text) { throw "書き出しが読めなかった：$report" }
    $text
}

function Save-ProbeShot([Parameter(Mandatory)][string]$Path) {
    # 窓を出したかは画面でしか分からない（Process.Start が返っても、Windows が「開くアプリを選んでください」を出していることがある）
    Invoke-VBox controlvm $script:ProbeVmName screenshotpng (Join-Path (Resolve-Path .) $Path)
    "撮った：$Path"
}

function Save-ProbeWindowShot([Parameter(Mandatory)][string]$Path) {
    # Windows の立ち上げの途中は、VBoxManage の screenshotpng が画面の元が無いとして失敗する（2026-10-06）。
    # VirtualBox の窓そのものを PrintWindow で撮れば、黒いのか・何の画面かは分かる
    Add-Type -AssemblyName System.Drawing
    if (-not ('ProbeWin' -as [type])) {
        Add-Type @"
using System; using System.Runtime.InteropServices;
public static class ProbeWin {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    public struct RECT { public int L, T, R, B; }
}
"@
    }
    $window = Get-Process VirtualBoxVM -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "$($script:ProbeVmName)*" } | Select-Object -First 1
    if (-not $window) { throw '仮想の PC の窓が見つからない' }
    $rect = New-Object ProbeWin+RECT
    [ProbeWin]::GetWindowRect($window.MainWindowHandle, [ref]$rect) | Out-Null
    $bitmap = New-Object System.Drawing.Bitmap ($rect.R - $rect.L), ($rect.B - $rect.T)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    [ProbeWin]::PrintWindow($window.MainWindowHandle, $dc, 2) | Out-Null
    $graphics.ReleaseHdc($dc); $graphics.Dispose()
    $bitmap.Save($Path); $bitmap.Dispose()
    "撮った：$Path"
}
