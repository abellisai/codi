import { HttpResponse, type HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { EXPECTED_NOT_FOUND, SILENT_SUCCESS, toApiError } from '@codi/api';
import { toast } from '@spartan-ng/brain/sonner';
import { tap } from 'rxjs';

/** Il dispatcher dei comandi: è la sola superficie che produce una notifica di successo. */
const COMMAND_PATH = '/execute/command/';
/**
 * Comandi il cui esito è composto e si legge nella pagina: la verifica elenca i problemi e la
 * pubblicazione mostra lo stato della bozza. Annunciarli anche a comparsa darebbe due conferme
 * per un solo gesto. Il salvataggio della bozza non è fra questi: è un'operazione singola e usa
 * l'avviso standard, come ogni altro comando.
 */
const INLINE_FEEDBACK_COMMANDS = ['ValidateExerciseDraft', 'PublishExerciseVersion'];

/**
 * Richieste di servizio: stretta di mano, sessione e verifica iniziale. Il loro esito non si
 * annuncia con un avviso a comparsa — l'accesso ha la sua schermata e la sessione ha il suo
 * ripristino — perché un errore previsto non è un errore da mostrare all'utente.
 */
const SILENT_PATHS = ['/auth/', '/bootstrap'];

/** Titolo dell'avviso per i comandi che il docente riconosce dal nome del caso d'uso. */
const COMMAND_TITLES: Readonly<Record<string, string>> = {
  CreateSchool: 'Scuola creata',
  UpdateSchool: 'Scuola aggiornata',
  ArchiveSchool: 'Scuola archiviata',
  CreateClassroom: 'Classe creata',
  UpdateClassroom: 'Classe aggiornata',
  ArchiveClassroom: 'Classe archiviata',
  CreateCourse: 'Corso creato',
  UpdateCourse: 'Corso aggiornato',
  ArchiveCourse: 'Corso archiviato',
  CreateLesson: 'Lezione creata',
  UpdateLesson: 'Lezione aggiornata',
  ArchiveLesson: 'Lezione archiviata',
  ReplaceLessonExercises: 'Sequenza della lezione salvata',
  CreateExercise: 'Esercizio creato',
  UpdateExercise: 'Esercizio aggiornato',
  ArchiveExercise: 'Esercizio archiviato',
  SaveExerciseDraft: 'Bozza salvata',
  CopyExerciseVersionToDraft: 'Versione copiata nella bozza',
  DeleteExerciseVersion: 'Versione eliminata',
};

/**
 * Mostra l'esito di ogni chiamata al server (§14.2).
 *
 * Le regole stanno qui e non nelle pagine: un successo si annuncia solo per i comandi, perché
 * una lettura riuscita è il funzionamento normale e non una novità da segnalare; un errore si
 * annuncia sempre, con il messaggio del backend e il codice del contratto, che è il dato su cui
 * si decide che cosa fare. Le richieste di servizio restano mute.
 *
 * L'unica eccezione la dichiara il chiamante: quando sa che un'assenza è una risposta prevista
 * (`EXPECTED_NOT_FOUND`), un `404` non produce avviso. Vale **solo** per il `404`: un guasto vero
 * continua ad avvisare, e il rifiuto continua a raggiungere chi ha chiesto la risorsa.
 */
export const notificationInterceptor: HttpInterceptorFn = (request, next) => {
  const url = request.url;
  const isSilent = SILENT_PATHS.some((path) => url.includes(path));
  const isCommand = url.includes(COMMAND_PATH);

  return next(request).pipe(
    tap({
      next: (event) => {
        if (
          !isSilent &&
          isCommand &&
          event instanceof HttpResponse &&
          // Un salvataggio interno a un'operazione composta non produce un avviso suo: la conferma
          // la dà chi compone l'operazione (§14.2).
          !request.context.get(SILENT_SUCCESS) &&
          !INLINE_FEEDBACK_COMMANDS.some((name) => url.split('?')[0].endsWith(COMMAND_PATH + name))
        ) {
          toast.success(commandTitle(url));
        }
      },
      error: (error: HttpErrorResponse | unknown) => {
        if (isSilent) {
          return;
        }

        const apiError = toApiError(error);

        // Un'assenza prevista è una risposta, non un guasto: la pagina che l'ha chiesta la
        // interpreta — «non esiste ancora» — e un avviso a comparsa la racconterebbe come un
        // errore. Vale solo per il 404: un guasto vero si annuncia comunque.
        if (request.context.get(EXPECTED_NOT_FOUND) && apiError.status === 404) {
          return;
        }

        toast.error(apiError.message, { description: apiError.code });
      },
    }),
  );
};

/** Titolo leggibile del comando; per un nome non previsto resta una formula neutra. */
function commandTitle(url: string): string {
  const name = url.slice(url.indexOf(COMMAND_PATH) + COMMAND_PATH.length).split('?')[0];

  return COMMAND_TITLES[decodeURIComponent(name)] ?? 'Operazione completata';
}
