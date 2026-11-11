<#
.SYNOPSIS
    Confronta la copertura Cobertura con la baseline per assembly e fallisce quando cala (§20.4).

.DESCRIPTION
    La copertura è un guardrail di regressione, non un punteggio: lo script non alza soglie e non
    esclude codice. Legge ogni `coverage.cobertura.xml` prodotto dal collector XPlat sotto la
    cartella dei risultati, calcola la copertura di riga per assembly e la confronta con
    `scripts/coverage-baseline.json`.

    Se un assembly cala sotto la baseline, l'esito è negativo. L'abbassamento di una soglia si fa
    con `-UpdateBaseline` ed è una decisione da motivare nella matrice di copertura
    (`docs/TEST-COVERAGE-MATRIX.md`), non un dettaglio da nascondere.

    Funziona con Windows PowerShell 5.1 e con PowerShell 7: usa solo costrutti presenti in
    entrambi, perché le macchine del laboratorio hanno il primo e la pipeline ha il secondo.

    Nessun numero è scritto a mano in questo script: la baseline nasce dalla prima misura reale.

    ResultsDirectory deve essere una cartella dedicata a una sola esecuzione. Non si deduce
    l'appartenenza da una finestra temporale: due esecuzioni vicine restano esecuzioni diverse.
    L'unione identifica ogni riga tramite assembly, file e numero; gli assembly della baseline
    assenti dai report fanno fallire il controllo.

    Il confronto avviene alla stessa precisione con cui la baseline è registrata: senza questo
    allineamento un assembly arrotondato per eccesso risulterebbe in calo a ogni esecuzione.

.PARAMETER ResultsDirectory
    Cartella passata a `--results-directory` di `dotnet test`.

.PARAMETER BaselinePath
    Percorso del file di baseline. Predefinito `scripts/coverage-baseline.json`.

.PARAMETER UpdateBaseline
    Riscrive la baseline con la misura corrente. Va usato solo dopo aver verificato che il calo
    sia giustificato e documentato.

.EXAMPLE
    .\scripts\assert-dotnet-coverage.ps1 -ResultsDirectory .\TestResults\run-20260930

.NOTES
    Il file va salvato in UTF-8 **con BOM**. Windows PowerShell 5.1 interpreta come ANSI i file
    privi di BOM, quindi ogni accento dei messaggi compare illeggibile: «più» diventa «piÃ¹». Se i
    messaggi tornano così, il BOM è stato perso da un salvataggio e va ripristinato con:

        $p = '.\scripts\assert-dotnet-coverage.ps1'
        [IO.File]::WriteAllText($p, [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8), (New-Object Text.UTF8Encoding $true))
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,

    [string] $BaselinePath = (Join-Path $PSScriptRoot 'coverage-baseline.json'),

    [switch] $UpdateBaseline
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    Write-Error "Cartella dei risultati non trovata: $ResultsDirectory. Eseguire prima dotnet test con --collect:'XPlat Code Coverage'."
    exit 1
}

$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' -File)
if ($reports.Count -eq 0) {
    Write-Error "Nessun report Cobertura sotto $ResultsDirectory. Il collector non è stato eseguito, oppure i progetti di test non lo referenziano: un comando che non produce report non è una verifica."
    exit 1
}

Write-Host "Report Cobertura dell'esecuzione in $ResultsDirectory : $($reports.Count)."

# Copertura di riga per assembly, unita fra i report. Ogni progetto di test produce il proprio
# report e in esso un assembly che quel progetto non esercita compare con copertura zero: prendere
# il minimo fra i report avrebbe dato zero per tutti, cioè un guardrail incapace di fallire. La
# misura corretta è l'unione: una riga conta come coperta se almeno una prova l'ha eseguita.
$lines = @{}
foreach ($report in $reports) {
    [xml] $document = Get-Content -LiteralPath $report.FullName -Raw

    foreach ($package in @($document.coverage.packages.package)) {
        $name = [string] $package.name

        if (-not $lines.ContainsKey($name)) {
            $lines[$name] = @{}
        }

        foreach ($class in @($package.classes.class)) {
            $fileName = ([string] $class.filename).Replace('\', '/')
            if ([string]::IsNullOrWhiteSpace($fileName)) {
                Write-Error "Report senza nome del file sorgente: $($report.FullName). Impossibile identificare le righe."
                exit 1
            }
            foreach ($line in @($class.lines.line)) {
                $number = $fileName + ':' + [string] $line.number
                $hits = [int] $line.hits

                if (-not $lines[$name].ContainsKey($number) -or $lines[$name][$number] -lt $hits) {
                    $lines[$name][$number] = $hits
                }
            }
        }
    }
}

$measured = @{}
foreach ($name in $lines.Keys) {
    $total = $lines[$name].Count
    $covered = @($lines[$name].Values | Where-Object { $_ -gt 0 }).Count

    $measured[$name] = if ($total -gt 0) { $covered / $total } else { 0 }
}
if ($measured.Count -eq 0) {
    Write-Error "I report letti sotto $ResultsDirectory non contengono assembly: controllare il filtro Include di coverlet.runsettings."
    exit 1
}

if ($UpdateBaseline) {
    $ordered = [ordered] @{}
    foreach ($name in ($measured.Keys | Sort-Object)) {
        # Arrotondamento a quattro decimali: sotto quella soglia c'è rumore di misura, non
        # una perdita di copertura.
        $ordered[$name] = [Math]::Round($measured[$name], 4)
    }

    $directory = Split-Path -Parent $BaselinePath
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    ($ordered | ConvertTo-Json) | Set-Content -LiteralPath $BaselinePath -Encoding utf8

    Write-Host "Baseline aggiornata in $BaselinePath con $($ordered.Count) assembly. Motivare ogni calo in docs/TEST-COVERAGE-MATRIX.md."

    exit 0
}

$baseline = @{}
if (Test-Path -LiteralPath $BaselinePath) {
    $content = Get-Content -LiteralPath $BaselinePath -Raw
    if ($content.Trim()) {
        $parsed = $content | ConvertFrom-Json
        foreach ($property in $parsed.PSObject.Properties) {
            # La baseline porta anche una nota di testo: non è una misura e non va convertita.
            if ($property.Name -like '_*') {
                continue
            }

            $baseline[$property.Name] = [double] $property.Value
        }
    }
}

if ($baseline.Count -eq 0) {
    Write-Host 'Copertura misurata (nessuna baseline registrata):'
    foreach ($name in ($measured.Keys | Sort-Object)) {
        Write-Host ("  {0,-40} {1:P2}" -f $name, $measured[$name])
    }

    Write-Error "La baseline $BaselinePath è vuota: senza una misura di riferimento non c'è nulla da confrontare. Verificare che i numeri siano sensati e registrare la baseline con -UpdateBaseline."
    exit 1
}

$regressions = @()
Write-Host 'Assembly                                 baseline      misurata   esito'

# Il confronto usa la stessa precisione con cui la baseline viene scritta. La misura è un rapporto
# fra interi e ha molte cifre, mentre la baseline ne conserva quattro: confrontare la misura intera
# con il valore arrotondato dichiarerebbe in calo qualunque assembly arrotondato per eccesso, cioè
# un guardrail che si accende da solo. La tabella mostra quattro decimali per la stessa ragione:
# con due decimali un calo reale al quarto apparirebbe come due numeri identici.
$precision = 4

foreach ($name in ($measured.Keys | Sort-Object)) {
    $current = [Math]::Round($measured[$name], $precision)
    $expected = if ($baseline.ContainsKey($name)) {
        [Math]::Round($baseline[$name], $precision)
    }
    else {
        $null
    }

    if ($null -eq $expected) {
        # Un assembly nuovo non è una regressione: la baseline lo assorbe al primo aggiornamento.
        Write-Host ("  {0,-38} {1,9}      {2,10:P4}   nuovo" -f $name, '-', $current)
        continue
    }

    if ($current -lt $expected) {
        $regressions += $name
        Write-Host ("  {0,-38} {1,10:P4}   {2,10:P4}   calo" -f $name, $expected, $current)
    }
    else {
        Write-Host ("  {0,-38} {1,10:P4}   {2,10:P4}   ok" -f $name, $expected, $current)
    }
}

foreach ($name in ($baseline.Keys | Sort-Object)) {
    if (-not $measured.ContainsKey($name)) {
        $regressions += $name
        Write-Host ("  {0,-38} {1,10:P4}      {2,9}   non misurato" -f $name, $baseline[$name], '-')
    }
}

if ($regressions.Count -gt 0) {
    Write-Error "Copertura calata sotto la baseline: $($regressions -join ', '). Aggiungere i test mancanti, oppure motivare l'abbassamento in docs/TEST-COVERAGE-MATRIX.md e registrarlo con -UpdateBaseline."
    exit 1
}

Write-Host 'Copertura non inferiore alla baseline.'
exit 0
