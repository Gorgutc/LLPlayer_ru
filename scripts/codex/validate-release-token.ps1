[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Ref", "Tag", "Hash", "Archive")]
    [string]$Kind,

    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$Value,

    [ValidatePattern('^[A-Za-z][A-Za-z0-9_-]*$')]
    [string]$OutputName,

    [string]$OutputFile
)

$ErrorActionPreference = "Stop"

function Fail-Token([string]$Reason) {
    throw "Invalid release $($Kind.ToLowerInvariant()) token: $Reason"
}

if ($Value -match '[\r\n]') {
    Fail-Token "line breaks are not allowed."
}

foreach ($character in $Value.ToCharArray()) {
    $codePoint = [int]$character
    if ($codePoint -lt 32 -or $codePoint -eq 127) {
        Fail-Token "control characters are not allowed."
    }
}

$token = $Value.Trim()
if (-not $token) {
    Fail-Token "the value is empty."
}
if ($token -ne $Value) {
    Fail-Token "leading or trailing whitespace is not allowed."
}

switch ($Kind) {
    "Ref" {
        if ($token.Length -gt 255) {
            Fail-Token "the ref is longer than 255 characters."
        }
        if ($token.StartsWith("-", [System.StringComparison]::Ordinal)) {
            Fail-Token "the ref must not start with '-'."
        }
        if ($token.StartsWith("refs/", [System.StringComparison]::Ordinal) -and
            $token -notmatch '^refs/(?:heads|tags)/') {
            Fail-Token "only refs/heads/* and refs/tags/* full refs are allowed."
        }
        if ($token -match '^[0-9A-Fa-f]{7,39}$') {
            Fail-Token "abbreviated commit ids are not supported; use the full 40-character id or a named ref."
        }
        if ($token -notmatch '^(?:[0-9A-Fa-f]{40}|(?:refs/(?:heads|tags)/)?[A-Za-z0-9][A-Za-z0-9._/+\-]*)$') {
            Fail-Token "only a full hexadecimal commit id or a heads/tags ref using path-safe characters is allowed."
        }
        if ($token.Contains("..") -or $token.Contains("@{") -or $token.Contains("//") -or
            $token.EndsWith("/", [System.StringComparison]::Ordinal) -or
            $token.EndsWith(".", [System.StringComparison]::Ordinal)) {
            Fail-Token "the ref contains a forbidden Git ref sequence."
        }

        $refBody = $token -replace '^refs/(?:heads|tags)/', ''
        foreach ($segment in $refBody.Split('/')) {
            if (-not $segment -or $segment.StartsWith(".", [System.StringComparison]::Ordinal) -or
                $segment.EndsWith(".lock", [System.StringComparison]::OrdinalIgnoreCase)) {
                Fail-Token "the ref contains a forbidden path segment."
            }
        }

        if ($token -notmatch '^[0-9A-Fa-f]{40}$') {
            $checkRef = if ($token.StartsWith("refs/", [System.StringComparison]::Ordinal)) {
                $token
            }
            else {
                "refs/heads/$token"
            }
            & git check-ref-format $checkRef *> $null
            if ($LASTEXITCODE -ne 0) {
                Fail-Token "Git rejected the ref format."
            }
        }
    }
    "Tag" {
        if ($token.Length -gt 64 -or $token -notmatch '^[0-9A-Za-z][0-9A-Za-z._+\-]*$') {
            Fail-Token "the tag must be a path-safe token of at most 64 characters."
        }
        if ($token.Contains("..") -or $token.EndsWith(".", [System.StringComparison]::Ordinal) -or
            $token.EndsWith(".lock", [System.StringComparison]::OrdinalIgnoreCase)) {
            Fail-Token "the tag contains a forbidden sequence."
        }
    }
    "Hash" {
        if ($token -notmatch '^[0-9A-Fa-f]{7,40}$') {
            Fail-Token "the hash must contain 7 to 40 hexadecimal characters."
        }
        $token = $token.ToLowerInvariant()
    }
    "Archive" {
        if ($token.Length -gt 160 -or $token -notmatch '^LLPlayer-[0-9A-Za-z][0-9A-Za-z._+\-]{0,139}\.7z$') {
            Fail-Token "the archive must be a single LLPlayer-*.7z basename."
        }
        if ($token.Contains("..")) {
            Fail-Token "the archive contains a forbidden sequence."
        }
    }
}

if ([bool]$OutputName -xor [bool]$OutputFile) {
    throw "OutputName and OutputFile must be supplied together."
}
if ($OutputName) {
    $line = "$OutputName=$token$([Environment]::NewLine)"
    [System.IO.File]::AppendAllText($OutputFile, $line, [System.Text.UTF8Encoding]::new($false))
}

Write-Output $token
