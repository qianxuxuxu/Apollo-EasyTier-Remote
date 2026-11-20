param(
    [string]$OutputDir = "",
    [switch]$SkipTests,
    [switch]$OfflineTests
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [Console]::OutputEncoding

$RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)
$SrcDir   = Join-Path $RepoRoot "src"
$TestsDir = Join-Path $RepoRoot "tests"
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot "bin"
}
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpfDir = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\WPF"
if (-not (Test-Path $csc) -or -not (Test-Path $wpfDir)) {
    throw "未找到 .NET Framework 4.8 csc.exe 或 WPF 程序集: $csc"
}

function Invoke-CscCompile {
    param([string[]]$Arguments, [string]$Label)
    Write-Host "正在编译 $Label ..." -ForegroundColor Cyan
    $p = Start-Process -FilePath $csc -ArgumentList ($Arguments -join " ") -NoNewWindow -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        throw "$Label 编译失败，退出码: $($p.ExitCode)"
    }
}

$uiSources = @(
    "`"$(Join-Path $SrcDir 'Program.cs')`"",
    "`"$(Join-Path $SrcDir 'Models.cs')`"",
    "`"$(Join-Path $SrcDir 'ThemeManager.cs')`"",
    "`"$(Join-Path $SrcDir 'UiControls.cs')`"",
    "`"$(Join-Path $SrcDir 'QrCodeEncoder.cs')`"",
    "`"$(Join-Path $SrcDir 'BackendService.cs')`"",
    "`"$(Join-Path $SrcDir 'MainWindow.cs')`"",
    "`"$(Join-Path $SrcDir 'MainWindow.Pages.cs')`""
)

$wpfRefs = @(
    "/r:`"$(Join-Path $wpfDir 'PresentationFramework.dll')`"",
    "/r:`"$(Join-Path $wpfDir 'PresentationCore.dll')`"",
    "/r:`"$(Join-Path $wpfDir 'WindowsBase.dll')`"",
    "/r:System.Xaml.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Drawing.dll",
    "/r:System.Management.dll",
    "/r:System.ServiceProcess.dll",
    "/r:System.Web.Extensions.dll"
)

$uiOut = Join-Path $OutputDir "RemoteControlUI.exe"
Invoke-CscCompile -Label "RemoteControlUI.exe" -Arguments (@(
    "/nologo", "/optimize+", "/warnaserror+", "/target:winexe", "/platform:anycpu", "/utf8output",
    "/win32icon:`"$(Join-Path $SrcDir 'app.ico')`"",
    "/win32manifest:`"$(Join-Path $SrcDir 'App.manifest')`"",
    "/out:`"$uiOut`""
) + $wpfRefs + $uiSources)

$launcherOut = Join-Path $OutputDir "RemoteControlLauncher.exe"
Invoke-CscCompile -Label "RemoteControlLauncher.exe" -Arguments @(
    "/nologo", "/optimize+", "/warnaserror+", "/target:winexe", "/platform:anycpu", "/utf8output",
    "/win32icon:`"$(Join-Path $SrcDir 'app.ico')`"",
    "/win32manifest:`"$(Join-Path $SrcDir 'Launcher.manifest')`"",
    "/out:`"$launcherOut`"",
    "`"$(Join-Path $SrcDir 'Launcher.cs')`""
)

$harnessOut = Join-Path $OutputDir "RemoteControlUI_TestHarness.exe"
Invoke-CscCompile -Label "RemoteControlUI_TestHarness.exe" -Arguments (@(
    "/nologo", "/optimize+", "/warnaserror+", "/target:winexe", "/platform:anycpu", "/utf8output",
    "/win32icon:`"$(Join-Path $SrcDir 'app.ico')`"",
    "/win32manifest:`"$(Join-Path $SrcDir 'Launcher.manifest')`"",
    "/out:`"$harnessOut`""
) + $wpfRefs + $uiSources)

if (-not $SkipTests) {
    $unitOut = Join-Path $OutputDir "UnitTests.exe"
    Invoke-CscCompile -Label "UnitTests.exe" -Arguments @(
        "/nologo", "/optimize+", "/warnaserror+", "/target:exe", "/platform:anycpu", "/utf8output",
        "/out:`"$unitOut`"",
        "/r:`"$(Join-Path $wpfDir 'PresentationCore.dll')`"",
        "/r:`"$(Join-Path $wpfDir 'WindowsBase.dll')`"",
        "/r:System.Xaml.dll",
        "/r:System.Management.dll",
        "/r:System.ServiceProcess.dll",
        "/r:System.Web.Extensions.dll",
        "`"$(Join-Path $TestsDir 'UnitTests.cs')`"",
        "`"$(Join-Path $SrcDir 'Models.cs')`"",
        "`"$(Join-Path $SrcDir 'QrCodeEncoder.cs')`"",
        "`"$(Join-Path $SrcDir 'BackendService.cs')`""
    )
    Write-Host "正在运行单元测试..." -ForegroundColor Yellow
    if ($OfflineTests) {
        & $unitOut --offline
    } else {
        & $unitOut
    }
    if ($LASTEXITCODE -ne 0) {
        throw "单元测试失败，退出码: $LASTEXITCODE"
    }
}

Write-Host "全部编译与测试完成！输出目录: $OutputDir" -ForegroundColor Green