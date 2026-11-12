# Apprendimenti agenti Codì

File condiviso dal ciclo Dream-RSI. Gli agenti worker appendono fatti tecnici verificati. Il coordinatore legge prima di decomporre. Le regole di comportamento stanno nei SOUL dei profili, non qui.

## Politica di decomposizione

- Un task per area con file disgiunti. Mai due scrittori sulla stessa specifica, soluzione o configurazione.
- Corpo del task: obiettivo, sezioni normative, file scrivibili, contratti condivisi, esito atteso, comandi di verifica.
- Assegnazione deterministica agli specialisti nominati nel SOUL del coordinatore.

## Lezioni attive

- [2026-11-12] [setup] `dotnet` nel PATH fallisce al caricamento: usare ~/.dotnet/dotnet con DOTNET_ROOT=/home/ale/.dotnet. global.json richiede SDK 10.0.401, presente in ~/.dotnet. (evidenza: build Release exit 0 del 2026-11-12)
- [2026-11-12] [setup] Build .NET verificata: restore --locked-mode, build Release, test dalla radice con ./Qwerty.Codi.slnx. 1397 test verdi: 398 Domain.Tests, 8 ProtocolTests, 991 IntegrationTests. (evidenza: output dotnet test exit 0)
- [2026-11-12] [setup] Node v26 disponibile solo via nvm (`source ~/.nvm/nvm.sh`). (evidenza: nvm.sh presente, node --version ok)
- [2026-11-12] [setup] pwsh assente su questa macchina: scripts/test-coverage-gate.ps1 non eseguibile. Gate copertura DA VALIDARE finché non c'è alternativa Linux. (evidenza: which pwsh vuoto)

## Pattern ricorrenti

- (nessuno ancora)

## Lezioni dai task

- [2026-11-12] [t_eb7c674d] La Resume per alunno (participantId) è lo sblocco non distruttivo della pausa di recupero: conserva AttemptId e workspace_json, azzera RecoveryBlockedFlag, e riapre InteractionEnabledFlag solo quando ResumeContextJson è presente. (evidenza: RecoveryUnblockTests 4/4 verdi, assert su DB)
- [2026-11-12] [t_eb7c674d] RunProgram è rifiutato con SessionStateConflict durante il blocco di recupero; il workspace salvato resta intatto. SaveAttemptWorkspace invece resta ammesso sul blocco. (evidenza: test 'EsecuzioneDuranteIlBloccoDiRecupero' verde)
