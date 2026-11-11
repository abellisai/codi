---
description: "SQLite/RepoDB Codì: connessioni, mapping, schema, transazioni e migrazioni."
applyTo: "**/src/Qwerty.Codi.Infrastructure/Persistence/**,**/src/Qwerty.Codi.Infrastructure/Migrations/**,**/src/Qwerty.Codi.Application/Abstractions/**,**/tests/Qwerty.Codi.IntegrationTests/Persistence/**"
---
# Persistenza

Fonti: §7–§8, §18.5, §20.6. Leggi [le regole comuni](../copilot-instructions.md).
Per il modello implementato e gli esempi leggi [accesso ai dati](../../docs/PERSISTENZA.md).

## Connessioni e transazioni

- Connessione dedicata per unità di lavoro; nessuna operazione concorrente sulla stessa connessione o transazione.
- Il chiamante usa `IUnitOfWorkContext.CreateAsync`, risolve i repository con `Repo<T>()` e decide commit/rollback. Non iniettare repository direttamente in handler/trasporto e non aprire connessioni nei loro metodi.
- Entità POCO in `Application/Persistence`, mapping FluentMapper in Infrastructure. Repository interni derivati da `BaseRepository` e interfacce da `IRepository<TEntity, TIdentity>`; filtri tramite `QueryAsync`/`ExistsAsync`, senza un metodo specifico per ogni WHERE.
- Il CRUD generico verifica RowVersion per Update/Delete. I metodi specifici servono per esiti o condizioni ulteriori, come revoca e stato d'accesso. Non sostituire i controlli del dominio con scritture cieche del POCO.
- Chiavi esterne nella connection string e PRAGMA per ogni apertura: foreign_keys=ON, synchronous=FULL, busy_timeout dalla configurazione (baseline 5000 ms).
- journal_mode=WAL nel bootstrap controllato, non in ogni apertura. Verifica il modo effettivo invece di presumere l'esito.
- Inizializza il provider RepoDB secondo il pacchetto realmente adottato; compatibilità e API si provano, non si deducono dal nome.
- Il chiamante possiede solo la connessione restituita: in caso di errore prima del return, la factory deve liberarla.
- Ordine unico: coordinatore di sessione, poi transazione. Repository della stessa unità condividono connessione e transazione.
- Commit solo dopo tutte le scritture; errore, cancellazione o dispose senza commit annullano. Niente SignalR, upload o rete nella transazione.
- L'annullamento riguarda UoW transazionali; senza transazione le scritture sono in auto-commit. Dopo commit, rollback o dispose la UoW e i repository ottenuti non sono riutilizzabili.
- SQL parametrizzato; ordinamenti da lista consentita. Retry SQLITE_BUSY limitati a operazioni interamente ritentabili.

## Schema e migrazioni

- snake_case SQL, PascalCase C#, mapping esplicito; UUID TEXT canonico minuscolo, date epoch millisecondi, booleani/stati con CHECK.
- JSON validato lato applicativo; json_valid dove supportato. Lunghezze con CHECK, non presunte da VARCHAR.
- row_version sulle righe modificabili; versioni immutabili e registri seguono il dizionario. Interi scalati per decimali, niente REAL.
- PK/FK, indici unici e coerenza fra sessione/partecipante/tentativo; storico preservato con ON DELETE RESTRICT salvo figli tecnici.
- Migrazioni numerate atomiche, checksum e versione registrati; una versione rilasciata non si modifica. Aggiorna anche il DDL d'installazione pulita.
- Schema sconosciuto/checksum errato impediscono readiness. Nessun seed di password predefinite.
- Test reali su file temporanei per mapping, vincoli, rollback, lock e concorrenza pertinenti. Memoria soltanto non dimostra WAL/backup.
- Non eseguire migrazioni, restore o test sui dati operativi; test e migrazioni si eseguono su database temporanei. Backup consistente prima dei percorsi di aggiornamento reale.

Zero righe in un UPDATE condizionato richiede distinguere conflitto e accesso negato senza rivelare dati altrui. Verifica proprietà e stato corrente nella medesima unità di lavoro.
