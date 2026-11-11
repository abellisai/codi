import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AdminSessionService } from '@core/api';
import { LoadingService } from '@core/http';
import { AdminShell } from './admin-shell';

/** Pagina di comodo: al guscio interessa solo che la route esista e venga montata. */
@Component({ selector: 'app-shell-placeholder', template: '<p>pagina di prova</p>' })
class Placeholder {}

/** Doppio del servizio di sessione: registra l'uscita senza toccare la rete. */
class SessionStub {
  signOuts = 0;

  async signOut(): Promise<void> {
    this.signOuts += 1;
  }
}

describe('AdminShell', () => {
  let harness!: RouterTestingHarness;
  let session: SessionStub;

  beforeEach(async () => {
    // jsdom non implementa `matchMedia`, che il componente dei toast usa per posizionarsi: senza
    // il doppio il guscio non si monta e ogni prova fallirebbe per un dettaglio dell'ambiente.
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

    session = new SessionStub();

    await TestBed.configureTestingModule({
      providers: [
        { provide: AdminSessionService, useValue: session },
        provideRouter([
          { path: 'login', component: Placeholder },
          {
            path: '',
            component: AdminShell,
            children: [
              { path: 'schools', component: Placeholder },
              { path: 'schools/:schoolId', component: Placeholder },
              { path: 'courses', component: Placeholder },
              { path: 'courses/:courseId', component: Placeholder },
              { path: 'courses/:courseId/lessons/:lessonId', component: Placeholder },
              { path: 'exercises', component: Placeholder },
              { path: 'sessions', component: Placeholder },
              { path: 'sessions/:sessionId', component: Placeholder },
              { path: 'prototipo', component: Placeholder, title: 'Pagina fuori menu' },
            ],
          },
        ]),
      ],
    }).compileComponents();
  });

  /**
   * Il guscio è nato perché le sezioni dell'area docente esistevano solo come indirizzi da
   * digitare: qui si difende che il menu le nomini tutte e che segni quella corrente.
   */
  it('mostra le sezioni dell’area docente e segna quella corrente', async () => {
    harness = await RouterTestingHarness.create('/schools');

    expect(labels()).toEqual(['Scuole e classi', 'Corsi e lezioni', 'Esercizi', 'Sessioni']);
    expect(activeLabels()).toEqual(['Scuole e classi']);
  });

  /**
   * Un indirizzo che nomina ancora una superficie a tab (le vecchie `/courses?tab=…`) deve
   * accendere comunque la voce «Corsi e lezioni» invece di lasciare il menu senza sezione
   * corrente: il confronto dei parametri è per sottoinsieme, non per uguaglianza.
   */
  it('accende la sezione del percorso anche quando l’indirizzo porta parametri', async () => {
    harness = await RouterTestingHarness.create('/courses?tab=lessons');

    expect(activeLabels()).toEqual(['Corsi e lezioni']);
  });

  it.each([
    ['/schools/s1', 'Scuole e classi'],
    ['/courses/c1', 'Corsi e lezioni'],
    ['/courses/c1/lessons/l1?vista=sequenza#esercizi', 'Corsi e lezioni'],
    ['/sessions/s1', 'Sessioni'],
  ])('mantiene la sezione attiva nel dettaglio %s', async (url, section) => {
    harness = await RouterTestingHarness.create(url);

    expect(activeLabels()).toEqual([section]);
    expect(barTitle()).toBe(section);
  });

  it('mostra lo spinner finché rimane una richiesta e lo toglie alla conclusione', async () => {
    harness = await RouterTestingHarness.create('/schools');
    const loading = TestBed.inject(LoadingService);

    expect(root().querySelector('app-global-spinner [role="status"]')).toBeNull();
    loading.begin();
    loading.begin();
    harness.fixture.detectChanges();
    expect(root().querySelector('app-global-spinner [role="status"]')).not.toBeNull();
    loading.end();
    harness.fixture.detectChanges();
    expect(root().querySelector('app-global-spinner [role="status"]')).not.toBeNull();
    loading.end();
    harness.fixture.detectChanges();
    expect(root().querySelector('app-global-spinner [role="status"]')).toBeNull();
  });

  it('esce dall’area docente e riporta all’accesso', async () => {
    harness = await RouterTestingHarness.create('/schools');

    click('Esci');
    await harness.fixture.whenStable();

    expect(session.signOuts).toBe(1);
    expect(TestBed.inject(Router).url).toBe('/login');
  });

  /** Le voci sono collegamenti veri: si può aprirle in una nuova scheda e copiarne l'indirizzo. */
  it('collega ogni voce al proprio indirizzo', async () => {
    harness = await RouterTestingHarness.create('/schools');

    const hrefs = Array.from(links()).map((link) => link.getAttribute('href'));

    expect(hrefs).toEqual(['/schools', '/courses', '/exercises', '/sessions']);
  });

  /**
   * Difende il difetto trovato il 1 ottobre 2026: un indirizzo con `?` passato come stringa a
   * `routerLink` diventa un solo segmento codificato (`/courses%3Ftab%3Dcontents`), quindi il
   * collegamento sembra giusto ma non porta alla sezione. Il clic deve cambiare indirizzo davvero.
   */
  it('apre la sezione scelta quando si clicca la voce', async () => {
    harness = await RouterTestingHarness.create('/schools');

    clickLink('Esercizi');
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/exercises');
  });

  /**
   * La barra dice in quale sezione si sta lavorando. La pagina dei corsi mostra lezioni e
   * sequenze senza cambiare percorso, quindi il titolo della route non le distinguerebbe: lo dice
   * la voce attiva.
   */
  it('mostra nella barra la sezione corrente', async () => {
    harness = await RouterTestingHarness.create('/courses');

    expect(barTitle()).toBe('Corsi e lezioni');
  });

  it('mostra nella barra il titolo della route per le pagine fuori menu', async () => {
    harness = await RouterTestingHarness.create('/prototipo');

    expect(barTitle()).toBe('Pagina fuori menu');
  });

  it('collega il nome dell’applicazione alla pagina iniziale', async () => {
    harness = await RouterTestingHarness.create('/schools');

    const link = root().querySelector<HTMLAnchorElement>('hlm-sidebar a[href="/"]');

    expect(link).not.toBeNull();
  });

  function root(): HTMLElement {
    const element = harness.routeNativeElement;
    if (element === null || element === undefined) {
      throw new Error('Il guscio non è montato.');
    }

    return element;
  }

  function links(): HTMLAnchorElement[] {
    return Array.from(root().querySelectorAll<HTMLAnchorElement>('a[hlmSidebarMenuButton]'));
  }

  function labels(): string[] {
    return links().map((link) => (link.textContent ?? '').trim());
  }

  function activeLabels(): string[] {
    return links()
      .filter((link) => link.getAttribute('aria-current') === 'page')
      .map((link) => (link.textContent ?? '').trim());
  }

  /** Titolo mostrato nella barra del guscio, accanto al pulsante del menu. */
  function barTitle(): string {
    return (root().querySelector('main header > span')?.textContent ?? '').trim();
  }

  function click(text: string): void {
    const button = Array.from(root().querySelectorAll('button')).find(
      (candidate) => (candidate.textContent ?? '').trim() === text,
    );

    if (button === undefined) {
      throw new Error(`Pulsante «${text}» non trovato.`);
    }

    button.click();
  }

  function clickLink(text: string): void {
    const link = links().find((candidate) => (candidate.textContent ?? '').trim() === text);

    if (link === undefined) {
      throw new Error(`Voce «${text}» non trovata.`);
    }

    link.click();
  }
});
