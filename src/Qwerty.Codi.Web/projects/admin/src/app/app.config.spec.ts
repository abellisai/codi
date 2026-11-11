import { HttpContext, HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EXPECTED_NOT_FOUND, SILENT_SUCCESS } from '@codi/api';
import { LoadingService } from '@core/http';
import { toast } from '@spartan-ng/brain/sonner';
import { appConfig } from './app.config';

/** Prova la catena realmente registrata nell'app, senza ripetere i provider degli interceptor. */
describe('HTTP dell’admin', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let loading: LoadingService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    loading = TestBed.inject(LoadingService);
    vi.spyOn(toast, 'success').mockReturnValue('successo');
    vi.spyOn(toast, 'error').mockReturnValue('errore');
  });

  afterEach(() => {
    backend.verify();
    vi.restoreAllMocks();
  });

  it('mantiene il caricamento tra richieste sovrapposte e lo termina anche su annullamento', () => {
    http.post('/api/v1/admin/execute/query/ListSchools', {}).subscribe();
    const pending = http.post('/api/v1/admin/execute/query/ListCourses', {}).subscribe();

    expect(loading.pendingCount()).toBe(2);
    backend.expectOne('/api/v1/admin/execute/query/ListSchools').flush([]);
    expect(loading.isLoading()).toBe(true);
    expect(loading.pendingCount()).toBe(1);
    const canceled = backend.expectOne('/api/v1/admin/execute/query/ListCourses');
    pending.unsubscribe();
    expect(canceled.cancelled).toBe(true);
    expect(loading.isLoading()).toBe(false);
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('annuncia un comando riuscito con un titolo leggibile e termina il caricamento', () => {
    http.post('/api/v1/admin/execute/command/CreateSchool', { name: 'Scuola' }).subscribe();
    expect(loading.isLoading()).toBe(true);

    backend.expectOne('/api/v1/admin/execute/command/CreateSchool').flush({ id: 's1' });

    expect(toast.success).toHaveBeenCalledExactlyOnceWith('Scuola creata');
    expect(loading.pendingCount()).toBe(0);
  });

  it('annuncia il rifiuto del backend e termina il caricamento senza successo', () => {
    http
      .post('/api/v1/admin/execute/command/CreateSchool', {})
      .subscribe({ error: () => undefined });

    backend
      .expectOne('/api/v1/admin/execute/command/CreateSchool')
      .flush(
        { code: 'SchoolAlreadyExists', detail: 'La scuola esiste già.' },
        { status: 409, statusText: 'Conflict' },
      );

    expect(toast.error).toHaveBeenCalledExactlyOnceWith('La scuola esiste già.', {
      description: 'SchoolAlreadyExists',
    });
    expect(toast.success).not.toHaveBeenCalled();
    expect(loading.isLoading()).toBe(false);
  });

  it('annuncia il salvataggio della bozza con il titolo dell’operazione', () => {
    const url = '/api/v1/admin/execute/command/SaveExerciseDraft';
    http.post(url, {}).subscribe();
    backend.expectOne(url).flush({});

    // Un salvataggio esplicito è un'operazione singola: usa l'avviso standard come ogni comando.
    expect(toast.success).toHaveBeenCalledExactlyOnceWith('Bozza salvata');
  });

  it('annuncia la copia di una versione con il titolo dell’operazione', () => {
    const url = '/api/v1/admin/execute/command/CopyExerciseVersionToDraft';
    http.post(url, {}).subscribe();
    backend.expectOne(url).flush({});

    expect(toast.success).toHaveBeenCalledExactlyOnceWith('Versione copiata nella bozza');
  });

  it('lascia alla pagina una sola conferma per verifica e pubblicazione', () => {
    for (const name of ['ValidateExerciseDraft', 'PublishExerciseVersion']) {
      const url = '/api/v1/admin/execute/command/' + name;
      http.post(url, {}).subscribe();
      backend.expectOne(url).flush({});
    }
    expect(toast.success).not.toHaveBeenCalled();
    expect(loading.pendingCount()).toBe(0);
  });

  it('non annuncia il salvataggio che fa parte di un’operazione composta', () => {
    const url = '/api/v1/admin/execute/command/SaveExerciseDraft';
    http.post(url, {}, { context: new HttpContext().set(SILENT_SUCCESS, true) }).subscribe();
    backend.expectOne(url).flush({});

    // Il salvataggio che precede la pubblicazione non produce una conferma sua: il docente ne legge
    // una sola, quella della pubblicazione.
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('lascia silenziosi bootstrap e controllo sessione pur conteggiando le richieste', () => {
    http.get('/api/v1/admin/bootstrap').subscribe();
    http.get('/api/v1/admin/auth/me').subscribe({ error: () => undefined });
    expect(loading.pendingCount()).toBe(2);

    backend.expectOne('/api/v1/admin/bootstrap').flush({});
    backend
      .expectOne('/api/v1/admin/auth/me')
      .flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(loading.pendingCount()).toBe(0);
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('non annuncia un 404 che il chiamante ha dichiarato previsto, ma annuncia gli altri', () => {
    // «La bozza non esiste ancora» è una risposta che la pagina interpreta: un avviso la
    // racconterebbe come un guasto e resterebbe accanto alla nota che spiega il caso.
    http
      .post(
        '/api/v1/admin/execute/query/GetExerciseDraft',
        {},
        { context: new HttpContext().set(EXPECTED_NOT_FOUND, true) },
      )
      .subscribe({ error: () => undefined });
    http.post('/api/v1/admin/execute/query/GetExercise', {}).subscribe({ error: () => undefined });

    backend
      .expectOne('/api/v1/admin/execute/query/GetExerciseDraft')
      .flush(
        { code: 'ResourceNotFound', detail: 'Risorsa non trovata.' },
        { status: 404, statusText: 'Not Found' },
      );
    expect(toast.error).not.toHaveBeenCalled();

    backend
      .expectOne('/api/v1/admin/execute/query/GetExercise')
      .flush(
        { code: 'ResourceNotFound', detail: 'Risorsa non trovata.' },
        { status: 404, statusText: 'Not Found' },
      );

    expect(toast.error).toHaveBeenCalledExactlyOnceWith('Risorsa non trovata.', {
      description: 'ResourceNotFound',
    });
    expect(loading.isLoading()).toBe(false);
  });
});
