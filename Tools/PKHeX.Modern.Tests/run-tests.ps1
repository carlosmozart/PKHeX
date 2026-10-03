# Testes de regressao headless do PKHeX.Modern.
# Cada teste roda numa pasta temporaria com COPIAS dos saves de ../../saves (os originais nunca sao alterados).
# Uso: powershell -File Tools/PKHeX.Modern.Tests/run-tests.ps1 [Nome...]   (sem nomes = todos)
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Only)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$saves = Join-Path $root 'saves'
# nome usado pelo teste -> arquivo em saves/
$suites = [ordered]@{
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
    $work = Join-Path ([IO.Path]::GetTempPath()) ("pkhex-modern-tests\" + $name)
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
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
    dotnet run -c Release --project $proj -- $work | Where-Object { $_ -notmatch 'warning' }
    if ($LASTEXITCODE -ne 0) { $failed += $name }
}
if ($failed.Count) { Write-Host "FALHARAM: $($failed -join ', ')" -ForegroundColor Red; exit 1 }
Write-Host 'Todos os testes passaram.' -ForegroundColor Green
