---
name: pagina-angular
description: "Crea o modifica una pagina Angular Codì collegata ai contratti backend e accessibile a docente o alunni."
---
# Pagina Angular

Leggi [le regole comuni](../../copilot-instructions.md), [Angular](../../instructions/frontend-angular.instructions.md), §14 e i contratti API pertinenti.

Per la UI admin carica anche [Codì Spartan](../../../.agents/skills/codi-spartan/SKILL.md), che rimanda alla skill ufficiale e al catalogo dei componenti. Le app sono in `projects/admin` e `projects/public` nel workspace Web.
Per le form admin segui [Codì Signal Forms](../../../.agents/skills/codi-signal-forms/SKILL.md): niente `ngModel`, layout Field, validazione e invio con Signal Forms anche negli editor inline.

1. Identifica app (`admin` docente o `public` alunni), route, autorizzazioni, stato iniziale e comportamento in caso di attesa/errore. Le route guard non sostituiscono il server. L'admin usa base `/admin/`, public base `/`; entrambe si avviano dalla propria cartella con `npm run start`.
2. Controlla scaffold, dipendenze e script reali. Realizza i prerequisiti inclusi nel perimetro; non chiamare un mock “integrazione completa”.
3. Usa i nomi catalogati per il dispatcher e servizi HTTP tipizzati per gli endpoint dedicati. Configura `API_AREA` dell'app e i prefissi di `libraries/api` secondo [API-ENDPOINTS](../../../docs/API-ENDPOINTS.md). Nessun HttpClient nei componenti; verifica il proxy `/api/**` senza confondere URL delle pagine e API.
4. Componenti standalone/OnPush, Signals e RxJS, cleanup corretto; contratti a 64 bit senza perdita di precisione.
5. Per player/dashboard leggi le istruzioni [realtime](../../instructions/protocollo-realtime.instructions.md); per editor/worker [Blockly](../../instructions/esercizi-blockly.instructions.md).
6. Implementa UI e test degli stati osservabili; testi semplici in italiano, tastiera/focus, etichette e messaggi non affidati solo al colore.
7. Consegna con [verifica-consegna](../verifica-consegna/SKILL.md), includendo comandi presenti e prove browser/accessibilità DA VALIDARE. Esegui lint, test e build; le prove browser e di accessibilità restano DA VALIDARE.
