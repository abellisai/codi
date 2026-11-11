# Comandi e prerequisiti

I comandi seguenti si eseguono dalla radice Codì e il loro output è l'evidenza della consegna. Verifica i prerequisiti sui file correnti, senza dedurre disponibilità dal piano T1.

## Backend

Dalla radice Codì:

```powershell
dotnet restore .\Qwerty.Codi.slnx --locked-mode
dotnet build .\Qwerty.Codi.slnx --configuration Release --no-restore
dotnet test .\Qwerty.Codi.slnx --configuration Release --no-build
```

Restore verifica la risoluzione, build la compilazione, test l'esecuzione dei casi scoperti. Registra numero di test ed esito per progetto; un assembly senza test non è una suite validata. --no-build presuppone una build aggiornata dello stesso sorgente/configurazione.

Solo quando esistono collector XPlat, coverlet.runsettings, baseline e script:

```powershell
$coverageRun = Join-Path .\TestResults ([Guid]::NewGuid().ToString('N'))
dotnet test .\Qwerty.Codi.slnx --configuration Release --no-build `
  --collect:"XPlat Code Coverage" --settings .\coverlet.runsettings `
  --results-directory $coverageRun
.\scripts\test-coverage-gate.ps1
.\scripts\assert-dotnet-coverage.ps1 -ResultsDirectory $coverageRun
```

Collector e file mancanti sono DA CREARE, non un comando già utilizzabile. Non abbassare soglie né escludere sorgenti per ottenere un esito.

La cartella dei risultati deve contenere soltanto i report della singola raccolta: lo script
non seleziona l'esecuzione in base all'orario. Per ogni nuova raccolta genera un nuovo percorso.

## Frontend

Due app, un'installazione: `projects/admin` e `projects/public` hanno ciascuna `npm run start`,
rispettivamente HTTPS 4200 e 4300. Il `package.json` con dipendenze, lockfile e gate è nella
root Web. `test` verifica entrambe le app e le librerie comuni, `build` produce i due bundle
e `dist/site`. I quattro launcher nella root Codì sono descritti nel [README](../../../../README.md#avvio-rapido-windows).

Dalla radice Codì, soltanto se esistono package.json, package-lock.json e gli script indicati:

```powershell
Set-Location .\src\Qwerty.Codi.Web
npm ci
npm run lint
npm run format:check
npm run test -- --watch=false
npm run build
npm run audit:runtime
```

Adatta il comando test allo script reale e alla configurazione del runner. Non passare argomenti non supportati. Se il lockfile manca, segnala che `npm ci` non è disponibile; genera il lockfile con `npm install` solo quando servono pacchetti nuovi.

Per API/UI/live aggiungi gli scenari pertinenti di §19 e gli AC funzionali. Una build frontend non dimostra accessibilità o interoperabilità di 12 client.

## Solo documentazione e personalizzazioni

Controlla leggibilità UTF-8, YAML, destinazioni dei link, nomi dei profili e riferimenti delle skill, ambito dei glob e assenza di regole contraddittorie. Indica separatamente la discovery nell'editor e il comportamento degli agenti: la correttezza dei file non prova l'attivazione nell'ambiente.

Le verifiche sono esclusivamente locali: il progetto non usa GitHub Actions. Non creare
workflow di CI senza una nuova richiesta esplicita dell'utente. Lo stato delle esecuzioni
vive nel registro di sviluppo, mai in affermazioni permanenti di questa guida.
