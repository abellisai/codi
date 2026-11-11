---
name: codi-signal-forms
description: "Crea, migra o revisiona le form Angular admin di Codì con Signal Forms e layout Spartan Field. Include editor inline, filtri, selettori e upload; non sceglie la UI degli alunni."
---

# Form admin con Signals e Spartan

Leggi [AGENTS](../../../AGENTS.md), [le istruzioni delle form](../../../.github/instructions/admin-signal-forms.instructions.md)
e [Codì Spartan](../codi-spartan/SKILL.md). Il workspace è `src/Qwerty.Codi.Web`,
l'app interessata è `projects/admin`; usa il Node già attivo con nvm.

Fonti primarie: [Angular Essentials](https://angular.dev/essentials/signal-forms),
[validazione](https://angular.dev/guide/forms/signals/validation),
[invio](https://angular.dev/guide/forms/signals/form-submission),
[controlli custom](https://angular.dev/guide/forms/signals/custom-controls),
[Spartan Signal Forms](https://spartan.ng/forms/signal-forms).
Controlla anche le dichiarazioni di `@angular/forms/signals` installate: la baseline del progetto
è Angular 22.2 e Spartan 1.5.0. Gli esempi online possono cambiare.

## Modello, schema e invio

1. Cerca tutte le superfici interessate, comprese modifiche inline e campi fuori dai form.
   Un `signal()` collegato con `ngModel` non è una Signal Form.
2. Crea un modello tipizzato con `signal({ ... })` e un `FieldTree` con `form(model, schema, options)`.
   Ogni operazione indipendente ha la propria form: un testo incompleto non blocca la creazione di un’immagine.
   I filtri booleani possono usare `form(model)` senza validatori o azioni d’invio.
3. Usa `[formField]` sui controlli. `required`, `disabled`, `readonly` e i limiti appartengono
   allo schema, non ad attributi concorrenti nel template. Angular 22 rifiuta, per esempio,
   `maxlength` su un nodo con `[formField]`.
4. Usa `<form [formRoot]="...">` e `submission.action` asincrona. Per attivare l’invio
   dall’esterno usa `submit(fieldTree)`, mai una chiamata diretta che salta la validazione.
   Pulsanti di salvataggio `type="submit"`, annullamento `type="button"`; niente form annidati.
   Considera `ignoreValidators: 'none'` e focus sul primo campo di `errorSummary()` in `onInvalid`.
5. Disabilita lo schema durante la mutazione tramite `disabled(path, () => busy())` e mantieni
   il blocco della mutazione nel metodo applicativo. Signal Forms impedisce invii concorrenti
   della stessa form; il blocco condiviso protegge anche operazioni di form diverse.
6. Dopo il successo azzera i valori necessari e chiama `fieldTree().reset()` per lo stato
   d’interazione. Carica e resetta la bozza quando apri un editor; annulla senza inviare.
   In caso di rifiuto conserva i valori e mostra l’errore tipizzato del servizio.

## Layout, controlli e contratti

- Componi `hlmField`, `hlmFieldLabel`, controllo ed eventuale `hlmFieldDescription`;
  mostra `hlm-field-error` con `validator=error.kind` quando il campo è touched e invalid.
  I messaggi sono in italiano. Verifica il testo realmente visibile, `aria-invalid` e
  `aria-describedby`, non solo l’esistenza del componente di errore.
- Usa `hlmInput`, `hlmTextarea` e `hlm-native-select` secondo i sorgenti locali.
  Il Native Select locale integra `FormValueControl<string>` con `model()`, propaga la
  descrizione alla select interna e offre `focus()`. Il provider CVA upstream è rimosso:
  in Angular 22 ha precedenza sul model e impedisce il focus custom di Signal Forms.
  L’input disabled usa una trasformazione da `unknown`, coerente con `FormValueControl`.
  Conserva questi adattamenti se rigeneri Helm.
- Riusa `admin-form-rules.ts` per testo e anno scolastico: nomi scuole/classi 120,
  titoli 160, descrizioni 2000; anno consecutivo con barra o trattino accettato.
- L'inventario corrente è in [ADMIN-SIGNAL-FORMS](../../../docs/ADMIN-SIGNAL-FORMS.md).
  Contenuti, immagini/upload e iscrizioni sono eliminati dal prodotto; ImageFileInput e
  MediaAssetsService sono rimossi. Non reintrodurli come funzioni future. Il nome del
  partecipante al join T3 va validato in grafemi secondo la specifica funzionale.
- Non modificare maiuscole o accenti del nome visualizzato. La password si invia esattamente
  come digitata. Unicità, normalizzazione, permessi e `baseRowVersion` rimangono sul server.
- I componenti usano servizi tipizzati. Dopo una mutazione rilegge il server: il modello
  della form è una bozza, non lo snapshot autorevole. Selettori gerarchici invalidano i dati dipendenti.

## Verifica

Controlla input validi e invalidi, invio nativo anche negli editor inline, errori accessibili,
doppio invio, reset/annullamento, conservazione della bozza su 409 e nuovo snapshot/versione.
Per i dialoghi verifica apertura, bozza iniziale, chiusura, focus ed errori. Per la sequenza
verifica selettore, riordino da tastiera, annullamento e salvataggio. Conserva le regressioni di navigazione,
login, API admin/public e rinomina `forcellini` → `Forcellini`.

Esegui lint, format:check, test di entrambe le app, build e audit runtime dalla root Web;
usa `healthcheck` in modalità rapporto e valida link/frontmatter della skill.
Aggiorna [lo stato](../../../.github/STATO-SVILUPPO.md) e la matrice con gli esiti reali.
Il test DOM non prova la persistenza nel database né un collaudo grafico nel browser.
