# Accesso ai dati di Codì

Il modello riprende connessione condivisa, repository base e unit of work dell'esempio
`src/Qwerty.ProjectAnt.Data`. Il progetto di esempio rimane un riferimento: Codì non lo
referenzia e mantiene provider, schema e migrazioni già adottati.

## Struttura

| Componente | Responsabilità |
|---|---|
| [IRepository](../src/Qwerty.Codi.Application/Abstractions/IRepository.cs) | Get, GetAll, Query, Exists, Insert, Update, Delete asincroni, con cancellazione. |
| [IUnitOfWork e factory](../src/Qwerty.Codi.Application/Abstractions/IUnitOfWork.cs) | Il chiamante crea il lavoro, risolve i repository e decide commit o rollback. |
| [Entità di persistenza](../src/Qwerty.Codi.Application/Persistence) | POCO senza dipendenze ORM; UUID in stringa, date epoch millisecondi, flag interi. Conversioni esplicite verso il dominio. |
| [Mapping RepoDB](../src/Qwerty.Codi.Infrastructure/Persistence/RepoDbSqliteBootstrap.cs) | FluentMapper configura tabelle, chiavi e colonne snake_case una volta. |
| [BaseRepository](../src/Qwerty.Codi.Infrastructure/Persistence/Repositories/BaseRepository.cs) | CRUD generato da RepoDB e controllo ottimistico di row_version; nessuna connessione aperta o confermata dal repository. |
| [DbContext](../src/Qwerty.Codi.Infrastructure/Persistence/UnitOfWork/DbContext.cs) | Connessione e transazione condivise nello scope isolato della UoW; smaltimento delle risorse. |
| [Registrazione](../src/Qwerty.Codi.Infrastructure/Persistence/CodiPersistenceServiceExtensions.cs) | Implementazioni interne scoped, factory singleton senza stato; registrazioni esplicite. |

Il mapping usa FluentMapper invece degli attributi RepoDB del riferimento per mantenere
`Application` e `Domain` indipendenti dall'ORM. Non si tenta la conversione implicita di UUID,
date o enum: è preservata la decisione A-06 e il formato di §7.1.

## Filtrare senza aggiungere un metodo per ogni WHERE

Nel servizio o nell'handler, con `IUnitOfWorkContext` ricevuto dal costruttore:

```csharp
await using var work = await unitOfWorkContext.CreateAsync(cancellationToken: ct);
var users = work.Repo<IAdminUserRepository>();
var rows = await users.QueryAsync(
    entity => entity.NormalizedUsername == normalizedName && entity.IsActiveFlag == 1, ct);
var account = rows.SingleOrDefault()?.ToDomain();
```

Il filtro è un'espressione sulle proprietà C# dell'entità di persistenza. RepoDB traduce la
condizione e i parametri usando il mapping; non si concatena SQL e non si filtra l'intera
tabella in memoria. Per UUID e date si preparano prima le costanti nel formato persistito:
`id.ToString("D")`, `instant.ToUnixTimeMilliseconds()`.

Usare predicati semplici supportati dal parser RepoDB, verificati contro SQLite: confronti,
AND/OR e null. Non promettere che qualsiasi metodo C# sia traducibile. Il risultato è una
lista materializzata nello scope, utilizzabile dopo il dispose senza reader aperti.

`TryInsertAsync`, `TryUpdateAsync` e `TryRevokeAsync` rimangono soltanto per le semantiche
specifiche del dominio: conflitto di bootstrap, aggiornamento dei soli campi di accesso e
revoca condizionata. Le ricerche per nome e per stato usano l'API generica.

## Più repository nella stessa transazione

```csharp
await using var work = await unitOfWorkContext.CreateAsync(useTransaction: true, cancellationToken: ct);
var users = work.Repo<IAdminUserRepository>();
var sessions = work.Repo<ICredentialSessionRepository>();
await users.InsertAsync(AdminUserEntity.FromDomain(admin), ct);
await sessions.InsertAsync(CredentialSessionEntity.FromDomain(session), ct);
await work.CommitAsync(ct);
```

Gli oggetti di dominio devono essere validati prima di convertirli nelle entità di persistenza;
queste ultime non sono DTO HTTP e non sostituiscono le invarianti. Ogni `Repo<T>()` nella stessa
UoW restituisce la stessa istanza scoped. UoW distinte hanno connessioni distinte, anche se
create dallo stesso handler o dalla stessa richiesta HTTP.

- Con transazione, dispose senza commit annulla tutte le scritture, anche dopo eccezione o
  cancellazione. `RollbackAsync` permette l'annullamento esplicito.
- Senza transazione, le scritture sono in auto-commit: il dispose o il rollback non le annullano.
  Usare questa modalità per letture o singole operazioni consapevolmente autonome.
- Commit e rollback completano il lavoro: repository già ottenuti e `Repo<T>()` non sono più
  utilizzabili; dopo dispose non viene aperta una connessione nascosta.
- Nessuna operazione parallela sulla stessa UoW. Hashing, chiamate esterne e notifiche restano
  fuori dalla transazione. Per la regia rimane l'ordine coordinatore → transazione (§8.2).
- Update e Delete generici confrontano identificativo e versione letta. Update incrementa la
  versione solo se la scrittura riesce; un conflitto restituisce falso e conserva la versione
  della copia. Dopo rollback scartare le entità modificate in memoria e rileggerle.

Il login legge e verifica la password prima della transazione di scrittura; UPDATE condizionato
e INSERT della credenziale avvengono poi nello stesso lavoro. Un errore nell'INSERT annulla
anche l'UPDATE. Un tentativo errato viene invece confermato prima di restituire il rifiuto,
altrimenti rollback cancellerebbe il conteggio. Il bootstrap controlla l'assenza di admin attivi
e inserisce il primo account nella stessa transazione non differita.

Migrazioni, bootstrap del file/WAL e readiness mantengono gli accessi tecnici dedicati tramite
la factory: non sono CRUD applicativi. Lo schema è la somma degli script numerati da `0001` a `0010`;
una versione applicata non si modifica, quindi ogni cambiamento aggiunge uno script nuovo (§8.3).

## Schema corrente e compatibilità

La registrazione comprende ventidue repository: admin, credenziali, scuole, classi, corsi,
lezioni, esercizi, caselle della sequenza, sessioni, bozza, versioni pubblicate, contenuti privati
della versione, postazioni, partecipanti, stato del partecipante, registro di idempotenza, comandi di
regia, consegne dei comandi, coda outbox, assegnazioni, tentativi e permessi conclusivi. Non sono
presenti repository di contenuti,
media, associazioni corso-classe o iscrizioni. Gli script applicati sono
[0001_schema.sql](../src/Qwerty.Codi.Infrastructure/Migrations/Scripts/0001_schema.sql) e le
migrazioni successive fino a
[0010_session_join_code.sql](../src/Qwerty.Codi.Infrastructure/Migrations/Scripts/0010_session_join_code.sql).
All'avvio il database dichiara la propria versione: il 2 ottobre 2026 un database allo
schema 5 è passato allo **schema 6** senza ricreazione; la `0007` ha aggiunto tre tabelle, la `0008`
le assegnazioni e i tentativi e la `0009` il permesso conclusivo, senza toccare le precedenti. La
`0010` aggiunge a `lesson_session` la colonna `join_code` con il suo vincolo di formato e l'indice
unico del codice di ingresso: è una colonna nuova su una tabella esistente, quindi le righe già
presenti restano con `join_code` nullo e si raggiungono con la catena scuola → classe → sessione. Lo
schema corrente è la versione **10**.

Due repository non derivano da `BaseRepository`, perché la loro tabella non ha `row_version` o una
chiave semplice: `CommandDeliveryRepository` (chiave composta comando + partecipante) e
`OutboxMessageRepository` (coda di messaggi). Il secondo conclude un messaggio con un `UPDATE`
condizionato a `processed_at_utc_ms IS NULL` e conta le righe toccate: se un altro giro l'ha già
concluso, la scrittura non ha effetto e il dispatcher non segnala nulla. È la concorrenza
ottimistica della coda, che non può usare la versione di riga perché la tabella non la espone.

Il consolidamento ha riscritto la baseline di sviluppo: database creati con i vecchi
script non vengono aggiornati, ma rifiutati per versione/checksum divergenti. Usare
database temporanei nuovi; non cancellare o ricreare dati operativi implicitamente.

ReplaceLessonExercises usa una sola transazione: posizioni temporanee negative,
riuso delle righe per esercizio e rimozione delle caselle eccedenti. Controlla la versione
della lezione ma non la incrementa. Due editor simultanei della lezione sono fuori
perimetro per decisione utente (A-11); non si dichiara una protezione aggregata fra editor.

## Aggiungere un repository

1. Definire l'entità POCO e la conversione al dominio, con identificativo e RowVersion.
2. Registrare tabella, chiave e colonne in `RepoDbSqliteBootstrap`.
3. Definire l'interfaccia applicativa ereditando `IRepository<TEntity, TIdentity>`.
4. Derivare l'implementazione interna da `BaseRepository`, aggiungendo solo operazioni che
   richiedono una semantica ulteriore al CRUD o al filtro.
5. Registrarla scoped in `AddCodiPersistence` e risolverla dalla UoW del chiamante.
6. Verificare mapping, vincoli, conflitti e atomicità su file temporanei; aggiornare la
   [matrice dei test](TEST-COVERAGE-MATRIX.md). Per schema nuovo seguire le migrazioni versionate.

Un futuro behavior transazionale dovrà fornire il proprio lavoro agli handler partecipanti:
non si apre una seconda UoW indipendente all'interno di una transazione già governata dal
behavior. La pipeline di regia e l'idempotenza restano nei successivi incrementi.
