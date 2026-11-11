import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AdminSessionService, ApiError } from '@core/api';
import { AdminLogin } from './admin-login';

/** Sostituto del servizio di accesso: registra le credenziali e restituisce l'esito voluto. */
class SessionStub {
  readonly attempts: { username: string; password: string }[] = [];

  failure: ApiError | null = null;
  pending: Promise<void> | null = null;

  /** Contesto restituito da `load`: `null` significa nessuna sessione utilizzabile. */
  existing: { username: string; policies: string[] } | null = null;

  async load() {
    return this.existing;
  }

  async signIn(username: string, password: string) {
    this.attempts.push({ username, password });
    await this.pending;

    if (this.failure !== null) {
      throw this.failure;
    }

    return { username, policies: ['Admin'] };
  }
}

describe('AdminLogin', () => {
  let fixture: ComponentFixture<AdminLogin>;
  let session: SessionStub;
  let navigated: string[];

  beforeEach(async () => {
    session = new SessionStub();
    navigated = [];

    await TestBed.configureTestingModule({
      imports: [AdminLogin],
      providers: [
        { provide: AdminSessionService, useValue: session },
        {
          provide: Router,
          useValue: {
            navigateByUrl: (url: string) => {
              navigated.push(url);
              return Promise.resolve(true);
            },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminLogin);
  });

  it('accede con le credenziali digitate e apre la gestione delle scuole', async () => {
    await render();
    setInput('#username', 'Maestra');
    setInput('#password', 'password-del-docente');
    await render();

    submit();
    await render();

    expect(session.attempts).toEqual([{ username: 'Maestra', password: 'password-del-docente' }]);
    expect(navigated).toEqual(['/schools']);
  });

  it('dimentica la password dopo un accesso riuscito', async () => {
    await render();
    setInput('#username', 'Maestra');
    setInput('#password', 'password-del-docente');
    await render();

    submit();
    await render();

    expect(input('#password').value).toBe('');
  });

  it('mostra il rifiuto e non prosegue quando le credenziali non corrispondono', async () => {
    await render();
    session.failure = new ApiError(
      'CredentialsRejected',
      401,
      'Nome utente o password non corrispondono.',
    );
    setInput('#username', 'Maestra');
    setInput('#password', 'sbagliata');
    await render();

    submit();
    await render();

    expect(text()).toContain('Nome utente o password non corrispondono.');
    expect(navigated).toEqual([]);
  });

  it('non chiama il server quando i campi sono vuoti', async () => {
    await render();

    submit();
    await render();

    expect(session.attempts).toEqual([]);
    expect(navigated).toEqual([]);
  });

  it('non mostra il modulo a chi ha già una sessione e prosegue verso la gestione', async () => {
    session.existing = { username: 'Maestra', policies: ['Admin'] };

    await render();

    expect(navigated).toEqual(['/schools']);
  });

  it('mostra errori Spartan associati ai campi solo dopo l’invio non valido', async () => {
    await render();
    expect(text()).not.toContain('Inserisci la password.');
    submit();
    await render();

    expect(text()).toContain('Nome utente: compila il campo.');
    expect(text()).toContain('Inserisci la password.');
    const root = fixture.nativeElement as HTMLElement;
    const errors = root.querySelectorAll('hlm-field-error');
    expect(errors.length).toBe(2);
    for (const error of errors) expect(error.hasAttribute('hidden')).toBe(false);
    expect(input('#username').getAttribute('aria-invalid')).toBe('true');
    const errorId = input('#username').getAttribute('aria-describedby');
    expect(errorId).toBeTruthy();
    expect(root.querySelector(`#${errorId}`)?.textContent).toContain('Nome utente');
    expect(session.attempts).toEqual([]);
  });

  it('blocca un nome fatto solo di spazi e conserva esattamente la password', async () => {
    await render();
    setInput('#username', '   ');
    setInput('#password', '  password  ');
    await render();
    submit();
    await render();
    expect(session.attempts).toEqual([]);
    expect(text()).toContain('non solo spazi');
    setInput('#username', 'Maestra');
    await render();
    submit();
    await render();
    expect(session.attempts).toEqual([{ username: 'Maestra', password: '  password  ' }]);
  });

  it('non duplica l’accesso e disabilita i campi fino alla risposta', async () => {
    let release!: () => void;
    session.pending = new Promise<void>((resolve) => {
      release = resolve;
    });
    await render();
    setInput('#username', 'Maestra');
    setInput('#password', 'segreto');
    await render();
    submit();
    submit();
    await render();
    expect(session.attempts.length).toBe(1);
    expect(input('#username').disabled).toBe(true);
    expect(input('#password').disabled).toBe(true);
    release();
    await render();
    expect(input('#password').disabled).toBe(false);
    expect(input('#password').value).toBe('');
  });

  /**
   * Aggiorna la vista e attende che la catena di promesse avviata dall'interazione sia conclusa.
   * `whenStable` da solo non basta: con la change detection senza `zone.js` la promessa di un
   * gestore non è un'attività registrata, quindi il test deve cedere il turno all'event loop.
   */
  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function input(selector: string): HTMLInputElement {
    const element = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      selector,
    );
    if (element === null) {
      throw new Error(`Campo non trovato: ${selector}`);
    }

    return element;
  }

  function setInput(selector: string, value: string): void {
    const element = input(selector);
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  function submit(): void {
    const form = (fixture.nativeElement as HTMLElement).querySelector('form');
    if (!form) {
      throw new Error('Modulo di accesso non trovato.');
    }

    form.dispatchEvent(new Event('submit'));
  }
});
