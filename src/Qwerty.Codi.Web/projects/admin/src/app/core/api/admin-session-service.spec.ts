import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '@codi/api';
import { AdminSessionService } from './admin-session-service';

describe('AdminSessionService', () => {
  let service: AdminSessionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminSessionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('accede tramite bootstrap, login e profilo nel gruppo admin, poi revoca la sessione', async () => {
    const pending = service.signIn('Maestra', 'password-di-test');
    const bootstrap = http.expectOne('/api/v1/admin/bootstrap');
    expect(bootstrap.request.method).toBe('GET');
    expect(bootstrap.request.withCredentials).toBe(true);
    bootstrap.flush({});
    await Promise.resolve();
    await Promise.resolve();

    const login = http.expectOne('/api/v1/admin/auth/login');
    expect(login.request.method).toBe('POST');
    expect(login.request.body).toEqual({ username: 'Maestra', password: 'password-di-test' });
    expect(login.request.withCredentials).toBe(true);
    login.flush({ username: 'Maestra' });
    await Promise.resolve();
    await Promise.resolve();

    const profile = http.expectOne('/api/v1/admin/auth/me');
    expect(profile.request.withCredentials).toBe(true);
    profile.flush({ username: 'Maestra', policies: ['Admin'] });
    await expect(pending).resolves.toEqual({ username: 'Maestra', policies: ['Admin'] });
    expect(service.context()?.username).toBe('Maestra');

    const logout = service.signOut();
    const request = http.expectOne('/api/v1/admin/auth/logout');
    expect(request.request.method).toBe('POST');
    expect(request.request.withCredentials).toBe(true);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await logout;
    expect(service.context()).toBeNull();
  });

  it('traduce un bootstrap non raggiungibile in errore applicativo senza inviare il login', async () => {
    const pending = service.signIn('Maestra', 'password-di-test');
    http.expectOne('/api/v1/admin/bootstrap').error(new ProgressEvent('error'));
    const error = await pending.catch((failure: unknown) => failure);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('NetworkUnavailable');
    http.expectNone('/api/v1/admin/auth/login');
  });

  it('interpreta un profilo 401 come sessione assente', async () => {
    const pending = service.load();
    http.expectOne('/api/v1/admin/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await expect(pending).resolves.toBeNull();
    expect(service.context()).toBeNull();
  });
});
