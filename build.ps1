<#
.SYNOPSIS
  Builds and tests Chess Database. To use the app, start the backend and the UI yourself (see README).
.EXAMPLE
  ./build.ps1          # same as ./build.ps1 test
  ./build.ps1 test     # build the UI and the backend, run every test (Chess Rules, API, Playwright smoke)
  ./build.ps1 ui       # build the UI only (ui/dist), which the smoke tests serve
#>
param([ValidateSet('test', 'ui')] [string] $Command = 'test')

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
}
