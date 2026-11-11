import { HttpClient, HttpContext, HttpContextToken, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiError } from './api-error';
import { API_AREA, ADMIN_API_PREFIX, PUBLIC_API_PREFIX } from './api-area';

/**
 * Vero quando il `404` è una risposta prevista: «la bozza non esiste ancora» è un esito che la
 * pagina interpreta, non un guasto da annunciare. Lo dichiara chi chiama, perché solo il
 * chiamante sa che cosa significa la risposta per il proprio contesto.
 */
export const EXPECTED_NOT_FOUND = new HttpContextToken<boolean>(() => false);

/**
 * Vero quando il successo della richiesta non deve produrre un avviso. Serve alle richieste che
 * fanno parte di un'operazione composta e mostrano il proprio esito altrove — il salvataggio che
 * precede la pubblicazione di un esercizio — così il docente legge una sola conferma.
 */
export const SILENT_SUCCESS = new HttpContextToken<boolean>(() => false);

/** Opzioni del trasporto che appartengono al chiamante e non al server. */
export interface RequestOptions {
  readonly idempotencyKey?: string;
  /** Il `404` è una risposta prevista e non produce un avviso all'utente. */
  readonly notFoundIsExpected?: boolean;
  /** Il successo non produce un avviso: l'esito lo mostra chi ha composto l'operazione. */
  readonly silentSuccess?: boolean;
}

/**
 * I due soli metodi del dispatcher (§10.1, §14.2): il nome pubblico della richiesta è il
 * contratto, quindi non esistono wrapper per singola richiesta. I componenti non usano
 * `HttpClient` direttamente.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly prefix = inject(API_AREA) === 'admin' ? ADMIN_API_PREFIX : PUBLIC_API_PREFIX;

  /** Esegue un comando di §10.4 e restituisce l'esito tipizzato del caso d'uso. */
  executeCommand<TResponse>(
    requestName: string,
    payload: object = {},
    options: RequestOptions = {},
  ): Promise<TResponse> {
    return this.execute<TResponse>('command', requestName, payload, options);
  }

  /** Esegue una query di §10.5; il server non muta stato persistente. */
  executeQuery<TResponse>(
    requestName: string,
    payload: object = {},
    options: RequestOptions = {},
  ): Promise<TResponse> {
    return this.execute<TResponse>('query', requestName, payload, options);
  }

  private async execute<TResponse>(
    kind: 'command' | 'query',
    requestName: string,
    payload: object,
    options: RequestOptions,
  ): Promise<TResponse> {
    const regia = kind === 'command' && this.prefix === ADMIN_API_PREFIX && 'sessionId' in payload;
    const key = options.idempotencyKey ?? (regia ? crypto.randomUUID() : undefined);
    for (let attempt = 0; ; attempt++) {
      try {
        return await firstValueFrom(
          this.http.post<TResponse>(
            `${this.prefix}/execute/${kind}/${encodeURIComponent(requestName)}`,
            payload,
            {
              // Le credenziali viaggiano nel cookie: senza questa opzione una richiesta
              // cross-origin per il browser partirebbe senza identità e diventerebbe un 401.
              withCredentials: true,
              headers: key === undefined ? {} : { 'Idempotency-Key': key },
              context: new HttpContext()
                .set(EXPECTED_NOT_FOUND, options.notFoundIsExpected === true)
                .set(SILENT_SUCCESS, options.silentSuccess === true),
            },
          ),
        );
      } catch (error) {
        if (regia && attempt === 0 && error instanceof HttpErrorResponse && error.status === 0)
          continue;
        throw toApiError(error);
      }
    }
  }
}

/** Traduce la risposta di errore del trasporto nell'errore applicativo di §10.7. */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }

  if (error instanceof HttpErrorResponse) {
    // Stato 0: la richiesta non ha raggiunto il server. È un guasto di rete, non una risposta.
    if (error.status === 0) {
      return new ApiError('NetworkUnavailable', 0, 'Il server non è raggiungibile.');
    }

    return ApiError.fromResponse(error.status, error.error);
  }

  return new ApiError('UnknownError', 0, 'Errore non previsto durante la richiesta.');
}
