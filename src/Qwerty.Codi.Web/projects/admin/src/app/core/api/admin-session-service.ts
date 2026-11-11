import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ADMIN_API_PREFIX, toApiError } from '@codi/api';
import { AdminContext } from '@codi/api';

/**
 * Accesso amministrativo (§9.1, §10.3). È un servizio di trasporto per gli endpoint dedicati,
 * non un wrapper del dispatcher: il cookie lo emette il server e il client non lo legge mai,
 * perché è `HttpOnly`.
 */
@Injectable({ providedIn: 'root' })
export class AdminSessionService {
  private readonly http = inject(HttpClient);

  private readonly contextSignal = signal<AdminContext | null>(null);

  /** Contesto amministrativo corrente, o `null` se non esiste una sessione utilizzabile. */
  readonly context = this.contextSignal.asReadonly();

  /**
   * Ottiene la coppia di token antiforgery dal bootstrap admin (§10.3). Va fatto prima della
   * prima mutazione: il token accompagna le richieste autenticate, compreso l'accesso.
   */
  async refreshAntiforgeryToken(): Promise<void> {
    try {
      await firstValueFrom(
        this.http.get(`${ADMIN_API_PREFIX}/bootstrap`, { withCredentials: true }),
      );
    } catch (error) {
      throw toApiError(error);
    }
  }

  /** Accede con le credenziali del docente e pubblica il contesto restituito dal server. */
  async signIn(username: string, password: string): Promise<AdminContext> {
    await this.refreshAntiforgeryToken();

    try {
      await firstValueFrom(
        this.http.post(
          `${ADMIN_API_PREFIX}/auth/login`,
          { username, password },
          { withCredentials: true },
        ),
      );
    } catch (error) {
      throw toApiError(error);
    }

    const context = await this.load();
    if (context === null) {
      // L'accesso è riuscito ma la credenziale non risulta utilizzabile: è un difetto del
      // server, non un errore di chi ha digitato la password.
      throw new Error('La sessione amministrativa non è utilizzabile dopo l’accesso.');
    }

    return context;
  }

  /** Revoca la sessione e dimentica il contesto locale. */
  async signOut(): Promise<void> {
    try {
      await firstValueFrom(
        this.http.post(`${ADMIN_API_PREFIX}/auth/logout`, {}, { withCredentials: true }),
      );
    } finally {
      // Il contesto locale si dimentica comunque: se la revoca non è arrivata, il cookie
      // potrebbe essere ancora valido, ma non è il client a decidere se la sessione esiste.
      this.contextSignal.set(null);
    }
  }

  /**
   * Rilegge il contesto dal server. Una sessione assente o scaduta non è un errore da mostrare:
   * è l'informazione che serve per decidere dove portare la persona.
   */
  async load(): Promise<AdminContext | null> {
    try {
      const context = await firstValueFrom(
        this.http.get<AdminContext>(`${ADMIN_API_PREFIX}/auth/me`, { withCredentials: true }),
      );
      this.contextSignal.set(context);
      return context;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.contextSignal.set(null);
        return null;
      }

      throw toApiError(error);
    }
  }
}
