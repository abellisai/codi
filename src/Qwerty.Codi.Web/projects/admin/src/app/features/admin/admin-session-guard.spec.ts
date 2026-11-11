import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { AdminSessionService } from '@core/api';
import { adminSessionGuard } from './admin-session-guard';

/** Doppio del servizio di sessione: i tre esiti che il guard deve distinguere. */
class SessionStub {
  /** Sessione già nota al client (il segnale del servizio). */
  contextValue: unknown = null;

  /** Risposta del server a `load()`. */
  loadedValue: unknown = null;

  failure: unknown = null;

  loads = 0;

  context(): unknown {
    return this.contextValue;
  }

  async load(): Promise<unknown> {
    this.loads += 1;

    if (this.failure !== null) {
      throw this.failure;
    }

    return this.loadedValue;
  }
}

describe('adminSessionGuard', () => {
  let session: SessionStub;

  beforeEach(async () => {
    session = new SessionStub();

    await TestBed.configureTestingModule({
      providers: [{ provide: AdminSessionService, useValue: session }, provideRouter([])],
    }).compileComponents();
  });

  /**
   * Il guard serve alla navigazione, non alla sicurezza: senza sessione l'area non ha nulla da
   * mostrare, quindi si torna all'accesso invece di far comparire sezioni vuote con un errore.
   */
  it('riporta all’accesso quando non c’è sessione', async () => {
    const result = await run();

    expect(result instanceof UrlTree).toBe(true);
    expect(String(result)).toBe('/login');
    expect(session.loads).toBe(1);
  });

  it('lascia entrare quando la sessione è già stata verificata', async () => {
    session.contextValue = { username: 'Maestra', policies: ['Admin'] };

    expect(await run()).toBe(true);
    expect(session.loads).toBe(0);
  });

  it('rilegge la sessione dal server quando il client non ne ha una', async () => {
    session.loadedValue = { username: 'Maestra', policies: ['Admin'] };

    expect(await run()).toBe(true);
    expect(session.loads).toBe(1);
  });

  /**
   * Un server irraggiungibile non è una sessione assente: bloccare la navigazione nasconderebbe
   * il guasto dietro un modulo di accesso, mentre la pagina sa mostrare l'errore vero.
   */
  it('non blocca la navigazione quando il server non risponde', async () => {
    session.failure = new Error('server non raggiungibile');

    expect(await run()).toBe(true);
  });

  async function run(): Promise<unknown> {
    return await TestBed.runInInjectionContext(() =>
      adminSessionGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
  }
});
