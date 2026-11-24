# LENSYNC regression run (ISO/IEC 25010 evidence).
# Rebuilds a THROWAWAY database "lensync_test" from database\schema.sql + seed.sql (your real lensync_db is never
# touched), builds the app and this harness, runs every check, and writes results.csv + screenshots to .\out.
# Needs XAMPP's MariaDB running on port 3306 (stop the MySQL80 service first if it is running).
#   powershell -ExecutionPolicy Bypass -File tests\LenSync.Regression\run-regression.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here '..\..')
$mysql = 'C:\xampp\mysql\bin\mysql.exe'
$out = Join-Path $here 'out'
$work = Join-Path $out 'db'
New-Item -ItemType Directory -Force $work | Out-Null

# 1. Fresh lensync_test from the real scripts (database name swapped).
$utf8 = New-Object Text.UTF8Encoding $false
foreach ($f in 'schema.sql', 'seed.sql') {
    $sql = [IO.File]::ReadAllText((Join-Path $repo "database\$f")).Replace('lensync_db', 'lensync_test')
    [IO.File]::WriteAllText((Join-Path $work $f), $sql, $utf8)
    & $mysql -u root --default-character-set=utf8mb4 -e "source $(Join-Path $work $f)"
    if ($LASTEXITCODE -ne 0) { throw "Loading $f into lensync_test failed" }
}

# 2. Build the app and the harness.
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild (Join-Path $repo 'LenSync.sln') /p:Configuration=Debug /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'App build failed' }
& $msbuild (Join-Path $here 'LenSync.Regression.csproj') /restore /p:Configuration=Debug /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Harness build failed' }

# 3. Run (windows and dialogs appear briefly while the UI checks drive the app).
& (Join-Path $here 'bin\Debug\LenSync.Regression.exe') $out
Write-Host "Results: $(Join-Path $out 'results.csv')"
exit $LASTEXITCODE
