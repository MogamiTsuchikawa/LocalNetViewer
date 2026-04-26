[CmdletBinding()]
param(
    [string] $Version = "v0.0.1",
    [string] $Prefix = "release",
    [switch] $NoPush
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

& git diff --quiet
if ($LASTEXITCODE -ne 0) {
    throw "Working tree is not clean. Commit or stash changes before creating a release tag."
}

& git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    throw "Index is not clean. Commit or stash changes before creating a release tag."
}

$stamp = Get-Date -Format "yyMMdd-HHmm"
$tag = "$Prefix/$Version-$stamp"

& git rev-parse $tag *> $null
if ($LASTEXITCODE -eq 0) {
    throw "Tag already exists: $tag"
}

Invoke-Git tag -a $tag -m "Release $tag"

if (-not $NoPush) {
    Invoke-Git push origin $tag
}

Write-Output $tag
