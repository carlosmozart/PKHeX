# Testes de regressao headless do PKHeX.Modern.
# Cada teste roda numa pasta temporaria com COPIAS dos saves de ../../saves (os originais nunca sao alterados).
# Uso: powershell -File Tools/PKHeX.Modern.Tests/run-tests.ps1 [Nome...]   (sem nomes = todos)
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Only)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$saves = Join-Path $root 'saves'
if ($env:PKHEX_TEST_SAVES) { $saves = $env:PKHEX_TEST_SAVES }
# nome usado pelo teste -> arquivo em saves/
$suites = [ordered]@{
    'Roamers' = @{ 'em.sav' = 'Pokemon Emerald Version.sav'; 'sa.sav' = 'Sapphire.sav'; 'fr.sav' = 'fire red.sav'; 'y.sav' = 'Pokemon Y' }
    'Clock' = @{ 'em.sav' = 'Pokemon Emerald Version.sav'; 'sa.sav' = 'Sapphire.sav' }
    'GameHistory' = @{ 'em.sav' = 'Pokemon Emerald Version.sav'; 'or.sav' = 'Omega Ruby.sav'; 'sh.sav' = 'shield'; 'sv.sav' = 'Scarlet' }
    'Raids' = @{ 'sh.sav' = 'shield'; 'sv.sav' = 'Scarlet' }
    'Qr' = @{ 'moon.sav' = 'moon.sav' }
    'Daycare' = @{ 'cr.sav' = 'Pokemon Crystal Version.sav'; 'em.sav' = 'Pokemon Emerald Version.sav'; 'bw.sav' = 'Pokemon - Black Version.sav' }
    'WonderCards' = @{ 'hg.sav' = 'Pokemon Heart Gold Version.sav'; 'bw.sav' = 'Pokemon - Black Version.sav'; 'or.sav' = 'Omega Ruby.sav' }
    'BoxFolders' = @{ 'fr.sav' = 'fire red.sav'; 'moon.sav' = 'moon.sav' }
    'HiddenPower' = @{ 'fr.sav' = 'fire red.sav'; 'y.sav' = 'Pokemon Y'; 'cr.sav' = 'Pokemon Crystal Version.sav' }
    'Pokerus' = @{ 'fr.sav' = 'fire red.sav'; 'moon.sav' = 'moon.sav' }
    'Android' = @{}
    # Sprites do porte x PKHeX original (System.Drawing), pixel a pixel; le os saves de saves/ direto, so em memoria
    'SpriteParity' = @{}
    'SaveFocus' = @{}
    'Search' = @{}
    'Batch' = @{}
    'Game' = @{ 'red.sav' = 'POKEMON RED-0.sav'; 'cr.sav' = 'Pokemon Crystal Version.sav'; 'fr.sav' = 'fire red.sav'; 'hg.sav' = 'Pokemon Heart Gold Version.sav'; 'bw.sav' = 'Pokemon - Black Version.sav'; 'y.sav' = 'Pokemon Y'; 'moon.sav' = 'moon.sav'; 'em.sav' = 'Pokemon Emerald Version.sav' }
    'NameHint' = @{ 'r.sav' = 'Ruby.sav'; 'red.sav' = 'POKEMON RED-0.sav'; 'fr.sav' = 'fire red.sav'; 'cr.sav' = 'Pokemon Crystal Version.sav' }
    'Home' = @{}
    'BoxTabs' = @{}
    'SidebarSaves' = @{}
    'Fonts' = @{}
    'GameEvents' = @{ 'sv.sav' = 'Scarlet'; 'red.sav' = 'POKEMON RED-0.sav'; 'yw.sav' = 'Yellow.sav'; 'za.sav' = 'ZA.sav'; 'sa.sav' = 'Sapphire.sav'; 'hg.sav' = 'Pokemon Heart Gold Version.sav'; 'or.sav' = 'Omega Ruby.sav'; 'fr.sav' = 'fire red.sav'; 'moon.sav' = 'moon.sav'; 'em.sav' = 'Pokemon Emerald Version.sav' }
    'Language' = @{}
    'Friendship' = @{}
    'Feebas' = @{}
    'SaveAndGifts' = @{}
    'Updates' = @{}
    'SaveManager' = @{}
    'Layout'   = @{ 'fr.sav' = 'fire red.sav'; 'hg.sav' = 'Pokemon Heart Gold Version.sav'; 'sh.sav' = 'shield' }
    'Shiny'    = @{ 's.sav' = 'Sapphire.sav'; 'sh.sav' = 'shield' }
    'Forms'    = @{ 'sa.sav' = 'Sapphire.sav'; 'sh.sav' = 'shield'; 'em.sav' = 'Pokemon Emerald Version.sav' }
    'Yellow'   = @{ 'y.sav' = 'Yellow.sav' }
    'Tabs'     = @{ 'fr.sav' = 'fire red.sav'; 'hg.sav' = 'Pokemon Heart Gold Version.sav'; 'em.sav' = 'Pokemon Emerald Version.sav' }
    'Variants' = @{ 'fr.sav' = 'fire red.sav' }
    'Fixes'    = @{ 's.sav' = 'Sapphire.sav'; 'sh0.sav' = 'shield' }
}
$failed = @()
foreach ($name in $suites.Keys) {
    if ($Only -and $Only -notcontains $name) { continue }
    if ($name -eq 'SpriteParity' -and [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        Write-Host '[SpriteParity] pulado: requer Windows (System.Drawing)' -ForegroundColor Yellow
        continue
    }
    $work = Join-Path ([IO.Path]::GetTempPath()) ("pkhex-modern-tests\" + $name + '-' + [guid]::NewGuid())
    New-Item -ItemType Directory -Force $work | Out-Null
    $missing = $false
    foreach ($kv in $suites[$name].GetEnumerator()) {
        $src = Join-Path $saves $kv.Value
        if (-not (Test-Path $src)) { Write-Host "[$name] pulado: falta saves\$($kv.Value)" -ForegroundColor Yellow; $missing = $true; break }
        Copy-Item $src (Join-Path $work $kv.Key)
    }
    if ($missing) { continue }
    Write-Host "== $name ($work)" -ForegroundColor Cyan
    $proj = Join-Path $PSScriptRoot "$name\$name.csproj"
    $testArgs = @($work)
    if ($name -eq 'SpriteParity') { $testArgs += $saves }
    dotnet run -c Release --project $proj -- @testArgs | Where-Object { $_ -notmatch 'warning' }
    if ($LASTEXITCODE -ne 0) { $failed += $name }
}
if ($failed.Count) { Write-Host "FALHARAM: $($failed -join ', ')" -ForegroundColor Red; exit 1 }
Write-Host 'Todos os testes passaram.' -ForegroundColor Green
