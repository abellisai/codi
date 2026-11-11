import { HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';

/**
 * Antiforgery delle mutazioni autenticate con cookie (§10.2, §17.2).
 *
 * Il token arriva in un cookie leggibile dal frontend, distinto da quello di autenticazione che
 * resta `HttpOnly`; il client lo rimanda in un'intestazione. L'intestazione si aggiunge solo dove
 * il token esiste già: chiederlo qui sarebbe una seconda politica di autenticazione accanto a
 * quella del server, che è l'unica a decidere.
 */
export const ANTIFORGERY_COOKIE_NAME = '__Host-CodiAntiforgery';
export const PUBLIC_ANTIFORGERY_COOKIE_NAME = '__Host-CodiPublicAntiforgery';

export const ANTIFORGERY_HEADER_NAME = 'X-Codi-Antiforgery';

/** Metodi che non cambiano stato: il server non chiede loro un token. */
const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);

/** Legge il token dal cookie omonimo; `null` se non c'è. */
export function readAntiforgeryToken(
  cookieSource: string,
  cookieName = ANTIFORGERY_COOKIE_NAME,
): string | null {
  const prefix = `${cookieName}=`;

  for (const cookie of cookieSource.split('; ')) {
    if (cookie.startsWith(prefix)) {
      return decodeURIComponent(cookie.slice(prefix.length));
    }
  }

  return null;
}

/** Le identità docente e alunno conservano token distinti sullo stesso host. */
export function readAntiforgeryTokenForUrl(cookieSource: string, url: string): string | null {
  const path = new URL(url, 'https://codi.invalid').pathname;
  const publicArea = path.startsWith('/api/v1/public') || path.startsWith('/hubs/v1/participant');
  return readAntiforgeryToken(
    cookieSource,
    publicArea ? PUBLIC_ANTIFORGERY_COOKIE_NAME : ANTIFORGERY_COOKIE_NAME,
  );
}

/**
 * Aggiunge l'intestazione del token alle mutazioni. La sorgente del token è un parametro perché
 * la decisione — quali richieste ricevono l'intestazione e con quale token — si possa provare
 * senza dipendere dai cookie del browser.
 */
export function addAntiforgeryHeader(
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
  readToken: () => string | null,
) {
  if (SAFE_METHODS.has(request.method.toUpperCase())) {
    return next(request);
  }

  const token = readToken();
  if (token === null) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { [ANTIFORGERY_HEADER_NAME]: token } }));
}

export const antiforgeryInterceptor: HttpInterceptorFn = (request, next) =>
  addAntiforgeryHeader(request, next, () =>
    readAntiforgeryTokenForUrl(document.cookie, request.url),
  );
