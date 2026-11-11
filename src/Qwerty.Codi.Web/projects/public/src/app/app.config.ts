import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { API_AREA, antiforgeryInterceptor } from '@codi/api';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: API_AREA, useValue: 'public' },
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([antiforgeryInterceptor])),
  ],
};
