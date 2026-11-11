import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AttemptTransport } from './attempt-transport';
describe('AttemptTransport', () => {
  let transport: AttemptTransport;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    transport = TestBed.inject(AttemptTransport);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('ritenta lo stesso corpo dopo un errore del server', async () => {
    const body = { saveId: 'same', workspaceJson: '{}' };
    const pending = transport.save('t', 'workspace', body);
    const first = http.expectOne('/api/v1/public/participant/attempts/t/workspace');
    expect(first.request.method).toBe('PUT');
    first.flush({}, { status: 503, statusText: 'Unavailable' });
    await Promise.resolve();
    const retry = http.expectOne('/api/v1/public/participant/attempts/t/workspace');
    expect(retry.request.body).toEqual(body);
    retry.flush({ saveId: 'same', attemptId: 't' });
    await pending;
  });
  it('non ritenta un conflitto e mantiene il codice HTTP', async () => {
    const pending = transport.save('t', 'checkpoint', {});
    const assertion = expect(pending).rejects.toMatchObject({ status: 412 });
    http
      .expectOne('/api/v1/public/participant/attempts/t/checkpoint')
      .flush({}, { status: 412, statusText: 'Conflict' });
    await assertion;
  });
  it('usa POST per il salvataggio conclusivo e GET per il grant', async () => {
    const pending = transport.save('t', 'finalization', {});
    const request = http.expectOne('/api/v1/public/participant/attempts/t/finalization');
    expect(request.request.method).toBe('POST');
    request.flush({});
    await pending;
    const grants = transport.grants();
    const read = http.expectOne('/api/v1/public/participant/finalizations');
    expect(read.request.method).toBe('GET');
    read.flush([]);
    expect(await grants).toEqual([]);
  });
});
