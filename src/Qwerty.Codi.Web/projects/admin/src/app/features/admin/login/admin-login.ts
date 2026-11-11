import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { disabled, form, FormField, FormRoot, required } from '@angular/forms/signals';
import { textRules } from '@shared/forms';
import { Router } from '@angular/router';
import { AdminSessionService, ApiError, toApiError } from '@core/api';

/**
 * Accesso del docente (§9.1, §14.1). È la porta dell'area amministrativa: senza sessione le
 * pagine di gestione non ottengono dati, perché il permesso è del server e non della schermata.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmButtonImports,
    HlmInputImports,
    HlmFieldImports,
    HlmCardImports,
    FormField,
    FormRoot,
  ],
  selector: 'app-admin-login',
  styleUrl: './admin-login.css',
  templateUrl: './admin-login.html',
})
export class AdminLogin implements OnInit {
  private readonly session = inject(AdminSessionService);
  private readonly router = inject(Router);

  private readonly loginFormModel = signal({ username: '', password: '' });
  protected readonly loginForm = form(
    this.loginFormModel,
    (path) => {
      disabled(path, () => this.busy());
      textRules(path.username, 'Nome utente', 120);
      required(path.password, { message: 'Inserisci la password.' });
    },
    {
      submission: {
        action: async () => {
          await this.submit();
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  async ngOnInit(): Promise<void> {
    // Una sessione già valida non deve rivedere il modulo di accesso (§9.1). Si chiede al server
    // chi è e, se il contesto esiste, si prosegue verso la gestione. Se la lettura non riesce, il
    // modulo resta la strada per accedere: sarà il tentativo a dire se le credenziali valgono.
    try {
      if ((await this.session.load()) !== null) {
        await this.router.navigateByUrl('/schools');
      }
    } catch {
      // La lettura del contesto non è indispensabile per accedere, quindi non è un errore da
      // mostrare a chi deve ancora digitare nome utente e password.
    }
  }

  protected async submit(): Promise<void> {
    if (
      this.busy() ||
      this.loginForm.username().value().trim() === '' ||
      this.loginForm.password().value() === ''
    ) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.session.signIn(
        this.loginForm.username().value(),
        this.loginForm.password().value(),
      );
      this.loginForm.password().value.set('');
      this.loginForm().reset();
      await this.router.navigateByUrl('/schools');
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.busy.set(false);
    }
  }
}
