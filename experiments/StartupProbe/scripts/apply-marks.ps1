# 脇のビルド用の作業ツリー（git worktree add --detach <置き場> <コミット>）に、起動の区切りの印を入れる。
# master と、ブランチに乗っている作業ツリーには入れない（計測だけの物で、アプリには残さない）。
# 入れた後は dotnet build <ツリー>\BoothAssetManager.App -c Release -o <出力> でビルドし、rounds.ps1 -Marks で測る
param([Parameter(Mandatory)][string]$Tree)
$ErrorActionPreference = 'Stop'
$branch = git -C $Tree branch --show-current
if ($branch) { throw "ブランチ（$branch）に乗っている作業ツリーには入れない。git worktree add --detach で脇に作る" }
$app = Join-Path $Tree 'BoothAssetManager.App'
Copy-Item (Join-Path $PSScriptRoot '..\AppPatch\BootMarks.cs'), (Join-Path $PSScriptRoot '..\AppPatch\BootJit.cs') $app
$f = Join-Path $app 'App.xaml.cs'
$t = [IO.File]::ReadAllText($f)
if ($t.Contains('BootMarks')) { return '入れてある' }
# 置き換える所（App.xaml.cs の形が変わったら、ここを合わせる）
$reps = [ordered]@{
  "    static App()`n    {" = "    static App()`n    {`n        BootJit.Begin(); BootMarks.Mark(`"static-App`");"
  "        base.OnStartup(e);`n" = "        BootMarks.BeginUi();`n        BootMarks.Mark(`"OnStartup`");`n        base.OnStartup(e);`n"
  "        ViewModels.AppTheme.Start();`n" = "        ViewModels.AppTheme.Start();`n        BootMarks.Mark(`"色`");`n"
  "        ViewModels.AppTheme.UseStoredMode();`n" = "        ViewModels.AppTheme.UseStoredMode();`n        BootMarks.Mark(`"保存先の確かめ`");`n"
  "        _services = new AppServiceContainer();`n" = "        BootMarks.Mark(`"初回の確かめ`");`n        _services = new AppServiceContainer();`n        BootMarks.Mark(`"サービス一式`");`n"
  "        var main = new MainViewModel(_services);`n        var mainWindow = new MainWindow { DataContext = main };`n" = @"
        BootMarks.Mark("色の初期化");
        var main = new MainViewModel(_services);
        BootMarks.Mark("MainViewModel");
        var mainWindow = new MainWindow { DataContext = main };
        BootMarks.Mark("MainWindow");
        if (BootMarks.On)
        {
            mainWindow.SourceInitialized += (_, _) => BootMarks.Mark("SourceInitialized");
            mainWindow.Loaded += (_, _) => BootMarks.Mark("Loaded");
            mainWindow.ContentRendered += (_, _) =>
            {
                BootMarks.Mark("ContentRendered");
                void Next(object? s, EventArgs a) { System.Windows.Media.CompositionTarget.Rendering -= Next; BootMarks.Mark("見せた"); }
                System.Windows.Media.CompositionTarget.Rendering += Next;
            };
            var counted = false;
            main.Search.PropertyChanged += (_, a) =>
            {
                if (!counted && a.PropertyName == nameof(SearchViewModel.TotalCount)) { counted = true; BootMarks.Mark("件数"); }
            };
        }

"@
  "        mainWindow.Show();`n" = "        BootMarks.Mark(`"窓の支度`");`n        mainWindow.Show();`n        BootMarks.Mark(`"Show`");`n"
  "        Services.UnityFocusWatch.Start();`n    }" = "        Services.UnityFocusWatch.Start();`n        BootMarks.Mark(`"OnStartupの終わり`");`n    }"
}
$nl = if ($t.Contains("`r`n")) { "`r`n" } else { "`n" }
foreach ($k in $reps.Keys) {
  $from = ($k -replace "`r?`n", "`n") -replace "`n", $nl
  $to = ($reps[$k] -replace "`r?`n", "`n") -replace "`n", $nl
  if (-not $t.Contains($from)) { throw "App.xaml.cs に見つからない所がある（形が変わった）: $($k.Trim())" }
  $t = $t.Replace($from, $to)
}
[IO.File]::WriteAllText($f, $t, [Text.UTF8Encoding]::new($true))
"入れた: $f"
