import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { antiforgeryInterceptor } from '@core/api';
import { loadingInterceptor, notificationInterceptor } from '@core/http';
import { API_AREA } from '@codi/api';
import { provideSpartanHlm } from '@spartan-ng/helm/utils';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: API_AREA, useValue: 'admin' },
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideSpartanHlm(),
    // Il token antiforgery accompagna le mutazioni autenticate con cookie (§10.2). Il client non
    // costruisce URL di risorse applicative: le richieste passano dal dispatcher (§14.2).
    provideHttpClient(
      withInterceptors([loadingInterceptor, antiforgeryInterceptor, notificationInterceptor]),
    ),
  ],
};
