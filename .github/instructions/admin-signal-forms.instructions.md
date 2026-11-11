---
description: "Form Angular admin: Signal Forms, layout Spartan Field, validazione e invio accessibili."
applyTo: "**/src/Qwerty.Codi.Web/projects/admin/**/*.ts,**/src/Qwerty.Codi.Web/projects/admin/**/*.html,**/src/Qwerty.Codi.Web/projects/admin/**/*.css"
---
# Form admin

Per creare o modificare una form carica [la skill locale](../../.agents/skills/codi-signal-forms/SKILL.md)
e [le istruzioni Angular](frontend-angular.instructions.md).
La scelta esplicita del progetto è Angular Signal Forms per tutte le form admin,
con il layout [Spartan Signal Forms](https://spartan.ng/forms/signal-forms).

- Modello tipizzato `signal`, schema `form` da `@angular/forms/signals`, binding `[formField]`.
  Usa `[formRoot]` e azione asincrona per invio e validazione; editor inline sono veri form.
  I nuovi form non usano `FormsModule`, `ngModel`, `ngSubmit` o Reactive Forms.
- `hlmField` con label, controllo, descrizione ed errori `hlm-field-error`; i messaggi
  appaiono dopo interazione/invio e devono essere associati al controllo effettivo.
  `required`, limiti e disabilitazione sono nello schema; niente attributi concorrenti.
- Modelli separati per operazioni indipendenti; reset dello stato e dei valori al momento
  opportuno, bozza conservata su errore, protezione dei doppi invii e focus sul campo non valido.
- Riusa `@shared/forms`; anno scolastico consecutivo, password non
  alterata e nomi visualizzati preservati. Il backend decide validità, unicità e permessi.
- Selettori e filtri usano FieldTree. Contenuti, immagini/upload e iscrizioni sono eliminati
  dal prodotto. Il nome del partecipante al join T3 si valida in grafemi secondo le specifiche.
  Preserva gli adattamenti di Signal Forms, descrizione e focus del Native Select locale.
- Dopo la mutazione rilegge dati e versione dal server. Testa ciò che viene mostrato,
  errori visibili/ARIA, invio, reset, rifiuto e versioni; il solo comando nello stub non basta.

Queste regole riguardano l’admin. Public e librerie comuni non importano UI, validatori o servizi admin.
