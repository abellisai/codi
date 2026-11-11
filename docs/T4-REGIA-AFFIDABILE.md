# T4: regia affidabile

T4 è completo e validato localmente il 5 ottobre 2026.
Il perimetro comprende regia, consegne, autorizzazione temporanea, osservazioni e dimostrazione finita.
Al momento di questa validazione i salvataggi (T5) e il player Blockly (T7) non esistevano ancora:
sono stati completati nelle porzioni successive, con le evidenze nel
[registro di stato](../.github/STATO-SVILUPPO.md). Questo rapporto resta il documento del collaudo T4.
Il laboratorio su 12 PC resta di T9.

## Comportamenti consegnati

La regia mantiene questi contratti:

- Coordinatore della sessione prima della transazione.
- Stato, comando, consegne, idempotenza e outbox nella stessa transazione.
- Stessa chiave e contenuto restituiscono la risposta registrata per 24 ore.
- Stessa chiave con contenuto differente produce conflitto senza nuove scritture.
- Ogni destinatario riceve tentativo, generazione, stato e direttiva propri.
- Una pausa individuale preserva le autorizzazioni degli altri partecipanti.
- Pausa, ripresa e chiusura producono una ricevuta 202 con lo stato della sessione.
- Un ingresso tardivo riceve un tentativo indipendente, senza direttive storiche.

Il dispatcher mantiene identità e revisione nei due ritentativi brevi.
Un errore di trasporto posticipa il messaggio con attesa esponenziale, fino a 30 secondi.
Il primo istante d'invio resta invariato.
Dopo cinque secondi senza conferma, la consegna diventa `Unconfirmed`.
Una conferma tardiva valida può portare la consegna ad `Applied`.

Il client verifica protocollo, server, partecipante, epoch e revisione.
Le revisioni a 64 bit usano stringhe decimali e confronti `BigInt`.
La conferma applicativa segue il caricamento del tentativo.
I caricamenti concorrenti dello stesso tentativo condividono la stessa operazione.
Una consegna duplicata non ripete la direttiva.

L'autorizzazione temporanea usa nonce monouso e scadenza monotona.
Un battito su una revisione non applicata non rinnova l'autorizzazione.
La pagina nascosta e la perdita del collegamento sospendono la dimostrazione.
Il registro conserva fino a 16.384 nonce per epoch.
Il limite richiede un nuovo handshake e impedisce crescita illimitata della memoria.

Un refresh con direttiva sconosciuta attiva `ReportRecoveryBlocked`.
Il server verifica epoch, tentativo e revisione prima della pausa di recupero.
La ripresa esplicita senza checkpoint riporta la dimostrazione allo stato `Ready`.
Il server non ripete automaticamente una direttiva storica.

## Osservazione docente

`ActivityStateUpdated` verifica identità, connessione, revisione, tentativo, generazione e sequenza.
La cache conserva al massimo 256 osservazioni e scade dopo 30 secondi.
La scena occupa al massimo 8.192 byte. La telemetria non salva il progetto in SQLite.
Il client invia osservazioni al massimo ogni 500 millisecondi durante un'autorizzazione valida.

`GetSessionOverview` restituisce sessione, partecipanti e gli ultimi otto comandi.
`TeacherSnapshot` aggiorna la griglia live e le ricevute.
Il servizio docente verifica la credenziale durante ogni pubblicazione.
La perdita del collegamento sospende i comandi della dashboard.
La presenza viaggia come valore JSON testuale.

## Difetti rilevati e corretti nel collaudo

Il browser locale ha rilevato questi difetti:

- SignalR omette il token antiforgery durante la negoziazione.
- Docente e alunno sovrascrivono il token condiviso sullo stesso host.
- La presenza numerica non corrisponde alle etichette del frontend.
- Un'osservazione precedente al comando produce un avviso durante la sincronizzazione.
- La chiusura mostra un collegamento sostituito sul client alunno.
- La regia offre comandi individuali dopo la conclusione.

Il trasporto SignalR legge il token corrente a ogni richiesta HTTP.
Admin conserva `__Host-CodiAntiforgery`. Public conserva `__Host-CodiPublicAntiforgery`.
I client inviano `X-Codi-Antiforgery` senza ridurre il controllo del server.
I test verificano la coesistenza delle identità e l'uscita dell'alunno senza invalidare il docente.

## Evidenze automatiche

Tutti i controlli seguenti terminano con esito positivo.

| Controllo | Esito | Evidenza locale |
|---|---|---|
| Restore con lockfile | Risoluzione completata | `dotnet restore .\Qwerty.Codi.slnx --locked-mode` |
| Build Release | Zero warning, zero errori | `TestResults/t4-build-final.log` |
| Test .NET | 1.111 superati, zero fallimenti | `TestResults/t4-delivery-20261005.log` |
| Dominio | 311 test superati | Report della raccolta finale |
| Integrazione | 792 test superati | Report della raccolta finale |
| Protocollo | 8 test superati | Report della raccolta finale |
| Angular | 280 test superati | `TestResults/t4-final-angular.log` |
| Formattazione .NET | Nessuna modifica richiesta | `TestResults/t4-format-verified.log` |
| Formattazione Angular | Tutti i file conformi | `TestResults/t4-terminal-format.log` |
| Lint | Nessun errore | `TestResults/t4-terminal-lint.log` |
| Bundle | Entrambe le app compilate | `TestResults/t4-terminal-build.log` |
| Audit runtime | Zero vulnerabilità | `TestResults/t4-audit-final.log` |
| Copertura | Quattro assembly sopra la baseline | `TestResults/t4-coverage-final.log` |
| Test del gate | Quattro regressioni superate | `TestResults/t4-gate-final.log` |

La raccolta finale della copertura è `TestResults/t4-delivery-20261005`.
La baseline resta invariata.
La verifica finale dei comandi conclusivi supera 16 test della pagina docente.
L'evidenza è `TestResults/t4-terminal-terminal.log`.

| Assembly | Baseline | Misura finale |
|---|---|---|
| Api | 86,17% | 87,83% |
| Application | 97,33% | 97,36% |
| Domain | 96,47% | 96,83% |
| Infrastructure | 97,69% | 97,77% |

La [matrice](TEST-COVERAGE-MATRIX.md#regia-affidabile-t4) collega i comportamenti ai test.
I test del protocollo simulano 12 connessioni.
La simulazione non certifica prestazioni, rete o browser del laboratorio.

## Procedura del collaudo locale

Il collaudo usa HTTPS locale e un database temporaneo alla versione 8.
La porta del server di prova è 5324.
Il database resta sotto `TestResults/t4-browser-data`.
Le credenziali di prova restano distinte da quelle operative.

La procedura verifica questi comportamenti:

1. Apri la regia docente e avvia la sessione.
2. Entra come alunno dal browser.
3. Verifica presenza e stato nella griglia live.
4. Assegna una versione pubblicata dalla sequenza della lezione.
5. Verifica il tentativo indipendente e la ricevuta applicativa.
6. Abilita l'attività.
7. Avvia la dimostrazione finita.
8. Metti in pausa la sessione.
9. Verifica la sospensione del partecipante e la ricevuta docente.
10. Riprendi la sessione.
11. Riavvia l'esecuzione.
12. Termina la sessione.
13. Verifica `Goodbye` sul partecipante e la ricevuta finale.

Esito osservato: assegnazione, abilitazione, avvio e completamento a 40 avanzamenti.
Pausa e ripresa aggiornano controlli e ricevute.
Un refresh segnala il recupero senza eseguire nuovamente la direttiva.
La ripresa esplicita rimuove la pausa di recupero.
Il riavvio incrementa la generazione e riparte da zero.
La chiusura consegna `Goodbye` e ferma la dimostrazione.
Il client mostra «lezione conclusa» e termina i battiti.
La regia mostra una consegna applicata e disabilita i comandi dopo la conclusione.
Le schermate del collaudo sono `TestResults/t4-regia-conclusa.jpg` e `TestResults/t4-alunno-concluso.jpg`.

## Limiti e prossimo incremento

La dimostrazione T4 ha 40 avanzamenti e uno stato di esecuzione in memoria.
La dimostrazione conserva il comportamento della regia senza eseguire il programma Blockly dell'alunno.
Il prossimo incremento è T5.
T5 deve completare salvataggi, checkpoint, recupero locale e grant conclusivi.
T7 deve integrare il worker e il player Blockly del partecipante.
T8 deve completare le anteprime remote.
T9 deve verificare 60 minuti di attività su 12 PC e le procedure operative.
La deroga seed A-09 resta aperta prima di T9.
