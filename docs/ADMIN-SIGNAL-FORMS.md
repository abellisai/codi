# Form dell’admin Codì

Tutte le form esistenti di `web.admin` usano Angular Signal Forms e il layout Spartan Field.
Il workspace è `src/Qwerty.Codi.Web`, l’app in `projects/admin`; `projects/public`
conserva la sua UI autonoma. La migrazione non modifica API, DTO, autenticazione o persistenza.

Le fonti adottate sono [Angular Essentials — Signal Forms](https://angular.dev/essentials/signal-forms)
e [Spartan — Signal Forms](https://spartan.ng/forms/signal-forms).
La guida operativa è la [skill locale](../.agents/skills/codi-signal-forms/SKILL.md);
le [istruzioni delle form](../.github/instructions/admin-signal-forms.instructions.md)
sono collegate da AGENTS, istruzioni Angular e profilo frontend.

## Inventario corrente — 1 ottobre 2026

Aggiornamento del 12 novembre 2026: la card del partecipante offre la rinomina con Signal Forms e Spartan Field.
Il nome ammette da 1 a 30 grafemi e rifiuta i caratteri di controllo.
La form conserva il testo dopo un rifiuto del server.
La conferma del nuovo nome deriva dalla rilettura, insieme alla nuova `rowVersion`.
L'editor GridPath salva sempre il progetto iniziale vuoto, separato dalla soluzione privata.

| Pagina | Modelli e operazioni |
|---|---|
| Accesso | Nome/password, invio asincrono, errori accessibili, password esatta e reset dopo successo |
| Elenco scuole | Creazione e rinomina in dialogo, modelli separati, snapshot/versione riletti |
| Dettaglio scuola | Creazione/modifica classi in dialogo, nome e anno consecutivo |
| Elenco corsi | Creazione/modifica titolo e descrizione in dialogo |
| Dettaglio corso | Creazione/modifica lezioni in dialogo |
| Dettaglio lezione | Selettore dell'esercizio da aggiungere; bozza sequenza e salvataggio esplicito |
| Esercizi | Creazione/modifica dei metadati in dialogo; elenco server senza gli archiviati |
| Editor dell'esercizio | Definizione della bozza come form (consegna, griglia, blocchi, durata massima, messaggi) e, nella stessa pagina, la soluzione di riferimento a blocchi con la sua anteprima e il pannello di pubblicazione: non sono campi del form, quindi non passano da `[formField]` e si salvano con la bozza o con i rispettivi comandi. Il pannello di pubblicazione ha il proprio form minimo di un campo — **Note dell'autore**, facoltative e senza regole oltre al blocco durante l'invio — perché le note accompagnano la versione e non la bozza, e un'**anteprima in sola lettura** della versione scelta, che non è un campo |

Dal 16 ottobre 2026 l'editor `Quiz` include il pannello di pubblicazione già usato dalla griglia.
Il docente usa «Rendi disponibile» per salvare, verificare e pubblicare la revisione corrente.
Lo stato dell'esercizio — **Bozza**, **Disponibile**, **Modifiche da pubblicare** — è un `hlm-badge`
Spartan: `secondary` per la bozza, `default` per ciò che è disponibile, `destructive` per le
modifiche che aspettano una nuova pubblicazione. Il testo resta nel badge, quindi lo stato si legge
anche senza distinguere i colori; il titolo del badge dice come cambiarlo.
La copia delle versioni resta disabilitata con modifiche non salvate.
La copia ricarica definizione, risposta privata e revisione della bozza; la conferma è l'avviso
standard del dispatcher («Versione copiata nella bozza»), non un messaggio in pagina.
L'anteprima della versione mostra rappresentazione e domanda come testo.

Anche l'editor `ValueProgram` usa Signal Forms e il pannello condiviso dal 16 ottobre 2026.
Il salvataggio conserva la revisione restituita dal server per il successivo aggiornamento.
Il confronto comprende i campi della definizione e il progetto iniziale Blockly.
«Rendi disponibile» salva le modifiche prima di verificare e pubblicare la revisione restituita dal server.
La copia di una versione ricarica i documenti e la revisione da usare al successivo salvataggio.
I limiti e il catalogo conservano i valori della bozza caricata.
La consultazione delle versioni mostra il programma iniziale in sola lettura.

Dal 16 ottobre 2026 i tre editor condividono la barra sticky con stato e azioni.

Dal 22 ottobre 2026 l'editor della griglia espone fra i limiti **solo la durata massima, in minuti**
(1–60): il campo `maxActiveDurationMinutes` del modello viene convertito in `maxActiveDurationMs`.
Gli altri budgets del documento — blocchi, annidamento, passi — restano nella bozza e non hanno un
campo in pagina, quindi il salvataggio li conserva.

Sempre dal 22 ottobre 2026 l'editor **non compone il programma iniziale**: la sezione «Progetto a
blocchi» e la sua anteprima non ci sono più, la pagina invia `workspaceJson` vuoto e resta la sola
**soluzione di riferimento**, con il proprio workspace e la propria prova.

Nello stesso giorno la tabella delle versioni offre anche **l'eliminazione di una versione che non
sia la più recente**: pulsante a sola icona (`lucideTrash2`, `variant="outline"`) e conferma **in
linea nella riga** — «Eliminare la versione N?» con conferma e annulla —, lo stesso gesto
dell'archiviazione degli elenchi. L'ultima versione non ha il pulsante, perché è quella che le
prossime lezioni userebbero; il server rifiuta comunque con `409` una versione già assegnata.
L'esito è l'avviso standard del dispatcher («Versione eliminata») e l'elenco si rilegge.
«Salva bozza» conserva anche il lavoro incompleto; «Rendi disponibile» esegue i controlli necessari alla pubblicazione.
Il salvataggio esplicito è un'operazione singola e il suo esito è l'**avviso standard** del dispatcher
(«Bozza salvata»), non un messaggio nella pagina. Dentro «Rendi disponibile» il salvataggio è
silenzioso (`silentSuccess`), perché la barra mostra una sola conferma del flusso.
La risposta attesa del Quiz appartiene alla Signal Form e diventa obbligatoria per la disponibilità.
Gli errori della form conservano il controllo associato, raggiungibile dal messaggio nella barra.
Durante le chiamate finali di verifica e pubblicazione i campi restano disabilitati.
Il blocco delle modifiche comprende i workspace Blockly degli editor a blocchi.
Il confronto della bozza Quiz normalizza l'ordine delle proprietà e gli escape del documento server.
La barra mostra una sola conferma del flusso; i comandi intermedi non generano avvisi di successo separati.
L'elenco legge `publicationStatus` dal server e mostra Bozza, Disponibile o Modifiche da pubblicare.

Contenuti Text/Image, immagini/upload e iscrizioni sono eliminati dal prodotto.
`ImageFileInput` e le sue regole non esistono più: l'inventario precedente era storico.
Archiviazione e riordino sono azioni esplicite; Blockly gestisce un workspace.
Le route di dettaglio sostituiscono le sezioni della pagina corsi con `?tab=`.

## Comportamento comune

- Ogni operazione ha un modello tipizzato `signal` e un `FieldTree` creato con `form`.
  Controlli con `[formField]`, invio con `[formRoot]` e `submission.action`; niente `ngModel`.
  Anche le modifiche inline si inviano come form nativi, quindi Invio salva e Annulla non invia.
- Lo schema gestisce validazione e disabilitazione. Il form può essere inviato con campi
  non validi per rivelare gli errori; l’azione HTTP parte solo dopo validazione positiva.
  Il primo campo non valido riceve il focus. Durante una mutazione i campi sono bloccati;
  Signal Forms e il blocco applicativo impediscono richieste duplicate.
- `hlmField`, label, input/textarea/select e `hlm-field-error` gestiscono layout e feedback.
  Gli errori appaiono dopo interazione o invio. Le prove controllano testo visibile e
  associazione ARIA, oltre alla validità del modello.
- Le regole riusabili sono in `shared/forms/admin-form-rules.ts`, esportate da `@shared/forms`.
  Il nome visualizzato mantiene maiuscole e accenti. La password conserva gli spazi digitati.
  Unicità, autorizzazione e concorrenza restano sul backend.
- Successo: reset dei valori pertinenti e dello stato d’interazione, seguito da rilettura
  dello snapshot server. Un rifiuto conserva la bozza e rende nuovamente disponibili i controlli.
  Le richieste di modifica continuano a inviare `baseRowVersion` dalla riga letta.

## Adattamenti dei controlli

Field, Native Select e Textarea sono stati generati con CLI Spartan 1.5.0 nella cartella admin.
Il Native Select locale è adattato a `FormValueControl<string>` con `model()`, associa gli
errori alla select interna ed espone il focus del controllo nativo. Rimosso il provider CVA
upstream: in Angular 22 avrebbe precedenza sul model e impedirebbe il focus custom di Signal Forms.
Questi adattamenti vanno preservati e verificati quando si rigenera Helm.

Il selettore della sequenza usa Native Select con Signal Forms. La UI esclude gli esercizi
già nella bozza; il backend ammette occorrenze multiple. Riordino con trascinamento CDK o
frecce della maniglia; le modifiche restano locali fino a Salva e Annulla ripristina la sequenza.
La sostituzione rilegge le caselle ma non incrementa la versione della lezione. Due editor
simultanei sono fuori perimetro per decisione utente (A-11).

## Verifiche

Esegui dalla root Web `npm run lint`, `npm run format:check`, `npm run test -- --watch=false`,
`npm run build`, `npm run audit:runtime` e il healthcheck Spartan in modalità rapporto.
Le prove coprono anche login, API area admin/public, rinomina `forcellini` → `Forcellini`,
versioni dopo rilettura, dialoghi, pagine di dettaglio e sequenza.
La verifica corrente ha superato 131 test admin/librerie e 2 public. Il collaudo grafico/tastiera
del 1 ottobre 2026 è stato eseguito nel browser su database temporaneo, senza difetti; gli esiti
delle superfici eliminate sono storici.
Gli esiti correnti sono nello [stato di sviluppo](../.github/STATO-SVILUPPO.md);
gli scenari nella [matrice di copertura](TEST-COVERAGE-MATRIX.md).
Test DOM e HTTP sono evidenze distinte; nessuno dei due sostituisce il collaudo grafico nel browser.
