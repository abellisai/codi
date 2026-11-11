import { HttpClient, HttpRequest, HttpResponse } from '@microsoft/signalr';
import { SignalRAntiforgeryClient } from './signalr-antiforgery';
import { ANTIFORGERY_HEADER_NAME } from './antiforgery';

class CaptureClient extends HttpClient {
  requests: HttpRequest[] = [];
  override send(request: HttpRequest): Promise<HttpResponse> {
    this.requests.push(request);
    return Promise.resolve(new HttpResponse(200));
  }
}
describe('Antiforgery SignalR', () => {
  it('usa il token corrente per negoziazione e riconnessione', async () => {
    const inner = new CaptureClient();
    let token = 'first';
    const client = new SignalRAntiforgeryClient('/hubs/v1/teacher', inner, () => token);
    await client.post('/hubs/v1/teacher/negotiate');
    token = 'renewed';
    await client.post('/hubs/v1/teacher/negotiate');
    expect(inner.requests.map((request) => request.headers?.[ANTIFORGERY_HEADER_NAME])).toEqual([
      'first',
      'renewed',
    ]);
  });
  it('preserva intestazioni e corpo del polling autenticato', async () => {
    const inner = new CaptureClient();
    const client = new SignalRAntiforgeryClient('/hubs/v1/teacher', inner, () => 'token');
    await client.post('/hubs/v1/participant', {
      content: 'payload',
      headers: { 'X-Other': 'value' },
    });
    expect(inner.requests[0].content).toBe('payload');
    expect(inner.requests[0].headers).toEqual({
      'X-Other': 'value',
      [ANTIFORGERY_HEADER_NAME]: 'token',
    });
  });
  it('lascia al server il rifiuto del token assente e preserva le letture', async () => {
    const inner = new CaptureClient();
    const client = new SignalRAntiforgeryClient('/hubs/v1/teacher', inner, () => null);
    await client.post('/hubs/v1/participant/negotiate');
    await client.get('/hubs/v1/participant');
    expect(
      inner.requests.every((request) => request.headers?.[ANTIFORGERY_HEADER_NAME] === undefined),
    ).toBe(true);
  });
});
