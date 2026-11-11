import { HttpRequest, HttpResponse } from '@angular/common/http';
import { lastValueFrom, of } from 'rxjs';
import {
  addAntiforgeryHeader,
  ANTIFORGERY_HEADER_NAME,
  readAntiforgeryToken,
  readAntiforgeryTokenForUrl,
} from './antiforgery';

describe('antiforgery', () => {
  it('mantiene distinti i token delle API e degli hub per area', () => {
    const cookies = '__Host-CodiAntiforgery=teacher; __Host-CodiPublicAntiforgery=pupil';
    expect(readAntiforgeryTokenForUrl(cookies, '/api/v1/admin/execute/command/Pause')).toBe(
      'teacher',
    );
    expect(readAntiforgeryTokenForUrl(cookies, '/api/v1/public/execute/query/GetAttempt')).toBe(
      'pupil',
    );
    expect(readAntiforgeryTokenForUrl(cookies, '/hubs/v1/teacher/negotiate')).toBe('teacher');
    expect(readAntiforgeryTokenForUrl(cookies, '/hubs/v1/participant/negotiate')).toBe('pupil');
  });
  describe('readAntiforgeryToken', () => {
    it('legge il token dal cookie omonimo fra altri cookie', () => {
      const cookies = '__Host-CodiAntiforgeryCookie=abc; __Host-CodiAntiforgery=token-123; altra=1';

      expect(readAntiforgeryToken(cookies)).toBe('token-123');
    });

    it('decodifica un token con caratteri codificati', () => {
      expect(readAntiforgeryToken('__Host-CodiAntiforgery=abc%2Fdef%3D')).toBe('abc/def=');
    });

    it('non confonde il cookie interno con quello del request token', () => {
      // Il cookie interno del framework è HttpOnly e ha un nome diverso: qui si legge solo
      // quello che il frontend può effettivamente vedere.
      expect(readAntiforgeryToken('__Host-CodiAntiforgeryCookie=abc')).toBeNull();
    });

    it('senza cookie restituisce null', () => {
      expect(readAntiforgeryToken('')).toBeNull();
      expect(readAntiforgeryToken('altra=1')).toBeNull();
    });
  });

  describe('addAntiforgeryHeader', () => {
    it('aggiunge l’intestazione alle mutazioni', async () => {
      const request = new HttpRequest('POST', '/api/v1/admin/execute/command/CreateSchool', {});
      const next = (forwarded: HttpRequest<unknown>) => {
        expect(forwarded.headers.get(ANTIFORGERY_HEADER_NAME)).toBe('token-123');
        return of(new HttpResponse({ status: 200 }));
      };

      await lastValueFrom(addAntiforgeryHeader(request, next, () => 'token-123'));
    });

    it('non aggiunge l’intestazione alle letture', async () => {
      const request = new HttpRequest('GET', '/api/v1/admin/auth/me');
      const next = (forwarded: HttpRequest<unknown>) => {
        expect(forwarded.headers.has(ANTIFORGERY_HEADER_NAME)).toBe(false);
        return of(new HttpResponse({ status: 200 }));
      };

      await lastValueFrom(addAntiforgeryHeader(request, next, () => 'token-123'));
    });

    it('senza token lascia passare la richiesta senza intestazione', async () => {
      // Il server decide l'esito: un token assente è un rifiuto applicativo previsto, non una
      // condizione che il client può nascondere scegliendo di non inviare nulla.
      const request = new HttpRequest('POST', '/api/v1/admin/execute/command/CreateSchool', {});
      const next = (forwarded: HttpRequest<unknown>) => {
        expect(forwarded.headers.has(ANTIFORGERY_HEADER_NAME)).toBe(false);
        return of(new HttpResponse({ status: 200 }));
      };

      await lastValueFrom(addAntiforgeryHeader(request, next, () => null));
    });
  });
});
