import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  AttemptFinalizationGrant,
  AttemptSave,
  PUBLIC_API_PREFIX,
  WorkspacePreview,
  WorkspacePreviewReceipt,
} from '@codi/api';
import { firstValueFrom, timeout } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AttemptTransport {
  private readonly http = inject(HttpClient);

  grants(): Promise<readonly AttemptFinalizationGrant[]> {
    return firstValueFrom(
      this.http.get<readonly AttemptFinalizationGrant[]>(
        `${PUBLIC_API_PREFIX}/participant/finalizations`,
      ),
    );
  }

  /**
   * Anteprima dal collegamento HTTP: si usa quando il progetto supera il limite del messaggio
   * SignalR (§15.5, §11.3). Non è un salvataggio e non si ritenta: un rifiuto resta un rifiuto.
   */
  preview(attemptId: string, body: WorkspacePreview): Promise<WorkspacePreviewReceipt> {
    return firstValueFrom(
      this.http
        .put<WorkspacePreviewReceipt>(
          `${PUBLIC_API_PREFIX}/participant/attempts/${attemptId}/preview`,
          body,
        )
        .pipe(timeout(5000)),
    );
  }

  async save(
    attemptId: string,
    kind: 'workspace' | 'checkpoint' | 'finalization',
    body: Record<string, unknown>,
  ): Promise<AttemptSave> {
    const url = `${PUBLIC_API_PREFIX}/participant/attempts/${attemptId}/${kind}`;
    const send = () =>
      firstValueFrom(
        kind === 'finalization'
          ? this.http.post<AttemptSave>(url, body).pipe(timeout(5000))
          : this.http.put<AttemptSave>(url, body).pipe(timeout(5000)),
      );
    try {
      return await send();
    } catch (failure) {
      const status = (failure as { status?: number }).status;
      if (
        (failure as { name?: string }).name !== 'TimeoutError' &&
        status !== 0 &&
        (status === undefined || status < 500)
      )
        throw failure;
      return send();
    }
  }
}
