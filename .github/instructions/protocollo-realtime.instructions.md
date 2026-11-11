---
description: "Regia live, hub, outbox, snapshot, lease, epoch, salvataggi e recovery di Codì."
applyTo: "**/src/Qwerty.Codi.Api/Hubs/**,**/src/Qwerty.Codi.Infrastructure/Realtime/**,**/src/Qwerty.Codi.Infrastructure/BackgroundJobs/**,**/src/Qwerty.Codi.Application/**/Sessions/**,**/src/Qwerty.Codi.Web/projects/*/src/app/core/**,**/src/Qwerty.Codi.Web/libraries/**,**/tests/Qwerty.Codi.ProtocolTests/**"
---
# Protocollo e recovery

Leggi §5–§6, §9, §11–§12, §15–§16 e gli scenari di §19.3 pertinenti prima di modificare il protocollo.

- Il server possiede lo stato desiderato. Distingui sempre consegna, applicazione confermata e salvataggio.
- Coordinatore per sessione → transazione breve → commit → distribuzione outbox; nessuna attesa di rete in transazione.
- Le revisioni sono monotone, non necessariamente contigue per partecipante. Nel JSON i contatori a 64 bit sono stringhe decimali.
- Duplicati ritrasmettono l'ACK noto senza rieseguire; vecchie revisioni, epoch, generazioni, run e istanze worker/server non alterano lo stato corrente.
- Trasporto connesso non significa player operativo: verifica credenziale, handshake, snapshot applicato e lease corrente.
- Una sola connessione operativa. Il vecchio OnDisconnected rimuove solo l'associazione con connection ID ed epoch ancora corrispondenti.
- Il riduttore ignora caricamenti HTTP e snapshot superati. Una direttiva già consumata non riparte al refresh; in dubbio si materializza recoveryBlocked.
- Lease: nonce, epoch, revisione e istanza server verificati; scadenza monotona calcolata dall'invio heartbeat. Una risposta tardiva non prolunga il permesso; worker e pagina lo verificano prima di ogni passo.
- Revoca verificata anche dopo l'handshake; gruppi assegnati dal server, isolamento fra partecipanti e sessioni.
- Salvataggi: una richiesta in corso per tentativo, coalescenza, stesso saveId/corpo nei retry, controlli di proprietà/epoch/versione nella transazione. Nessuna sovrascrittura cieca su 409/412.
- EndSession e cambio attività usano grant limitati: un salvataggio tardivo può aggiornare solo lo storico autorizzato. Non allargare la policy operativa per risolvere un ACK terminale ambiguo: consulta i punti aperti nello stato.
- Al riavvio: nuova istanza, recovery prima della readiness, pause persistite, vecchi avvii outbox superati.
- Telemetria e preview in cache limitata non dimostrano persistenza; PublishWorkspacePreview segue §15.5.
- Test deterministici con ritardi/disordine simulati e tempo controllabile; per E2E 12 contesti browser indipendenti, non 12 schede con cookie condivisi.

I valori di timeout e prestazione della specifica sono proposte configurabili, da misurare; non dichiararli garantiti dalla sola implementazione.
