import {
  lucideGraduationCap,
  lucideListChecks,
  lucideMonitorPlay,
  lucideSchool,
} from '@ng-icons/lucide';

/**
 * Catalogo delle sezioni dell'area docente (§14.1, §14.2).
 *
 * La stessa elencazione serve il menu del guscio (`admin-shell`) e i riquadri della pagina
 * iniziale (`home`): etichette, destinazioni e spiegazioni stanno in un posto solo, così i due
 * elenchi non possono divergere. Il guscio aggiunge lo stato attivo, che dipende dal router.
 */
export interface AdminSection {
  /** Etichetta visibile: è anche il nome della sezione nella barra del guscio. */
  readonly label: string;
  /** Che cosa si configura qui, detto in una riga. */
  readonly description: string;
  /** Nome dell'icona Lucide; chi la mostra la registra con `provideIcons`. */
  readonly icon: string;
  /**
   * Percorso per `routerLink`. I parametri di ricerca stanno a parte in `queryParams`: una
   * stringa che contiene `?` diventa un solo segmento codificato e non porta alla sezione.
   */
  readonly commands: readonly string[];
  readonly queryParams?: Readonly<Record<string, string>>;
  /** Indirizzo completo della sezione: è il confronto della voce attiva nel menu. */
  readonly url: string;
}

/** Gruppo di sezioni con un titolo proprio. */
export interface AdminSectionGroup {
  readonly label: string;
  readonly sections: readonly AdminSection[];
}

/** Icone delle sezioni, registrate da ogni componente che le mostra. */
export const AdminSectionIcons = {
  lucideGraduationCap,
  lucideListChecks,
  lucideMonitorPlay,
  lucideSchool,
};

/**
 * Le sezioni nell'ordine in cui si configura la didattica: prima le scuole con le loro classi,
 * poi i corsi con le lezioni, infine gli esercizi che le lezioni compongono in sequenza.
 *
 * Ogni voce corrisponde a una route di `app.routes.ts`: un menu che nomina una pagina assente
 * manderebbe l'utente su un elenco che non sa mostrare quella superficie.
 */
export const AdminSections: readonly AdminSectionGroup[] = [
  {
    label: 'Organizzazione',
    sections: [
      {
        label: 'Scuole e classi',
        description: 'Le scuole e le loro classi, con l’anno scolastico.',
        icon: 'lucideSchool',
        commands: ['/schools'],
        url: '/schools',
      },
      {
        label: 'Corsi e lezioni',
        description: 'I corsi, le loro lezioni e la sequenza degli esercizi.',
        icon: 'lucideGraduationCap',
        commands: ['/courses'],
        url: '/courses',
      },
    ],
  },
  {
    label: 'Didattica',
    sections: [
      {
        label: 'Esercizi',
        description: 'Gli esercizi della libreria, pronti per le lezioni.',
        icon: 'lucideListChecks',
        commands: ['/exercises'],
        url: '/exercises',
      },
    ],
  },
  {
    // La regia viene dopo la configurazione: prima si preparano scuole e corsi, poi si conduce
    // la lezione. Una sessione si istanzia da una lezione e da una classe, quindi è un passo
    // successivo all'organizzazione e alla didattica.
    label: 'Regia',
    sections: [
      {
        label: 'Sessioni',
        description: 'Le sessioni per classe: istanziazione della lezione, avvio e regia dal vivo.',
        icon: 'lucideMonitorPlay',
        commands: ['/sessions'],
        url: '/sessions',
      },
    ],
  },
];
