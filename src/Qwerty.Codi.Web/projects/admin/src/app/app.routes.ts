import { Routes } from '@angular/router';
import { adminSessionGuard } from './features/admin/admin-session-guard';

export const routes: Routes = [
  {
    // Il permesso è del server, che risponde 401 senza sessione: la route non è una barriera
    // di sicurezza. Il guscio resta fuori dall'accesso, che non ha sezioni da navigare.
    path: 'login',
    loadComponent: () =>
      import('./features/admin/login/admin-login').then((module) => module.AdminLogin),
    title: 'Accesso docente',
  },
  {
    // Area docente di §14.1: le pagine dell'organizzazione arrivano dentro il guscio, che tiene
    // il menu delle sezioni (§14.2). Il guard riporta all'accesso quando la sessione non c'è o
    // non è più utilizzabile; il permesso decisivo resta comunque sul server.
    path: '',
    canActivate: [adminSessionGuard],
    loadComponent: () =>
      import('./features/admin/admin-shell/admin-shell').then((module) => module.AdminShell),
    children: [
      {
        path: 'local-recovery',
        loadComponent: () =>
          import('./features/admin/local-recovery/local-recovery').then((m) => m.LocalRecovery),
        title: 'Recupero sul PC',
      },
      {
        // La radice dell'area è la pagina iniziale: elenca le sezioni, invece di rimandare
        // all'accesso chi ha già una sessione.
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
          import('./features/admin/home/admin-home').then((module) => module.AdminHome),
        title: 'Panoramica',
      },
      {
        path: 'schools',
        loadComponent: () =>
          import('./features/admin/schools/school-list').then((module) => module.SchoolList),
        title: 'Scuole e classi',
      },
      {
        // Le pagine di dettaglio vivono sotto il livello dell'elenco: la stessa destinazione si
        // ottiene dal collegamento nella riga, da un segnalibro o dal tasto indietro del browser,
        // quindi l'elemento arriva dall'indirizzo e non dallo stato dell'elenco.
        path: 'schools/:schoolId',
        loadComponent: () =>
          import('./features/admin/schools/school-detail').then((module) => module.SchoolDetail),
        title: 'Classi della scuola',
      },
      {
        path: 'courses',
        loadComponent: () =>
          import('./features/admin/courses/course-list').then((module) => module.CourseList),
        title: 'Corsi e lezioni',
      },
      {
        path: 'courses/:courseId',
        loadComponent: () =>
          import('./features/admin/courses/course-detail').then((module) => module.CourseDetail),
        title: 'Lezioni del corso',
      },
      {
        path: 'courses/:courseId/lessons/:lessonId',
        loadComponent: () =>
          import('./features/admin/courses/lesson-detail').then((module) => module.LessonDetail),
        title: 'Sequenza della lezione',
      },
      {
        path: 'exercises',
        loadComponent: () =>
          import('./features/admin/exercises/exercise-list').then((module) => module.ExerciseList),
        title: 'Esercizi',
      },
      {
        // L'editor è il secondo livello dell'esercizio: la definizione della bozza si compone qui.
        // L'esercizio arriva dall'indirizzo, quindi la pagina è indirizzabile e ricaricabile.
        path: 'exercises/:exerciseId/editor',
        loadComponent: () =>
          import('./features/admin/exercises/exercise-editor').then(
            (module) => module.ExerciseEditor,
          ),
        title: 'Editor esercizio',
      },
      {
        // La sessione istanzia una lezione in una classe: l'elenco mostra lo stato e porta alla
        // regia, che comanda il ciclo di vita con i comandi di §10.4.
        path: 'sessions',
        loadComponent: () =>
          import('./features/admin/sessions/session-list').then((module) => module.SessionList),
        title: 'Sessioni',
      },
      {
        // La regia vive sotto il livello dell'elenco: la stessa destinazione si ottiene dal
        // collegamento nella riga, da un segnalibro o dal tasto indietro, quindi la sessione
        // arriva dall'indirizzo e non dallo stato dell'elenco.
        path: 'sessions/:sessionId',
        loadComponent: () =>
          import('./features/admin/sessions/session-detail').then((module) => module.SessionDetail),
        title: 'Regia della sessione',
      },
      {
        // Terzo livello della sessione: i partecipanti come card, per vedere la classe in un colpo
        // d'occhio. La sessione arriva dall'indirizzo, come nella regia, e lo stato è condiviso con
        // essa dallo stesso servizio (§14.1).
        path: 'sessions/:sessionId/participants',
        loadComponent: () =>
          import('./features/admin/sessions/session-participants').then(
            (module) => module.SessionParticipants,
          ),
        title: 'Partecipanti della sessione',
      },
      {
        // Caricamento lazy: Blockly è una dipendenza pesante e non deve entrare nel bundle
        // iniziale, che ha un budget di 500 kB in configurazione production.
        // Area docente di §14.1: la route definitiva dell'autore, in T6, sarà
        // `/exercises/:id/edit`, quindi questa resta fuori da quel segmento.
        path: 'workspace-prototype',
        loadComponent: () =>
          import('./features/exercise-authoring/blockly-workspace-prototype/blockly-workspace-prototype').then(
            (module) => module.BlocklyWorkspacePrototype,
          ),
        title: 'Prototipo workspace Blockly',
      },
    ],
  },
  { path: '**', redirectTo: 'login' },
];
