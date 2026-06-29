<#
.SYNOPSIS
  One-command release: bump version, build both single-file exes, commit/push,
  and publish a GitHub release with both builds attached.

.EXAMPLE
  ./release.ps1            # bump minor (1.0.0 -> 1.1.0), tag v1.1
  ./release.ps1 -Major     # bump major (1.1.0 -> 2.0.0), tag v2.0
  ./release.ps1 -Patch     # bump patch  (1.1.0 -> 1.1.1), tag v1.1.1
  ./release.ps1 1.5        # set an explicit version
#>
param(
    [string]$Version = "",   # explicit "major.minor[.patch]"; empty = bump minor
    [switch]$Major,          # bump major instead of minor
    [switch]$Patch           # bump patch (third number) for a bugfix release
)

$ErrorActionPreference = 'Stop'
$repo   = "ITMarco/ADGroupBrowser"
$csproj = "ADGroupBrowser.csproj"

# ── 1. Work out the new version ──────────────────────────────────────────────
[xml]$xml = Get-Content $csproj
$verNode  = $xml.SelectSingleNode('//Version')
if (-not $verNode) { throw "No <Version> element in $csproj" }
$cur = [version]$verNode.InnerText

if     ($Version) { $nv = [version]($(if ($Version -match '\.') { $Version } else { "$Version.0" })) }
elseif ($Major)   { $nv = [version]"$($cur.Major + 1).0.0" }
elseif ($Patch)   { $nv = [version]"$($cur.Major).$($cur.Minor).$([math]::Max($cur.Build,0) + 1)" }
else              { $nv = [version]"$($cur.Major).$($cur.Minor + 1).0" }

$build  = [math]::Max($nv.Build, 0)
$newVer = "$($nv.Major).$($nv.Minor).$build"
# Patch releases tag as vMAJOR.MINOR.PATCH; minor/major use the clean vMAJOR.MINOR tag.
$tag    = if ($build -gt 0) { "v$($nv.Major).$($nv.Minor).$build" }
          else              { "v$($nv.Major).$($nv.Minor)" }
Write-Host "Releasing $tag (version $newVer, was $cur)" -ForegroundColor Cyan

# ── 2. Update csproj + commit + push ─────────────────────────────────────────
$verNode.InnerText = $newVer
# Also keep AssemblyVersion and FileVersion in sync
$xml.SelectSingleNode('//AssemblyVersion').InnerText = $newVer
$xml.SelectSingleNode('//FileVersion').InnerText     = $newVer
$xml.Save((Resolve-Path $csproj))

git add -A
if (git status --porcelain) {
    git commit -m "Release $tag"
}
git push origin main
if ($LASTEXITCODE -ne 0) {
    throw "git push failed. Run 'git pull --rebase origin main', then re-run release.ps1."
}

# ── 3. Build both single-file builds ─────────────────────────────────────────
$dist = "dist"
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue

$common = @(
    $csproj, "-c", "Release", "-r", "win-x64",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=none"
)

Write-Host "Building self-contained (standalone)..." -ForegroundColor Cyan
dotnet publish @common --self-contained true -p:EnableCompressionInSingleFile=true -o "$dist/standalone"

Write-Host "Building framework-dependent..." -ForegroundColor Cyan
dotnet publish @common --self-contained false -o "$dist/framework"

Copy-Item "$dist/framework/ADGroupBrowser.exe"  "$dist/ADGroupBrowser.exe"            -Force
Copy-Item "$dist/standalone/ADGroupBrowser.exe" "$dist/ADGroupBrowser-standalone.exe" -Force

$shaFramework  = (Get-FileHash "$dist/ADGroupBrowser.exe"            -Algorithm SHA256).Hash
$shaStandalone = (Get-FileHash "$dist/ADGroupBrowser-standalone.exe" -Algorithm SHA256).Hash

# ── 4. Get a GitHub token from the git credential store ──────────────────────
$cred  = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
$token = ($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=', ''
if (-not $token) { throw "Could not get a GitHub token from the credential store." }

$headers = @{
    Authorization = "token $token"
    "User-Agent"  = "ADGroupBrowser-release"
    Accept        = "application/vnd.github+json"
}

# ── 5. Create the GitHub release ─────────────────────────────────────────────
$notes = @"
AD Group Browser $tag — browse on-premises Active Directory group memberships from Azure AD-joined PCs.

Downloads:
- **ADGroupBrowser.exe** (~0.4 MB) — requires the free [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0)
- **ADGroupBrowser-standalone.exe** (~50 MB) — fully self-contained, no .NET install needed

### Setup
Copy ``ADGroupBrowser.exe`` and ``config.sample.json`` to a folder, rename the sample to ``config.json``, and edit it. See the [README](https://github.com/$repo#technical-manual--admin-setup) for the full admin guide.

### Checksums (SHA-256)
- ADGroupBrowser.exe: $shaFramework
- ADGroupBrowser-standalone.exe: $shaStandalone
"@

$body = @{
    tag_name         = $tag
    target_commitish = "main"
    name             = "AD Group Browser $tag"
    body             = $notes
    draft            = $false
    prerelease       = $false
} | ConvertTo-Json

$rel        = Invoke-RestMethod -Method Post -Headers $headers -ContentType "application/json" `
                -Uri "https://api.github.com/repos/$repo/releases" -Body $body
$uploadBase = "https://uploads.github.com/repos/$repo/releases/$($rel.id)/assets"

# ── 6. Upload assets ─────────────────────────────────────────────────────────
foreach ($asset in @("ADGroupBrowser.exe", "ADGroupBrowser-standalone.exe")) {
    Write-Host "Uploading $asset..." -ForegroundColor Cyan
    Invoke-RestMethod -Method Post -Headers $headers -ContentType "application/octet-stream" `
        -Uri ("{0}?name={1}" -f $uploadBase, $asset) -InFile "$dist/$asset" | Out-Null
}

Write-Host "`nReleased: $($rel.html_url)" -ForegroundColor Green
