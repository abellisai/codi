import { TestBed } from '@angular/core/testing';
import { AttemptRecoveryStore } from '@codi/api';
import { AdminSessionService } from '@core/api';
import { vi } from 'vitest';
import { LocalRecovery } from './local-recovery';
describe('LocalRecovery', () => {
  it('verifica il docente prima di leggere IndexedDB', async () => {
    const read = vi.fn();
    const load = vi.fn().mockResolvedValue(null);
    TestBed.configureTestingModule({
      providers: [
        { provide: AdminSessionService, useValue: { load } },
        { provide: AttemptRecoveryStore, useValue: { list: read } },
      ],
    });
    const fixture = TestBed.createComponent(LocalRecovery);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(load).toHaveBeenCalledOnce();
    expect(read).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Accedi come docente');
  });

  it('mostra soltanto le copie dopo la verifica corrente', async () => {
    const read = vi.fn().mockResolvedValue([]);
    const load = vi.fn().mockResolvedValue({ username: 'Docente' });
    TestBed.configureTestingModule({
      providers: [
        { provide: AdminSessionService, useValue: { load } },
        { provide: AttemptRecoveryStore, useValue: { list: read } },
      ],
    });
    const fixture = TestBed.createComponent(LocalRecovery);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(read).toHaveBeenCalledOnce();
    expect(fixture.nativeElement.textContent).toContain('Nessuna copia da recuperare');
  });
});
