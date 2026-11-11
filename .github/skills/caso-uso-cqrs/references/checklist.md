# Checklist CQRS

Seleziona i controlli pertinenti, senza imporre obblighi di regia a CRUD o query.

## Tutte le richieste

- [ ] Nome pubblico canonico già catalogato oppure nuovo contratto autorizzato e documentato.
- [ ] Record con policy e canale ammesso; nessuna esposizione HTTP di BootstrapAdminAccount.
- [ ] Registro risolve la richiesta e rifiuta metadati mancanti o nomi duplicati.
- [ ] Identità dal trasporto autenticato, proprietà risorsa verificata.
- [ ] Payload e limiti validati; errori con status/codici previsti.
- [ ] Handler senza HttpContext/hub, senza invocare altre richieste.
- [ ] CancellationToken propagato; tempo controllabile dove serve.
- [ ] Test significativi, asserzioni osservabili, nessuno scaffold vuoto.
- [ ] Policy negata provata nella pipeline/trasporto con un soggetto autenticato non autorizzato.
- [ ] Contratti aggiornati solo se cambiati; implementazione ed evidenze registrate separatamente.

## Regia

- [ ] target.scope e destinatario coerenti; EndSession riguarda l'intera sessione.
- [ ] Coordinatore acquisito prima della transazione.
- [ ] Tutte le precondizioni prima delle scritture; nessun successo parziale implicito.
- [ ] Revisione attesa e Idempotency-Key, stesso risultato per retry identico e conflitto per payload diverso.
- [ ] Stato, comando, consegne, audit, idempotenza e outbox nella medesima transazione.
- [ ] Nessun invio prima del commit; 202 non presentato come ACK.
- [ ] Duplicazione e disordine non ripetono effetti o retrocedono stato.

## CRUD, query ed eccezioni

- [ ] CRUD: row_version/baseRowVersion o If-Match, conflitto senza sovrascrittura.
- [ ] Query: DTO dedicati, sola lettura e niente dati di altri partecipanti.
- [ ] Comandi speciali seguono il catalogo: PublishWorkspacePreview aggiorna cache, non il progetto persistente.
- [ ] Gli endpoint dedicati mantengono i contratti di §10.3, inclusi cookie e salvataggi.
- [ ] Area HTTP coerente con la policy: esecuzione nell'area ammessa e rifiuto nell'altra; bootstrap locale escluso da entrambe. Le autorizzazioni restano verificate anche nell'area corretta.
- [ ] Test di integrazione SQLite usano file temporanei; unit test senza filesystem/rete.
- [ ] Test e build eseguiti dall'agente; nessuna dichiarazione di superamento senza l'output che lo mostri.
