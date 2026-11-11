---
description: "Test xUnit Codì: livello, regressioni, determinismo, pipeline e persistenza."
applyTo: "**/tests/Qwerty.Codi.*/**"
---
# Test .NET

Fonti: §19 e §20.3–§20.5. Leggi [le regole comuni](../copilot-instructions.md).

- Scrivi test significativi per nuove funzionalità e difetti: devono fallire se il comportamento viene rimosso. Nessun Fact vuoto, asserzione tautologica o skip che maschera lavoro incompleto.
- Nomi Membro_Scenario_ComportamentoAtteso; Arrange, Act, Assert leggibili; Trait Category=Unit o Integration.
- Test Unit: logica isolata, nessun filesystem/SQLite/rete/attesa reale. Test Integration: cooperazione effettiva fra componenti.
- Domain.Tests ospita logica pura; IntegrationTests persistenza/pipeline; ProtocolTests contratti, autorizzazioni e ordinamento. Classifica il test in base alle risorse effettive, non al solo nome del progetto.
- Tempo con TimeProvider e FakeTimeProvider (aggiungere il relativo riferimento se necessario nel perimetro); flussi esterni sostituiti con handler controllati.
- SQLite: database temporaneo dedicato, dispose garantito, pulizia dei file dopo connessioni e transazioni. Prova anche errore/cancellazione e rollback senza commit quando pertinente.
- Autorizzazioni nel livello che le applica: identità anonima 401 e autenticata non autorizzata 403 sono casi distinti; proprietà risorsa e isolamento vanno verificati.
- Dispatcher: metadati, nomi sconosciuti, payload incoerenti, policy, canali ammessi e bootstrap locale non esposto.
- Area HTTP: provare l'esecuzione nell'area ammessa, rifiuto 404 nell'altra anche con credenziale valida, bootstrap locale escluso da entrambe; nell'area corretta preservare 401/403 e antiforgery. Usare la composizione reale come `HttpEndpointAreaTests`, senza presentare handler di prova come casi d'uso alunni implementati.
- Idempotenza: stesso payload non riesegue; chiave uguale con payload differente confligge. Query senza scritture, mutazioni atomiche in caso di errore.
- Per CRUD non basta un 200 o la registrazione della richiesta in uno stub: verifica il dato modificato e riletto con un'altra richiesta/unit of work. Per la UI controlla anche il valore mostrato dopo la rilettura e la versione usata dalla modifica successiva.
- Scegli status e codici dal contratto specifico: non uniformare ogni errore a 400.
- Test di frontend/E2E sono in Web quando lo scaffold li rende disponibili. Non introdurre riferimenti frontend nei progetti .NET solo per raccoglierli.
- Mantieni la matrice di copertura quando presente o quando la sua creazione è parte della fase; nessuna percentuale inventata.
- Scrivere i test fa parte dello sviluppo; eseguili e riporta l'esito reale. Non promuovere una fase a VALIDATO solo perché gli assembly compilano.
