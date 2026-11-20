param(
    [string]$AutoOption = ""
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

$ScriptRoot       = Split-Path -Parent $MyInvocation.MyCommand.Definition
$PackageRoot      = Split-Path -Parent $ScriptRoot
$DownloadDir      = Join-Path $PackageRoot "Downloaded_Files"
$TabletApkDir     = Join-Path $PackageRoot "平板APK安装包"
$BundledUiExe     = Join-Path $PackageRoot "RemoteControlUI.exe"
$BundledLauncher  = Join-Path $PackageRoot "RemoteControlLauncher.exe"
$ProjectRepo      = "qianxuxuxu/Apollo-EasyTier-Remote"

function Test-IsAdmin {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Prompt-GitHubProxy {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host " [GitHub 下载加速配置]" -ForegroundColor Cyan
    Write-Host " 如果您的网络直连 GitHub 较慢，可输入加速代理前缀" -ForegroundColor Gray
    Write-Host " 例如: https://ghfast.top/  (直接按回车键则使用 GitHub 官方直连)" -ForegroundColor Gray
    Write-Host "================================================================" -ForegroundColor Cyan
    $prefix = Read-Host "请输入 GitHub 代理前缀 (默认直接回车直连)"
    $prefix = $prefix.Trim()
    if (-not [string]::IsNullOrWhiteSpace($prefix)) {
        if (-not $prefix.EndsWith("/")) {
            $prefix = $prefix + "/"
        }
        Write-Host "[已启用代理前缀] $prefix" -ForegroundColor Green
    } else {
        Write-Host "[已启用官方直连] https://github.com/" -ForegroundColor Green
    }
    return $prefix
}

function Get-LatestGitHubRelease {
    param(
        [Parameter(Mandatory = $true)][string]$Repo
    )
    $apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
    $headers = @{
        "User-Agent" = "RemoteControl-DeployToolkit/3.0"
        "Accept"     = "application/vnd.github+json"
    }
    $maxRetries = 3
    for ($i = 1; $i -le $maxRetries; $i++) {
        try {
            Write-Host "正在查询最新版本: $Repo (第 $i/$maxRetries 次)..." -ForegroundColor Cyan
            $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -TimeoutSec 20
            if ($null -ne $release -and -not $release.prerelease -and -not $release.draft) {
                Write-Host "  -> 发现最新正式版: $($release.tag_name)" -ForegroundColor Green
                return $release
            }
            $allReleases = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases?per_page=10" -Headers $headers -TimeoutSec 20
            $stable = $allReleases | Where-Object { -not $_.prerelease -and -not $_.draft } | Select-Object -First 1
            if ($null -ne $stable) {
                Write-Host "  -> 发现最新正式版: $($stable.tag_name)" -ForegroundColor Green
                return $stable
            }
            throw "未找到 $Repo 的正式版发布。"
        } catch {
            Write-Host "  [警告] 查询 $Repo 失败: $($_.Exception.Message)" -ForegroundColor Yellow
            if ($i -eq $maxRetries) {
                throw "无法查询 GitHub Release ($Repo)，请检查网络连接。"
            }
            Start-Sleep -Seconds 2
        }
    }
}

function Invoke-FileDownloadWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [string]$ProxyPrefix = ""
    )
    $targetDir = Split-Path -Parent $DestinationPath
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    }

    $candidateUrls = @()
    if (-not [string]::IsNullOrWhiteSpace($ProxyPrefix)) {
        $candidateUrls += ($ProxyPrefix + $Url)
    }
    $candidateUrls += $Url

    $maxRetries = 3
    foreach ($candidate in $candidateUrls) {
        for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
            try {
                Write-Host "正在下载: $(Split-Path -Leaf $DestinationPath) [尝试 $attempt/$maxRetries]" -ForegroundColor Cyan
                Write-Host "  下载源: $candidate" -ForegroundColor DarkGray
                $curlCmd = Get-Command "curl.exe" -ErrorAction SilentlyContinue
                if ($null -ne $curlCmd) {
                    & curl.exe -L --fail --connect-timeout 20 --max-time 600 --retry 2 -o $DestinationPath $candidate
                    if ($LASTEXITCODE -ne 0) {
                        throw "curl.exe 退出码: $LASTEXITCODE"
                    }
                } else {
                    Invoke-WebRequest -Uri $candidate -OutFile $DestinationPath -TimeoutSec 300 -UseBasicParsing
                }

                if ((Test-Path $DestinationPath) -and ((Get-Item $DestinationPath).Length -gt 102400)) {
                    $sizeMB = [Math]::Round((Get-Item $DestinationPath).Length / 1MB, 2)
                    Write-Host "  -> 下载完成! 大小: ${sizeMB} MB" -ForegroundColor Green
                    return $true
                } else {
                    throw "下载后的文件不存在或文件大小异常。"
                }
            } catch {
                Write-Host "  [下载失败] $($_.Exception.Message)" -ForegroundColor Yellow
                if (Test-Path $DestinationPath) {
                    Remove-Item -Force $DestinationPath -ErrorAction SilentlyContinue
                }
                Start-Sleep -Seconds 2
            }
        }
        if ($candidateUrls.Count -gt 1 -and $candidate -eq $candidateUrls[0]) {
            Write-Host "代理下载失败，正在自动切换至 GitHub 官方源重试..." -ForegroundColor Yellow
        }
    }
    throw "文件下载失败: $(Split-Path -Leaf $DestinationPath)"
}

function Download-AllLatestPackages {
    param(
        [string]$ProxyPrefix = "",
        [switch]$WindowsOnly
    )
    if (-not (Test-Path $DownloadDir)) {
        New-Item -ItemType Directory -Force -Path $DownloadDir | Out-Null
    }

    # 1. Apollo Windows EXE
    $apolloRelease = Get-LatestGitHubRelease -Repo "ClassicOldSong/Apollo"
    $apolloAsset = $apolloRelease.assets | Where-Object { $_.name -match '\.exe$' } | Select-Object -First 1
    if ($null -eq $apolloAsset) {
        throw "未在 ClassicOldSong/Apollo 最新版中找到 Windows .exe 安装包。"
    }
    $apolloDest = Join-Path $DownloadDir $apolloAsset.name
    Invoke-FileDownloadWithRetry -Url $apolloAsset.browser_download_url -DestinationPath $apolloDest -ProxyPrefix $ProxyPrefix | Out-Null

    # 2. EasyTier Windows x86_64 ZIP & Android ARM64 APK
    $easytierRelease = Get-LatestGitHubRelease -Repo "EasyTier/EasyTier"
    $etWinAsset = $easytierRelease.assets | Where-Object { $_.name -match 'easytier-windows-x86_64-.*\.zip$' } | Select-Object -First 1
    if ($null -eq $etWinAsset) {
        throw "未在 EasyTier/EasyTier 最新版中找到 Windows x86_64 .zip 安装包。"
    }
    $etWinDest = Join-Path $DownloadDir $etWinAsset.name
    Invoke-FileDownloadWithRetry -Url $etWinAsset.browser_download_url -DestinationPath $etWinDest -ProxyPrefix $ProxyPrefix | Out-Null

    $etApkDest = $null
    $artemisApkDest = $null

    if (-not $WindowsOnly) {
        $etApkAsset = $easytierRelease.assets | Where-Object { $_.name -match '(app-arm64|easytier-android-arm64).*\.apk$' } | Select-Object -First 1
        if ($null -eq $etApkAsset) {
            $etApkAsset = $easytierRelease.assets | Where-Object { $_.name -match '\.apk$' } | Select-Object -First 1
        }
        if ($null -eq $etApkAsset) {
            throw "未在 EasyTier/EasyTier 最新版中找到 Android ARM64 .apk 安装包。"
        }
        $etApkDest = Join-Path $DownloadDir $etApkAsset.name
        Invoke-FileDownloadWithRetry -Url $etApkAsset.browser_download_url -DestinationPath $etApkDest -ProxyPrefix $ProxyPrefix | Out-Null

        # 3. Artemis (moonlight-android) ARM64 APK
        $artemisRelease = Get-LatestGitHubRelease -Repo "ClassicOldSong/moonlight-android"
        $artemisAsset = $artemisRelease.assets | Where-Object { $_.name -match 'arm64.*\.apk$' } | Select-Object -First 1
        if ($null -eq $artemisAsset) {
            $artemisAsset = $artemisRelease.assets | Where-Object { $_.name -match 'release.*\.apk$' -or $_.name -match '\.apk$' } | Select-Object -First 1
        }
        if ($null -eq $artemisAsset) {
            throw "未在 ClassicOldSong/moonlight-android 最新版中找到 Artemis .apk 安装包。"
        }
        $artemisApkDest = Join-Path $DownloadDir $artemisAsset.name
        Invoke-FileDownloadWithRetry -Url $artemisAsset.browser_download_url -DestinationPath $artemisApkDest -ProxyPrefix $ProxyPrefix | Out-Null

        if (-not (Test-Path $TabletApkDir)) {
            New-Item -ItemType Directory -Force -Path $TabletApkDir | Out-Null
        }
        Copy-Item -Force $etApkDest (Join-Path $TabletApkDir $etApkAsset.name)
        Copy-Item -Force $artemisApkDest (Join-Path $TabletApkDir $artemisAsset.name)
    }

    return @{
        ApolloExe      = $apolloDest
        EasyTierWinZip = $etWinDest
        EasyTierApk    = $etApkDest
        ArtemisApk     = $artemisApkDest
    }
}

function New-RandomString {
    param(
        [int]$Length,
        [string]$CharSet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    )
    $n = $CharSet.Length
    $limit = 256 - (256 % $n)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $sb = New-Object System.Text.StringBuilder
    $buf = New-Object byte[] ([Math]::Max(32, $Length * 2))
    try {
        while ($sb.Length -lt $Length) {
            $rng.GetBytes($buf)
            for ($i = 0; $i -lt $buf.Length -and $sb.Length -lt $Length; $i++) {
                if ($buf[$i] -lt $limit) {
                    [void]$sb.Append($CharSet[$buf[$i] % $n])
                }
            }
        }
    } finally {
        $rng.Dispose()
    }
    return $sb.ToString()
}

function Get-DefaultInstallDir {
    try {
        $dDrive = [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.Name -eq "D:\" -and $_.DriveType -eq "Fixed" -and $_.IsReady } | Select-Object -First 1
        if ($null -ne $dDrive) { return "D:\远程控制" }
    } catch { }
    return "C:\远程控制"
}

function Write-Utf8BomFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8Bom)
}

function Write-Utf8NoBomFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Write-PureAsciiBatFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $asciiBytes = [System.Text.Encoding]::ASCII.GetBytes($Content)
    [System.IO.File]::WriteAllBytes($Path, $asciiBytes)
}

function Test-ApolloInstalled {
    $portListen = Get-NetTCPConnection -LocalPort 47990 -State Listen -ErrorAction SilentlyContinue
    if ($portListen) { return $true }
    $svcs = Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "apollo|sunshine" -or $_.DisplayName -match "apollo|sunshine" }
    if ($svcs) { return $true }
    foreach ($p in @(
        "D:\Apollo\sunshine.exe",
        "D:\Apollo\apollo.exe",
        "C:\Program Files\Apollo\sunshine.exe",
        "C:\Program Files\Apollo\apollo.exe",
        "C:\Program Files\Sunshine\sunshine.exe",
        "C:\Program Files (x86)\Apollo\sunshine.exe"
    )) {
        if (Test-Path $p) { return $true }
    }
    foreach ($regRoot in @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )) {
        $match = Get-ItemProperty $regRoot -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match "Apollo|Sunshine" } | Select-Object -First 1
        if ($null -ne $match) { return $true }
    }
    return $false
}

function Ensure-UiBinaries {
    param(
        [string]$ProxyPrefix = ""
    )
    if ((Test-Path $BundledUiExe) -and (Test-Path $BundledLauncher)) {
        return $true
    }

    $srcDir = Join-Path $PackageRoot "src"
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $wpfDir = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\WPF"
    $buildOutDir = Join-Path $PackageRoot "bin"

    # 1. GitHub 源码仓库模式: 直接用 Windows 内置 .NET 4.8 csc.exe 本地编译，无需下载 VS
    if ((Test-Path (Join-Path $srcDir "Program.cs")) -and (Test-Path $csc) -and (Test-Path $wpfDir)) {
        try {
            Write-Host "  [源码模式] 未检测到预编译控制台，正在使用系统内置 .NET 4.8 自动编译..." -ForegroundColor Cyan
            if (-not (Test-Path $buildOutDir)) { New-Item -ItemType Directory -Force -Path $buildOutDir | Out-Null }

            $uiOut = Join-Path $buildOutDir "RemoteControlUI.exe"
            $launcherOut = Join-Path $buildOutDir "RemoteControlLauncher.exe"

            $uiArgs = @(
                "/nologo", "/optimize+", "/target:winexe", "/platform:anycpu", "/utf8output",
                "/win32icon:`"$(Join-Path $srcDir 'app.ico')`"",
                "/win32manifest:`"$(Join-Path $srcDir 'App.manifest')`"",
                "/out:`"$uiOut`"",
                "/r:`"$(Join-Path $wpfDir 'PresentationFramework.dll')`"",
                "/r:`"$(Join-Path $wpfDir 'PresentationCore.dll')`"",
                "/r:`"$(Join-Path $wpfDir 'WindowsBase.dll')`"",
                "/r:System.Xaml.dll", "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll",
                "/r:System.Management.dll", "/r:System.ServiceProcess.dll", "/r:System.Web.Extensions.dll",
                "`"$(Join-Path $srcDir 'Program.cs')`"", "`"$(Join-Path $srcDir 'Models.cs')`"",
                "`"$(Join-Path $srcDir 'ThemeManager.cs')`"", "`"$(Join-Path $srcDir 'UiControls.cs')`"",
                "`"$(Join-Path $srcDir 'QrCodeEncoder.cs')`"", "`"$(Join-Path $srcDir 'BackendService.cs')`"",
                "`"$(Join-Path $srcDir 'MainWindow.cs')`"", "`"$(Join-Path $srcDir 'MainWindow.Pages.cs')`""
            )
            $p1 = Start-Process -FilePath $csc -ArgumentList ($uiArgs -join " ") -NoNewWindow -Wait -PassThru
            if ($p1.ExitCode -ne 0 -or -not (Test-Path $uiOut)) { throw "csc 编译 RemoteControlUI.exe 失败 ($($p1.ExitCode))" }

            $lArgs = @(
                "/nologo", "/optimize+", "/target:winexe", "/platform:anycpu", "/utf8output",
                "/win32icon:`"$(Join-Path $srcDir 'app.ico')`"",
                "/win32manifest:`"$(Join-Path $srcDir 'Launcher.manifest')`"",
                "/out:`"$launcherOut`"",
                "`"$(Join-Path $srcDir 'Launcher.cs')`""
            )
            $p2 = Start-Process -FilePath $csc -ArgumentList ($lArgs -join " ") -NoNewWindow -Wait -PassThru
            if ($p2.ExitCode -ne 0 -or -not (Test-Path $launcherOut)) { throw "csc 编译 RemoteControlLauncher.exe 失败 ($($p2.ExitCode))" }

            $script:BundledUiExe = $uiOut
            $script:BundledLauncher = $launcherOut
            Write-Host "  -> 已从源码成功编译 RemoteControlUI.exe 与 RemoteControlLauncher.exe！" -ForegroundColor Green
            return $true
        } catch {
            Write-Host "  [提示] 本地编译未成功 ($($_.Exception.Message))，尝试从 GitHub Releases 下载预编译包..." -ForegroundColor Yellow
        }
    }

    # 2. 兜底: 从本项目 GitHub Releases 下载预编译版
    try {
        if (-not (Test-Path $buildOutDir)) { New-Item -ItemType Directory -Force -Path $buildOutDir | Out-Null }
        $rel = Get-LatestGitHubRelease -Repo $ProjectRepo
        $uiAsset = $rel.assets | Where-Object { $_.name -eq "RemoteControlUI.exe" } | Select-Object -First 1
        $lAsset  = $rel.assets | Where-Object { $_.name -eq "RemoteControlLauncher.exe" } | Select-Object -First 1
        if ($null -ne $uiAsset) {
            $uiOut = Join-Path $buildOutDir "RemoteControlUI.exe"
            Invoke-FileDownloadWithRetry -Url $uiAsset.browser_download_url -DestinationPath $uiOut -ProxyPrefix $ProxyPrefix | Out-Null
            $script:BundledUiExe = $uiOut
            if ($null -ne $lAsset) {
                $lOut = Join-Path $buildOutDir "RemoteControlLauncher.exe"
                Invoke-FileDownloadWithRetry -Url $lAsset.browser_download_url -DestinationPath $lOut -ProxyPrefix $ProxyPrefix | Out-Null
                $script:BundledLauncher = $lOut
            }
            return $true
        }
    } catch {
        Write-Host "  [提示] 未能下载预编译控制台: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    return $false
}

function Register-UiScheduledTasksAndShortcuts {
    param(
        [Parameter(Mandatory = $true)][string]$InstallDir,
        [string]$ProxyPrefix = ""
    )
    [void](Ensure-UiBinaries -ProxyPrefix $ProxyPrefix)
    $uiDestExe = Join-Path $InstallDir "RemoteControlUI.exe"
    $launcherDestExe = Join-Path $InstallDir "RemoteControlLauncher.exe"
    if (Test-Path $BundledUiExe) {
        Get-Process -Name "RemoteControlUI","RemoteControlLauncher" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        Copy-Item -Path $BundledUiExe -Destination $uiDestExe -Force
        Write-Host "  -> 桌面控制台已同步至: $uiDestExe" -ForegroundColor Green
        if (Test-Path $BundledLauncher) {
            Copy-Item -Path $BundledLauncher -Destination $launcherDestExe -Force
            Write-Host "  -> 快捷启动器已同步至: $launcherDestExe" -ForegroundColor Green
        }
    } else {
        Write-Host "  [提示] 未找到控制台二进制文件且编译/下载不可用，跳过控制台复制。" -ForegroundColor Yellow
        return $false
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $userAccount = $identity.Name
    $userSid = $identity.User.Value

    # 1. 开机自启计划任务 (登录时静默最小化到系统托盘，RunLevel Highest，零 UAC 弹窗)
    $tAuto = "RemoteControlUI-Autostart"
    Unregister-ScheduledTask -TaskName $tAuto -Confirm:$false -ErrorAction SilentlyContinue
    $aAuto  = New-ScheduledTaskAction -Execute $uiDestExe -Argument "--minimized" -WorkingDirectory $InstallDir
    $trAuto = New-ScheduledTaskTrigger -AtLogOn -User $userAccount
    $prAuto = New-ScheduledTaskPrincipal -UserId $userAccount -LogonType Interactive -RunLevel Highest
    $stAuto = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $tAuto -Action $aAuto -Trigger $trAuto -Principal $prAuto -Settings $stAuto -Force | Out-Null

    # 2. 双击免 UAC 管理员快捷启动任务
    $tElev = "RemoteControlUI-ElevatedLaunch"
    Unregister-ScheduledTask -TaskName $tElev -Confirm:$false -ErrorAction SilentlyContinue
    $aElev  = New-ScheduledTaskAction -Execute $uiDestExe -WorkingDirectory $InstallDir
    $prElev = New-ScheduledTaskPrincipal -UserId $userAccount -LogonType Interactive -RunLevel Highest
    $stElev = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances Parallel
    Register-ScheduledTask -TaskName $tElev -Action $aElev -Principal $prElev -Settings $stElev -Force | Out-Null

    # 授予当前用户免 UAC 触发权限 (SetSecurityDescriptor flags = 0, ACE = FRFX)
    try {
        $svc = New-Object -ComObject Schedule.Service
        $svc.Connect()
        $folder = $svc.GetFolder("\")
        foreach ($tn in @($tAuto, $tElev)) {
            $rt = $folder.GetTask($tn)
            $sddl = $rt.GetSecurityDescriptor(4)
            if ($sddl -notmatch ("\(A;;[^;]*(?:GX|FX|FA|GA|0x1200a9|0x1f01ff)[^;]*;;;" + [regex]::Escape($userSid) + "\)")) {
                $ace = "(A;;FRFX;;;$userSid)"
                $newSddl = if ($sddl -match "S:") { $sddl -replace "S:", ($ace + "S:") } else { $sddl + $ace }
                $rt.SetSecurityDescriptor($newSddl, 0)
            }
            $taskFile = Join-Path "C:\Windows\System32\Tasks" $tn
            if (Test-Path $taskFile) {
                & icacls.exe $taskFile /grant "*${userSid}:(RX)" /C | Out-Null
            }
        }
        Write-Host "  -> 开机静默托盘与免弹窗启动任务已注册完成。" -ForegroundColor Green
    } catch {
        Write-Host "  [提示] 计划任务 SDDL 附加授权未完全应用，桌面启动器将自动兜底直启。" -ForegroundColor Yellow
    }

    # 3. 桌面仅生成 1 个干净快捷方式: 远程控制中心.lnk
    try {
        $wsh = New-Object -ComObject WScript.Shell
        $desktop = [Environment]::GetFolderPath('Desktop')

        $lnkUi = $wsh.CreateShortcut((Join-Path $desktop "远程控制中心.lnk"))
        if (Test-Path $launcherDestExe) {
            $lnkUi.TargetPath = $launcherDestExe
        } else {
            $lnkUi.TargetPath = $uiDestExe
        }
        $lnkUi.Arguments = ""
        $lnkUi.WindowStyle = 1
        $lnkUi.WorkingDirectory = $InstallDir
        $lnkUi.IconLocation = "$uiDestExe,0"
        $lnkUi.Description = "远程控制中心"
        $lnkUi.Save()
        Write-Host "  -> 已在桌面生成快捷方式: 远程控制中心.lnk" -ForegroundColor Green
    } catch {
        Write-Host "  [警告] 创建桌面快捷方式失败: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    return $true
}

function New-ManagementScripts {
    param(
        [Parameter(Mandatory = $true)][string]$InstallDir
    )
    $mgmtDir = Join-Path $InstallDir "管理工具"
    if (-not (Test-Path $mgmtDir)) {
        New-Item -ItemType Directory -Force -Path $mgmtDir | Out-Null
    }

    # 0. 打开远程控制UI.bat (纯 ASCII)
    $bat0 = "@echo off`r`ncd /d ""%~dp0..""`r`nif exist ""%~dp0..\RemoteControlLauncher.exe"" (`r`n  start """" ""%~dp0..\RemoteControlLauncher.exe""`r`n) else (`r`n  start """" ""%~dp0..\RemoteControlUI.exe""`r`n)`r`n"
    Write-PureAsciiBatFile -Path (Join-Path $mgmtDir "打开远程控制UI.bat") -Content $bat0

    # 1. 查看P2P直连状态.bat + check_p2p.ps1
    $bat1 = "@echo off`r`ncd /d ""%~dp0""`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File ""%~dp0check_p2p.ps1""`r`n"
    Write-PureAsciiBatFile -Path (Join-Path $mgmtDir "查看P2P直连状态.bat") -Content $bat1

    $ps1CheckLines = @(
        '$ErrorActionPreference = "Continue"',
        '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8',
        '$BaseDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)',
        '$CliPath = Join-Path $BaseDir "EasyTier\easytier-cli.exe"',
        'Write-Host "================================================================" -ForegroundColor Cyan',
        'Write-Host "                EasyTier 虚拟组网与 P2P 状态检查                " -ForegroundColor Cyan',
        'Write-Host "================================================================" -ForegroundColor Cyan',
        'if (-not (Test-Path $CliPath)) {',
        '    Write-Host "[错误] 未找到 $CliPath" -ForegroundColor Red',
        '    Read-Host "按回车键退出"',
        '    exit 1',
        '}',
        'Write-Host "`n[1] 本机节点状态 (node):" -ForegroundColor Yellow',
        '& $CliPath node',
        'Write-Host "`n[2] 对端连接状态 (peer):" -ForegroundColor Yellow',
        '& $CliPath peer',
        'Write-Host "`n----------------------------------------------------------------" -ForegroundColor Gray',
        'try {',
        '    $jsonRaw = (& $CliPath -o json peer 2>$null) -join "`n"',
        '    $peers = if ($jsonRaw.Trim().StartsWith("[")) { $jsonRaw | ConvertFrom-Json } else { @() }',
        '    $remoteDevices = @($peers | Where-Object { $_.cost -ne "Local" -and -not [string]::IsNullOrWhiteSpace($_.ipv4) -and $_.ipv4 -ne "-" -and ($_.hostname -notmatch "^PublicServer") })',
        '    if ($remoteDevices.Count -eq 0) {',
        '        Write-Host "[状态说明] 本机已接入公共节点，暂无在线平板/远程设备。" -ForegroundColor Cyan',
        '    } elseif ($remoteDevices | Where-Object { $_.cost -match "p2p|direct|^1$" }) {',
        '        Write-Host "[状态说明] 已与远程设备建立 P2P 直连 (p2p)，延迟最低、画质最佳。" -ForegroundColor Green',
        '    } else {',
        '        Write-Host "[状态说明] 远程设备当前通过公共节点中继连接 (relay)。" -ForegroundColor Yellow',
        '    }',
        '} catch {',
        '    Write-Host "[状态说明] 表格中非 PublicServer 项显示 p2p 即为直连，relay 为中继转发。" -ForegroundColor Cyan',
        '}',
        'Write-Host "----------------------------------------------------------------" -ForegroundColor Gray',
        'Read-Host "`n按回车键关闭窗口"'
    )
    Write-Utf8BomFile -Path (Join-Path $mgmtDir "check_p2p.ps1") -Content ($ps1CheckLines -join "`r`n")

    # 2. 重启EasyTier服务.bat + restart_easytier.ps1
    $bat2 = "@echo off`r`ncd /d ""%~dp0""`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File ""%~dp0restart_easytier.ps1""`r`n"
    Write-PureAsciiBatFile -Path (Join-Path $mgmtDir "重启EasyTier服务.bat") -Content $bat2

    $ps1RestartLines = @(
        '$ErrorActionPreference = "Continue"',
        '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8',
        '$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())',
        'if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {',
        '    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""',
        '    exit 0',
        '}',
        '$BaseDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)',
        '$CorePath = Join-Path $BaseDir "EasyTier\easytier-core.exe"',
        '$ConfigPath = Join-Path $BaseDir "EasyTier\config.toml"',
        'Write-Host "正在停止现有的 easytier-core 进程..." -ForegroundColor Yellow',
        '$task = Get-ScheduledTask -TaskName "EasyTier-NoTun-Autostart" -ErrorAction SilentlyContinue',
        'if ($null -ne $task) {',
        '    Stop-ScheduledTask -TaskName "EasyTier-NoTun-Autostart" -ErrorAction SilentlyContinue',
        '    Start-Sleep -Milliseconds 500',
        '}',
        'Get-Process -Name "easytier-core" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue',
        'Start-Sleep -Seconds 1',
        'if ($null -ne $task) {',
        '    Write-Host "正在通过计划任务启动 EasyTier-NoTun-Autostart..." -ForegroundColor Cyan',
        '    Start-ScheduledTask -TaskName "EasyTier-NoTun-Autostart"',
        '} else {',
        '    Write-Host "正在直接后台启动 easytier-core.exe (no_tun = true)..." -ForegroundColor Cyan',
        '    Start-Process -FilePath $CorePath -ArgumentList "-c `"$ConfigPath`" --console-log-level warn" -WorkingDirectory (Split-Path -Parent $CorePath) -WindowStyle Hidden',
        '}',
        'Start-Sleep -Seconds 3',
        '$proc = Get-Process -Name "easytier-core" -ErrorAction SilentlyContinue',
        'if ($null -ne $proc) {',
        '    Write-Host "[成功] EasyTier 服务已重新启动 (PID: $($proc.Id -join '', ''))" -ForegroundColor Green',
        '} else {',
        '    Write-Host "[警告] 未检测到 easytier-core 进程，请检查配置文件 $ConfigPath" -ForegroundColor Red',
        '}',
        'Read-Host "`n按回车键关闭窗口"'
    )
    Write-Utf8BomFile -Path (Join-Path $mgmtDir "restart_easytier.ps1") -Content ($ps1RestartLines -join "`r`n")

    # 3. 打开Apollo管理后台.bat + open_apollo.ps1
    $bat3 = "@echo off`r`ncd /d ""%~dp0""`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File ""%~dp0open_apollo.ps1""`r`n"
    Write-PureAsciiBatFile -Path (Join-Path $mgmtDir "打开Apollo管理后台.bat") -Content $bat3

    $ps1ApolloLines = @(
        '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8',
        'Write-Host "正在打开 Apollo Web 管理后台: https://localhost:47990 ..." -ForegroundColor Cyan',
        'Write-Host "提示 1: 首次打开若浏览器提示证书风险，请点击【高级】->【继续前往 localhost】。" -ForegroundColor Yellow',
        'Write-Host "提示 2: 平板 Artemis 4 位数字配对时，请在 PIN 页面点击切换到【PIN Pairing】标签。" -ForegroundColor Yellow',
        'Start-Process "https://localhost:47990"',
        'Start-Sleep -Seconds 2'
    )
    Write-Utf8BomFile -Path (Join-Path $mgmtDir "open_apollo.ps1") -Content ($ps1ApolloLines -join "`r`n")
}

function Invoke-FullDeployment {
    if (-not (Test-IsAdmin)) {
        Write-Host "`n[权限提升] 正在请求管理员权限以完成防火墙、电源与开机自启配置..." -ForegroundColor Yellow
        Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -AutoOption 2"
        exit 0
    }

    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "      [选项 2] 一键全自动部署远程控制 (Apollo + EasyTier)       " -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan

    $defaultInstallDir = Get-DefaultInstallDir
    $inputDir = Read-Host "请输入安装目标路径 (直接按回车默认安装到 $defaultInstallDir)"
    $InstallDir = if ([string]::IsNullOrWhiteSpace($inputDir)) { $defaultInstallDir } else { $inputDir.Trim() }

    Write-Host "[目标安装路径] $InstallDir" -ForegroundColor Green

    $EasyTierDir = Join-Path $InstallDir "EasyTier"
    $ApolloDir   = Join-Path $InstallDir "Apollo"
    $MgmtDir     = Join-Path $InstallDir "管理工具"

    New-Item -ItemType Directory -Force -Path $EasyTierDir | Out-Null
    New-Item -ItemType Directory -Force -Path $ApolloDir | Out-Null
    New-Item -ItemType Directory -Force -Path $MgmtDir | Out-Null

    # 授予当前用户目标目录完全控制权
    $userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls.exe $InstallDir /grant "*${userSid}:(OI)(CI)F" /grant "*S-1-5-32-544:(OI)(CI)F" /grant "*S-1-5-18:(OI)(CI)F" /T /C | Out-Null

    # 检查 Downloaded_Files 是否已有安装包
    $existingApollo = $null
    $existingEtZip  = $null
    if (Test-Path $DownloadDir) {
        $existingApollo = Get-ChildItem -Path $DownloadDir -Filter "*Apollo*.exe" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        $existingEtZip  = Get-ChildItem -Path $DownloadDir -Filter "easytier-windows-x86_64-*.zip" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    }

    $apolloExePath = $null
    $etZipPath     = $null
    $proxyPrefix   = ""

    if ($null -ne $existingApollo -and $existingApollo.Length -gt 102400 -and $null -ne $existingEtZip -and $existingEtZip.Length -gt 102400) {
        Write-Host "[发现本地缓存安装包] 直接复用 Downloaded_Files 中的安装包:" -ForegroundColor Green
        Write-Host "  - Apollo  : $($existingApollo.FullName)" -ForegroundColor Gray
        Write-Host "  - EasyTier: $($existingEtZip.FullName)" -ForegroundColor Gray
        $apolloExePath = $existingApollo.FullName
        $etZipPath     = $existingEtZip.FullName
    } else {
        $proxyPrefix = Prompt-GitHubProxy
        $downloaded = Download-AllLatestPackages -ProxyPrefix $proxyPrefix -WindowsOnly
        $apolloExePath = $downloaded.ApolloExe
        $etZipPath     = $downloaded.EasyTierWinZip
    }

    # 1. 解压并部署 EasyTier（跳过未使用的 easytier-web*.exe 节省空间）
    Write-Host "`n[步骤 1/8] 正在解压并部署 EasyTier..." -ForegroundColor Cyan
    Stop-ScheduledTask -TaskName "EasyTier-NoTun-Autostart" -ErrorAction SilentlyContinue
    Get-Process -Name "easytier-core" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    $tempExtract = Join-Path $EasyTierDir "_extract_tmp"
    if (Test-Path $tempExtract) { Remove-Item -Recurse -Force $tempExtract }
    Expand-Archive -Path $etZipPath -DestinationPath $tempExtract -Force
    Get-ChildItem -Path $tempExtract -Recurse -File | Where-Object { $_.Name -notmatch '^easytier-web' } | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $EasyTierDir $_.Name) -Force
    }
    Remove-Item -Recurse -Force $tempExtract -ErrorAction SilentlyContinue

    $coreExe = Join-Path $EasyTierDir "easytier-core.exe"
    if (-not (Test-Path $coreExe)) {
        throw "解压失败: 未找到 $coreExe"
    }

    # 2. 读取或本地随机生成个人专属 network_name 与 network_secret
    Write-Host "[步骤 2/8] 正在准备本机专属组网凭证..." -ForegroundColor Cyan
    $configPath = Join-Path $EasyTierDir "config.toml"
    $networkName = ""
    $networkSecret = ""
    if (Test-Path $configPath) {
        $existingRaw = Get-Content -Path $configPath -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        $mName = [regex]::Match($existingRaw, 'network_name\s*=\s*"([^"]+)"')
        $mSec  = [regex]::Match($existingRaw, 'network_secret\s*=\s*"([^"]+)"')
        if ($mName.Success -and $mSec.Success) {
            $regenChoice = (Read-Host "检测到已有 config.toml ($($mName.Groups[1].Value))，是否重新生成随机密钥？[y/N，默认回车保留原密钥]").Trim()
            if ($regenChoice -notmatch '^(y|yes)$') {
                $networkName = $mName.Groups[1].Value
                $networkSecret = $mSec.Groups[1].Value
                Write-Host "  -> 保留原有组网凭证，并同步最新可用节点与 no_tun = true。" -ForegroundColor Green
            }
        }
        Copy-Item -Path $configPath -Destination ($configPath + ".bak") -Force -ErrorAction SilentlyContinue
    }
    if ([string]::IsNullOrWhiteSpace($networkName) -or [string]::IsNullOrWhiteSpace($networkSecret)) {
        $networkName   = "rc-" + (New-RandomString -Length 8 -CharSet "abcdefghijklmnopqrstuvwxyz0123456789")
        $networkSecret = New-RandomString -Length 20 -CharSet "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    }
    $configToml = @"
ipv4 = "10.126.126.1/24"
dhcp = false
listeners = ["tcp://0.0.0.0:11010", "udp://0.0.0.0:11010"]

[network_identity]
network_name = "$networkName"
network_secret = "$networkSecret"

[[peer]]
uri = "tcp://225284.xyz:11010"

[[peer]]
uri = "tcp://easytier.weiai.org.cn:11010"

[[peer]]
uri = "tcp://183.230.36.171:11010"

[flags]
no_tun = true
enable_encryption = true
enable_ipv6 = true
"@
    Write-Utf8NoBomFile -Path $configPath -Content $configToml
    Write-Host "  -> 配置文件已写入: $configPath (no_tun = true)" -ForegroundColor Green

    # 3. 备份 Apollo 安装包（采用安全交互向导，绝不使用 /S 静默安装以免触发 ViGEmBus PnP 锁冲突代理 Wintun）
    Write-Host "`n[步骤 3/8] 正在准备 Apollo 串流服务端安装包..." -ForegroundColor Cyan
    $apolloDestExe = Join-Path $ApolloDir (Split-Path -Leaf $apolloExePath)
    Copy-Item -Path $apolloExePath -Destination $apolloDestExe -Force
    $apolloAlreadyReady = Test-ApolloInstalled
    if ($apolloAlreadyReady) {
        Write-Host "  -> 检测到本机已安装 Apollo 服务端，跳过重复安装。" -ForegroundColor Green
    } else {
        Write-Host "  -> 已保存至 $apolloDestExe ，稍后将打开正常安装向导。" -ForegroundColor Gray
    }

    # 4. 配置 Windows 防火墙规则
    Write-Host "`n[步骤 4/8] 正在配置 Windows 防火墙入站放行规则..." -ForegroundColor Cyan
    $ruleNames = @(
        "Apollo-Stream-TCP-In", "Apollo-Stream-UDP-In", "Apollo-TCP-In", "Apollo-UDP-In",
        "EasyTier-P2P-Program-In", "EasyTier-P2P-TCP-In", "EasyTier-P2P-UDP-In",
        "EasyTier-Core-Program", "EasyTier-TCP-11010", "EasyTier-UDP-11010"
    )
    foreach ($rName in $ruleNames) {
        Remove-NetFirewallRule -DisplayName $rName -ErrorAction SilentlyContinue
    }
    New-NetFirewallRule -DisplayName "Apollo-Stream-TCP-In" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 47984,47989,47990,48010 -Profile Any | Out-Null
    New-NetFirewallRule -DisplayName "Apollo-Stream-UDP-In" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 47998-48000,48002,48010 -Profile Any | Out-Null
    New-NetFirewallRule -DisplayName "EasyTier-P2P-Program-In" -Direction Inbound -Action Allow -Program $coreExe -Profile Any | Out-Null
    New-NetFirewallRule -DisplayName "EasyTier-P2P-TCP-In" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 11010 -Profile Any | Out-Null
    New-NetFirewallRule -DisplayName "EasyTier-P2P-UDP-In" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 11010 -Profile Any | Out-Null
    Write-Host "  -> 防火墙 5 条规则放行完成。" -ForegroundColor Green

    # 5. 设置接通电源时不休眠
    Write-Host "`n[步骤 5/8] 正在设置接通电源时不自动休眠..." -ForegroundColor Cyan
    & powercfg.exe /change standby-timeout-ac 0
    Write-Host "  -> 已设置接电永不自动休眠 (standby-timeout-ac = 0)。" -ForegroundColor Green

    # 6. 注册 EasyTier 开机静默自启计划任务并立即启动
    Write-Host "`n[步骤 6/8] 正在注册 EasyTier 后台开机自启计划任务..." -ForegroundColor Cyan
    $taskName = "EasyTier-NoTun-Autostart"
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName "EasyTier-RemoteControl" -Confirm:$false -ErrorAction SilentlyContinue
    $action    = New-ScheduledTaskAction -Execute $coreExe -Argument "-c `"$configPath`" --console-log-level warn" -WorkingDirectory $EasyTierDir
    $trigger1  = New-ScheduledTaskTrigger -AtStartup
    $trigger2  = New-ScheduledTaskTrigger -AtLogOn
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($trigger1, $trigger2) -Principal $principal -Settings $settings -Force | Out-Null
    Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "EasyTier-NoTun" -ErrorAction SilentlyContinue
    Start-ScheduledTask -TaskName $taskName
    Start-Sleep -Seconds 3

    $runningCore = Get-Process -Name "easytier-core" -ErrorAction SilentlyContinue
    if ($null -eq $runningCore) {
        Start-Process -FilePath $coreExe -ArgumentList "-c `"$configPath`" --console-log-level warn" -WorkingDirectory $EasyTierDir -WindowStyle Hidden
        Start-Sleep -Seconds 2
    }
    Write-Host "  -> EasyTier 已静默后台启动并配置开机自启 (no_tun = true)。" -ForegroundColor Green

    # 7. 生成管理工具与配对信息操作手册
    Write-Host "`n[步骤 7/8] 正在生成管理工具与本机配对手册..." -ForegroundColor Cyan
    New-ManagementScripts -InstallDir $InstallDir

    $pairingText = @"
====================================================================
                  远程控制操作手册与本机配对信息
====================================================================
生成时间: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
安装目录: $InstallDir

一、本机配对参数（请妥善保管）
--------------------------------------------------------------------
  电脑虚拟 IP (Artemis 填写) : 10.126.126.1
  平板虚拟 IP (EasyTier 填写): 10.126.126.2 (掩码 24)
  网络名称 (Network Name)    : $networkName
  网络密钥 (Network Secret)  : $networkSecret
  公共节点 (推荐主节点)       : tcp://225284.xyz:11010
  备用公共节点               : tcp://easytier.weiai.org.cn:11010
                               tcp://183.230.36.171:11010
  最新可用节点监控           : https://info.qtet.cn/uptime/easytier
  Apollo 管理后台            : https://localhost:47990
  组网模式                   : no_tun = true（无虚拟网卡，兼容代理软件）

二、日常怎么用（配好之后非常简单）
--------------------------------------------------------------------
  1. 电脑端：开机后 EasyTier、ApolloService、控制台托盘都会自动后台运行，
     平时不用手动开任何黑框；需要看状态或复制参数时，双击桌面【远程控制中心】。
  2. 平板端日常连接 2 步：
     - 第 1 步：打开平板 EasyTier，确认已连接；
     - 第 2 步：打开 Artemis，点电脑图标即可进入桌面。
     * 外网 / 不同 Wi-Fi：连虚拟 IP 10.126.126.1。
     * 在家同 Wi-Fi：也可以在 Artemis 里直接添加电脑的局域网 IP（如 192.168.x.x），
       走本地局域网直连可跑满最高码率。
  3. 熄灭电脑物理屏：连上桌面后按 Win + P -> 选【仅第二屏幕】，电脑本机屏幕
     立即熄灭；断开串流后，Apollo 自动销毁虚拟屏并恢复电脑物理屏幕。
  4. 笔记本合盖使用：打开 Windows 电源选项 -> 选择关闭盖子的功能 ->
     将【关闭盖子时（接通电源）】改为【不采取任何操作】。

三、首次配置与常见避坑（实测踩过的坑）
--------------------------------------------------------------------
  1. 代理软件图形弹窗加直连规则，不要整行粘贴
     - 如果开了 Clash / Mihomo / FlClash 等代理，在图形界面添加规则弹窗中
       （有独立的「代理类型 / 内容 / 分流策略」下拉框），切勿在类型选 DOMAIN
       时把整行 IP-CIDR,10.126.126.0/24,DIRECT,no-resolve 贴进内容框。
     - 正确做法是分成 4 条逐条添加（放到规则顶部）：
         规则 1：类型 PROCESS-NAME  | 内容 easytier-core.exe   | 策略 DIRECT
         规则 2：类型 IP-CIDR       | 内容 10.126.126.0/24     | 策略 DIRECT
         规则 3：类型 PROCESS-NAME  | 内容 apollo.exe          | 策略 DIRECT
         规则 4：类型 PROCESS-NAME  | 内容 sunshine.exe        | 策略 DIRECT
     - 若编辑 YAML 配置文件，直接粘贴：
         - PROCESS-NAME,easytier-core.exe,DIRECT
         - IP-CIDR,10.126.126.0/24,DIRECT,no-resolve
         - PROCESS-NAME,apollo.exe,DIRECT
         - PROCESS-NAME,sunshine.exe,DIRECT

  2. 安装 Apollo 务必用正常安装向导，不要用 /S 静默安装
     - 静默参数 /S 会连带静默安装 ViGEmBus 驱动并触发 Windows PnP 重启锁，
       容易导致代理软件的 Wintun 虚拟网卡异常断网直到重启。
     - 正常双击安装包按向导点 Next 即可；不用手柄的话，组件页取消勾选 ViGEmBus。

  3. 旧公共节点已失效 & 避免开机弹黑框
     - 旧官方节点 tcp://public.easytier.top:11010 已停服，tcp://39.108.52.138:11010
       已超时（注意：开着代理 TUN 时测延迟可能出现 0~1ms 假通，请以 EasyTier
       实际连上节点为准）。
     - 不要把 easytier-core.exe 加到注册表 HKCU\Run，否则开机弹黑框日志；
       当前已通过系统计划任务 EasyTier-NoTun-Autostart 静默后台运行。
     - 若某天公共节点连不上，打开控制台 ->【网络与服务】->【节点测速】切换，
       或到 https://info.qtet.cn/uptime/easytier 找新节点添加。

  4. Apollo 网页配对注意切换「PIN Pairing」标签
     - 浏览器打开 https://localhost:47990 提示证书不安全，点【高级 -> 继续前往】。
     - 点顶部【PIN】进入配对页后，默认停在【OTP Pairing】（二维码地址默认是
       art://0.0.0.0:47989）。
     - 用平板 Artemis 弹出的 4 位数字配对时，必须点旁边切换到【PIN Pairing】
       再输入 4 位 PIN 码；如果用 OTP 扫码，需先手动把 0.0.0.0 改成 10.126.126.1。

  5. 画面发糊或色彩发灰（HDR 泛白）怎么调
     - Artemis 默认码率只有 10 Mbps、Apollo 默认 NVENC 预设 P1，且 Windows
       可能会给虚拟屏自动开 HDR 导致发灰。
     - 推荐调优：
       * 平板 Artemis：码率调到 25~40 Mbps（同 Wi-Fi 局域网可开 40~80 Mbps），
         分辨率选平板原生分辨率，编码选 HEVC 或 AV1（看文字清晰可开 YUV 4:4:4）。
       * 电脑 Apollo 网页后台：Configuration -> NVIDIA NVENC -> Preset 调为 P4 或 P5。
       * Windows 显示设置：选中虚拟屏，把【使用 HDR】关闭。

  6. 杀毒软件误报（火绒 / 360 / Defender）
     - 控制台 RemoteControlUI.exe 是无数字签名的开源工具，若火绒等安全软件
       误报拦截，请将目录 $InstallDir 加入安全软件信任区/白名单。

  7. 第三方虚拟屏冲突提醒
     - 本工具不会卸载你原有的虚拟屏驱动（如 MuMu / ToDesk / UU远程）。若串流出现
       黑屏或鼠标跑到看不见的屏幕，可打开设备管理器 -> 显示适配器，临时禁用闲置虚拟屏。
====================================================================
"@
    $installDoc = Join-Path $InstallDir "我的配对信息与避坑手册.txt"
    Write-Utf8BomFile -Path $installDoc -Content $pairingText

    # 8. 部署桌面控制台 + 注册开机自启与免弹窗启动任务 + 桌面快捷方式
    Write-Host "`n[步骤 8/8] 正在部署桌面控制台与桌面快捷方式..." -ForegroundColor Cyan
    $uiReady = Register-UiScheduledTasksAndShortcuts -InstallDir $InstallDir -ProxyPrefix $proxyPrefix

    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host "              远程控制中心部署完成！                            " -ForegroundColor Green
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host "  网络名称 (network_name)  : $networkName" -ForegroundColor Yellow
    Write-Host "  网络密钥 (network_secret): $networkSecret" -ForegroundColor Yellow
    Write-Host "  电脑端虚拟 IP            : 10.126.126.1" -ForegroundColor Yellow
    Write-Host "  配对手册已保存           : $installDoc" -ForegroundColor Cyan
    Write-Host "  桌面快捷入口             : 远程控制中心.lnk" -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Green

    if ($uiReady) {
        $uiExe = Join-Path $InstallDir "RemoteControlUI.exe"
        Write-Host "正在启动【远程控制中心】..." -ForegroundColor Cyan
        Start-Process -FilePath $uiExe -WorkingDirectory $InstallDir
    }

    if (-not $apolloAlreadyReady -and (Test-Path $apolloDestExe)) {
        Write-Host "`n[最后一步] 正在打开 Apollo 图形安装向导..." -ForegroundColor Yellow
        Write-Host "  提示: 请按默认 Next 完成安装；若不用游戏手柄，组件页取消勾选 ViGEmBus 避免影响代理虚拟网卡。" -ForegroundColor Cyan
        Start-Process -FilePath $apolloDestExe -WorkingDirectory $ApolloDir
    }
}

function Invoke-UiOnlyInstall {
    if (-not (Test-IsAdmin)) {
        Write-Host "`n[权限提升] 正在请求管理员权限以更新控制台与自启任务..." -ForegroundColor Yellow
        Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -AutoOption 3"
        exit 0
    }
    $defaultInstallDir = if (Test-Path "D:\远程控制\EasyTier") { "D:\远程控制" } elseif (Test-Path "C:\远程控制\EasyTier") { "C:\远程控制" } else { Get-DefaultInstallDir }
    $inputDir = Read-Host "请输入已有远程控制安装目录 (直接按回车默认 $defaultInstallDir)"
    $InstallDir = if ([string]::IsNullOrWhiteSpace($inputDir)) { $defaultInstallDir } else { $inputDir.Trim() }
    if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null }
    $mgmtDir = Join-Path $InstallDir "管理工具"
    if (Test-Path $mgmtDir) {
        New-ManagementScripts -InstallDir $InstallDir
    }
    $proxyPrefix = ""
    if (-not (Test-Path $BundledUiExe) -and -not (Test-Path (Join-Path $PackageRoot "src\Program.cs"))) {
        $proxyPrefix = Prompt-GitHubProxy
    }
    $ok = Register-UiScheduledTasksAndShortcuts -InstallDir $InstallDir -ProxyPrefix $proxyPrefix
    if ($ok) {
        Start-Process -FilePath (Join-Path $InstallDir "RemoteControlUI.exe") -WorkingDirectory $InstallDir
        Write-Host "新版控制台已更新并启动！" -ForegroundColor Green
    }
}

try {
    Clear-Host
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "         远程控制中心一键部署 (Apollo + EasyTier)               " -ForegroundColor Cyan
    Write-Host "         默认无虚拟网卡 no_tun = true · 兼容代理软件            " -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  [1] 仅下载全部最新安装包（无需管理员权限）" -ForegroundColor White
    Write-Host "      -> 下载最新版 Apollo / EasyTier / Artemis 到 Downloaded_Files\" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  [2] 一键全自动部署本机远程控制【推荐】" -ForegroundColor Green
    Write-Host "      -> 安装控制台 + EasyTier (no_tun=true) + 防火墙 + 静默自启" -ForegroundColor Gray
    Write-Host "      -> 本地随机生成专属密钥，末尾打开 Apollo 正常安装向导" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  [3] 仅升级桌面控制台与快捷方式（保留现有配置）" -ForegroundColor Yellow
    Write-Host "      -> 适合已安装好 Apollo / EasyTier 的电脑" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  [Q] 退出" -ForegroundColor DarkGray
    Write-Host "================================================================" -ForegroundColor Cyan

    $choice = $AutoOption
    if ([string]::IsNullOrWhiteSpace($choice)) {
        $choice = (Read-Host "请选择功能序号 [1 / 2 / 3 / Q]").Trim()
    }

    switch ($choice.ToUpper()) {
        "1" {
            $proxyPrefix = Prompt-GitHubProxy
            $files = Download-AllLatestPackages -ProxyPrefix $proxyPrefix
            Write-Host ""
            Write-Host "================================================================" -ForegroundColor Green
            Write-Host " 全部最新安装包已下载完成！保存在: $DownloadDir" -ForegroundColor Green
            Write-Host "  - Apollo (Windows)   : $(Split-Path -Leaf $files.ApolloExe)" -ForegroundColor White
            Write-Host "  - EasyTier (Windows) : $(Split-Path -Leaf $files.EasyTierWinZip)" -ForegroundColor White
            Write-Host "  - EasyTier (Android) : $(Split-Path -Leaf $files.EasyTierApk)" -ForegroundColor White
            Write-Host "  - Artemis (Android)  : $(Split-Path -Leaf $files.ArtemisApk)" -ForegroundColor White
            Write-Host "================================================================" -ForegroundColor Green
        }
        "2" {
            Invoke-FullDeployment
        }
        "3" {
            Invoke-UiOnlyInstall
        }
        "Q" {
            Write-Host "已退出。" -ForegroundColor Gray
            exit 0
        }
        default {
            Write-Host "[提示] 无效的选项: $choice" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Red
    Write-Host " [执行出错] $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "================================================================" -ForegroundColor Red
}

Write-Host ""
Read-Host "按回车键退出窗口"
