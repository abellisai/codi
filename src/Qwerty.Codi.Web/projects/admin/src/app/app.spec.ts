import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AdminSessionService, ApiService } from '@core/api';
import { App } from './app';
import { routes } from './app.routes';

/** Doppio del servizio di sessione: distingue chi ha una sessione da chi non ce l'ha. */
function sessionStub(context: unknown): {
  context: () => unknown;
  load: () => Promise<unknown>;
} {
  return { context: () => context, load: async () => context };
}

/**
 * Doppio dell'API: le pagine di dettaglio rileggono dal server il proprio elemento e i suoi
 * dipendenti. Qui interessa solo che la pagina si monti, quindi gli elenchi sono vuoti e l'elemento
 * chiesto ha i campi che il modello mostra.
 */
class ApiStub {
  private readonly single: Record<string, unknown> = {
    GetSchool: { id: 'a1', name: 'Scuola Manzoni', isArchived: false, rowVersion: '1' },
    GetCourse: {
      id: 'c1',
      title: 'Informatica di base',
      description: '',
      isArchived: false,
      rowVersion: '1',
    },
    GetLesson: {
      id: 'l1',
      courseId: 'c1',
      title: 'Lezione uno',
      description: '',
      isArchived: false,
      rowVersion: '1',
    },
  };

  constructor(overrides: Record<string, unknown> = {}) {
    Object.assign(this.single, overrides);
  }

  async executeQuery<TResponse>(requestName: string): Promise<TResponse> {
    return (this.single[requestName] ?? []) as TResponse;
  }

  executeCommand<TResponse>(): Promise<TResponse> {
    throw new Error('Nessun comando è atteso in queste prove.');
  }
}

describe('App', () => {
  beforeEach(() => {
    // jsdom non implementa `matchMedia`, che il componente dei toast usa per posizionarsi: senza
    // il doppio il guscio non si monta e la prova fallirebbe per un dettaglio dell'ambiente,
    // non del comportamento.
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      writable: true,
      value: (query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        addListener: () => undefined,
        removeListener: () => undefined,
        dispatchEvent: () => false,
      }),
    });
  });

  /**
   * Il guscio non ha interfaccia propria: monta la pagina della route. La prova difende il difetto
   * trovato il 30 settembre 2026, quando `app.html` conteneva ancora il segnaposto del modello di
   * Angular e la pagina di accesso compariva sotto di esso.
   *
   * Dal 1 ottobre la radice dell'area è la pagina iniziale: senza sessione il guard riporta
   * all'accesso, che è la condizione di chi apre l'applicazione per la prima volta.
   */
  it('porta all’accesso chi apre la radice senza sessione', async () => {
    const { fixture, router } = await open(sessionStub(null));

    expect(router.url).toBe('/login');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Accesso docente');
    expect(text).not.toContain('Hello, codi-web');
  });

  /**
   * La pagina iniziale esiste perché la radice portava all'accesso: chi entrava non trovava le
   * sezioni se non digitandone l'indirizzo.
   */
  it('mostra le sezioni a chi ha una sessione', async () => {
    const { fixture, router } = await open(
      sessionStub({ username: 'Maestra', policies: ['Admin'] }),
    );

    expect(router.url).toBe('/');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Da dove cominciare');
    expect(text).toContain('Scuole e classi');
    expect(text).toContain('Esercizi');
  });

  /**
   * Le pagine di dettaglio stanno sotto il livello dell'elenco e si raggiungono dal proprio
   * indirizzo. La prova difende i collegamenti delle righe — «Classi» di una scuola, «Lezioni» di
   * un corso, e la sequenza di una lezione — che senza queste route ricadono sulla regola di
   * riserva e riportano all'accesso invece di aprire la pagina.
   */
  it.each(['/schools/a1', '/courses/c1', '/courses/c1/lessons/l1'])(
    'apre la pagina di dettaglio di %s',
    async (url) => {
      const { fixture, router } = await open(
        sessionStub({ username: 'Maestra', policies: ['Admin'] }),
        url,
      );

      expect(router.url).toBe(url);

      const page = fixture.nativeElement as HTMLElement;
      expect(page.querySelector('app-detail-header')).not.toBeNull();
    },
  );

  /**
   * L'azione che l'utente compie davvero: dall'elenco preme il livello successivo ed entra nella
   * scuola scelta. La prova usa la tabella delle route reale e **preme** il collegamento: una route
   * dichiarata ma non raggiungibile dal clic resta un vicolo cieco, e il difetto non si vede
   * provando l'indirizzo a mano.
   */
  it('entra nella scuola quando si preme «Classi» nella riga', async () => {
    const school = { id: 'a1', name: 'Scuola Manzoni', isArchived: false, rowVersion: '1' };
    const { fixture, router } = await open(
      sessionStub({ username: 'Maestra', policies: ['Admin'] }),
      '/schools',
      new ApiStub({ ListSchools: [school], GetSchool: school }),
    );

    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'a[aria-label^="Classi di "]',
    );
    expect(link?.getAttribute('href')).toBe('/schools/a1');

    link?.click();
    await fixture.whenStable();
    // La change detection è senza `zone.js`: la promessa della navigazione non è un'attività
    // registrata, quindi la prova deve cedere il turno all'event loop.
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(router.url).toBe('/schools/a1');
  });

  async function open(
    service: { context: () => unknown; load: () => Promise<unknown> },
    url = '/',
    api = new ApiStub(),
  ): Promise<{
    fixture: ReturnType<typeof TestBed.createComponent>;
    router: Router;
  }> {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        { provide: AdminSessionService, useValue: service },
        { provide: ApiService, useValue: api },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return { fixture, router };
  }
});
