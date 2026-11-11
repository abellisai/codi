import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AdminSessionService } from '@core/api';

/**
 * Lascia entrare nell'area docente solo con una sessione utilizzabile (§9.1, §14.1).
 *
 * Serve alla navigazione, non alla sicurezza: il permesso decisivo resta sul server, che rifiuta
 * ogni richiesta senza credenziale valida. Serve a non mostrare sezioni vuote a chi non ha ancora
 * effettuato l'accesso e a riportare all'accesso quando la sessione è scaduta, revocata o
 * appartiene a un altro profilo.
 *
 * La sessione già verificata in questa pagina non si richiede di nuovo a ogni clic: si rilegge
 * dal server solo quando il client non ne ha una. Se il server non risponde, la navigazione
 * prosegue e il guasto lo mostra la pagina, che sa distinguere l'errore dalla sessione assente.
 */
export const adminSessionGuard: CanActivateFn = async () => {
  const session = inject(AdminSessionService);
  const router = inject(Router);

  if (session.context() !== null) {
    return true;
  }

  try {
    return (await session.load()) === null ? router.parseUrl('/login') : true;
  } catch {
    return true;
  }
};
