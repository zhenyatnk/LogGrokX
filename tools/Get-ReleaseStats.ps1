<#
.SYNOPSIS
    Builds download, installation and update statistics for LogGrokX GitHub releases (#61).

.DESCRIPTION
    GitHub counts downloads of every release asset. The assets are classified as:
      - installer  LogGrokX-<ver>-<arch>-setup.exe         (manual installs and in-app updates)
      - update     LogGrokX-<ver>-<arch>-setup.exe.sha256  (downloaded only by the in-app updater)
      - portable   LogGrokX-<ver>-win-<arch>-*.zip
      - checksums  SHA256SUMS.txt
    New installations are estimated as installer downloads minus updates. Releases published
    before the .sha256 files existed have no update data: their updater used SHA256SUMS.txt,
    which is also downloaded manually.

    With -HistoryPath the current counters are stored as a daily snapshot (CSV), and the report
    also shows downloads for the last 1, 7 and 30 days.

.EXAMPLE
    ./tools/Get-ReleaseStats.ps1

.EXAMPLE
    ./tools/Get-ReleaseStats.ps1 -HistoryPath stats/downloads.csv -OutputPath stats/README.md
#>
[CmdletBinding()]
param(
    [string]$Repository = 'zhenyatnk/LogGrokX',
    [string]$HistoryPath,
    [string]$OutputPath,
    [string]$Token = $(if ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { $env:GH_TOKEN }),
    [string]$ReleasesJsonPath,
    [datetime]$Date = [datetime]::UtcNow
)

Set-StrictMode -Version 3
$ErrorActionPreference = 'Stop'

$Categories = @('installer', 'update', 'portable', 'checksums', 'other')
$Archs = @('x64', 'x86')

function Get-Releases {
    if ($ReleasesJsonPath) {
        return @(Get-Content -Raw -Path $ReleasesJsonPath | ConvertFrom-Json)
    }

    $headers = @{ 'Accept' = 'application/vnd.github+json'; 'User-Agent' = 'LogGrokX-release-stats' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }

    $result = @()
    for ($page = 1; ; $page++) {
        $uri = "https://api.github.com/repos/$Repository/releases?per_page=100&page=$page"
        $response = Invoke-RestMethod -Uri $uri -Headers $headers
        $batch = @($response)
        $result += $batch
        if ($batch.Count -lt 100) { break }
    }
    return $result
}

function Get-AssetKind([string]$Name) {
    if ($Name -match '-(x64|x86)-setup\.exe\.sha256$') { return @{ Category = 'update'; Arch = $Matches[1] } }
    if ($Name -match '-(x64|x86)-setup\.exe$') { return @{ Category = 'installer'; Arch = $Matches[1] } }
    if ($Name -match '-win-(x64|x86)-[^-]+\.zip$') { return @{ Category = 'portable'; Arch = $Matches[1] } }
    if ($Name -eq 'SHA256SUMS.txt') { return @{ Category = 'checksums'; Arch = '' } }
    return @{ Category = 'other'; Arch = '' }
}

function New-Counters {
    $c = [ordered]@{}
    foreach ($category in $Categories) { $c[$category] = 0 }
    foreach ($arch in $Archs) { $c["installer-$arch"] = 0; $c["update-$arch"] = 0 }
    $c['total'] = 0
    return $c
}

function Add-Download($Counters, [string]$Name, [long]$Count) {
    $kind = Get-AssetKind $Name
    $Counters[$kind.Category] += $Count
    if ($kind.Arch -and $Counters.Contains("$($kind.Category)-$($kind.Arch)")) {
        $Counters["$($kind.Category)-$($kind.Arch)"] += $Count
    }
    $Counters['total'] += $Count
}

function Format-Count([long]$Value) { $Value.ToString('N0', [Globalization.CultureInfo]::InvariantCulture) }

function Format-Installs($Counters, [bool]$HasUpdates) {
    if (-not $HasUpdates) { return '-' }
    return Format-Count ([Math]::Max(0, $Counters['installer'] - $Counters['update']))
}

function Format-Updates($Counters, [bool]$HasUpdates) {
    if (-not $HasUpdates) { return '-' }
    return "$(Format-Count $Counters['update']) ($(Format-Count $Counters['update-x64']) / $(Format-Count $Counters['update-x86']))"
}

$snapshotDate = $Date.ToUniversalTime().ToString('yyyy-MM-dd')
$releases = @(Get-Releases | Where-Object { -not $_.draft } |
    Sort-Object { [datetime]$_.published_at } -Descending)

$snapshot = @()
foreach ($release in $releases) {
    foreach ($asset in @($release.assets)) {
        $snapshot += [pscustomobject]@{
            date      = $snapshotDate
            tag       = $release.tag_name
            asset     = $asset.name
            downloads = [long]$asset.download_count
        }
    }
}

$history = @()
if ($HistoryPath) {
    if (Test-Path $HistoryPath) {
        $history = @(Import-Csv -Path $HistoryPath | Where-Object { $_.date -ne $snapshotDate })
    }
    $directory = Split-Path -Parent $HistoryPath
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    @($history + $snapshot) | Sort-Object date, tag, asset |
        Export-Csv -Path $HistoryPath -NoTypeInformation -Encoding utf8
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# LogGrokX release statistics")
$lines.Add('')
$lines.Add("Repository: [$Repository](https://github.com/$Repository/releases). Snapshot: $snapshotDate (UTC).")
$lines.Add('')

$totals = New-Counters
foreach ($row in $snapshot) { Add-Download $totals $row.asset $row.downloads }
$trackedReleases = @($releases | Where-Object { @($_.assets | Where-Object { $_.name -like '*-setup.exe.sha256' }).Count -gt 0 })
$trackedTags = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($release in $trackedReleases) { [void]$trackedTags.Add($release.tag_name) }
$tracked = New-Counters
foreach ($release in $trackedReleases) {
    foreach ($asset in @($release.assets)) { Add-Download $tracked $asset.name ([long]$asset.download_count) }
}

$lines.Add('## Summary')
$lines.Add('')
$lines.Add('| Metric | Value |')
$lines.Add('| --- | ---: |')
$lines.Add("| Releases | $($releases.Count) |")
$lines.Add("| All downloads | $(Format-Count $totals['total']) |")
$lines.Add("| Installer downloads (x64 / x86) | $(Format-Count $totals['installer']) ($(Format-Count $totals['installer-x64']) / $(Format-Count $totals['installer-x86'])) |")
$lines.Add("| Portable ZIP downloads | $(Format-Count $totals['portable']) |")
$lines.Add("| In-app updates (x64 / x86) | $(Format-Updates $tracked ($trackedReleases.Count -gt 0)) |")
$lines.Add("| New installations, estimate (x64 + x86) | $(Format-Installs $tracked ($trackedReleases.Count -gt 0)) |")
$lines.Add('')

if ($history.Count -gt 0) {
    $lines.Add('## Recent downloads')
    $lines.Add('')
    $lines.Add('| Period | Since | All | Installer | Portable | Updates | New installations |')
    $lines.Add('| --- | --- | ---: | ---: | ---: | ---: | ---: |')
    $current = @{}
    foreach ($row in $snapshot) { $current["$($row.tag)|$($row.asset)"] = $row }
    foreach ($days in @(1, 7, 30)) {
        $limit = $Date.ToUniversalTime().AddDays(-$days).ToString('yyyy-MM-dd')
        $baseDate = @($history | Where-Object { $_.date -le $limit } | Sort-Object date -Descending | Select-Object -First 1)
        if ($baseDate.Count -eq 0) {
            $lines.Add("| $days d | n/a | | | | | |")
            continue
        }
        $base = @{}
        foreach ($row in @($history | Where-Object { $_.date -eq $baseDate[0].date })) { $base["$($row.tag)|$($row.asset)"] = [long]$row.downloads }
        $delta = New-Counters
        $trackedDelta = New-Counters
        foreach ($key in $current.Keys) {
            $previous = 0
            if ($base.ContainsKey($key)) { $previous = $base[$key] }
            $count = [Math]::Max(0, $current[$key].downloads - $previous)
            Add-Download $delta $current[$key].asset $count
            if ($trackedTags.Contains($current[$key].tag)) { Add-Download $trackedDelta $current[$key].asset $count }
        }
        $hasUpdates = $trackedReleases.Count -gt 0
        $lines.Add("| $days d | $($baseDate[0].date) | $(Format-Count $delta['total']) | $(Format-Count $delta['installer']) | $(Format-Count $delta['portable']) | $(Format-Updates $trackedDelta $hasUpdates) | $(Format-Installs $trackedDelta $hasUpdates) |")
    }
    $lines.Add('')
}

$lines.Add('## By release')
$lines.Add('')
$lines.Add('| Release | Published | All | Installer x64 / x86 | Portable | SHA256SUMS | Updates x64 / x86 | New installations |')
$lines.Add('| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |')
foreach ($release in $releases) {
    $c = New-Counters
    foreach ($asset in @($release.assets)) { Add-Download $c $asset.name ([long]$asset.download_count) }
    $hasUpdates = @($release.assets | Where-Object { $_.name -like '*-setup.exe.sha256' }).Count -gt 0
    $published = ([datetime]$release.published_at).ToUniversalTime().ToString('yyyy-MM-dd')
    $name = "[$($release.tag_name)]($($release.html_url))"
    if ($release.prerelease) { $name += ' (pre)' }
    $lines.Add("| $name | $published | $(Format-Count $c['total']) | $(Format-Count $c['installer-x64']) / $(Format-Count $c['installer-x86']) | $(Format-Count $c['portable']) | $(Format-Count $c['checksums']) | $(Format-Updates $c $hasUpdates) | $(Format-Installs $c $hasUpdates) |")
}
$lines.Add('')
$lines.Add('Notes:')
$lines.Add('')
$lines.Add('- GitHub counts download requests, not unique users: repeated or interrupted downloads are counted every time.')
$lines.Add('- Updates are downloads of `*-setup.exe.sha256`, which only the in-app updater requests. "-" means the release has no such files, so updates cannot be told apart from manual downloads.')
$lines.Add('- New installations = installer downloads minus updates, counted only for releases with `*-setup.exe.sha256`. Portable ZIPs are not included.')

$report = $lines -join [Environment]::NewLine
if ($OutputPath) {
    $directory = Split-Path -Parent $OutputPath
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    Set-Content -Path $OutputPath -Value $report -Encoding utf8
}
$report
