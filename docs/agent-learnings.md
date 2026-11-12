# Apprendimenti agenti Codì

File condiviso dal ciclo Dream-RSI. Gli agenti worker appendono fatti tecnici verificati. Il coordinatore legge prima di decomporre. Le regole di comportamento stanno nei SOUL dei profili, non qui.

## Politica di decomposizione

- Un task per area con file disgiunti. Mai due scrittori sulla stessa specifica, soluzione o configurazione.
- Corpo del task: obiettivo, sezioni normative, file scrivibili, contratti condivisi, esito atteso, comandi di verifica.
- Assegnazione deterministica agli specialisti nominati nel SOUL del coordinatore.

## Lezioni attive

- [2026-11-12] [setup] `dotnet` nel PATH fallisce al caricamento: usare ~/.dotnet/dotnet con DOTNET_ROOT=/home/ale/.dotnet. global.json richiede SDK 10.0.401, presente in ~/.dotnet. (evidenza: build Release exit 0 del 2026-11-12)
- [2026-11-12] [setup] Build .NET verificata: restore --locked-mode, build Release, test dalla radice con ./Qwerty.Codi.slnx. 1410 test verdi: 398 Domain.Tests, 8 ProtocolTests, 1004 IntegrationTests. (evidenza: output dotnet test exit 0, gate 2026-11-12)
- [2026-11-12] [t_d0e2dfbf] L'impronta studente `content_hash` non include la soluzione. L'impronta della soluzione è la colonna separata `exercise_version.reference_hash` (migrazione 0013), calcolata da `ExerciseContentHash.ComputeReference`. La snapshot della soluzione vive in `exercise_version_private.reference_solution_json`. — perché `content_hash` è citata dai tentativi già pubblicati e cambiarne la formula invaliderebbe il confronto con le righe esistenti (evidenza: src/Qwerty.Codi.Infrastructure/Migrations/Scripts/0013_version_reference_hash.sql, test verdi)
- [2026-11-12] [t_a35834e3] Node v26 di sistema in /usr/sbin/node (v26.12.0). `nvm.sh` non presente il 2026-11-12. `npx ng test web.admin` verde con il node di sistema: 394 test, exit 0. (evidenza: which node, gate ng test 2026-11-12)
- [2026-11-12] [setup] pwsh assente su questa macchina: scripts/test-coverage-gate.ps1 non eseguibile. Gate copertura DA VALIDARE finché non c'è alternativa Linux. (evidenza: which pwsh vuoto)
- [2026-11-12] [t_a35834e3] Comandi admin headless: POST /api/v1/admin/execute/command/{NomeComando}. Corpo = payload JSON del comando. Bootstrap GET /api/v1/admin/bootstrap. Il cookie __Host-CodiAntiforgery va copiato nell'header X-Codi-Antiforgery. Comandi di sessione richiedono l'header Idempotency-Key. (evidenza: SaveExerciseDraft, ValidateExerciseDraft, PublishExerciseVersion 200 e AssignExercise 202 il 2026-11-12)
- [2026-11-12] [t_a35834e3] La pubblicazione scrive `exercise_version.initial_workspace_json` con il `workspace_json` della bozza. La reference resta in `exercise_version_private.reference_solution_json` con `reference_hash`. Il tentativo alunno nasce con `workspace_json` identico all'iniziale della versione. (evidenza: GP02 versione 4 id 82fa3147-1e56-48cc-9af1-710c463d61fc, check byte-identici su codi.db il 2026-11-12)
- [2026-11-12] [t_a35834e3] Join alunno: POST /api/v1/public/join con header Idempotency-Key. Accetta anche su sessione Paused quando admissions_open=1. (evidenza: join 200 con view Exercise su sessione 50047730 in Paused)
- [2026-11-12] [t_a35834e3] vision_analyze non disponibile su questa macchina: il server locale è senza vision encoder. Le prove visive vanno fatte con letture DOM testuali. (evidenza: errore 400 'started without the vision encoder' il 2026-11-12)

## Pattern ricorrenti

- (nessuno ancora)

## Lezioni dai task

- [2026-11-12] [t_eb7c674d] La Resume per alunno (participantId) è lo sblocco non distruttivo della pausa di recupero: conserva AttemptId e workspace_json, azzera RecoveryBlockedFlag, e riapre InteractionEnabledFlag solo quando ResumeContextJson è presente. (evidenza: RecoveryUnblockTests 4/4 verdi, assert su DB)
- [2026-11-12] [t_eb7c674d] RunProgram è rifiutato con SessionStateConflict durante il blocco di recupero; il workspace salvato resta intatto. SaveAttemptWorkspace invece resta ammesso sul blocco. (evidenza: test 'EsecuzioneDuranteIlBloccoDiRecupero' verde)
