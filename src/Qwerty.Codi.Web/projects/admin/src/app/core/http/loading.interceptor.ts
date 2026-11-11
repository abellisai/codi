import type { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs';
import { LoadingService } from './loading-service';

/**
 * Segnala al guscio che una chiamata al server è in corso (§14.2).
 *
 * `finalize` vale su tutti gli esiti — risposta, errore, annullamento — quindi l'indicatore non
 * può restare acceso per una richiesta fallita. Nessuna pagina gestisce il proprio stato di
 * attesa globale: lo stato locale di un pulsante resta una scelta della pagina, ma l'indicatore
 * complessivo è questo.
 */
export const loadingInterceptor: HttpInterceptorFn = (request, next) => {
  const loading = inject(LoadingService);

  loading.begin();

  return next(request).pipe(finalize(() => loading.end()));
};
