<#
.SYNOPSIS
  One entry point for building, testing and running Chess Database.
.EXAMPLE
  ./build.ps1          # same as ./build.ps1 test
  ./build.ps1 test     # build the UI and the backend, run every test (Chess Rules, API, Playwright smoke)
  ./build.ps1 run      # build everything and open the desktop window
  ./build.ps1 dev      # backend on http://localhost:5080 plus `ng serve` on http://localhost:4200
#>
param([ValidateSet('test', 'run', 'dev', 'ui')] [string] $Command = 'test')

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Invoke-Checked([scriptblock] $Block) {
    & $Block
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE" }
}

function Build-Ui {
    Push-Location ui
    try {
        if (-not (Test-Path node_modules)) { Invoke-Checked { npm ci } }
        Invoke-Checked { npm run build }
    } finally { Pop-Location }
}

switch ($Command) {
    'ui'   { Build-Ui }
    'test' { Build-Ui; Invoke-Checked { dotnet test ChessDatabase.slnx } }
    'run'  { Build-Ui; Invoke-Checked { dotnet run --project src/ChessDatabase.Desktop } }
    'dev'  {
        Push-Location ui
        try { if (-not (Test-Path node_modules)) { Invoke-Checked { npm ci } } } finally { Pop-Location }
        $api = Start-Process dotnet -ArgumentList 'run', '--project', 'src/ChessDatabase.Api', '--launch-profile', 'http' -PassThru -NoNewWindow
        try { Push-Location ui; npm start } finally { Pop-Location; Stop-Process -Id $api.Id -ErrorAction SilentlyContinue }
    }
}
