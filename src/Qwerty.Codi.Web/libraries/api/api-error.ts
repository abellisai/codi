/**
 * Errore applicativo del backend (§10.7). Il codice è il contratto: il messaggio è per la
 * persona che legge, la decisione del client si prende sul codice e sullo stato HTTP.
 *
 * Un errore che non porta un codice applicativo resta un errore, non un successo con corpo
 * inatteso: il client non deve dedurre nulla da un corpo che non riconosce.
 */
export class ApiError extends Error {
  constructor(
    readonly code: string,
    readonly status: number,
    message: string,
    readonly traceId?: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }

  /**
   * Vero se la richiesta è stata rifiutata perché manca un contesto autenticato valido: la
   * schermata deve riportare all'accesso, non invitare a ripetere la richiesta.
   */
  get requiresSignIn(): boolean {
    return this.status === 401;
  }

  /** Converte un Problem Details del backend in un errore tipizzato, senza inventare codici. */
  static fromResponse(status: number, body: unknown): ApiError {
    const problem = isProblemDetails(body) ? body : undefined;
    const code = problem?.code ?? 'UnknownError';
    const message =
      problem?.detail ?? problem?.title ?? `Richiesta non riuscita (stato ${String(status)}).`;

    return new ApiError(code, status, message, problem?.traceId);
  }
}

interface ProblemDetails {
  readonly code?: string;
  readonly title?: string;
  readonly detail?: string;
  readonly traceId?: string;
}

function isProblemDetails(body: unknown): body is ProblemDetails {
  return typeof body === 'object' && body !== null;
}
