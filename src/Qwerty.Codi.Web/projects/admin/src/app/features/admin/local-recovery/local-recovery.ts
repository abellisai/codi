import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { AdminSessionService } from '@core/api';
import { AttemptRecoveryStore, RecoveryCopy, toApiError } from '@codi/api';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';

/** Recupero sul PC condiviso. Nessun progetto locale sostituisce automaticamente il server. */
@Component({
  selector: 'app-local-recovery',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButtonImports, HlmCardImports],
  template: `<section class="flex flex-col gap-4">
    <h1>Copie da recuperare su questo PC</h1>
    <p>Apri questa pagina sul PC del bambino. Le copie non confermate restano per 24 ore.</p>
    <p>
      Scarica il progetto per esaminarlo. Il download conserva i blocchi senza modificare il
      progetto sul server.
    </p>
    @if (error()) {
      <p role="alert">{{ error() }}</p>
    }
    @for (copy of copies(); track copy.key) {
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>Progetto non confermato</h2>
          <p hlmCardDescription>Tentativo {{ copy.attemptId }}</p>
        </div>
        <div hlmCardContent>
          <p>Revisione {{ copy.projectRevision }}</p>
          <p>Sessione {{ copy.sessionId }} · partecipante {{ copy.participantId }}</p>
        </div>
        <div hlmCardFooter>
          <button hlmBtn type="button" (click)="download(copy)">Scarica progetto</button>
        </div>
      </section>
    } @empty {
      <p role="status">Nessuna copia da recuperare.</p>
    }
  </section>`,
})
export class LocalRecovery implements OnInit {
  private readonly session = inject(AdminSessionService);
  private readonly store = inject(AttemptRecoveryStore);
  protected readonly copies = signal<readonly RecoveryCopy[]>([]);
  protected readonly error = signal('');
  ngOnInit(): void {
    void this.load();
  }
  private async load(): Promise<void> {
    try {
      if ((await this.session.load()) === null) {
        this.error.set('Accedi come docente per vedere le copie.');
        return;
      }
      this.copies.set(await this.store.list());
    } catch (failure) {
      this.error.set(toApiError(failure).message);
    }
  }
  protected async download(copy: RecoveryCopy): Promise<void> {
    try {
      if ((await this.session.load()) === null) {
        this.copies.set([]);
        this.error.set('La sessione docente è scaduta.');
        return;
      }
      const url = URL.createObjectURL(new Blob([copy.workspaceJson], { type: 'application/json' }));
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `progetto-${copy.attemptId}.json`;
      anchor.click();
      URL.revokeObjectURL(url);
    } catch (failure) {
      this.error.set(toApiError(failure).message);
    }
  }
}
