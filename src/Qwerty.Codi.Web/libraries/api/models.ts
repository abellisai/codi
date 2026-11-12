/**
 * Contratti di lettura del backend, come li serializza il dispatcher (§10.2). I contatori a 64 bit
 * viaggiano come stringhe decimali: dichiararli `number` in TypeScript perderebbe cifre senza che
 * nulla lo segnali, perché il tipo non distingue i due casi.
 */

/** Scuola come la restituiscono `ListSchools` e `GetSchool` (§10.5). */
export interface School {
  readonly id: string;
  readonly name: string;
  readonly isArchived: boolean;
  /** Versione della riga, da rimandare come `baseRowVersion` per modificare la scuola (§10.2). */
  readonly rowVersion: string;
}

/** Classe di una scuola, da `ListClassrooms` e `GetClassroom` (§10.5). */
export interface Classroom {
  readonly id: string;
  readonly schoolId: string;
  readonly name: string;
  /** Anno scolastico nella forma canonica `AAAA/AAAA`. */
  readonly schoolYear: string;
  readonly isArchived: boolean;
  readonly rowVersion: string;
}

/** Corso, da `ListCourses` e `GetCourse` (§10.5). La descrizione è vuota quando non c'è. */
export interface Course {
  readonly id: string;
  readonly title: string;
  readonly description: string;
  readonly isArchived: boolean;
  readonly rowVersion: string;
}

/** Profilo minimo del contesto autenticato, da `GET /api/v1/admin/auth/me` (§10.3). */
export interface AdminContext {
  readonly username: string;
  readonly policies: readonly string[];
}

/** Lezione di un corso, da `ListLessons` e `GetLesson` (§10.5). La descrizione è vuota quando non c'è. */
export interface Lesson {
  readonly id: string;
  readonly courseId: string;
  readonly title: string;
  readonly description: string;
  readonly isArchived: boolean;
  readonly rowVersion: string;
}

/**
 * Esercizio della libreria, da `ListExercises` e `GetExercise` (§10.5). La descrizione è vuota
 * quando non c'è.
 *
 * `exerciseType` è il tipo di attività scelto alla creazione e non cambia: l'editor lo usa per
 * mostrare i campi giusti anche quando la bozza non esiste ancora.
 */
export interface Exercise {
  readonly id: string;
  readonly exerciseType: string;
  readonly title: string;
  readonly description: string;
  readonly isArchived: boolean;
  readonly rowVersion: string;
  /** Stato della bozza rispetto all'ultima versione pubblicata, letto dal server. */
  readonly publicationStatus?: 'Draft' | 'Available' | 'UnpublishedChanges';
}

/**
 * Bozza dell'esercizio per l'editor, da `GetExerciseDraft` e come la restituisce
 * `SaveExerciseDraft` (§10.4, §10.5). `definitionJson` e `workspaceJson` sono testi JSON: la
 * definizione didattica e il workspace Blockly hanno formati distinti e non vanno scambiati
 * (§13). `rowVersion` viaggia come stringa decimale, come le altre versioni di riga (§10.2).
 */
export interface ExerciseDraft {
  readonly exerciseId: string;
  readonly definitionJson: string;
  readonly workspaceJson: string;
  readonly rowVersion: string;
  readonly updatedAtUtc: string;
}

/**
 * Versione pubblicata di un esercizio, da `ListExerciseVersions`, `GetExerciseVersion` e
 * `PublishExerciseVersion` (§10.4, §10.5). È immutabile: i due documenti escono come sono stati
 * pubblicati, e `contentHash` è l'impronta che identifica il contenuto pubblicato. `referenceHash`
 * è l'impronta della soluzione congelata nella versione: riconosce una pubblicazione che ha
 * cambiato solo la soluzione e non la rivela (§13.1); è nulla sulle versioni pubblicate prima di
 * quella migrazione. `versionNumber` viaggia come stringa decimale, come gli altri contatori (§10.2).
 * La soluzione privata non c'è:
 * il catalogo la riserva a `GetReferenceSolution` (§13.1); `authorNotes` invece arriva dalla riga
 * compagna ed è il promemoria del docente sulla versione, visibile solo nell'area admin.
 */
export interface ExerciseVersion {
  readonly id: string;
  readonly exerciseId: string;
  readonly versionNumber: string;
  readonly definitionJson: string;
  readonly workspaceJson: string;
  /** Tipo di esercizio della versione: serve a riprodurne l'anteprima con **il suo** motore. */
  readonly exerciseType: string;
  readonly engineVersion: string;
  readonly schemaVersion: string;
  readonly contentHash: string;
  /** Impronta della soluzione di riferimento della versione; nulla sulle versioni storiche. */
  readonly referenceHash: string | null;
  readonly createdAt: string;
  readonly authorNotes: string | null;
}

/**
 * Esito di `DeleteExerciseVersion` (§10.4, §13.8): la versione indicata e il suo contenuto privato
 * non esistono più. `versionNumber` viaggia come stringa decimale come nell'elenco delle versioni.
 */
export interface ExerciseVersionDeleted {
  readonly exerciseId: string;
  readonly versionNumber: string;
}

/**
 * Esito di `ValidateExerciseDraft` (§10.4, §13.8): vero quando l'esercizio è pubblicabile, con
 * l'elenco dei problemi che lo impediscono. Un elenco vuoto è la risposta «non c'è nulla da
 * correggere», non l'assenza di una risposta.
 */
export interface ExerciseDraftValidation {
  readonly isValid: boolean;
  readonly problems: readonly string[];
}

/**
 * Soluzione di riferimento della bozza, da `GetReferenceSolution` (§10.5): `workspaceJson` è nullo
 * quando il docente non ne ha ancora composta una.
 */
export interface ExerciseReferenceSolution {
  readonly exerciseId: string;
  readonly workspaceJson: string | null;
}

/**
 * Casella della sequenza ordinata di una lezione, da `ListLessonExercises` (§10.5). La posizione è
 * `1..N` e l'ordine delle righe è già quello di posizione.
 */
export interface LessonExercise {
  readonly id: string;
  readonly lessonId: string;
  readonly exerciseId: string;
  readonly position: number;
  readonly rowVersion: string;
}

/**
 * Casella inviata a `ReplaceLessonExercises` (§10.4). La posizione non si dichiara: è l'indice
 * della casella nell'elenco, perché la sequenza si sostituisce in blocco.
 */
export interface LessonExerciseInput {
  readonly exerciseId: string;
}

/**
 * Stato di una sessione di lezione (§6.1, §10.4). È l'unione chiusa che il dominio ammette: gli
 * stati arrivano dal server come stringhe, ma qui si nominano per non confrontare testo libero.
 */
export type SessionStatus = 'Lobby' | 'Active' | 'Paused' | 'Ended';

/**
 * Sessione come la mostrano `ListSessions` e `GetSession` (§10.5): stato, ingressi e revisione
 * della sessione insieme ai titoli della catena lezione → corso → classe → scuola, che permettono
 * di riconoscerla senza altre letture. È distinta da `Session`, che è l'esito dei comandi di regia
 * e porta la versione di riga. `sessionRevision` viaggia come stringa decimale (§10.2).
 *
 * `joinCode` è il codice di ingresso che il docente detta a voce (§9.4): è presente solo quando la
 * sessione è avviata, ed è nullo in attesa e per le sessioni avviate prima dell'introduzione.
 */
export interface SessionSummary {
  readonly id: string;
  readonly status: SessionStatus;
  readonly admissionsOpen: boolean;
  readonly joinCode: string | null;
  readonly sessionRevision: string;
  readonly startedAtUtc: string | null;
  readonly endedAtUtc: string | null;
  readonly lessonId: string;
  readonly lessonTitle: string;
  readonly courseTitle: string;
  readonly classroomName: string;
  readonly schoolName: string;
}

/**
 * Sessione come la restituiscono i comandi di regia — `CreateSession`, `StartSession`, `Pause`,
 * `Resume`, `EndSession` (§10.4). Rispetto a `SessionSummary` porta gli identificativi e la
 * `rowVersion`, perché è l'esito di una mutazione: `sessionRevision` e `rowVersion` viaggiano come
 * stringhe decimali (§10.2). `joinCode` è il codice di ingresso (§9.4), presente dalla partenza.
 */
export interface Session {
  readonly id: string;
  readonly lessonId: string;
  readonly classroomId: string;
  readonly status: SessionStatus;
  readonly admissionsOpen: boolean;
  readonly joinCode: string | null;
  readonly sessionRevision: string;
  readonly startedAtUtc: string | null;
  readonly rowVersion: string;
}

/**
 * Esito di `DeleteSession` (§10.4): la sessione indicata e i suoi dati sono stati rimossi. Non
 * porta revisione né `rowVersion`, perché la riga non esiste più.
 */
export interface SessionDeleted {
  readonly sessionId: string;
}

/**
 * Esito di un comando di regia che ha cambiato lo stato desiderato (§10.6): il comando registrato
 * con la revisione risultante e il riepilogo delle consegne. La revisione viaggia come stringa
 * decimale (§10.2) e il riepilogo dice quante consegne sono ancora da inviare.
 */
export interface CommandAccepted {
  readonly commandId: string;
  readonly sessionId: string;
  readonly sessionRevision: string;
  readonly deliverySummary: CommandDeliverySummary;
}

/** Riepilogo delle consegne di un comando (§10.6). */
export interface CommandDeliverySummary {
  readonly pending: number;
  readonly applied: number;
  readonly failed: number;
}

/**
 * Presenza misurata dal server (§11.6): un segno di vita recente, uno vecchio ma dentro
 * l'autorizzazione, un client non più operativo o nessuna connessione nota.
 */
export type ParticipantPresence = 'Fresh' | 'Degraded' | 'NotOperational' | 'Disconnected';

/**
 * Riga della griglia dei partecipanti (§10.5), da `ListSessionParticipants`. Lo stato è quello
 * **desiderato** — la telemetria del player non è persistita — e i campi del lavoro sono assenti
 * finché non c'è un'esercitazione assegnata: l'assenza è un'informazione, non uno zero.
 * `attemptRowVersion` serve alla conferma dell'azzeramento (§10.6): è la versione del lavoro che il
 * docente ha visto. I contatori a 64 bit viaggiano come stringhe decimali (§10.2).
 */
export interface SessionParticipantOverview {
  readonly observedState?: ParticipantObservation | null;
  readonly participantId: string;
  readonly displayName: string;
  readonly stationId: string | null;
  readonly admissionStatus: string;
  readonly connectionEpoch: string;
  readonly joinedAtUtc: string;
  readonly excludedAtUtc: string | null;
  readonly presence: ParticipantPresence;
  readonly lastHeartbeatUtc: string | null;
  readonly view: string | null;
  readonly interactionEnabled: boolean;
  readonly teacherPaused: boolean;
  readonly recoveryBlocked: boolean;
  readonly executionGeneration: string | null;
  readonly hasDirective: boolean;
  readonly attemptId: string | null;
  readonly attemptNumber: string | null;
  readonly attemptRowVersion: string | null;
  readonly attemptStatus: string | null;
  readonly projectRevision: string | null;
  readonly lastSavedAtUtc: string | null;
  /** Messaggi scritti dall'alunno che il docente non ha ancora letto (§8, §15.1). */
  readonly unreadMessages: number;
}

/**
 * Messaggio della chat fra il docente e il singolo alunno (§8, §15.1). Lo stesso tipo arriva
 * dall'evento dell'hub e dalla query: il client legge sempre lo stesso contratto. L'autore è il nome
 * dell'autore, non un numero.
 */
export interface ParticipantMessage {
  readonly messageId: string;
  readonly sessionId: string;
  readonly participantId: string;
  readonly author: 'Teacher' | 'Participant';
  readonly text: string;
  readonly writtenAtUtc: string;
}

/** Thread di un alunno: i messaggi in ordine di scrittura e i non letti dal docente (§15.1). */
export interface ParticipantThread {
  readonly participantId: string;
  readonly messages: readonly ParticipantMessage[];
  /** Per l'alunno è sempre zero: il server non misura se un bambino ha letto. */
  readonly unreadByTeacher: number;
}

export interface ParticipantObservation {
  readonly connectionEpoch: string;
  readonly effectiveRevision: string;
  readonly attemptId: string | null;
  readonly executionGeneration: string;
  readonly sequence: string;
  readonly runtimeStatus: string;
  readonly reason: string | null;
  readonly runId: string | null;
  readonly workspaceHash: string | null;
  readonly scene: unknown;
  readonly receivedAtUtc: string;
}

export interface CommandStatus {
  readonly commandId: string;
  readonly sessionId: string;
  readonly commandName: string;
  readonly revision: string;
  readonly summary: { readonly pending: number; readonly applied: number; readonly failed: number };
  readonly deliveries: readonly {
    readonly participantId: string;
    readonly status: string;
    readonly requiresRecovery: boolean;
  }[];
}

export interface TeacherSnapshot {
  readonly protocolVersion: number;
  readonly serverInstanceId: string;
  readonly session: Session;
  readonly participants: readonly SessionParticipantOverview[];
  readonly commands: readonly CommandStatus[];
}

/**
 * Materiale assegnato a un alunno, dentro il dettaglio (§10.5).
 *
 * `engineVersion` è quella fissata dall'assegnazione: serve a sapere **come leggere** il lavoro
 * dell'alunno — un progetto a blocchi oppure un verbale di risposta — e non cambia se la bozza
 * dell'esercizio viene modificata dopo la pubblicazione.
 */
export interface ParticipantAssignment {
  readonly assignmentId: string;
  readonly scope: string;
  readonly lessonExerciseId: string | null;
  readonly exerciseVersionId: string;
  readonly exerciseId: string;
  readonly exerciseTitle: string;
  readonly engineVersion: string;
}

/** Lavoro corrente di un alunno, dentro il dettaglio (§10.5). */
export interface ParticipantAttempt {
  readonly attemptId: string;
  readonly attemptNumber: string;
  readonly status: string;
  readonly workspaceJson: string;
  readonly workspaceHash: string;
  readonly projectRevision: string;
  readonly hasCheckpoint: boolean;
  readonly checkpointEngineVersion: string | null;
  readonly lastSavedAtUtc: string | null;
  readonly updatedAtUtc: string;
  readonly rowVersion: string;
}

/**
 * Dettaglio di un alunno (§10.5), da `GetParticipantDetail`: la riga della griglia più il materiale
 * e il lavoro corrente, con il numero di tentativi che rende visibile un azzeramento (§7.3).
 */
export interface SessionParticipantDetail {
  readonly participant: SessionParticipantOverview;
  readonly assignment: ParticipantAssignment | null;
  readonly attempt: ParticipantAttempt | null;
  readonly attemptCount: number;
}

/** Scuola con ingressi disponibili, dalla scoperta pubblica pre-join (§10.3). */
export interface AvailableSchool {
  readonly id: string;
  readonly name: string;
}

/** Classe della scuola scelta, dalla scoperta pubblica pre-join (§10.3). */
export interface AvailableClassroom {
  readonly id: string;
  readonly schoolId: string;
  readonly name: string;
  readonly schoolYear: string;
}

/**
 * Sessione **in play** di una classe (§10.3): la lezione determina il corso. Nessun dato dei
 * partecipanti, perché la scoperta precede l'ingresso.
 */
export interface PlaySession {
  readonly schoolId: string;
  readonly classroomId: string;
  readonly sessionId: string;
  readonly lessonId: string;
  readonly courseId: string;
  readonly courseTitle: string;
  readonly lessonTitle: string;
}

/** Esito dell'ingresso (§9.4): la partecipazione creata o recuperata, senza segreti. */
export interface ParticipantJoined {
  readonly participantId: string;
  readonly sessionId: string;
  readonly displayName: string;
  readonly view: string;
}

/**
 * Contesto del partecipante autenticato (§10.5): chi è, la sua ammissione, la vista voluta e i nomi
 * del percorso — scuola, classe, lezione — con cui la pagina dice all'alunno dove si trova (0.40).
 * I nomi possono essere vuoti se la riga corrispondente non c'è più: sono contesto, non permessi.
 */
export interface ParticipantContext {
  readonly participantId: string;
  readonly sessionId: string;
  readonly displayName: string;
  readonly admissionStatus: string;
  readonly view: string;
  readonly sessionStatus: string;
  readonly schoolName: string;
  readonly classroomName: string;
  readonly lessonTitle: string;
}

/**
 * Direttiva di esecuzione dello stato desiderato (§6.2): l'ultimo comando esecutivo, applicabile una
 * volta sola. La generazione assente significa «quella del destinatario».
 */
export interface ExecutionDirective {
  readonly directiveId: string;
  readonly kind: string;
  readonly executionGeneration: string | null;
  readonly issuedAtUtc: string;
}

/**
 * Stato desiderato del partecipante (§6.2). Un campo assente significa «il comando non cambia
 * questo valore»: dopo un comando di classe i riferimenti individuali arrivano dallo snapshot.
 */
export interface DesiredState {
  readonly view: string;
  readonly interactionEnabled: boolean;
  readonly assignmentId: string | null;
  readonly attemptId: string | null;
  readonly teacherPaused: boolean;
  readonly recoveryBlocked: boolean;
  readonly executionGeneration: string | null;
  readonly executionDirective: ExecutionDirective | null;
}

/** Snapshot del partecipante (§11.2): revisione, epoca e stato desiderato da applicare. */
export interface ParticipantSnapshot {
  readonly protocolVersion: number;
  readonly serverInstanceId: string;
  readonly sessionId: string;
  readonly participantId: string;
  readonly effectiveRevision: string;
  readonly connectionEpoch: string;
  readonly issuedAtUtc: string;
  readonly desiredState: DesiredState;
}

/**
 * Definizione pubblica dell'esercizio assegnato (§10.5), da `GetAssignedExercise`: la versione
 * **fissata dall'assegnazione**, mai la soluzione di riferimento.
 *
 * `exerciseType` dice **che cosa** il client sta per ricevere: è il primo campo da leggere, perché
 * un tipo che questa versione dell'app non sa svolgere va detto al bambino invece di essere
 * scoperto interpretando una definizione che non si riconosce.
 */
export interface AssignedExercise {
  readonly sessionId: string;
  readonly assignmentId: string;
  readonly exerciseVersionId: string;
  readonly lessonExerciseId: string | null;
  readonly scope: string;
  readonly attemptId: string;
  readonly attemptNumber: string;
  readonly exerciseId: string;
  readonly exerciseTitle: string;
  readonly exerciseType: string;
  readonly definitionJson: string;
  readonly engineVersion: string;
  readonly schemaVersion: string;
  readonly contentHash: string;
}

/**
 * Esito della propria risposta su un esercizio dichiarativo (§10.5): lo corregge il server, che è
 * l'unico a conoscere la risposta attesa — vive nella soluzione privata della versione pubblicata e
 * non esce mai verso gli alunni.
 *
 * Il DTO dice **se** la risposta è corretta e ripete i messaggi del docente; non porta la risposta
 * attesa, e il bambino può riprovare finché vuole.
 */
export interface QuizOutcome {
  readonly correct: boolean;
  readonly successMessage: string;
  readonly wrongMessage: string;
}

/**
 * Esito del proprio programma su un esercizio che conserva valori (§13.9), da
 * `GetValueProgramOutcome`. Lo calcola il server **eseguendo** il progetto sui casi di prova.
 *
 * `problem` dice che cosa non va: che cosa il programma ha mostrato invece di che cosa doveva, oppure
 * perché non si esegue. Senza, «Riprova» lascerebbe il bambino a cercare l'errore a caso.
 */
export interface ValueProgramOutcome {
  readonly passed: boolean;
  readonly successMessage: string;
  readonly wrongMessage: string;
  readonly problem: string;
}

/** Progetto e checkpoint del proprio tentativo (§10.5), da `GetAttempt`. */
export interface Attempt {
  readonly attemptId: string;
  readonly assignmentId: string;
  readonly exerciseVersionId: string;
  readonly attemptNumber: string;
  readonly status: string;
  readonly workspaceJson: string;
  readonly workspaceHash: string;
  readonly projectRevision: string;
  readonly checkpointJson: string | null;
  readonly checkpointEngineVersion: string | null;
  readonly lastSavedAtUtc: string | null;
  readonly updatedAtUtc: string;
  readonly rowVersion?: string;
}

export interface AttemptCheckpoint {
  readonly schemaVersion: number;
  readonly engineVersion: string;
  readonly attemptId: string;
  readonly exerciseVersionId: string;
  readonly executionGeneration: string;
  readonly runId: string;
  readonly workspaceHash: string;
  readonly projectRevision: string;
  readonly runtimeStatus: string;
  readonly runtimeState: Record<string, unknown>;
}

/**
 * Anteprima dei blocchi inviata dal player (§15.5): non è un salvataggio. Identità, epoca e
 * generazione non stanno nel corpo del messaggio hub, ma qui servono a comporre i dati dichiarati
 * dal client.
 */
export interface WorkspacePreview {
  readonly protocolVersion: number;
  readonly attemptId: string;
  readonly connectionEpoch: string;
  readonly executionGeneration: string;
  readonly projectRevision: string;
  readonly workspaceHash: string;
  readonly workspaceJson: string;
}

/** Ricevuta dell'anteprima ricevuta dal server (§15.5). */
export interface WorkspacePreviewReceipt {
  readonly participantId: string;
  readonly sessionId: string;
  readonly attemptId: string;
  readonly projectRevision: string;
  readonly workspaceHash: string;
  readonly receivedAtUtc: string;
}

/**
 * Anteprima letta dal docente (§10.5, §15.5). `partial` è vero quando l'impronta non corrisponde
 * al progetto persistito: in quel caso si mostra il contenuto, ma dichiarandolo parziale.
 */
export interface ParticipantPreviewView {
  readonly participantId: string;
  readonly attemptId: string;
  readonly projectRevision: string;
  readonly workspaceHash: string;
  readonly workspaceJson: string;
  readonly receivedAtUtc: string;
  readonly attemptWorkspaceHash: string | null;
  readonly partial: boolean;
}

export interface AttemptSave {
  readonly attemptId: string;
  readonly saveId: string;
  readonly rowVersion: string;
  readonly projectRevision: string;
  readonly workspaceHash: string;
  readonly lastSavedAtUtc: string;
  readonly checkpointSaved: boolean;
}

export interface AttemptFinalizationGrant {
  readonly sessionId: string;
  readonly participantId: string;
  readonly grantId: string;
  readonly attemptId: string;
  readonly connectionEpoch: string;
  readonly executionGeneration: string;
  readonly expiresAtUtc: string;
}

/**
 * Autorizzazione temporanea di esecuzione (§12.6) consegnata dal server: nonce monouso, revisione,
 * epoca, durata massima e scadenza. I contatori viaggiano come stringhe decimali (§10.2).
 */
export interface ExecutionLease {
  readonly nonce: string;
  readonly effectiveRevision: string;
  readonly connectionEpoch: string;
  readonly maxDurationSeconds: string;
  readonly validUntilUtc: string;
  readonly serverInstanceId: string;
}

/**
 * Comando già risolto per il destinatario come arriva al client (§11.4): l'involucro porta quale
 * comando, per quale revisione, su quale epoca di connessione e lo stato desiderato da applicare.
 */
export interface CommandIssuedEnvelope {
  readonly protocolVersion: number;
  readonly serverInstanceId: string;
  readonly sessionId: string;
  readonly participantId: string;
  readonly commandId: string;
  readonly effectiveRevision: string;
  readonly connectionEpoch: string;
  readonly issuedAtUtc: string;
  readonly commandName: string;
  readonly desiredState: DesiredState;
}

/** Un'altra pagina dello stesso alunno è già operativa: attendere e riprovare (§12.5). */
export interface ActiveConnectionExistsDto {
  readonly reason: string;
  readonly connectionEpoch: string;
  readonly retryAfterSeconds: string;
}

/** La connessione ha perso l'operatività perché un'altra l'ha acquisita (§11.3). */
export interface ConnectionSupersededDto {
  readonly reason: string;
  readonly connectionEpoch: string;
  readonly currentEpoch: string;
}

/** La partecipazione non è più utilizzabile: credenziale revocata o alunno escluso (§11.3). */
export interface ParticipationRevokedDto {
  readonly reason: string;
}
