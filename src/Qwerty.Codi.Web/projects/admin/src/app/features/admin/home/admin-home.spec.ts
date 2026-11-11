import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminHome } from './admin-home';

describe('AdminHome', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminHome],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  /**
   * La pagina iniziale esiste perché la radice portava all'accesso e le sezioni non si
   * trovavano. Qui si difende che le elenchi tutte, raggruppate come nel menu.
   */
  it('elenca le sezioni dell’area docente', async () => {
    const element = await render();

    const text = element.textContent ?? '';
    expect(text).toContain('Organizzazione');
    expect(text).toContain('Didattica');
    expect(text).toContain('Regia');
    expect(text).toContain('Scuole e classi');
    expect(text).toContain('Corsi e lezioni');
    expect(text).toContain('Esercizi');
    expect(text).toContain('Sessioni');
    expect(cards(element).length).toBe(4);
  });

  /**
   * Difende il difetto trovato il 1 ottobre 2026 nel menu: un indirizzo con `?` passato come
   * stringa a `routerLink` diventa un solo segmento codificato e non porta alla sezione.
   */
  it('collega ogni riquadro al proprio indirizzo', async () => {
    const element = await render();

    expect(cards(element).map((card) => card.getAttribute('href'))).toEqual([
      '/schools',
      '/courses',
      '/exercises',
      '/sessions',
    ]);
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(AdminHome);
    await fixture.whenStable();

    return fixture.nativeElement as HTMLElement;
  }

  function cards(element: HTMLElement): HTMLAnchorElement[] {
    return Array.from(element.querySelectorAll<HTMLAnchorElement>('a[hlmCard]'));
  }
});
