import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError } from './api-error';
import { ApiService, EXPECTED_NOT_FOUND } from './api-service';
import { API_AREA } from './api-area';

describe('ApiService', () => {
  let service: ApiService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_AREA, useValue: 'admin' },
      ],
    });

    service = TestBed.inject(ApiService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('ripete una richiesta di regia con la stessa chiave dopo una risposta persa', async () => {
    const pending = service.executeCommand('Pause', {
      sessionId: 's',
      expectedSessionRevision: '2',
    });
    const first = httpTesting.expectOne('/api/v1/admin/execute/command/Pause');
    const key = first.request.headers.get('Idempotency-Key');
    expect(key).toBeTruthy();
    first.error(new ProgressEvent('network'));
    await Promise.resolve();
    const retry = httpTesting.expectOne('/api/v1/admin/execute/command/Pause');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    expect(retry.request.body).toEqual(first.request.body);
    retry.flush({ status: 'Paused' });
    await expect(pending).resolves.toEqual({ status: 'Paused' });
  });

  it('conserva la chiave esplicita e non ripete un conflitto', async () => {
    const pending = service.executeCommand(
      'Pause',
      { sessionId: 's', expectedSessionRevision: '2' },
      { idempotencyKey: 'request' },
    );
    const rejected = expect(pending).rejects.toMatchObject({ status: 409 });
    const request = httpTesting.expectOne('/api/v1/admin/execute/command/Pause');
    expect(request.request.headers.get('Idempotency-Key')).toBe('request');
    request.flush({ code: 'SessionStateConflict' }, { status: 409, statusText: 'Conflict' });
    await rejected;
    httpTesting.expectNone('/api/v1/admin/execute/command/Pause');
  });

  it('usa il dispatcher pubblico quando viene configurato per gli alunni', async () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_AREA, useValue: 'public' },
      ],
    });
    service = TestBed.inject(ApiService);
    httpTesting = TestBed.inject(HttpTestingController);

    const pending = service.executeQuery('GetParticipantSnapshot', { sessionId: 'session-1' });
    const request = httpTesting.expectOne('/api/v1/public/execute/query/GetParticipantSnapshot');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.body).toEqual({ sessionId: 'session-1' });
    request.flush({ sessionId: 'session-1' });
    await expect(pending).resolves.toEqual({ sessionId: 'session-1' });
  });

  it('invia i comandi al percorso del dispatcher con il corpo e le credenziali', async () => {
    const pending = service.executeCommand<{ id: string }>('CreateSchool', {
      name: 'Scuola Manzoni',
    });

    const request = httpTesting.expectOne('/api/v1/admin/execute/command/CreateSchool');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Scuola Manzoni' });
    // Le credenziali viaggiano nel cookie: senza withCredentials la richiesta partirebbe senza
    // identità e diventerebbe un 401.
    expect(request.request.withCredentials).toBe(true);
    // Senza dichiarazione, un'assenza resta un guasto: è il chiamante a dire che è prevista.
    expect(request.request.context.get(EXPECTED_NOT_FOUND)).toBe(false);
    request.flush({ id: 'e1e2c9b0-2f4e-4f2f-9c2e-2d1c2b3a4d5e' });

    await expect(pending).resolves.toEqual({ id: 'e1e2c9b0-2f4e-4f2f-9c2e-2d1c2b3a4d5e' });
  });

  it('invia le query al percorso del dispatcher come mutazioni senza corpo', async () => {
    const pending = service.executeQuery<readonly unknown[]>('ListSchools', {
      includeArchived: false,
    });

    const request = httpTesting.expectOne('/api/v1/admin/execute/query/ListSchools');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ includeArchived: false });
    request.flush([]);

    await expect(pending).resolves.toEqual([]);
  });

  it('dichiara previsto un 404 quando il chiamante lo chiede, senza nascondere il rifiuto', async () => {
    const pending = service.executeQuery(
      'GetExerciseDraft',
      { exerciseId: 'ex1' },
      { notFoundIsExpected: true },
    );

    const request = httpTesting.expectOne('/api/v1/admin/execute/query/GetExerciseDraft');
    expect(request.request.context.get(EXPECTED_NOT_FOUND)).toBe(true);
    request.flush(
      { code: 'ResourceNotFound', detail: 'Risorsa non trovata.' },
      { status: 404, statusText: 'Not Found' },
    );

    // La dichiarazione zittisce l'avviso, non il rifiuto: chi ha chiesto la risorsa deve poterlo
    // ancora distinguere da una risposta positiva.
    const error = await pending.catch((failure: unknown) => failure);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(404);
  });

  it('traduce una risposta di errore del contratto in un errore con codice', async () => {
    const pending = service.executeCommand('CreateSchool', { name: 'Scuola Manzoni' });

    httpTesting.expectOne('/api/v1/admin/execute/command/CreateSchool').flush(
      {
        status: 409,
        title: 'Scuola già esistente',
        detail: 'Esiste già una scuola con il nome «Scuola Manzoni».',
        code: 'SchoolAlreadyExists',
        traceId: 'trace-1',
      },
      { status: 409, statusText: 'Conflict' },
    );

    const error = await pending.catch((failure: unknown) => failure);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('SchoolAlreadyExists');
    expect((error as ApiError).status).toBe(409);
    expect((error as ApiError).traceId).toBe('trace-1');
    expect((error as ApiError).requiresSignIn).toBe(false);
  });

  it('segnala come accesso richiesto il rifiuto di un contesto non autenticato', async () => {
    const pending = service.executeQuery('ListSchools');

    httpTesting
      .expectOne('/api/v1/admin/execute/query/ListSchools')
      .flush(
        { status: 401, code: 'AuthenticationRequired', detail: 'Serve un accesso.' },
        { status: 401, statusText: 'Unauthorized' },
      );

    const error = (await pending.catch((failure: unknown) => failure)) as ApiError;
    expect(error.requiresSignIn).toBe(true);
  });

  it('non inventa un codice quando il corpo dell’errore non è un contratto', async () => {
    const pending = service.executeQuery('ListSchools');

    httpTesting.expectOne('/api/v1/admin/execute/query/ListSchools').flush('errore interno', {
      status: 500,
      statusText: 'Server Error',
    });

    const error = (await pending.catch((failure: unknown) => failure)) as ApiError;
    expect(error.code).toBe('UnknownError');
    expect(error.status).toBe(500);
  });

  it('distingue il guasto di rete dalla risposta di errore del server', async () => {
    const pending = service.executeQuery('ListSchools');

    httpTesting
      .expectOne('/api/v1/admin/execute/query/ListSchools')
      .error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    const error = (await pending.catch((failure: unknown) => failure)) as ApiError;
    expect(error.code).toBe('NetworkUnavailable');
    expect(error).toBeInstanceOf(ApiError);
    expect(error).not.toBeInstanceOf(HttpErrorResponse);
  });
});
