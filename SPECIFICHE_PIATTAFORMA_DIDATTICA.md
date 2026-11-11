# Codì: piattaforma web per la regia delle lezioni di informatica

Versione: 0.17 — Bozza da discutere e migliorare
Data: 16 ottobre 2026
Destinatari: docente e sviluppatori

## 1. Obiettivo

Realizzare un'applicazione web per condurre in modo centralizzato lezioni di informatica rivolte a bambini di 8–9 anni, utilizzando inizialmente 12 PC per gli alunni e una postazione docente.

Il docente deve poter preparare la lezione, vedere chi è collegato, scegliere cosa mostrare, assegnare esercizi e controllarne lo svolgimento in tempo reale. I bambini devono utilizzare un'interfaccia essenziale, con poche azioni comprensibili e senza credenziali personali complesse.

**Principio guida: semplicità per l'alunno, controllo e visibilità per il docente.**

Questo documento descrive il prodotto e l'infrastruttura da realizzare. Non attesta la presenza di software già implementato. I requisiti esplicitamente richiesti sono distinti dalle proposte iniziali e dalle decisioni ancora aperte.

### 1.1 Perimetro aggiornato — 1 ottobre 2026

Per decisione dell'utente, associazioni corso-classe, iscrizioni ai corsi e gestione di
contenuti/immagini sono eliminate dal prodotto. Il corso organizza lezioni riutilizzabili;
la sessione collega una lezione a una classe. L'alunno entra con scuola, classe e nome:
il server crea direttamente la partecipazione alla sessione, senza iscrizione persistente.
Le lezioni sono sequenze di esercizi. Non è prevista la modifica simultanea della stessa
lezione da parte di due editor.

Il sorgente contiene scuole/classi, corsi/lezioni, anagrafica esercizi e sequenza ordinata, l'aula
live (sessioni, ingressi, regia e hub), l'autore Blockly con le versioni pubblicate e il player che
esegue l'esercizio nel browser dell'alunno, con salvataggi e osservazione del lavoro. L'ingresso in
aula usa il **codice di lezione** di otto cifre che il docente detta a voce (0.7); la scelta di
scuola e classe resta come ripiego. Restano da fare
il laboratorio su 12 PC e la grafica definitiva dell'app alunni. La
[mappa di ripresa](docs/RIPRESA-SVILUPPO.md) distingue codice presente, prove eseguite e
verifiche mancanti. Le checklist di §15 sono criteri progettuali, non un registro di avanzamento.

## 2. Requisiti confermati e scelte proposte

### 2.1 Requisiti richiesti

- Applicazione web con backend .NET 10 condiviso e due applicazioni Angular: admin/docente e public/alunni.
- SQLite come database e RepoDB per l'accesso ai dati.
- Un amministratore crea e gestisce scuole e classi.
- Il docente, avviata la lezione, riceve un **codice di lezione** numerico di otto cifre da dettare alla classe; il codice resta valido anche durante una pausa e non si riusa in un'altra lezione.
- L'alunno apre il sito, digita il codice di lezione e il proprio nome: non deve scegliere scuola, classe o sessione.
- In alternativa al codice, l'alunno può ancora scegliere scuola e classe ed entrare nella sessione in play di quella classe: è il ripiego, non la via ordinaria.
- L'alunno vede a colpo d'occhio, con **icone colorate e parole**, se è collegato, se i controlli sono attivi e che cosa sta facendo il suo programma.
- L'alunno **prova il proprio algoritmo** quando i controlli sono attivi e vede il personaggio muoversi sulla scena; la prova resta sua: non è un'esecuzione comandata dal docente e non ne prende il posto.
- L'alunno **compone e guarda insieme**: il progetto a blocchi e la scena stanno affiancati sulla stessa pagina, che usa tutta la larghezza dello schermo (0.9).
- L'alunno **sa dove si trova**: in cima alla pagina legge il percorso **scuola / classe / lezione / esercizio** — l'esercizio compare quando il docente lo assegna — e sotto il percorso il proprio nome (0.10). Il percorso si costruisce da ciò che la sessione sa di sé, senza chiedere nulla al bambino.
- Il docente vede in tempo reale l'arrivo e la presenza dei singoli alunni.
- Il docente sceglie gli esercizi e la schermata di attesa da mostrare sui client.
- L'admin/docente crea esercizi di programmazione a blocchi con Blockly e li assegna ai client perché gli alunni li risolvano.
- Gli alunni eseguono gli esercizi nel browser.
- Il docente osserva lo svolgimento e può fermare e far ripartire gli esercizi.
- Il sistema deve gestire almeno 12 client contemporanei, oltre alla postazione docente.

### 2.2 Proposte iniziali da confermare

Le decisioni del 1 ottobre 2026 confermano i due punti seguenti su sessione per classe e accesso degli alunni; le altre voci restano proposte.

- Nella prima versione amministratore e docente coincidono in un unico ruolo autenticato.
- **Confermato il 1 ottobre 2026.** Una sola sessione attiva per classe; un solo docente ne detiene il controllo. Istanziare una lezione in una classe crea la sessione di quel momento e non un'associazione permanente tra lezione e classe (funzionale §5.1).
- **Confermato il 1 ottobre 2026.** L'accesso degli alunni è possibile soltanto quando la sessione è in play, cioè dopo l'avvio del docente.
- **Deciso il 9 ottobre 2026.** L'ingresso ordinario è il **codice di lezione** di otto cifre, detto a voce dal docente: la home dell'alunno si accorge che una lezione è in play e chiede il codice e il nome. La scelta di scuola e classe, che chiedeva all'alunno informazioni che non ha, resta come ripiego per chi non ha il codice.
- I nomi inseriti sono nomi brevi o soprannomi; non si richiedono email, cognome o data di nascita.
- Il monitoraggio mostra lo stato dell'applicazione e degli esercizi, senza acquisire il desktop.
- Il motore didattico di esecuzione degli esercizi Blockly viene sviluppato per la piattaforma.
- Prima installazione su un unico server, preferibilmente raggiungibile nella rete della scuola.

## 3. Perimetro del controllo

Il docente controlla **ciò che avviene dentro l'applicazione web**: schermata attiva, esercizio, possibilità di interagire, esecuzione e stato di avanzamento.

Un normale sito web non offre il controllo generale del PC: non può impedire in modo affidabile la chiusura del browser, bloccare altre applicazioni o guidare mouse e tastiera del sistema operativo. Un'eventuale modalità chiosco deve essere configurata separatamente sui PC.

La visibilità della scheda del browser può essere segnalata come indizio operativo, ma non dimostra che il bambino stia seguendo la lezione. La cattura dello schermo richiede un flusso specifico di autorizzazione dell'utente e non rientra nella prima versione. Riferimento: [MDN — acquisizione dello schermo](https://developer.mozilla.org/en-US/docs/Web/API/MediaDevices/getDisplayMedia).

## 4. Ruoli e organizzazione

| Concetto | Significato |
|---|---|
| Amministratore/docente | Utente autenticato che prepara e conduce le attività. |
| Scuola | Contenitore delle classi, con nome e stato attivo/archiviato. |
| Classe | Gruppo di alunni appartenente a una scuola, ad esempio “3ª A”. |
| Corso | Percorso didattico che raccoglie lezioni riutilizzabili con classi diverse. |
| Lezione | Sequenza ordinata di esercizi di un corso. |
| Sessione | Erogazione concreta di una lezione a una classe, in una certa data. |
| Lezione in play | Sessione **avviata** (`Active` oppure `Paused`) **con ingressi aperti**, unica per classe, raggiungibile dagli alunni con il codice di lezione. La pausa sospende l'attività, non l'ingresso: chi arriva durante una spiegazione entra e vede l'attesa. |
| Codice di lezione | Otto cifre numeriche che il docente riceve all'avvio e detta a voce alla classe: l'alunno le digita per entrare. Vale solo per la lezione avviata — anche in pausa — e non si riusa in un'altra lezione. Non identifica nessuno. |
| Partecipante | Alunno ammesso a una sessione, con identificativo stabile durante quella sessione. |
| Postazione | Identificativo del browser/dispositivo, eventualmente etichettato “PC 01”. |
| Tentativo | Lavoro di un partecipante su una specifica attività assegnata. |

Il nome serve per la visualizzazione, non come identificativo tecnico. Due bambini possono avere lo stesso nome. La postazione non coincide con l'alunno: lo stesso PC può essere utilizzato da persone diverse in lezioni successive.

## 5. Flusso della lezione

### 5.1 Preparazione del docente

1. Accede all'area amministrativa.
2. Crea o seleziona scuola, classe e corso.
3. Prepara una lezione con una sequenza di attività.
4. Istanzia la lezione nella classe: sceglie la classe e la lezione del corso; il sistema crea la sessione di quel momento per quella coppia, in attesa e con gli ingressi chiusi. L'istanziazione non crea un'associazione permanente tra lezione e classe.
5. Avvia la lezione: la sessione va in play, gli ingressi si aprono e i client mostrano la schermata di attesa, senza attività assegnata.
6. Legge il **codice di lezione** di otto cifre che il sistema gli mostra e lo detta agli alunni: è l'unica informazione che i bambini devono ricevere per entrare.
7. Visualizza l'indirizzo del sito da aprire sui 12 PC.

### 5.2 Accesso dell'alunno

1. Apre il sito.
2. Il sito si accorge che una lezione è in play e chiede il **codice di lezione**.
3. Digita le otto cifre che il maestro ha detto.
4. Inserisce il nome e preme **Entra**.
5. Il server individua la sessione dal codice e crea la partecipazione alla sessione con una credenziale di sessione limitata ai permessi dell'alunno.
6. Il bambino vede “Ciao, [nome]! Aspetta il maestro” oppure l'attività già assegnata.
7. Il docente vede immediatamente la nuova presenza, senza aggiornare la pagina.

Se l'alunno non ha il codice, il sito offre la scelta di scuola e classe: individua la lezione in play di quella classe e l'ingresso prosegue allo stesso modo. È un ripiego, non la via ordinaria: chiede al bambino informazioni che non ha.

Il codice vale soltanto per una sessione in play, e la pausa non lo invalida: chi arriva durante una spiegazione entra e vede l'attesa. Dopo alcuni codici errati di seguito il sito invita ad aspettare un momento prima di riprovare. Un codice sbagliato non rivela se una lezione esiste.

Gli alunni accedono soltanto a una sessione in play: una sessione istanziata ma non ancora avviata non è accessibile. Se gli ingressi sono chiusi, il sito mostra un messaggio semplice. La chiusura dei nuovi ingressi non interrompe i partecipanti già ammessi. La riconnessione di una partecipazione valida deve restare possibile finché la sessione non viene terminata o il partecipante escluso.

### 5.3 Conduzione

Il docente seleziona un esercizio e lo assegna alla classe. Può avviare un'attività, sospenderla per una spiegazione e riprenderla. Può anche selezionare una singola postazione per assegnarle un'attività o intervenire sul suo esercizio.

L'alunno non sceglie liberamente altre lezioni e non può riattivare un esercizio bloccato dal docente.

Quando i controlli sono attivi l'alunno **prova** il proprio programma: preme **Prova** (o avanza **Un passo** alla volta) e vede il personaggio muoversi sulla scena, con l'esito che il gioco gli dà — traguardo raggiunto, ostacolo, uscita dalla griglia, passi esauriti. La prova è un modo per capire che cosa fa il proprio algoritmo prima di farlo partire per la classe: non cambia il progetto, non produce un salvataggio conclusivo e non compare come esecuzione nella regia. Quando il docente comanda l'esecuzione, la prova si ferma e la scena mostra la corsa comandata.

### 5.4 Chiusura

Lo **Stop** del docente è il termine definitivo della sessione. I client interrompono l'esecuzione, inviano l'ultimo salvataggio disponibile e mostrano una schermata di saluto. Gli invii conclusivi devono essere accettati soltanto per il tentativo già autorizzato, entro una finestra definita; non possono riaprire la sessione. Una sessione terminata non viene riaperta: per una nuova lezione operativa si istanzia e si avvia una nuova sessione.

L'admin vede eventuali client che non hanno confermato la chiusura e l'istante dell'ultimo salvataggio ricevuto.

## 6. Funzioni amministrative

### 6.1 Gestione dell'organizzazione

- Creare, modificare e archiviare scuole e classi.
- Creare, modificare e archiviare corsi e relativa anagrafica degli esercizi.
- Gestire lezioni e ordinare i relativi esercizi; un solo editor modifica una lezione alla volta.
- Consultare le partecipazioni alle sessioni.
- Correggere il nome visualizzato o escludere un partecipante dalla sessione.
- Aprire e chiudere gli ingressi, avviare la sessione in play, sospendere e riprendere la classe, terminare definitivamente la sessione.
- Eliminare definitivamente una sessione **conclusa o mai avviata**, con conferma esplicita: la sessione sparisce con le sue presenze, i suoi comandi e i suoi lavori, mentre scuole, classi, lezioni ed esercizi restano. Una sessione in corso non si elimina: prima si conclude.

L'archiviazione conserva lo storico. Le eventuali funzioni di cancellazione dei dati devono essere separate e chiaramente riconoscibili: l'eliminazione di una sessione è una di queste, non è la chiusura della lezione e non è raggiungibile per errore da un comando di regia.

### 6.2 Pannello di regia

La schermata principale deve mostrare una griglia di 12 riquadri, adattabile al numero effettivo di partecipanti. Ogni riquadro contiene:

- Nome dell'alunno ed eventuale numero della postazione.
- Presenza: connesso, in riconnessione, disconnesso.
- Attività assegnata e stato effettivamente confermato dal client.
- Esecuzione: pronto, in esecuzione, in pausa, terminato, errore.
- Avanzamento essenziale e ultimo aggiornamento ricevuto.
- Esito dell'ultimo comando: in attesa, applicato, fallito o non confermato.
- Dalla fase degli esercizi a blocchi: anteprima della scena e accesso al dettaglio del lavoro.

I controlli di regia sono **Avvia**, **Pausa**, **Riprendi** e **Stop**. Avvia e Stop riguardano l'intera sessione; Pausa e Riprendi possono essere collettivi o individuali. Il docente deve poter agire su tutta la classe o su un singolo alunno. L'ambito del comando deve essere sempre evidente, ad esempio “Pausa per tutti” oppure “Pausa per Luca — PC 04”.

## 7. Comandi e comportamento degli esercizi

La possibilità di lavorare e lo stato del programma sono distinti. Quando l'attività è abilitata, il bambino può modificare i blocchi e premere **Prova**. Quando il docente mette in pausa, entrambi vengono bloccati.

| Comando docente | Comportamento atteso |
|---|---|
| Avvia lezione | Porta in play la sessione della classe, apre gli ingressi e mostra sui client la schermata di attesa, senza attività assegnata. |
| Mostra attesa | Sospende l'attività e mostra una schermata di attesa, conservando il lavoro. |
| Assegna esercizio | Carica la versione scelta in stato pronto, inizialmente bloccato. |
| Abilita attività | Consente di modificare i blocchi e utilizzare i comandi locali previsti. |
| Esegui | Avvia il programma corrente dei destinatari; richiede un'attività abilitata. |
| Pausa | Sospende l'esecuzione nel punto raggiunto e blocca le modifiche. |
| Riprendi | Ripristina le possibilità precedenti; continua l'esecuzione solo se era in corso. |
| Riavvia esecuzione | Mantiene i blocchi dell'alunno, azzera la scena e avvia il programma dall'inizio. |
| Azzera lavoro | Crea un nuovo tentativo dal progetto iniziale, conservando quello precedente nello storico. Richiede conferma docente. |
| Stop | Termina definitivamente la sessione: interrompe le attività e chiude la partecipazione operativa alla lezione; non viene riaperta. |

“Fermare e far ripartire” deve quindi coprire due azioni diverse: **riprendere dal punto raggiunto** e **ricominciare l'esecuzione dall'inizio**.

Regole proposte per evitare conflitti:

- Un comando per tutta la classe si applica anche agli alunni con una precedente assegnazione individuale e annulla gli override pertinenti.
- Un comando individuale successivo modifica soltanto il destinatario, senza cambiare l'impostazione per i nuovi ingressi.
- La pausa generale blocca tutti; il docente può poi riprendere esplicitamente un singolo alunno.
- La chiusura della sessione prevale sempre su qualsiasi comando precedente.
- Operazioni simultanee vengono ordinate dal server e ricevono una revisione progressiva.
- Gli alunni entrati in ritardo ricevono lo stato corrente della classe, senza riprodurre tutti i comandi passati.

## 8. Interfaccia per bambini di 8–9 anni

- Testi brevi, in italiano, con caratteri grandi e buon contrasto.
- Pulsanti grandi con icona e testo: **Entra**, **Prova**, **Ferma**, **Aiuto**.
- Una sola attività principale alla volta e navigazione ridotta al minimo.
- Nessun menu tecnico, elenco di errori o identificativo interno visibile al bambino.
- Colori accompagnati da testo o icone, senza affidare il significato soltanto al colore.
- Salvataggio automatico con indicazione comprensibile dello stato.
- Messaggi rassicuranti per attesa, pausa e perdita di connessione.
- Azioni utilizzabili con mouse e tastiera; trascinamento dei blocchi facilitato.
- Messaggi brevi con il maestro, con il campo della risposta dove il bambino lo cerca.

Esempi: “Guarda il maestro”, “Tra poco ricominciamo”, “Sto cercando di ricollegarmi”. Il pulsante **Aiuto**, previsto come evoluzione, invia una richiesta al docente senza aprire chat tra bambini.

### 8.1 Messaggi con il maestro

Il maestro può scrivere a un alunno dalla sua card nella pagina dei partecipanti, e l'alunno
**risponde** dalla propria pagina: il pannello si apre da sé quando arriva un messaggio, perché a
otto anni non si cerca una conversazione. Il messaggio è breve — al massimo 300 caratteri — e il
testo si mostra come testo, mai come HTML.

La conversazione è **fra il maestro e quel bambino**: non esiste una chat fra bambini e il bambino
non può scrivere nel thread di un altro. La pagina dell'alunno mostra sempre ciò che il maestro ha
scritto, anche durante una pausa, ma la risposta è ammessa solo mentre la pagina è al lavoro: in
attesa o in pausa il campo non c'è e la pagina dice perché, invece di offrire un campo che non
funziona.

Il maestro vede sulla card quanti messaggi dell'alunno non ha ancora letto, e il conteggio si azzera
quando apre la conversazione. La conversazione **non resta**: vive finché dura la lezione e viene
eliminata alla fine, quindi non c'è uno storico da consultare dopo la lezione.

## 9. Esercizi di programmazione a blocchi

### 9.1 Ambito

“Scratch-like” significa un ambiente visuale semplificato in cui collegare blocchi per controllare un personaggio o risolvere un problema. Non implica, nella prima versione, compatibilità con file Scratch `.sb3` o riproduzione completa di Scratch.

L'editor utilizza Blockly sia nell'area di creazione dell'admin sia nell'area di risoluzione degli alunni. Blockly fornisce l'editor a blocchi personalizzabile; scena, regole dell'esercizio, esecuzione e monitoraggio devono essere realizzati nella piattaforma. Riferimento: [Blockly — panoramica](https://docs.blockly.com/guides/get-started/what-is-blockly/).

### 9.2 Creazione da parte del docente

L'admin deve disporre di un editor visuale Blockly integrato nell'area amministrativa per creare e modificare gli esercizi. Definisce la consegna, la scena e i blocchi utilizzabili, compone la soluzione di riferimento, prova l'attività e la pubblica per l'assegnazione. Il **progetto iniziale degli alunni è vuoto**: il docente non lo compone, così l'alunno parte da un lavoro che il maestro può vedere e correggere per intero.

Il docente deve poter impostare:

- Titolo e consegna breve.
- Scenario iniziale, personaggio e obiettivo.
- Blocchi disponibili.
- **Durata massima della corsa, espressa in minuti.** Gli altri limiti tecnici del motore — numero di blocchi, profondità di annidamento, passi di esecuzione — restano fissati dal sistema e non sono campi del docente: sono vincoli tecnici, non scelte didattiche.
- Condizioni di completamento e messaggi di feedback.
- Eventuali suggerimenti.

La prima versione dell'autore può basarsi su un modello guidato, ad esempio “porta il personaggio al traguardo”. Un editor generalista di giochi e animazioni è un'evoluzione successiva.

Blocchi iniziali proposti: inizio, avanti, gira a destra/sinistra, ripeti un numero di volte. Condizioni, variabili e altri blocchi saranno aggiunti progressivamente.

Gli esercizi hanno una bozza modificabile e versioni pubblicate immutabili. Una sessione usa una versione precisa: modificare la bozza non altera l'esercizio già in corso.

#### Preparazione e disponibilità per le lezioni

Il docente usa un solo comando **Rendi disponibile** nell'editor di ogni tipologia.
Il comando salva le modifiche e verifica i dati prima di pubblicare la revisione salvata.
**Salva bozza** resta un'azione separata per conservare il lavoro incompleto.
I controlli ammettono un programma iniziale vuoto o parziale negli esercizi da completare.
Le domande richiedono una risposta corretta prima della disponibilità; una bozza può conservarla ancora vuota.

La barra delle azioni resta visibile durante lo scorrimento della pagina.
L'editor e l'elenco mostrano **Bozza**, **Disponibile** oppure **Modifiche da pubblicare**.
La ripubblicazione di una versione precedente rende quel contenuto disponibile per le prossime assegnazioni.
Lo stato considera anche la soluzione privata e confronta la bozza con l'ultima versione pubblicata.
Una versione pubblicata che **non sia la più recente** si può eliminare, con conferma nella riga dell'elenco: la cronologia si ripulisce senza toccare il materiale. L'ultima versione resta, perché è quella che le prossime assegnazioni userebbero, e una versione già assegnata a una lezione non si elimina: il lavoro degli alunni non cambia sotto i loro occhi. L'eliminazione tocca la versione e i suoi contenuti privati; bozza, esercizio e lezioni non cambiano.
Gli errori indicano il dato da correggere e permettono di raggiungere il controllo associato.
Un errore conserva il lavoro del docente e interrompe la pubblicazione.
Rendere disponibile un esercizio non avvia l'attività degli alunni. L'assegnazione resta nella regia.

#### Assegnazione e risoluzione sui client

L'admin seleziona un esercizio pubblicato e lo assegna a tutta la classe o a un singolo client. Sul browser dell'alunno compaiono la consegna, lo scenario e il progetto iniziale nell'editor Blockly. Quando il docente abilita l'attività, il bambino costruisce o completa la sequenza di blocchi, la esegue e la corregge per risolvere il problema.

Ogni alunno lavora su una copia indipendente: le sue modifiche non alterano il modello creato dall'admin né il lavoro degli altri. Il sistema salva il tentativo e ne comunica avanzamento ed esito al docente in tempo reale. Restano disponibili i comandi centralizzati di pausa, ripresa e riavvio.

### 9.3 Motore di esecuzione

- Interpretare un insieme limitato di istruzioni didattiche, evitando l'esecuzione libera di codice JavaScript fornito dagli utenti.
- Conservare lo stato necessario a sospendere e riprendere un programma, inclusi cicli, istruzione corrente e scena.
- Eseguire passi brevi e interrompibili, lasciando l'interfaccia e i comandi remoti reattivi.
- Prevedere un limite di passi e di durata per gestire programmi che non terminano.
- Mostrare il blocco in esecuzione e un messaggio semplice in caso di errore.
- Separare il progetto a blocchi, lo stato della scena e l'avanzamento dell'esecuzione.

## 10. Monitoraggio in tempo reale

Il docente deve vedere **cosa sta facendo l'alunno nell'esercizio**, non soltanto sapere se il browser è collegato.

Nella fase degli esercizi a blocchi il client invia modifiche del progetto, stato della scena, istruzione corrente, avanzamento, completamenti ed errori. Il pannello ricostruisce una vista in sola lettura, con possibilità di ingrandire un alunno.

Le anteprime sono rappresentazioni dello stato ricevuto, non flussi video del monitor. Non eseguono autonomamente il programma dell'alunno e riportano il momento dell'ultimo aggiornamento.

Valori iniziali proposti, da misurare sui PC reali:

- Aggiornamenti ordinari aggregati: massimo 2 al secondo per client.
- Cambiamenti importanti, come pausa applicata o completamento: invio immediato.
- Progetti completi: all'apertura, alla risincronizzazione e con salvataggi limitati in frequenza.
- Obiettivo di salvataggio: entro 5 secondi dall'ultima modifica, salvo disconnessioni.

Non salvare ogni movimento del mouse. Trasmettere i soli dati utili alla didattica e al ripristino del lavoro.

## 11. Architettura tecnica

### 11.1 Componenti

| Componente | Tecnologia e responsabilità |
|---|---|
| Frontend admin | Applicazione Angular e TypeScript indipendente per docente, autore e regia, con Spartan UI. |
| Frontend alunni | Applicazione Angular e TypeScript indipendente per ingresso e player; grafica da definire. |
| Backend | ASP.NET Core su .NET 10; API HTTP, autenticazione, autorizzazioni e regole applicative. |
| Comunicazione live | ASP.NET Core SignalR e client TypeScript `@microsoft/signalr`. |
| Persistenza | SQLite sul disco del server e RepoDB tramite provider SQLite. |
| Editor visuale | Blockly per la creazione degli esercizi da parte dell'admin e la risoluzione da parte degli alunni. |

Architettura iniziale: monolite modulare con una sola istanza backend. Non sono necessari microservizi o infrastrutture distribuite per il primo laboratorio.

Le due applicazioni condividono contratti e motore degli esercizi nello stesso workspace Angular,
con bundle e stili distinti. L'admin è servito sotto `/admin/`, gli alunni alla radice: entrambi
consumano le stesse API, con operazioni e dati autorizzati dal backend secondo il ruolo.

Le API HTTP gestiscono anagrafiche, configurazioni degli esercizi e salvataggi. SignalR distribuisce stato e comandi e riceve conferme, presenza e aggiornamenti del lavoro. Le regole applicative restano in servizi condivisi, fuori dagli hub.

### 11.2 Struttura proposta del repository

```text
src/
  Codi.Api/                 API, autenticazione, hub SignalR e hosting
  Codi.Application/         Casi d'uso, contratti e gestione delle sessioni
  Codi.Domain/              Entità, stati e regole
  Codi.Infrastructure/      RepoDB, SQLite e migrazioni
  Qwerty.Codi.Web/      Workspace Angular condiviso
    projects/
      admin/     Docente, autore e regia; Spartan UI
      public/  Ingresso alunni e player
    libraries/             Contratti, trasporto e motore comuni
tests/
  Codi.UnitTests/
  Codi.IntegrationTests/
  aula-e2e/
docs/
```

Le versioni precise dei pacchetti devono essere fissate durante l'avvio del progetto. La compatibilità della combinazione .NET 10, RepoDB e provider SQLite deve essere verificata con un test di integrazione; non si assume verificata da questa specifica. Il provider candidato è `RepoDb.Sqlite.Microsoft` con `Microsoft.Data.Sqlite`. Riferimento: [RepoDB — SQLite](https://repodb.net/tutorial/get-started-sqlite/).

### 11.3 Persistenza

- Repository con query parametrizzate e transazioni esplicite per operazioni correlate.
- Script SQL numerati e tabella delle migrazioni applicate; esecuzione controllata all'avvio o al rilascio.
- Chiavi esterne, indici e vincoli di unicità coerenti con il dominio.
- Timestamp in UTC e visualizzazione locale nell'interfaccia.
- Database e backup su disco persistente, fuori dalla directory pubblica del sito.
- Modalità WAL e gestione dei timeout di lock da verificare nei test concorrenti.
- Scritture brevi e aggregate: non una transazione per ogni evento grafico.
- Procedura di backup coerente con SQLite e prova di ripristino; evitare la semplice copia a caldo del solo file principale.

SQLite in modalità WAL consente lettori contemporanei a uno scrittore, ma non elimina la serializzazione delle scritture. Il database deve essere locale al server, non condiviso direttamente dai PC degli alunni. Riferimento: [SQLite — WAL](https://www.sqlite.org/wal.html).

## 12. Affidabilità e protocollo SignalR

### 12.1 Identità e autorizzazione

Usare gruppi logici per sessione, docenti e partecipanti. Il server decide l'appartenenza ai gruppi in base alla credenziale autenticata e alla sessione autorizzata.

L'identità del partecipante deve essere indipendente dal `connectionId`. Dopo una riconnessione, verificare nuovamente i permessi, ricostruire le associazioni e inviare uno stato completo. I gruppi SignalR non sostituiscono i controlli di autorizzazione. Riferimento: [Microsoft — utenti e gruppi SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/groups?view=aspnetcore-10.0).

### 12.2 Comandi confermati e stato autorevole

Il server è la fonte dello stato desiderato della lezione. Il pannello distingue sempre tale stato dallo stato realmente confermato dai client.

Ogni comando contiene almeno `commandId`, sessione, destinatario/ambito, revisione, tipo, dati e istante di emissione. I comandi relativi agli esercizi identificano anche assegnazione, versione dell'esercizio e tentativo.

1. Il server autorizza il docente e valida la transizione richiesta.
2. Salva il nuovo stato e il comando prima di distribuirlo.
3. Il client scarta revisioni obsolete e non applica due volte lo stesso comando.
4. Applica il comando e restituisce un esito associato a `commandId` e revisione.
5. L'admin vede “applicato” soltanto dopo la conferma.

Un comando senza conferma entro 5 secondi è segnalato come non confermato, non come riuscito. Eventuali ritentativi mantengono lo stesso identificativo. Alla riconnessione il client riceve lo stato corrente: i comandi transitori superati non vengono riprodotti ciecamente.

Un riavvio o azzeramento deve essere riconoscibile anche nello stato sincronizzato, ad esempio attraverso una generazione di esecuzione o un nuovo tentativo, per non dipendere dalla consegna di un singolo messaggio.

### 12.3 Connessione e ripristino

- Configurare la riconnessione automatica e gestire anche il fallimento della prima connessione.
- Durante una disconnessione rilevata, sospendere localmente l'esecuzione e bloccare nuove modifiche.
- Al ripristino, sincronizzare lo stato prima di riabilitare l'attività.
- Una pagina aggiornata recupera la partecipazione tramite la credenziale, senza iscrivere nuovamente l'alunno.
- Il solo nome non permette di recuperare il lavoro di un'altra persona. Il cambio PC richiede un'associazione effettuata dal docente.
- Consentire una sola connessione operativa per partecipante; una seconda scheda deve essere gestita senza duplicare comandi e presenze.
- Dopo un riavvio del server, ricostruire lo stato persistito e porre le attività in pausa finché il docente non le riprende.

Il client SignalR richiede una configurazione esplicita della riconnessione; i tentativi di avvio iniziale devono essere gestiti separatamente. Riferimento: [Microsoft — client JavaScript SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client?view=aspnetcore-10.0).

Un comando remoto non può raggiungere un PC scollegato. Come protezione operativa, prevedere un'autorizzazione di esecuzione rinnovata periodicamente dal server: alla scadenza il client si mette in pausa. Una durata iniziale di 10 secondi è da validare. Schede sospese e reti instabili possono ritardare il rilevamento: al ritorno in primo piano il client verifica la validità prima di eseguire altri passi.

Il ripristino dopo un crash del browser garantisce il recupero dell'ultimo progetto confermato come salvato. La ripresa dall'esatta istruzione richiede anche un checkpoint valido del motore; in sua assenza il sistema resta in pausa e propone al docente il riavvio dell'esecuzione.

### 12.4 Contratti iniziali proposti

| Direzione | Messaggi |
|---|---|
| Server → client | `SessionSnapshot`, `CommandIssued`, `SessionEnded` |
| Client → server | `CommandAcknowledged`, `ActivityStateUpdated`, `Heartbeat` |
| Server → docente | `ParticipantJoined`, `ParticipantPresenceChanged`, `ParticipantStateUpdated`, `CommandStatusChanged` |

Definire DTO versionati, dimensioni massime e validazione dei payload. Il server ricava l'identità dalla credenziale: non si fida di un `participantId` inviato dal browser. Gli aggiornamenti di un vecchio tentativo non devono sovrascrivere quello corrente.

## 13. Modello dati iniziale

| Entità | Dati principali |
|---|---|
| `AdminUser` | Identità amministrativa, credenziale protetta, stato. |
| `School` | Nome, stato, date di creazione/modifica. |
| `Classroom` | Scuola, nome, anno scolastico obbligatorio con anni consecutivi, stato. |
| `Course` | Titolo, descrizione, stato. |
| `Lesson` | Corso, titolo e descrizione. |
| `LessonExercise` | Lezione, posizione e riferimento all’identità dell’esercizio; la versione pubblicata viene fissata all’assegnazione. |
| `Exercise` / `ExerciseVersion` | Identità dell'esercizio e definizioni pubblicate versionate. |
| `LessonSession` | Lezione, classe, docente, ingressi aperti, stato e revisione. |
| `SessionParticipant` | Sessione, nome visualizzato, postazione, stato di ammissione; nomi duplicati ammessi. |
| `ActivityAssignment` | Sessione, destinatario/ambito, attività, versione e stato desiderato. |
| `ExerciseAttempt` | Partecipante, assegnazione, progetto, checkpoint, esito e ultimo salvataggio. |
| `SessionCommand` / `CommandDelivery` | Comando, destinatari e conferme individuali. |

Le connessioni attive e la telemetria frequente possono risiedere in memoria nella prima istanza server. Anagrafiche, sessioni, assegnazioni, comandi significativi e lavoro degli alunni devono essere persistenti. Dopo un riavvio, nessuna presenza è considerata attiva finché il client non si ricollega.

## 14. Accesso e protezione dei dati

- Autenticazione per l'admin con password gestite tramite meccanismi consolidati, hashing e limitazione dei tentativi.
- Credenziale casuale dell'alunno, limitata alla partecipazione autorizzata, revocabile e con scadenza.
- Autorizzazione server su ogni API, connessione e metodo SignalR: un alunno non può impartire comandi né leggere il lavoro altrui.
- HTTPS, gestione sicura dei cookie e protezione CSRF dove si usano cookie di autenticazione.
- Niente credenziali nei log; evitare dati identificativi nei log tecnici.
- Nessun elenco pubblico degli alunni. Nomi duplicati gestiti con un suffisso/postazione visibile al docente.
- Per l'accesso iniziale, pubblicare soltanto scuole, classi e sessioni abilitate dal docente; limitare ingressi e richieste anomale.
- Se il sito è esposto su Internet, definire un filtro aggiuntivo all'ingresso, come codice breve o ammissione docente, prima del rilascio pubblico.
- Prevedere durata di conservazione, cancellazione dei lavori e pulizia delle credenziali sui PC condivisi.

La registrazione con il solo nome è una partecipazione guidata alla lezione, non una verifica dell'identità del bambino. L'eventuale riconoscimento persistente tra lezioni deve essere progettato senza aggiungere difficoltà al suo accesso.

## 15. Prima fase: infrastruttura utilizzabile

La prima fase deve dimostrare l'intero percorso docente → server → 12 browser, prima di realizzare il motore completo a blocchi.

### Incluso

- [ ] Soluzione .NET 10, applicazione Angular e struttura modulare.
- [ ] SQLite, RepoDB, migrazioni iniziali e configurazioni per sviluppo/esercizio.
- [ ] Creazione iniziale sicura dell'account admin e login.
- [ ] Gestione di scuole, classi e struttura minima corsi/lezioni.
- [ ] Ciclo di vita della sessione: istanziazione, avvio in play, apertura e chiusura degli ingressi, pausa e ripresa della classe, conclusione.
- [ ] Accesso alunni con il codice di lezione e il nome; scelta di scuola e classe come ripiego.
- [ ] Stato dell'alunno leggibile a colpo d'occhio con icone colorate e parole: collegamento, controlli, programma.
- [ ] Prova locale dell'algoritmo dell'alunno, con l'animazione del personaggio e l'esito del gioco, senza toccare l'esecuzione comandata dal docente.
- [ ] Dashboard con presenze live per almeno 12 partecipanti.
- [ ] Schermata di attesa applicabile a tutti o a un singolo client, conservando il lavoro sospeso.
- [ ] Attività dimostrativa interrompibile, ad esempio un personaggio che avanza a passi, per verificare pausa, ripresa e riavvio.
- [ ] Conferme per client, stato sincronizzato e gestione dei comandi duplicati.
- [ ] Riconnessione, recupero della partecipazione e comportamento in caso di server riavviato.
- [ ] Salvataggio del lavoro dimostrativo e visualizzazione dell'ultimo stato ricevuto.
- [ ] Log diagnostici, controllo di salute del servizio e istruzioni per l'avvio in laboratorio.
- [ ] Test di isolamento fra sessioni e verifica con 12 client contemporanei.

### Fase successiva

- [ ] Editor Blockly nell'area admin per creare, provare e pubblicare esercizi.
- [ ] Assegnazione degli esercizi alla classe o a singoli client e risoluzione in Blockly su copie indipendenti.
- [ ] Motore interrompibile con stato serializzabile.
- [ ] Progetti versionati, tentativi e salvataggio automatico.
- [ ] Vista live dei blocchi e della scena di ogni alunno.
- [ ] Obiettivi, completamenti, suggerimenti e richiesta di aiuto.

### Evoluzioni eventuali

Ruoli separati, più docenti, report tra lezioni, modelli di esercizio aggiuntivi, import/export e integrazione con Scratch. Il controllo remoto del sistema operativo e lo streaming dei desktop costituiscono ambiti separati.

## 16. Criteri di accettazione

Le soglie seguenti sono obiettivi iniziali da verificare in laboratorio, non prestazioni già misurate.

| ID | Scenario | Risultato atteso |
|---|---|---|
| AC-01 | L'admin crea scuola, classe e corso e riavvia il servizio. | Le configurazioni rimangono disponibili. |
| AC-02 | Dodici alunni accedono alla sessione in play. | L'admin vede dodici partecipazioni distinte senza refresh, anche con nomi uguali. |
| AC-03 | Un alunno entra durante la lezione. | Entro 2 secondi dal completamento dell'accesso compare nel pannello e riceve l'attività corrente, sulla rete di prova. |
| AC-04 | Il docente mostra attesa alla classe durante un esercizio. | I 12 client connessi applicano e confermano la vista di attesa, conservando il lavoro; quelli senza conferma sono identificati chiaramente. |
| AC-05 | Il docente mette tutti in pausa e poi riprende. | Esecuzione e modifiche vengono bloccate; la ripresa conserva lavoro e punto raggiunto quando disponibile. |
| AC-06 | Il docente riavvia l'esecuzione di un singolo alunno. | Solo quel client riparte dall'inizio mantenendo i propri blocchi. |
| AC-07 | Un comando viene ritrasmesso o arriva in ritardo. | Non provoca doppie azioni e non ripristina uno stato superato. |
| AC-08 | Un PC perde la rete durante l'attività. | Si mette in pausa alla rilevazione o alla scadenza dell'autorizzazione; il pannello non lo mostra come aggiornato. |
| AC-09 | Un alunno aggiorna la pagina o si riconnette dopo una pausa generale. | Recupera la stessa partecipazione e lo stato corrente, senza ripartire autonomamente. |
| AC-10 | Si riavvia il server. | Dati e lavoro confermato sono recuperati; la sessione torna in pausa e le presenze vengono ricostruite. |
| AC-11 | Un alunno prova a inviare un comando docente o leggere un altro tentativo. | Il server rifiuta l'operazione. |
| AC-12 | Si termina la sessione. | I client raggiungibili mostrano la conclusione; nuovi ingressi e comandi operativi vengono rifiutati. |
| AC-13 | In fase Blockly un alunno modifica il progetto o esegue il programma. | Il docente vede blocchi e scena aggiornati entro 2 secondi nelle condizioni di prova. |
| AC-14 | Un programma contiene un ciclo molto lungo. | Il motore resta interrompibile e applica i limiti senza bloccare l'interfaccia. |
| AC-15 | Un client di un'altra sessione tenta di iscriversi al gruppo live della classe. | L'accesso è rifiutato e non riceve dati. |
| AC-16 | L'admin crea un esercizio con Blockly, lo pubblica e lo assegna ai client. | Ogni destinatario riceve consegna e progetto iniziale, può risolverlo quando abilitato e il docente ne vede avanzamento ed esito. Le modifiche di un alunno non alterano il modello né i tentativi degli altri. |
| AC-17 | Un alunno guarda lo schermo mentre aspetta, mentre lavora e quando il docente mette in pausa. | Vede con un'icona colorata e una frase che cosa sta succedendo: se è collegato, se può lavorare e che cosa fa il suo programma. |
| AC-18 | Un alunno preme **Prova** sul proprio programma. | Vede il personaggio muoversi sulla scena e riceve l'esito del gioco; il progetto non cambia, il docente non vede una nuova esecuzione e, se il docente comanda l'esecuzione, la prova lascia il posto a quella. |

Per i comandi live, obiettivo proposto: almeno il 95% delle conferme entro 1 secondo su rete locale stabile, con 12 client attivi per una lezione di 60 minuti. Dopo 5 secondi senza conferma, il pannello evidenzia l'incertezza. Misurare distintamente tempi di consegna, applicazione e salvataggio.

## 17. Installazione e uso nel laboratorio

Proposta iniziale: un server raggiungibile da tutte le postazioni tramite un unico indirizzo. Il frontend compilato può essere distribuito dallo stesso host del backend, semplificando configurazione e accesso.

Il database resta sul server. I PC degli alunni richiedono soltanto un browser supportato; nessuna installazione applicativa locale.

Se ospitata nella rete scolastica, l'applicazione deve poter svolgere le attività previste anche senza Internet: librerie e materiali necessari vanno distribuiti localmente. Rimane necessaria la connessione al server della lezione. Un hosting esterno richiede invece la connettività Internet per la regia.

Documentare avvio del servizio, indirizzo per i client, configurazione HTTPS, firewall, directory dei dati, backup, ripristino e aggiornamento. Prima del rilascio verificare la compatibilità con browser, risoluzione e prestazioni effettive dei 12 PC.

## 18. Decisioni aperte per la prossima revisione

| Tema | Proposta di partenza | Decisione |
|---|---|---|
| Nome del prodotto | Nome provvisorio “Codi”. | Da definire. |
| Collocazione server | Rete locale scolastica con indirizzo stabile. | PC docente, server dedicato o hosting? |
| Browser e sistema operativo | Browser aggiornati presenti in laboratorio. | Rilevare i 12 PC. |
| Accesso alunni | **Deciso** il 9 ottobre 2026: codice di lezione numerico di otto cifre detto a voce dal docente, con il nome dell'alunno; la scelta di scuola e classe resta come ripiego nascosto. | Deciso: nessuna domanda aperta. Restano da misurare in aula la leggibilità del codice e la frequenza degli errori di digitazione. |
| Memoria tra lezioni | Conservare i lavori per sessione; eventuale recupero assistito dal docente. | Come recuperare un lavoro da un’altra sessione senza iscrizioni permanenti? |
| Postazioni | Etichette facoltative PC 01–12. | Serve un'associazione iniziale fissa? |
| Primo esercizio | Percorso con personaggio e blocchi di movimento. | **Deciso** il 2026-10-02: `GridPath`, personaggio su griglia con ostacoli e traguardo, con i blocchi inizio, avanti, gira e ripeti. |
| Scratch | Editor semplificato basato su Blockly. | **Deciso** il 2026-10-02: la prima versione non carica file `.sb3`; l'ambiente resta semplificato e cresce aggiungendo blocchi. L'import resta fra le evoluzioni eventuali di §15. |
| Nuove tipologie di esercizio | Un tipo nuovo è un motore nuovo: la definizione dichiara il tipo, e le regole di una versione non si allargano in silenzio. | **Introdotto il 2026-10-09**: il registro delle versioni del motore rende una tipologia nuova un'aggiunta, non una modifica ai consumatori (tecnica §13.9). **Realizzato il 2026-10-09** il primo tipo dichiarativo `Quiz` (`quiz-1`): l'alunno legge una rappresentazione, risponde e riceve un esito corretto dal server, senza progetto a blocchi (tecnica §13.9.1). La scelta di quale tipologia aggiungere dopo resta aperta. || Regia individuale | Comandi alla classe o a un singolo alunno. | Servono piccoli gruppi selezionabili? |
| Dati | Nome breve e lavori degli esercizi. | Definire durata di conservazione e cancellazione. |
| Ruoli | Admin e docente unificati inizialmente. | Quando separare i permessi? |

## 19. Scenario guida

> Il docente apre la lezione della classe 3ª A, la avvia e legge ad alta voce il codice di otto cifre che il sistema gli mostra. Dodici bambini aprono il sito, digitano il codice e il proprio nome. Nel pannello compaiono dodici riquadri. Il docente assegna un esercizio con la relativa consegna con un personaggio da guidare al traguardo. Abilita il lavoro: i bambini costruiscono le loro sequenze, le **provano** sulla scena e vedono il personaggio muoversi e l'esito — «bravo», «c'è un ostacolo» — senza che la prova conti come esecuzione della classe. Quando sono pronti, il docente avvia la classe: le prove si fermano e la scena mostra la corsa comandata. Il docente osserva blocchi, scena e avanzamento. Mette tutti in pausa per spiegare un passaggio — il codice resta valido, quindi chi arriva adesso entra lo stesso — poi li fa riprendere. Interviene su una singola postazione senza cambiare l'attività degli altri. Infine chiude la sessione e verifica quali lavori sono stati salvati.

Questo scenario costituisce il riferimento principale per valutare le successive scelte di implementazione.


## 20. Registro delle revisioni

| Versione | Data | Modifica |
|---|---|---|
| 0.17 | 2026-10-22 | **Eliminazione di una versione pubblicata non più recente**, su richiesta dell'utente del 22 ottobre 2026: dall'elenco delle versioni dell'editor una versione si elimina con conferma, purché non sia la più recente e non sia già stata assegnata a una lezione; l'ultima resta perché è quella che le prossime assegnazioni userebbero, e per sostituirla se ne pubblica una nuova (§9.2). |
| 0.16 | 2026-10-22 | **Niente programma iniziale nell'admin**, su richiesta dell'utente del 22 ottobre 2026: l'editor non compone più il progetto di partenza e l'anteprima che lo eseguiva sparisce; resta la soluzione di riferimento, che verifica la risolvibilità. Il progetto iniziale degli alunni è vuoto (§9.2). |
| 0.15 | 2026-10-22 | **Limiti dell'esercizio**, su richiesta dell'utente del 22 ottobre 2026: nel percorso sulla griglia il docente configura **solo la durata massima della corsa, in minuti**. Numero di blocchi, annidamento e passi restano budgets tecnici del sistema. Nella stessa giornata la scelta è passata da secondi a minuti (§9.2). |
| 0.14 | 2026-10-22 | **Eliminazione di una sessione**, su richiesta dell'utente del 22 ottobre 2026: dal pannello admin una sessione **conclusa o mai avviata** si cancella definitivamente, con conferma esplicita. Spariscono la sessione e ciò che le appartiene — presenze, comandi, lavori — mentre scuole, classi, lezioni ed esercizi restano. Una sessione in corso non si elimina: prima si conclude (§6.1). |
| 0.13 | 2026-10-16 | Semplificazione della pubblicazione admin: comando unico, stati espliciti, errori collegati ai campi e barra delle azioni sempre visibile. |
| 0.12 | 2026-10-16 | Nome del prodotto **Codì**, su decisione dell'utente. Cartelle e identificatori tecnici usano `Codi`. Le funzioni didattiche restano quelle descritte nelle sezioni precedenti. |
| 0.11 | 2026-10-14 | **Chat fra il maestro e il singolo alunno**, su richiesta dell'utente del 13–14 ottobre 2026: il maestro scrive all'alunno dalla sua card e l'alunno **risponde** dalla propria pagina. La chat è **solo fra il maestro e quel bambino** — una chat fra bambini non esiste — e il messaggio è breve: al massimo 300 caratteri. Il bambino **legge sempre**, anche durante una pausa, ma **scrive** solo mentre la pagina è al lavoro: in attesa o in pausa la risposta non è ammessa, e la pagina lo spiega invece di lasciare un campo che non funziona. Il maestro vede sulla card quanti messaggi dell'alunno non ha ancora letto. La conversazione **non resta** dopo la lezione: vive in memoria e viene eliminata quando la lezione finisce (§6.2, §8). |
| 0.10 | 2026-10-09 | Percorso dell'alunno, su richiesta dell'utente del 9 ottobre 2026: quando entra nella lezione l'alunno legge **scuola / classe / lezione / esercizio** e, sulla riga sotto, il proprio nome. L'esercizio compare nel percorso solo quando il docente lo ha assegnato; gli altri tre livelli arrivano dal contesto della sessione, che l'alunno può leggere con la propria credenziale (§2.1, §5.3). |
| 0.9 | 2026-10-09 | Impaginazione della pagina dell'alunno, su richiesta dell'utente del 9 ottobre 2026: **progetto a sinistra e programma a destra**, affiancati nella stessa area di lavoro, e pagina a **tutta larghezza** invece della colonna stretta. Su schermi stretti le due colonne tornano una sotto l'altra, e dove manca un pannello — in attesa non c'è ancora un progetto — quello presente occupa la riga invece di lasciare un vuoto (§2.1, §5.3). |
| 0.8 | 2026-10-09 | Esperienza dell'alunno, su feedback dell'utente del 9 ottobre 2026: lo stato si legge a colpo d'occhio con **icone colorate e parole** — collegamento, permesso di lavorare, stato del programma — e l'alunno **prova** il proprio algoritmo quando i controlli sono attivi, vedendo il personaggio muoversi sulla scena e l'esito del gioco. La prova è locale: non cambia il progetto, non produce un'esecuzione comandata e si ferma quando il docente avvia la classe (§2.1, §5.3, §15, §16, §19). |
| 0.7 | 2026-10-09 | Ingresso con il **codice di lezione**, su decisione dell'utente del 9 ottobre 2026: il docente riceve all'avvio un codice numerico di otto cifre da dettare alla classe, la home dell'alunno si accorge che una lezione è in play e chiede il codice e il nome. La pausa non invalida il codice, che non si riusa in un'altra lezione; dopo alcuni codici errati il tentativo è sospeso per poco e un codice sbagliato non rivela se una lezione esiste. La scelta di scuola e classe resta come ripiego, non più come via ordinaria (§2.1, §2.2, §4, §5.1–§5.2, §15, §18, §19). |
| 0.6 | 2026-10-02 | Chiuse due domande di §18 su decisione del docente: il primo modello didattico è confermato (`GridPath` con quattro blocchi di movimento) e la prima versione non carica file Scratch `.sb3`, che restano fra le evoluzioni eventuali di §15. |
| 0.5 | 2026-10-02 | «Lezione in play» comprende la sessione in pausa con ingressi aperti: la pausa sospende l'attività, non l'ingresso, quindi gli alunni che arrivano durante una spiegazione vedono ancora la lezione e possono entrare (§4). Decisione dell'utente del 2 ottobre 2026, emersa dal collaudo nel browser. |
| 0.4 | 2026-10-01 | Ciclo di vita della sessione: istanziazione della lezione in una classe (sessione del momento in attesa, ingressi chiusi, senza associazione permanente lezione-classe) e avvio in play che apre gli ingressi; regia con Avvia, Pausa, Riprendi e Stop definitivo; confermata una sola sessione attiva per classe e l'accesso degli alunni alla sola lezione in play (§2.2, §5, §6, §7). Aggiunta la voce di glossario «Lezione in play», l'istanziazione e l'avvio della sessione tra i criteri della prima fase (§15) e la terminologia «sessione in play» in §2.1, §16 e §18. |
| 0.3 | 2026-10-01 | Eliminati dal prodotto associazioni corso-classe, iscrizioni e contenuti/immagini. Ingresso direttamente nella sessione, lezioni composte da esercizi, AC-04 basato sull’attesa; modifica simultanea della stessa lezione fuori perimetro. |
| 0.2 | 2026-10-01 | Distinzione fra sorgente presente e requisiti da realizzare nella rianalisi iniziale. |
