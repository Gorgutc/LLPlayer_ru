# Offline, tiny filesystem/COM fixtures. Never builds, launches, or touches a real installation.
param([ValidateSet('all','hosted-process','record-unknown','reference-alias','native-process','resolved-reference')][string]$ReviewRegression='all')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$helper = Join-Path $PSScriptRoot 'desktop-build-retention.ps1'
$work = Join-Path $repoRoot ('artifacts/desktop-retention-fixture-' + [guid]::NewGuid().ToString('N'))
# Every mutating target below is a fresh literal GUID fixture beneath this physical repo artifacts root.
$ancestor=$work
while ($ancestor) {
    if (Test-Path -LiteralPath $ancestor) {
        $probe = Get-Item -LiteralPath $ancestor -Force
        if (($probe.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Unsafe fixture ancestor.' }
    }
    $parent=[IO.Directory]::GetParent($ancestor); $ancestor=if($parent){$parent.FullName}else{$null}
}
$null = [IO.Directory]::CreateDirectory($work)
$utf8 = New-Object Text.UTF8Encoding($false)
$icon = Join-Path $PSScriptRoot 'assets/llplayer-avalonia-blue.ico'
$owner = 'LLPlayer.Avalonia.DesktopTest'
$script:passed = 0
$shell = New-Object -ComObject WScript.Shell
function Check([bool]$Value,[string]$Message) { if (-not $Value) { throw $Message }; $script:passed++ }
function Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Write-Text([string]$Path,[string]$Text) { $null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)); [IO.File]::WriteAllText($Path,$Text,$utf8) }
function Write-Json([string]$Path,$Value) { Write-Text $Path ($Value | ConvertTo-Json -Depth 9) }
function Read-Json([string]$Path) { Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json }
function New-Fixture([string]$Name) {
    $root = Join-Path $work ($Name+'/install'); $desktop = Join-Path $work ($Name+'/desktop')
    $null = [IO.Directory]::CreateDirectory((Join-Path $root 'builds')); $null = [IO.Directory]::CreateDirectory($desktop)
    Write-Json (Join-Path $root 'owner.json') @{schema=2;owner=$owner;installRoot=$root;shortcutName='LLPlayer Avalonia (Windows).lnk'}
    $f = [pscustomobject]@{ root=$root; desktop=$desktop; builds=@{} }
    foreach ($id in @('a','b','c','d')) {
        $path = Join-Path $root ('builds/'+$id); $payload = Join-Path $path 'payload'
        Write-Text (Join-Path $payload 'LLPlayer.Avalonia.exe') ('fixture-'+$id)
        Write-Text (Join-Path $payload 'LLPlayer.Avalonia.dll') ('fixture-assembly-'+$id)
        Write-Text (Join-Path $payload 'lib/library.dll') ('library-'+$id)
        Write-Text (Join-Path $payload 'Plugins/YoutubeDL/YoutubeDL.dll') ('plugin-'+$id)
        Write-Text (Join-Path $payload 'Plugins/YoutubeDL/YoutubeDL.pdb') ('plugin-symbols-'+$id)
        [IO.File]::Copy($icon,(Join-Path $path 'blue.ico'))
        $files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object { @{path=$_.FullName.Substring($payload.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Hash $_.FullName)} })
        $r = [pscustomobject]@{schema=2;owner=$owner;buildId=$id;buildMode='prebuilt-validated';sourceCommit=$null;createdUtc=('2026-11-0'+(1+[int][char]$id-[int][char]'a')+'T00:00:00Z');repository=$repoRoot;packageSource=(Join-Path $work 'source-package');sourceArchive=$null;sourceArchiveSha256=$null;payloadPath=$payload;executablePath=(Join-Path $payload 'LLPlayer.Avalonia.exe');executableSha256=(Hash (Join-Path $payload 'LLPlayer.Avalonia.exe'));workingDirectory=$payload;targetArguments='';iconPath=(Join-Path $path 'blue.ico');iconSha256=(Hash $icon);shortcutPath=(Join-Path $desktop 'LLPlayer Avalonia (Windows).lnk');shortcutSha256='';files=$files}
        Write-Json (Join-Path $path 'receipt.json') $r
        $f.builds[$id] = $path
    }
    Select-Build $f 'c'
    return $f
}
function Select-Build($Fixture,[string]$Id) {
    $receiptPath = Join-Path $Fixture.builds[$Id] 'receipt.json'; $r = Read-Json $receiptPath
    $link = $shell.CreateShortcut($r.shortcutPath)
    try { $link.TargetPath=$r.executablePath; $link.WorkingDirectory=$r.workingDirectory; $link.Arguments=''; $link.IconLocation=$r.iconPath+',0'; $link.Save() }
    finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
    $r.shortcutSha256 = Hash $r.shortcutPath; Write-Json $receiptPath $r
    $c = @{schema=2;owner=$owner;buildId=$Id;sourceCommit=$r.sourceCommit;receiptPath=$receiptPath;receiptSha256=(Hash $receiptPath)}
    foreach ($field in @('executablePath','executableSha256','workingDirectory','iconPath','iconSha256','shortcutPath','shortcutSha256')) { $c[$field]=$r.$field }
    Write-Json (Join-Path $Fixture.root 'current.json') $c
}
function Invoke-Retention($Fixture,[hashtable]$Options=@{}) {
    & $helper -InstallRoot $Fixture.root -DesktopPath $Fixture.desktop -IconPath $icon -ReferenceRoots @($Fixture.desktop) @Options
}
function Kept($Fixture,[string]$Id) { return Test-Path -LiteralPath $Fixture.builds[$Id] -PathType Container }
function Rejected([scriptblock]$Action,[string]$Pattern) {
    try { & $Action | Out-Null } catch { Check ($_.Exception.Message -match $Pattern) ('Wrong rejection: '+$_.Exception.Message); return }
    throw 'Unsafe retention was accepted.'
}
function Restore-NewestBuild($Fixture) {
    Check (-not (Test-Path -LiteralPath $Fixture.builds.d)) 'Fixture donor target must be absent; an earlier prune did not complete.'
    $g = New-Fixture ('donor-'+[guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $g.builds.d -Destination $Fixture.builds.d -Recurse
    $rPath=Join-Path $Fixture.builds.d 'receipt.json'; $r=Read-Json $rPath
    $r.payloadPath=Join-Path $Fixture.builds.d 'payload'; $r.workingDirectory=$r.payloadPath
    $r.executablePath=Join-Path $r.payloadPath 'LLPlayer.Avalonia.exe'; $r.iconPath=Join-Path $Fixture.builds.d 'blue.ico'
    $r.shortcutPath=Join-Path $Fixture.desktop 'LLPlayer Avalonia (Windows).lnk'
    Write-Json $rPath $r
}
try {
    if ($ReviewRegression -in @('all','resolved-reference')) {
        $f=New-Fixture 'resolved-reference'
        $null=Invoke-Retention $f # Loads the same native type used by physical paths and read-only references.
        Check ([LLPlayerRetention.Native]::NormalizeResolvedReference('C:\fixture\LLPlayer.Avalonia.exe') -ceq 'C:\fixture\LLPlayer.Avalonia.exe') 'A resolved local drive path must remain usable.'
        Check ([LLPlayerRetention.Native]::NormalizeResolvedReference('\\?\C:\fixture\LLPlayer.Avalonia.exe') -ceq 'C:\fixture\LLPlayer.Avalonia.exe') 'An extended local drive path must normalize to its local path.'
        foreach ($unsupported in @('\\server\share\LLPlayer.Avalonia.exe','\\?\UNC\server\share\LLPlayer.Avalonia.exe','\\?\Volume{11111111-1111-1111-1111-111111111111}\LLPlayer.Avalonia.exe','UNC\server\share\LLPlayer.Avalonia.exe')) {
            Rejected { [LLPlayerRetention.Native]::NormalizeResolvedReference($unsupported) } 'absolute local drive path'
        }
        if ($ReviewRegression -eq 'resolved-reference') { Write-Host 'Resolved-reference normalization review regressions passed.'; return }
    }
    if ($ReviewRegression -in @('all','native-process')) {
        $f=New-Fixture 'native-process-alias'
        $alias=Join-Path $work 'native-image-alias'
        $null=New-Item -ItemType Junction -Path $alias -Target (Join-Path $f.builds.a 'payload')
        $result=Invoke-Retention $f @{Apply=$true;ProcessDescriptorsForTests=@([pscustomobject]@{ProcessName='LLPlayer.Avalonia';Path=(Join-Path $alias 'LLPlayer.Avalonia.exe')})}
        Check ((Kept $f 'a') -and -not (Kept $f 'b')) 'A native LLPlayer image through an alias must protect its physical build while unrelated old builds prune.'
        foreach ($unknownPath in @($null,(Join-Path $work 'missing-image/LLPlayer.Avalonia.exe'),'relative/LLPlayer.Avalonia.exe')) {
            $f=New-Fixture ('unknown-native-process-'+[guid]::NewGuid().ToString('N'))
            $result=Invoke-Retention $f @{Apply=$true;ProcessDescriptorsForTests=@([pscustomobject]@{ProcessName='LLPlayer.Avalonia';Path=$unknownPath})}
            Check ((Kept $f 'a') -and (Kept $f 'b') -and $result.deletedBuilds.Count -eq 0) 'An unresolved native LLPlayer image must preserve all prune candidates.'
        }
        if ($ReviewRegression -eq 'native-process') { Write-Host 'Native-process review regressions passed.'; return }
    }
    if ($ReviewRegression -in @('all','reference-alias')) {
        $f=New-Fixture 'known-programs-alias'
        $menu=Join-Path $work 'known-reference-menu'; $programs=Join-Path $menu 'Programs'
        $null=[IO.Directory]::CreateDirectory($programs)
        $null=New-Item -ItemType Junction -Path (Join-Path $menu 'LocalizedPrograms') -Target $programs
        $r=Read-Json (Join-Path $f.builds.a 'receipt.json')
        $link=$shell.CreateShortcut((Join-Path $programs 'User pinned older build.lnk'))
        try { $link.TargetPath=$r.executablePath; $link.Save() } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
        $result=& $helper -InstallRoot $f.root -DesktopPath $f.desktop -IconPath $icon -ReferenceRoots @($f.desktop,$menu,$programs) -Apply
        Check ((Kept $f 'a') -and -not (Kept $f 'b')) 'A covered Programs alias must not block pruning or hide its canonical shortcut.'
        $f=New-Fixture 'unknown-reference-alias'
        $unknownMenu=Join-Path $work 'unknown-reference-menu'; $outside=Join-Path $work 'unknown-reference-target'
        $null=[IO.Directory]::CreateDirectory($unknownMenu); Write-Text (Join-Path $outside 'sentinel.txt') 'REFERENCE SENTINEL'
        $null=New-Item -ItemType Junction -Path (Join-Path $unknownMenu 'UnknownAlias') -Target $outside
        $result=& $helper -InstallRoot $f.root -DesktopPath $f.desktop -IconPath $icon -ReferenceRoots @($f.desktop,$unknownMenu) -Apply
        Check ((Kept $f 'a') -and (Kept $f 'b') -and $result.deletedBuilds.Count -eq 0) 'Unknown reference alias must preserve candidates.'
        Check ((Get-Content -LiteralPath (Join-Path $outside 'sentinel.txt') -Raw) -ceq 'REFERENCE SENTINEL') 'Read-only alias inventory changed its target.'
        if ($ReviewRegression -eq 'reference-alias') { Write-Host 'Reference-alias review regressions passed.'; return }
    }
    if ($ReviewRegression -ne 'record-unknown') {
    $f = New-Fixture 'hosted-process'
    $result = Invoke-Retention $f @{Apply=$true;ProcessDescriptorsForTests=@([pscustomobject]@{ProcessName='dotnet';Path='C:\fixture-host\dotnet.exe';CommandLine=('dotnet "'+(Join-Path $f.builds.a 'payload/LLPlayer.Avalonia.dll')+'"')})}
    Check ((Kept $f 'a') -and -not (Kept $f 'b')) 'A dotnet-hosted old assembly must be protected while an unrelated old build prunes.'
    $f = New-Fixture 'unknown-hosted-process'
    $result = Invoke-Retention $f @{Apply=$true;ProcessDescriptorsForTests=@([pscustomobject]@{ProcessName='dotnet';Path='C:\fixture-host\dotnet.exe';CommandLine=$null})}
    Check ((Kept $f 'a') -and (Kept $f 'b') -and $result.deletedBuilds.Count -eq 0) 'An unreadable dotnet command line must preserve candidates.'
    if ($ReviewRegression -eq 'hosted-process') { Write-Host 'Hosted-process review regressions passed.'; return }
    }
    if ($ReviewRegression -eq 'all') {
    $f = New-Fixture 'preview'
    $preview = Invoke-Retention $f
    Check ($preview.mode -eq 'preview' -and @(Get-ChildItem -LiteralPath (Join-Path $f.root 'builds')).Count -eq 4) 'Preview mutated builds.'
    Check ($preview.retainedBuildIds -contains 'c' -and $preview.retainedBuildIds -contains 'd') 'Fallback must be newest verified when no previous selection is given.'
    $result = Invoke-Retention $f @{Apply=$true;PreviousBuildId='a'}
    Check ((Kept $f 'c') -and (Kept $f 'a') -and -not (Kept $f 'b') -and -not (Kept $f 'd')) 'Keep two must prioritize current and verified previous selection.'
    Check ($result.deletedBuilds.Count -eq 2) 'Expected two actual build removals.'

    $f = New-Fixture 'modified-extra-model'
    Write-Text (Join-Path $f.builds.a 'payload/lib/library.dll') 'modified'
    Write-Text (Join-Path $f.builds.b 'payload/Models/model.gguf') 'MODEL SENTINEL'
    Write-Text (Join-Path $f.builds.b 'payload/settings.json') 'SETTINGS SENTINEL'
    $modelHash = Hash (Join-Path $f.builds.b 'payload/Models/model.gguf')
    $result = Invoke-Retention $f @{Apply=$true}
    Check ((Kept $f 'a') -and (Kept $f 'b') -and $result.deletedBuilds.Count -eq 0) 'Modified/extra models/settings must be additive protected extras.'
    Check ((Hash (Join-Path $f.builds.b 'payload/Models/model.gguf')) -ceq $modelHash) 'Model sentinel changed.'
    Check ($result.protected.Count -ge 2) 'Protection reasons must be reported.'

    $f = New-Fixture 'empty-directory'
    $null = [IO.Directory]::CreateDirectory((Join-Path $f.builds.a 'payload/Models'))
    $result = Invoke-Retention $f @{Apply=$true}
    Check (Kept $f 'a') 'Unknown empty directory was removed.'

    $f = New-Fixture 'bad-receipts'
    $rPath = Join-Path $f.builds.a 'receipt.json'; $r = Read-Json $rPath; $r.files[0].path='../sentinel'; Write-Json $rPath $r
    $rPath = Join-Path $f.builds.b 'receipt.json'; $r = Read-Json $rPath; $r.owner='foreign'; Write-Json $rPath $r
    $result = Invoke-Retention $f @{Apply=$true}
    Check ((Kept $f 'a') -and (Kept $f 'b')) 'Traversal/foreign receipts were removed.'
    $cPath=Join-Path $f.root 'current.json'; $c=Read-Json $cPath; $c.receiptPath=Join-Path $work 'foreign.json'; Write-Json $cPath $c
    Rejected { Invoke-Retention $f @{Apply=$true} } 'Current receipt identity differs'

    $f = New-Fixture 'references'
    $r = Read-Json (Join-Path $f.builds.a 'receipt.json')
    $link = $shell.CreateShortcut((Join-Path $f.desktop 'another-owned-by-user.lnk'))
    try { $link.TargetPath=$r.executablePath; $link.Save() } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
    $result = Invoke-Retention $f @{Apply=$true;RunningPathsForTests=@((Join-Path $f.builds.b 'payload/LLPlayer.Avalonia.exe'))}
    Check ((Kept $f 'a') -and (Kept $f 'b') -and (Kept $f 'c') -and (Kept $f 'd')) 'Referenced/running extras displaced fallback or were removed.'

    $f = New-Fixture 'junction'
    $sentinel = Join-Path $work 'junction-target'; Write-Text (Join-Path $sentinel 'sentinel.txt') 'OUTSIDE SENTINEL'
    $null = New-Item -ItemType Junction -Path (Join-Path $f.builds.a 'payload/linked') -Target $sentinel
    $result = Invoke-Retention $f @{Apply=$true}
    Check ((Kept $f 'a') -and (Test-Path -LiteralPath (Join-Path $sentinel 'sentinel.txt'))) 'Junction sentinel was traversed/deleted.'
    $f = New-Fixture 'hardlink'
    $hardlink = Join-Path $work 'hardlink-sentinel.dll'
    $null = New-Item -ItemType HardLink -Path $hardlink -Target (Join-Path $f.builds.a 'payload/lib/library.dll')
    $result = Invoke-Retention $f @{Apply=$true}
    Check ((Kept $f 'a') -and (Test-Path -LiteralPath $hardlink)) 'Hardlinked build was removed.'

    $f = New-Fixture 'candidate-drift'
    $hook = { param($candidate,$currentPath) [IO.File]::AppendAllText((Join-Path $candidate 'payload/LLPlayer.Avalonia.exe'),'drift') }
    $result = Invoke-Retention $f @{Apply=$true;BeforeDeleteForTests=$hook}
    Check ((Kept $f 'a') -and (Kept $f 'b') -and $result.deletedBuilds.Count -eq 0) 'Changed candidate was removed.'
    $f = New-Fixture 'current-drift'
    $hook = { param($candidate,$currentPath) [IO.File]::AppendAllText($currentPath,' ') }
    $result = Invoke-Retention $f @{Apply=$true;BeforeDeleteForTests=$hook}
    Check ((Kept $f 'a') -and (Kept $f 'b') -and $result.deletedBuilds.Count -eq 0) 'Current CAS drift allowed mutation.'
    $f = New-Fixture 'delete-failure'
    $currentHash = Hash (Join-Path $f.root 'current.json')
    $result = Invoke-Retention $f @{Apply=$true;FailDeleteForTests=$true}
    Check ($result.status -eq 'partial' -and (Kept $f 'a') -and (Hash (Join-Path $f.root 'current.json')) -ceq $currentHash) 'Prune failure changed activation.'
    }

    # A future fresh-publish workspace has its own ownership+complete tree manifest; arbitrary packages never do.
    $jobCases=if($ReviewRegression -eq 'record-unknown'){@('record-unknown','adopt-fallback','reject-unselected')}else{@('clean','modified','dependency','record-unknown','adopt-fallback','reject-unselected')}
    foreach ($jobCase in $jobCases) {
    $f = New-Fixture ('publish-job-'+$jobCase)
    Select-Build $f 'a'
    $job = Join-Path $repoRoot ('artifacts/windows-desktop-test-build-'+[guid]::NewGuid().ToString('N'))
    $packageName = 'LLPlayer-Avalonia-0.0-fixture-win-x64'
    $package = Join-Path $job ('staging/'+$packageName)
    $null = [IO.Directory]::CreateDirectory((Join-Path $job 'staging/portable-plugin'))
    $null = [IO.Directory]::CreateDirectory((Join-Path $job 'staging/archive-readback'))
    Copy-Item -LiteralPath (Join-Path $f.builds.a 'payload') -Destination $package -Recurse
    Copy-Item -LiteralPath $package -Destination (Join-Path $job ('staging/archive-readback/'+$packageName)) -Recurse
    foreach($pluginFile in @('YoutubeDL.dll','YoutubeDL.pdb')) { [IO.File]::Copy((Join-Path $package ('Plugins/YoutubeDL/'+$pluginFile)),(Join-Path $job ('staging/portable-plugin/'+$pluginFile))) }
    Write-Text (Join-Path $job 'staging/portable-plugin/YoutubeDL.deps.json') '{}'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive=Join-Path $job ('archive/'+$packageName+'.zip'); $null=[IO.Directory]::CreateDirectory((Join-Path $job 'archive'))
    [IO.Compression.ZipFile]::CreateFromDirectory($package,$archive)
    [IO.File]::Copy($archive,(Join-Path $job ('staging/'+$packageName+'.zip')))
    Write-Text ($archive+'.sha256') ((Hash $archive)+'  '+$packageName+'.zip')
    $rPath=Join-Path $f.builds.a 'receipt.json'; $r=Read-Json $rPath; $r.packageSource=$package; $r.sourceArchive=$archive; $r.sourceArchiveSha256=Hash $archive; Write-Json $rPath $r
    Select-Build $f 'a'
    if ($jobCase -eq 'record-unknown') {
        Write-Text (Join-Path $job 'staging/portable-plugin/important-notes.txt') 'OWNER NOTES'
        Write-Text (Join-Path $job 'staging/portable-plugin/user-video.mp4') 'OWNER VIDEO'
    }
    if ($jobCase -eq 'reject-unselected') {
        Select-Build $f 'c'
        $result=Invoke-Retention $f @{Apply=$true;CreatedPublishJob=$job}
        Check (-not (Test-Path -LiteralPath (Join-Path $job 'desktop-publish-owner.json'))) 'An unselected/unretained job was adopted.'
        Check (($result.errors -join ' ') -match 'fully verified retained') 'Unretained ownership rejection was not reported.'
        Check (Test-Path -LiteralPath $archive) 'Rejected unretained source job was removed.'
        continue
    }
    if ($jobCase -eq 'adopt-fallback') {
        $result=Invoke-Retention $f @{Apply=$true;PreviousBuildId='c'}
        Select-Build $f 'c'
        $preview=Invoke-Retention $f @{CreatedPublishJob=$job;PreviousBuildId='a'}
        Check (-not (Test-Path -LiteralPath (Join-Path $job 'desktop-publish-owner.json'))) 'Preview adopted a fallback job.'
        $result=Invoke-Retention $f @{Apply=$true;CreatedPublishJob=$job;PreviousBuildId='a'}
        Check ($result.retainedBuildIds -contains 'a' -and $result.currentBuildId -ceq 'c') 'Explicit migration changed the selected build.'
    }
    else { $result=Invoke-Retention $f @{Apply=$true;CreatedPublishJob=$job;PreviousBuildId='c'} }
    if (($jobCase -ne 'record-unknown' -and $result.errors.Count) -or (Kept $f 'b') -or (Kept $f 'd')) { throw ('Initial publish-job retention did not complete: '+($result | ConvertTo-Json -Depth 8 -Compress)) }
    if ($jobCase -eq 'record-unknown') {
        Check (-not (Test-Path -LiteralPath (Join-Path $job 'desktop-publish-owner.json'))) 'Unknown portable-plugin files must not be adopted into a deletion manifest.'
    }
    else { Check (Test-Path -LiteralPath (Join-Path $job 'desktop-publish-owner.json')) 'Successful fresh job did not get scoped ownership evidence.' }
    Check (Test-Path -LiteralPath $archive) 'Retained source archive was removed.'
    Restore-NewestBuild $f
    if ($jobCase -eq 'modified') { Write-Text (Join-Path $package 'settings.json') 'PRESERVE USER SETTINGS' }
    if ($jobCase -eq 'dependency') {
        $rPath=Join-Path $f.builds.d 'receipt.json'; $r=Read-Json $rPath; $r.packageSource=$package; $r.sourceArchive=$archive; $r.sourceArchiveSha256=Hash $archive; Write-Json $rPath $r
    }
    Select-Build $f 'c'
    $result=Invoke-Retention $f @{Apply=$true}
    Check (-not (Kept $f 'a')) 'Old verified build did not prune.'
    if ($jobCase -in @('clean','adopt-fallback')) {
        Check (-not (Test-Path -LiteralPath $job) -and $result.deletedJobs.Count -eq 1) 'Unreferenced owned publish workspace did not prune.'
    }
    else { Check (Test-Path -LiteralPath $archive) 'Changed/dependent publish workspace was removed.' }
    if ($jobCase -eq 'modified') { Check ((Get-Content -LiteralPath (Join-Path $package 'settings.json') -Raw) -ceq 'PRESERVE USER SETTINGS') 'Publish workspace user settings changed.' }
    if ($jobCase -eq 'record-unknown') {
        Check ((Get-Content -LiteralPath (Join-Path $job 'staging/portable-plugin/important-notes.txt') -Raw) -ceq 'OWNER NOTES') 'Pre-marker owner notes changed.'
        Check ((Get-Content -LiteralPath (Join-Path $job 'staging/portable-plugin/user-video.mp4') -Raw) -ceq 'OWNER VIDEO') 'Pre-marker owner video changed.'
    }
    }
    if ($ReviewRegression -eq 'all') { Check ((Hash (Join-Path $work 'hardlink-sentinel.dll')) -eq (Hash (Join-Path (Join-Path $work 'hardlink/install/builds/a') 'payload/lib/library.dll'))) 'Hardlink sentinel changed.' }

    Write-Host "Desktop retention contracts passed: $script:passed. Fixtures: $work"
    [pscustomobject]@{status='PASS';assertions=$script:passed;fixtureRoot=$work}
}
finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
