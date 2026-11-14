<# Test di regressione del gate su report sintetici, senza modificare la baseline del progetto. #>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$engine = (Get-Process -Id $PID).Path
$gate = Join-Path $PSScriptRoot 'assert-dotnet-coverage.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('codi-coverage-' + [Guid]::NewGuid().ToString('N'))
$testRoot = [IO.Path]::GetFullPath($testRoot)
New-Item -ItemType Directory -Path $testRoot | Out-Null

function Assert-Gate {
    param([string] $Name, [string[]] $Reports, [string] $Baseline, [int] $ExpectedExitCode, [string] $ExpectedMessage = '')
    $caseRoot = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path $caseRoot | Out-Null
    for ($index = 0; $index -lt $Reports.Count; $index++) {
        $reportRoot = Join-Path $caseRoot ('report-' + $index)
        New-Item -ItemType Directory -Path $reportRoot | Out-Null
        [IO.File]::WriteAllText((Join-Path $reportRoot 'coverage.cobertura.xml'), $Reports[$index])
    }
    $baselineFile = Join-Path $caseRoot 'baseline.json'
    [IO.File]::WriteAllText($baselineFile, $Baseline)
    $previousPreference = $ErrorActionPreference
    try {
        # PowerShell 5.1 rappresenta lo stderr nativo come ErrorRecord: un fallimento atteso
        # del processo figlio non deve interrompere il runner prima del confronto dell'exit code.
        $ErrorActionPreference = 'Continue'
        $output = & $engine -NoProfile -File $gate -ResultsDirectory $caseRoot -BaselinePath $baselineFile 2>&1 | Out-String
        $actualExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($actualExitCode -ne $ExpectedExitCode) {
        throw "Caso $Name : atteso exit $ExpectedExitCode, ottenuto $actualExitCode.`n$output"
    }
    if ($ExpectedMessage -and $output -notlike "*$ExpectedMessage*") {
        throw "Caso $Name : messaggio atteso assente: $ExpectedMessage.`n$output"
    }
    Write-Host "PASS $Name"
}

try {
    Assert-Gate 'righe-omonime-in-file-diversi' @('
        <coverage><packages><package name="A"><classes>
        <class filename="one.cs"><lines><line number="10" hits="1" /></lines></class>
        <class filename="two.cs"><lines><line number="10" hits="0" /></lines></class>
        </classes></package></packages></coverage>') '{"A":0.75}' 1

    Assert-Gate 'unione-stessa-riga-fra-report' @('
        <coverage><packages><package name="A"><classes>
        <class filename="dir/one.cs"><lines><line number="10" hits="0" /></lines></class>
        </classes></package></packages></coverage>', '
        <coverage><packages><package name="A"><classes>
        <class filename="dir\one.cs"><lines><line number="10" hits="1" /></lines></class>
        </classes></package></packages></coverage>') '{"A":1}' 0

    Assert-Gate 'assembly-della-baseline-assente' @('
        <coverage><packages><package name="A"><classes>
        <class filename="one.cs"><lines><line number="10" hits="1" /></lines></class>
        </classes></package></packages></coverage>') '{"A":0.5,"B":0.5}' 1

    Assert-Gate 'calo-effettivo' @('
        <coverage><packages><package name="A"><classes>
        <class filename="one.cs"><lines><line number="10" hits="0" /></lines></class>
        </classes></package></packages></coverage>') '{"A":0.5}' 1

    Assert-Gate 'report-vuoto' @('<coverage><sources /><packages /></coverage>') '{"A":0.5}' 1 'Report Cobertura vuoto'

    Assert-Gate 'report-vuoto-insieme-a-report-valido' @('
        <coverage><packages><package name="A"><classes>
        <class filename="one.cs"><lines><line number="10" hits="1" /></lines></class>
        </classes></package></packages></coverage>',
        '<coverage><sources /><packages /></coverage>') '{"A":0.5}' 1 'Report Cobertura vuoto'

    Write-Host '6/6 regressioni del gate superate.'

    # L'ultimo caso attende exit 1 dal processo figlio, quindi $LASTEXITCODE resta 1: senza questa
    # uscita il test del gate riuscito terminerebbe con il codice del caso che doveva fallire.
    # L'uscita si raggiunge solo se tutti i casi hanno avuto l'esito atteso; un caso diverso solleva
    # un'eccezione prima di qui e il processo termina comunque con codice diverso da zero.
    exit 0
}
finally {
    # Il target ricorsivo deve rimanere sotto la directory temporanea e avere il nostro prefisso.
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($testRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        ([IO.Path]::GetFileName($testRoot)).StartsWith('codi-coverage-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
