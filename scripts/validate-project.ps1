#requires -Version 5.1
<#
.SYNOPSIS
    Game Language Explorer için salt okunur derleme, test ve Git doğrulaması.

.DESCRIPTION
    Proje kökünü betiğin bulunduğu scripts klasöründen bulur; çözümü derler,
    testleri çalıştırır ve Git durumunu salt okunur komutlarla özetler.
    Raporları %LOCALAPPDATA%\GameLanguageExplorer\validation-runs altına yazar.
    Kaynak kodu, oyun dosyalarını veya Git çalışma ağacını değiştirmez.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$script:startedAt = [DateTime]::UtcNow
$script:scriptRoot = if ($PSScriptRoot) {
    $PSScriptRoot
} else {
    Split-Path -Parent $MyInvocation.MyCommand.Path
}
$script:projectRoot = Split-Path -Parent $script:scriptRoot
$script:solutionName = 'GameLanguageExplorer.slnx'
$script:solutionPath = Join-Path $script:projectRoot $script:solutionName
$script:errors = New-Object 'System.Collections.Generic.List[object]'
$script:warnings = New-Object 'System.Collections.Generic.List[object]'
$script:reportDir = $null
$script:reportPaths = [ordered]@{}
$script:runId = $null

function Protect-Text {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return ''
    }

    $result = $Text
    $result = [regex]::Replace(
        $result,
        '(?im)((?:api[_-]?key|access[_-]?token|auth[_-]?token|secret|password|passwd|authorization)\s*[:=]\s*)([^\s]+)',
        '$1[REDACTED]')
    $result = [regex]::Replace(
        $result,
        '(?i)(Bearer\s+)[A-Za-z0-9\-\._~\+/]+=*',
        '$1[REDACTED]')
    $result = [regex]::Replace(
        $result,
        '(?i)(sk-[A-Za-z0-9]{20,})',
        '[REDACTED]')
    return $result
}

function Add-Error {
    param([string]$Stage, [string]$Message)
    $script:errors.Add([pscustomobject]@{
        stage = $Stage
        message = Protect-Text $Message
    })
}

function Add-Warning {
    param([string]$Stage, [string]$Message)
    $script:warnings.Add([pscustomobject]@{
        stage = $Stage
        message = Protect-Text $Message
    })
}

function Get-TextSummary {
    param(
        [AllowNull()][string]$Text,
        [int]$MaxLines = 40,
        [int]$MaxChars = 8000
    )

    $safe = Protect-Text $Text
    if ([string]::IsNullOrEmpty($safe)) {
        return [pscustomobject]@{
            text = ''
            totalLines = 0
            truncated = $false
        }
    }

    $lines = $safe -split "`r?`n"
    $total = $lines.Count
    $start = [Math]::Max(0, $total - $MaxLines)
    $selected = @($lines[$start..($total - 1)])
    $joined = ($selected -join "`n").TrimEnd()
    $truncated = ($start -gt 0) -or ($joined.Length -gt $MaxChars)

    if ($joined.Length -gt $MaxChars) {
        $joined = '...[truncated]...' + "`n" + $joined.Substring($joined.Length - $MaxChars)
    }

    return [pscustomobject]@{
        text = $joined
        totalLines = $total
        truncated = $truncated
    }
}

function Invoke-CapturedProcess {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$Label
    )

    $safeLabel = $Label -replace '[^A-Za-z0-9_.-]', '_'
    $nonce = [Guid]::NewGuid().ToString('N')
    $stdoutPath = Join-Path $script:reportDir ("$safeLabel.$nonce.out.tmp")
    $stderrPath = Join-Path $script:reportDir ("$safeLabel.$nonce.err.tmp")
    $started = [DateTime]::UtcNow
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $exitCode = -1
    $launchError = $null

    try {
        $process = Start-Process `
            -FilePath $FilePath `
            -ArgumentList $Arguments `
            -WorkingDirectory $WorkingDirectory `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath `
            -WindowStyle Hidden `
            -Wait `
            -PassThru `
            -ErrorAction Stop
        $exitCode = $process.ExitCode
    } catch {
        $launchError = $_.Exception.Message
    } finally {
        $watch.Stop()
    }

    $stdoutText = ''
    $stderrText = ''
    if (Test-Path -LiteralPath $stdoutPath) {
        $stdoutText = [System.IO.File]::ReadAllText($stdoutPath, [System.Text.Encoding]::UTF8)
    }
    if (Test-Path -LiteralPath $stderrPath) {
        $stderrText = [System.IO.File]::ReadAllText($stderrPath, [System.Text.Encoding]::UTF8)
    }

    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

    $success = ($null -eq $launchError) -and ($exitCode -eq 0)
    $commandText = $FilePath + ' ' + ($Arguments -join ' ')

    return [pscustomobject]@{
        command = $commandText
        startedAt = $started.ToString('o')
        finishedAt = [DateTime]::UtcNow.ToString('o')
        durationSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
        exitCode = $exitCode
        success = $success
        launchError = $launchError
        stdoutText = $stdoutText
        stderrText = $stderrText
        stdout = Get-TextSummary -Text $stdoutText -MaxLines 40 -MaxChars 8000
        stderr = Get-TextSummary -Text $stderrText -MaxLines 40 -MaxChars 8000
    }
}

function Convert-AttemptForReport {
    param([object]$Attempt)

    return [ordered]@{
        command = $Attempt.command
        startedAt = $Attempt.startedAt
        finishedAt = $Attempt.finishedAt
        durationSeconds = $Attempt.durationSeconds
        exitCode = $Attempt.exitCode
        success = $Attempt.success
        launchError = Protect-Text $Attempt.launchError
        stdout = $Attempt.stdout
        stderr = $Attempt.stderr
    }
}

function Write-AtomicText {
    param([string]$Path, [string]$Text)

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $tempPath = Join-Path $directory ('.' + [System.IO.Path]::GetFileName($Path) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    [System.IO.File]::WriteAllText($tempPath, $Text, (New-Object System.Text.UTF8Encoding($false)))

    try {
        if (Test-Path -LiteralPath $Path) {
            [System.IO.File]::Replace($tempPath, $Path, $null)
        } else {
            [System.IO.File]::Move($tempPath, $Path)
        }
    } catch {
        if (Test-Path -LiteralPath $tempPath) {
            Move-Item -LiteralPath $tempPath -Destination $Path -Force
        } else {
            throw
        }
    }
}

function Get-DotnetVersionFromInfo {
    param([AllowNull()][string]$Text)
    if ([string]::IsNullOrEmpty($Text)) {
        return 'unknown'
    }

    $match = [regex]::Match($Text, '(?m)^\s*Version:\s*(\S+)')
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    $match = [regex]::Match($Text, '(?m)^\.NET SDKs installed:\s*([^\r\n]+)')
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }

    return 'unknown'
}

function Get-TestSummary {
    param([AllowNull()][string]$Text)
    $result = [ordered]@{
        total = $null
        passed = $null
        failed = $null
        skipped = $null
        durationSeconds = $null
    }
    if ([string]::IsNullOrEmpty($Text)) {
        return $result
    }

    $match = [regex]::Match($Text, 'Toplam test sayısı:\s*(\d+)')
    if ($match.Success) { $result.total = [int]$match.Groups[1].Value }
    $match = [regex]::Match($Text, '(?m)^\s*Geçti:\s*(\d+)')
    if ($match.Success) { $result.passed = [int]$match.Groups[1].Value }
    $match = [regex]::Match($Text, '(?m)^\s*Başarısız:\s*(\d+)')
    if ($match.Success) { $result.failed = [int]$match.Groups[1].Value }
    $match = [regex]::Match($Text, '(?m)^\s*Atlanan:\s*(\d+)')
    if ($match.Success) { $result.skipped = [int]$match.Groups[1].Value }

    $match = [regex]::Match($Text, 'Başarısız:\s*(\d+),\s*Başarılı:\s*(\d+),\s*Atlanan:\s*(\d+),\s*Toplam:\s*(\d+)')
    if ($match.Success) {
        $result.failed = [int]$match.Groups[1].Value
        $result.passed = [int]$match.Groups[2].Value
        $result.skipped = [int]$match.Groups[3].Value
        $result.total = [int]$match.Groups[4].Value
    }

    $match = [regex]::Match($Text, 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')
    if ($match.Success) {
        $result.failed = [int]$match.Groups[1].Value
        $result.passed = [int]$match.Groups[2].Value
        $result.skipped = [int]$match.Groups[3].Value
        $result.total = [int]$match.Groups[4].Value
    }

    $match = [regex]::Match($Text, 'Toplam süre:\s*([\d.,]+)\s*Saniye')
    if ($match.Success) {
        $durationText = $match.Groups[1].Value.Replace(',', '.')
        $parsed = 0.0
        if ([double]::TryParse(
                $durationText,
                [System.Globalization.NumberStyles]::Float,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [ref]$parsed)) {
            $result.durationSeconds = [Math]::Round($parsed, 3)
        }
    } elseif (($match = [regex]::Match($Text, '(?:Süre|Duration):\s*([\d.,]+)\s*s')).Success) {
        $durationText = $match.Groups[1].Value.Replace(',', '.')
        $parsed = 0.0
        if ([double]::TryParse(
                $durationText,
                [System.Globalization.NumberStyles]::Float,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [ref]$parsed)) {
            $result.durationSeconds = [Math]::Round($parsed, 3)
        }
    }

    return $result
}

function Format-CodeBlock {
    param([AllowNull()][string]$Text)
    if ([string]::IsNullOrEmpty($Text)) {
        return '(empty)'
    }
    return $Text.Replace('```', "'''")
}

$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
if ([string]::IsNullOrWhiteSpace($localAppData)) {
    $localAppData = $env:LOCALAPPDATA
}
$reportsRoot = Join-Path $localAppData 'GameLanguageExplorer\validation-runs'
New-Item -ItemType Directory -Path $reportsRoot -Force | Out-Null

$baseRunId = Get-Date -Format 'yyyyMMdd_HHmmss'
$script:runId = $baseRunId
$suffix = 2
while (Test-Path -LiteralPath (Join-Path $reportsRoot $script:runId)) {
    $script:runId = $baseRunId + '_' + $suffix
    $suffix++
}
$script:reportDir = Join-Path $reportsRoot $script:runId
New-Item -ItemType Directory -Path $script:reportDir -Force | Out-Null
$script:reportPaths = [ordered]@{
    json = Join-Path $script:reportDir 'report.json'
    markdown = Join-Path $script:reportDir 'report.md'
}

$buildReport = [ordered]@{
    success = $false
    command = 'dotnet build GameLanguageExplorer.slnx --no-restore'
    attempts = @()
    exitCode = $null
    durationSeconds = 0.0
    retried = $false
    retryReason = $null
}
$testReport = [ordered]@{
    success = $false
    command = 'dotnet test GameLanguageExplorer.slnx --no-build'
    attempts = @()
    exitCode = $null
    durationSeconds = 0.0
    retried = $false
    retryReason = $null
    blockedByBuild = $false
    staleBuild = $false
    summary = [ordered]@{ total = $null; passed = $null; failed = $null; skipped = $null; durationSeconds = $null }
}
$gitReport = [ordered]@{
    available = $false
    isRepository = $false
    statusShort = @()
    statusShortTruncated = $false
    diffStat = ''
    diffNameOnly = @()
    diffCheck = ''
    staged = [ordered]@{
        hasChanges = $false
        stat = ''
        check = ''
        nameOnly = @()
    }
    error = $null
}
$dotnetInfoReport = [ordered]@{
    success = $false
    command = 'dotnet --info'
    exitCode = $null
    durationSeconds = 0.0
    stdout = $null
    stderr = $null
    launchError = $null
}

$report = [ordered]@{
    runId = $script:runId
    startedAt = $script:startedAt.ToString('o')
    finishedAt = $null
    projectRoot = $script:projectRoot
    dotnetVersion = 'unknown'
    dotnetInfo = $dotnetInfoReport
    build = $buildReport
    tests = $testReport
    git = $gitReport
    overallStatus = 'FAILED'
    errors = @()
    warnings = @()
    reports = [ordered]@{
        json = $script:reportPaths.json
        markdown = $script:reportPaths.markdown
    }
    powershellVersion = $PSVersionTable.PSVersion.ToString()
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { $null }
$gitCommand = Get-Command git -ErrorAction SilentlyContinue
$gitPath = if ($gitCommand) { $gitCommand.Source } else { $null }

try {
    $solutionExists = Test-Path -LiteralPath $script:solutionPath
    if (-not $solutionExists) {
        Add-Error 'solution' ("Çözüm dosyası bulunamadı: " + $script:solutionPath)
    }

    if (-not $dotnetPath) {
        Add-Error 'dotnet' 'dotnet komutu bulunamadı; derleme ve testler çalıştırılamaz.'
    } else {
        $dotnetInfoResult = Invoke-CapturedProcess -FilePath $dotnetPath -Arguments @('--info') -WorkingDirectory $script:projectRoot -Label 'dotnet-info'
        $dotnetInfoReport.success = $dotnetInfoResult.success
        $dotnetInfoReport.exitCode = $dotnetInfoResult.exitCode
        $dotnetInfoReport.durationSeconds = $dotnetInfoResult.durationSeconds
        $dotnetInfoReport.stdout = $dotnetInfoResult.stdout
        $dotnetInfoReport.stderr = $dotnetInfoResult.stderr
        $dotnetInfoReport.launchError = Protect-Text $dotnetInfoResult.launchError
        $report.dotnetVersion = Get-DotnetVersionFromInfo $dotnetInfoResult.stdoutText

        if (-not $dotnetInfoResult.success) {
            Add-Warning 'dotnet-info' 'dotnet --info başarısız oldu; sürüm bilgisi sınırlı olabilir.'
        }
    }

    if ($solutionExists -and $dotnetPath) {
        $buildAttempt1 = Invoke-CapturedProcess -FilePath $dotnetPath -Arguments @('build', $script:solutionName, '--no-restore') -WorkingDirectory $script:projectRoot -Label 'build-no-restore'
        $buildReport.attempts = @($buildReport.attempts) + @(Convert-AttemptForReport $buildAttempt1)
        $buildOutput = $buildAttempt1.stdoutText + "`n" + $buildAttempt1.stderrText

        $restoreRelated = $buildOutput -match '(?i)(NU1\d{3}|NETSDK1064|project\.assets\.json|assets file.*not found|run a nuget package restore|unable to find package|package.*not found)'
        $finalBuildAttempt = $buildAttempt1

        if ((-not $buildAttempt1.success) -and $restoreRelated) {
            $buildReport.retried = $true
            $buildReport.retryReason = '--no-restore sırasında eksik paket/asset göstergesi bulundu; normal restore''lu build bir kez denendi.'
            $finalBuildAttempt = Invoke-CapturedProcess -FilePath $dotnetPath -Arguments @('build', $script:solutionName) -WorkingDirectory $script:projectRoot -Label 'build-restore'
            $buildReport.attempts = @($buildReport.attempts) + @(Convert-AttemptForReport $finalBuildAttempt)
        }

        $buildReport.success = $finalBuildAttempt.success
        $buildReport.exitCode = $finalBuildAttempt.exitCode
        $buildReport.durationSeconds = [Math]::Round((($buildReport.attempts | ForEach-Object { $_.durationSeconds }) | Measure-Object -Sum).Sum, 3)

        if (-not $buildReport.success) {
            $failureText = $finalBuildAttempt.stderrText
            if ([string]::IsNullOrWhiteSpace($failureText)) {
                $failureText = $finalBuildAttempt.stdoutText
            }
            $failureText = Protect-Text $failureText
            if ($failureText.Length -gt 2000) {
                $failureText = $failureText.Substring($failureText.Length - 2000)
            }
            Add-Error 'build' ("Build başarısız (exit code " + $finalBuildAttempt.exitCode + ").`n" + $failureText)
        }
    }
} catch {
    Add-Error 'validation' ('Beklenmeyen doğrulama hatası: ' + $_.Exception.Message)
}

try {
    if ($solutionExists -and $dotnetPath) {
        $buildSucceeded = [bool]$buildReport.success
        $testAttempt1 = Invoke-CapturedProcess -FilePath $dotnetPath -Arguments @('test', $script:solutionName, '--no-build') -WorkingDirectory $script:projectRoot -Label 'test-no-build'
        $testReport.attempts = @($testReport.attempts) + @(Convert-AttemptForReport $testAttempt1)
        $testOutput = $testAttempt1.stdoutText + "`n" + $testAttempt1.stderrText
        $testInfraFailure = $testOutput -match '(?i)(MSB1009|MSB3021|MSB3027|VSTest|testhost|not found|does not exist|could not find|no test is available|dll.*bulunamadı)'
        $testRetry = (-not $testAttempt1.success) -and ((-not $buildSucceeded) -or $testInfraFailure)
        $finalTestAttempt = $testAttempt1

        if ($testRetry) {
            $testReport.retried = $true
            $testReport.retryReason = '--no-build testi çalıştırılamadı veya önceki build başarısızdı; normal dotnet test bir kez denendi.'
            $finalTestAttempt = Invoke-CapturedProcess -FilePath $dotnetPath -Arguments @('test', $script:solutionName) -WorkingDirectory $script:projectRoot -Label 'test-with-build'
            $testReport.attempts = @($testReport.attempts) + @(Convert-AttemptForReport $finalTestAttempt)
        }

        $testReport.success = $finalTestAttempt.success
        $testReport.exitCode = $finalTestAttempt.exitCode
        $testReport.durationSeconds = [Math]::Round((($testReport.attempts | ForEach-Object { $_.durationSeconds }) | Measure-Object -Sum).Sum, 3)
        $testReport.blockedByBuild = -not $buildSucceeded
        $testReport.staleBuild = $testReport.success -and (-not $buildSucceeded)
        $testReport.summary = Get-TestSummary ($finalTestAttempt.stdoutText + "`n" + $finalTestAttempt.stderrText)

        if (-not $buildSucceeded) {
            Add-Warning 'tests' 'Build başarısız olduğu için test sonucu güvenilir kabul edilmemeli; testler --no-build ile mevcut çıktılar üzerinde çalışmış olabilir.'
        }

        if (-not $testReport.success) {
            $failureText = $finalTestAttempt.stderrText
            if ([string]::IsNullOrWhiteSpace($failureText)) {
                $failureText = $finalTestAttempt.stdoutText
            }
            $failureText = Protect-Text $failureText
            if ($failureText.Length -gt 2000) {
                $failureText = $failureText.Substring($failureText.Length - 2000)
            }
            Add-Error 'tests' ("Testler başarısız (exit code " + $finalTestAttempt.exitCode + ").`n" + $failureText)
        }
    } elseif ($solutionExists -and -not $dotnetPath) {
        $testReport.blockedByBuild = $true
        Add-Error 'tests' 'Testler çalıştırılamadı: dotnet komutu yok.'
    }
} catch {
    Add-Error 'validation' ('Beklenmeyen test hatası: ' + $_.Exception.Message)
}

try {
    if (-not $gitPath) {
        $gitReport.error = 'git komutu bulunamadı.'
        Add-Warning 'git' 'Git komutu bulunamadı; Git değişiklikleri raporlanamadı.'
    } else {
        $gitReport.available = $true
        $repoCheck = Invoke-CapturedProcess -FilePath $gitPath -Arguments @('rev-parse', '--is-inside-work-tree') -WorkingDirectory $script:projectRoot -Label 'git-repo-check'

        if (-not $repoCheck.success -or $repoCheck.stdoutText.Trim() -ne 'true') {
            $gitReport.error = Protect-Text (($repoCheck.stderrText + "`n" + $repoCheck.stdoutText).Trim())
            Add-Warning 'git' 'Proje bir Git deposu değil; Git değişiklikleri raporlanamadı.'
        } else {
            $gitReport.isRepository = $true

            $statusResult = Invoke-CapturedProcess -FilePath $gitPath -Arguments @('status', '--short') -WorkingDirectory $script:projectRoot -Label 'git-status'
            $statusLines = @($statusResult.stdoutText -split "`r?`n" | Where-Object { $_.Length -gt 0 })
            if ($statusLines.Count -gt 200) {
                $gitReport.statusShort = @($statusLines[0..199])
                $gitReport.statusShortTruncated = $true
            } else {
                $gitReport.statusShort = $statusLines
            }

            $diffStat = Invoke-CapturedProcess -FilePath $gitPath -Arguments @('diff', '--stat') -WorkingDirectory $script:projectRoot -Label 'git-diff-stat'
            $gitReport.diffStat = (Get-TextSummary -Text $diffStat.stdoutText -MaxLines 200 -MaxChars 12000).text

            $diffNames = Invoke-CapturedProcess -FilePath $gitPath -Arguments @('diff', '--name-only') -WorkingDirectory $script:projectRoot -Label 'git-diff-names'
            $gitReport.diffNameOnly = @($diffNames.stdoutText -split "`r?`n" | Where-Object { $_.Length -gt 0 })

            $diffCheck = Invoke-CapturedProcess -FilePath $gitPath -Arguments @('diff', '--check') -WorkingDirectory $script:projectRoot -Label 'git-diff-check'
            $gitReport.diffCheck = (Get-TextSummary -Text ($diffCheck.stdoutText + "`n" + $diffCheck.stderrText) -MaxLines 80 -MaxChars 8000).text
            if (-not [string]::IsNullOrWhiteSpace($gitReport.diffCheck)) {
                Add-Warning 'git' 'git diff --check uyarı üretti; ayrıntı Markdown raporunda.'
            }

            $stagedNames = Invoke-CapturedProcess -FilePath $gitPath -Arguments @('diff', '--cached', '--name-only') -WorkingDirectory $script:projectRoot -Label 'git-staged-names'
            $stagedNameList = @($stagedNames.stdoutText -split "`r?`n" | Where-Object { $_.Length -gt 0 })
            $gitReport.staged.nameOnly = $stagedNameList
            $gitReport.staged.hasChanges = $stagedNameList.Count -gt 0

            if ($gitReport.staged.hasChanges) {
                $stagedStat = Invoke-CapturedProcess -FilePath $gitPath -Arguments @('diff', '--cached', '--stat') -WorkingDirectory $script:projectRoot -Label 'git-staged-stat'
                $gitReport.staged.stat = (Get-TextSummary -Text $stagedStat.stdoutText -MaxLines 120 -MaxChars 8000).text

                $stagedCheck = Invoke-CapturedProcess -FilePath $gitPath -Arguments @('diff', '--cached', '--check') -WorkingDirectory $script:projectRoot -Label 'git-staged-check'
                $gitReport.staged.check = (Get-TextSummary -Text ($stagedCheck.stdoutText + "`n" + $stagedCheck.stderrText) -MaxLines 80 -MaxChars 8000).text
                if (-not [string]::IsNullOrWhiteSpace($gitReport.staged.check)) {
                    Add-Warning 'git' 'git diff --cached --check uyarı üretti; ayrıntı Markdown raporunda.'
                }
            }

            if (-not ($statusResult.success -and $diffStat.success -and $diffNames.success -and $diffCheck.success)) {
                Add-Warning 'git' 'Bazı Git komutları sıfır dışı çıktı; ayrıntı raporda.'
            }
        }
    }
} catch {
    $gitReport.error = Protect-Text $_.Exception.Message
    Add-Warning 'git' ('Git bilgisi toplanırken hata: ' + $_.Exception.Message)
}

function Add-MarkdownCode {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [AllowNull()][string]$Text
    )
    $Lines.Add('```text')
    $Lines.Add((Format-CodeBlock $Text))
    $Lines.Add('```')
}

$report.finishedAt = [DateTime]::UtcNow.ToString('o')
$report.errors = $script:errors.ToArray()
$report.warnings = $script:warnings.ToArray()
$overallStatus = 'PASSED'
if ($script:errors.Count -gt 0 -or -not $buildReport.success -or -not $testReport.success) {
    $overallStatus = 'FAILED'
} elseif (-not $gitReport.isRepository) {
    $overallStatus = 'WARNING'
}
$report.overallStatus = $overallStatus

$markdown = New-Object 'System.Collections.Generic.List[string]'
$markdown.Add('# Game Language Explorer - Doğrulama Raporu')
$markdown.Add('')
$markdown.Add('| Alan | Değer |')
$markdown.Add('|---|---|')
$markdown.Add("| Run ID | ``$($report.runId)`` |")
$markdown.Add("| Genel durum | **$($report.overallStatus)** |")
$markdown.Add("| Başlangıç (UTC) | $($report.startedAt) |")
$markdown.Add("| Bitiş (UTC) | $($report.finishedAt) |")
$markdown.Add("| Proje kökü | ``$($report.projectRoot)`` |")
$markdown.Add("| .NET sürümü | ``$($report.dotnetVersion)`` |")
$markdown.Add("| PowerShell | $($report.powershellVersion) |")
$markdown.Add('')
$markdown.Add('## Genel Sonuç')
$markdown.Add('')
$markdown.Add("Genel durum: **$($report.overallStatus)**")
if ($script:errors.Count -gt 0) {
    $markdown.Add('')
    $markdown.Add(('Zorunlu kontrollerde {0} hata var.' -f $script:errors.Count))
}
if (-not $gitReport.isRepository) {
    $markdown.Add('')
    $markdown.Add('Git deposu bulunamadığı için Git bölümü WARNING olarak işaretlendi; bu tek başına build/test başarısızlığı değildir.')
}
$markdown.Add('')
$markdown.Add('## Derleme')
$markdown.Add('')
$markdown.Add("- Komut: ``dotnet build $($script:solutionName) --no-restore``")
$markdown.Add("- Sonuç: **$(if ($buildReport.success) { 'PASSED' } else { 'FAILED' })**")
$markdown.Add("- Exit code: ``$($buildReport.exitCode)``")
$markdown.Add("- Toplam süre: $($buildReport.durationSeconds) s")
$markdown.Add("- Retry: $($buildReport.retried)")
if ($buildReport.retryReason) {
    $markdown.Add("- Retry nedeni: $($buildReport.retryReason)")
}
$markdown.Add('')
foreach ($attempt in $buildReport.attempts) {
    $markdown.Add("### Build denemesi: ``$($attempt.command)``")
    $markdown.Add('')
    $markdown.Add("- Exit code: ``$($attempt.exitCode)``; başarılı: $($attempt.success); süre: $($attempt.durationSeconds) s")
    if ($attempt.launchError) {
        $markdown.Add("- Başlatma hatası: $($attempt.launchError)")
    }
    $markdown.Add('')
    $markdown.Add('Stdout (son satırlar):')
    Add-MarkdownCode -Lines $markdown -Text $attempt.stdout.text
    $markdown.Add('')
    $markdown.Add('Stderr (son satırlar):')
    Add-MarkdownCode -Lines $markdown -Text $attempt.stderr.text
    $markdown.Add('')
}

$markdown.Add('## Testler')
$markdown.Add('')
$markdown.Add("- Komut: ``dotnet test $($script:solutionName) --no-build``")
$markdown.Add("- Sonuç: **$(if ($testReport.success) { 'PASSED' } else { 'FAILED' })**")
$markdown.Add("- Exit code: ``$($testReport.exitCode)``")
$markdown.Add("- Toplam süre: $($testReport.durationSeconds) s")
$markdown.Add("- Retry: $($testReport.retried)")
$markdown.Add("- Build başarısızlığı nedeniyle etkilendi: $($testReport.blockedByBuild)")
$markdown.Add("- Eski build çıktıları üzerinde çalışmış olabilir: $($testReport.staleBuild)")
if ($testReport.retryReason) {
    $markdown.Add("- Retry nedeni: $($testReport.retryReason)")
}
$markdown.Add("- Test özeti: total=$($testReport.summary.total), passed=$($testReport.summary.passed), failed=$($testReport.summary.failed), skipped=$($testReport.summary.skipped)")
$markdown.Add('')
foreach ($attempt in $testReport.attempts) {
    $markdown.Add("### Test denemesi: ``$($attempt.command)``")
    $markdown.Add('')
    $markdown.Add("- Exit code: ``$($attempt.exitCode)``; başarılı: $($attempt.success); süre: $($attempt.durationSeconds) s")
    if ($attempt.launchError) {
        $markdown.Add("- Başlatma hatası: $($attempt.launchError)")
    }
    $markdown.Add('')
    $markdown.Add('Stdout (son satırlar):')
    Add-MarkdownCode -Lines $markdown -Text $attempt.stdout.text
    $markdown.Add('')
    $markdown.Add('Stderr (son satırlar):')
    Add-MarkdownCode -Lines $markdown -Text $attempt.stderr.text
    $markdown.Add('')
}

$markdown.Add('## Git Değişiklik Özeti')
$markdown.Add('')
if (-not $gitReport.available) {
    $markdown.Add('Git komutu bulunamadı veya çalıştırılamadı.')
} elseif (-not $gitReport.isRepository) {
    $markdown.Add('**Proje bir Git deposu değil.** Git komutları çalıştırılmadı; bu durum WARNING olarak raporlandı.')
} else {
    $markdown.Add('### git status --short')
    $markdown.Add('')
    Add-MarkdownCode -Lines $markdown -Text (($gitReport.statusShort -join "`n"))
    if ($gitReport.statusShortTruncated) {
        $markdown.Add('')
        $markdown.Add('_Status çıktısı 200 satırda kesildi._')
    }
    $markdown.Add('')
    $markdown.Add('### git diff --stat')
    $markdown.Add('')
    Add-MarkdownCode -Lines $markdown -Text $gitReport.diffStat
    $markdown.Add('')
    $markdown.Add('### git diff --name-only')
    $markdown.Add('')
    if ($gitReport.diffNameOnly.Count -eq 0) {
        $markdown.Add('Değişiklik yok.')
    } else {
        foreach ($name in $gitReport.diffNameOnly) { $markdown.Add("- $name") }
    }
    $markdown.Add('')
    $markdown.Add('### git diff --check')
    $markdown.Add('')
    Add-MarkdownCode -Lines $markdown -Text $gitReport.diffCheck
    $markdown.Add('')
    $markdown.Add('### Staged değişiklikler')
    $markdown.Add('')
    if (-not $gitReport.staged.hasChanges) {
        $markdown.Add('Staged değişiklik yok.')
    } else {
        $markdown.Add('Dosyalar:')
        foreach ($name in $gitReport.staged.nameOnly) { $markdown.Add("- $name") }
        $markdown.Add('')
        $markdown.Add('`git diff --cached --stat`:')
        Add-MarkdownCode -Lines $markdown -Text $gitReport.staged.stat
        $markdown.Add('')
        $markdown.Add('`git diff --cached --check`:')
        Add-MarkdownCode -Lines $markdown -Text $gitReport.staged.check
    }
}
$markdown.Add('')

$markdown.Add('## Hatalar')
$markdown.Add('')
if ($script:errors.Count -eq 0) {
    $markdown.Add('Hata yok.')
} else {
    foreach ($errorItem in $script:errors) {
        $message = ($errorItem.message -replace "`r?`n", ' / ')
        $markdown.Add("- **$($errorItem.stage)**: $message")
    }
}
$markdown.Add('')
$markdown.Add('## Uyarılar')
$markdown.Add('')
if ($script:warnings.Count -eq 0) {
    $markdown.Add('Uyarı yok.')
} else {
    foreach ($warningItem in $script:warnings) {
        $message = ($warningItem.message -replace "`r?`n", ' / ')
        $markdown.Add("- **$($warningItem.stage)**: $message")
    }
}
$markdown.Add('')
$markdown.Add('## Rapor Dosyaları')
$markdown.Add('')
$markdown.Add("- JSON: ``$($script:reportPaths.json)``")
$markdown.Add("- Markdown: ``$($script:reportPaths.markdown)``")
$markdown.Add('')
$markdown.Add('## Notlar')
$markdown.Add('')
$markdown.Add('- Bu betik kaynak kodu, XAML, proje dosyaları, testler, oyun dosyaları veya Git çalışma ağacını değiştirmez.')
$markdown.Add('- Build/test çıktıları bin/obj altında normal araç çıktısı olarak oluşabilir.')
$markdown.Add('- Raporlarda API anahtarı, token, parola veya benzeri gizli bilgi maskelenir.')

$jsonText = $report | ConvertTo-Json -Depth 12
$markdownText = ($markdown -join "`n") + "`n"

$reportWriteError = $null
try {
    Write-AtomicText -Path $script:reportPaths.json -Text $jsonText
} catch {
    $reportWriteError = $_.Exception.Message
}
try {
    Write-AtomicText -Path $script:reportPaths.markdown -Text $markdownText
} catch {
    if (-not $reportWriteError) {
        $reportWriteError = $_.Exception.Message
    }
}

if ($reportWriteError) {
    [Console]::Error.WriteLine('Rapor yazılamadı: ' + $reportWriteError)
    exit 2
}

Write-Host ('Genel durum: ' + $report.overallStatus)
Write-Host ('JSON: ' + $script:reportPaths.json)
Write-Host ('Markdown: ' + $script:reportPaths.markdown)

if ($report.overallStatus -eq 'FAILED') {
    exit 1
}
exit 0
