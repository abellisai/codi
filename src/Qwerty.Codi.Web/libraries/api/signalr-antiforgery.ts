import {
  DefaultHttpClient,
  HttpClient,
  HttpRequest,
  HttpResponse,
  NullLogger,
} from '@microsoft/signalr';
import { ANTIFORGERY_HEADER_NAME, readAntiforgeryTokenForUrl } from './antiforgery';

/** Legge il token corrente anche durante negoziazione, riconnessione e polling SignalR. */
export class SignalRAntiforgeryClient extends HttpClient {
  constructor(
    url = '/hubs/v1/teacher',
    private readonly inner: HttpClient = new DefaultHttpClient(NullLogger.instance),
    private readonly readToken: () => string | null = () =>
      readAntiforgeryTokenForUrl(document.cookie, url),
  ) {
    super();
  }

  override send(request: HttpRequest): Promise<HttpResponse> {
    const token = this.readToken();
    if (
      token !== null &&
      !['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(request.method?.toUpperCase() ?? 'GET')
    ) {
      request = { ...request, headers: { ...request.headers, [ANTIFORGERY_HEADER_NAME]: token } };
    }
    return this.inner.send(request);
  }

  override getCookieString(url: string): string {
    return this.inner.getCookieString(url);
  }
}
