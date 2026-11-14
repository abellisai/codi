import { TestBed } from '@angular/core/testing';
import { ResultFeedback } from './result-feedback';

describe('Risultato visivo', () => {
  it('usa l’esito della correzione anche quando i testi sono uguali', () => {
    const fixture = TestBed.createComponent(ResultFeedback);
    fixture.componentRef.setInput('success', true);
    fixture.componentRef.setInput('message', 'Hai finito.');
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('.success')).not.toBeNull();
    expect(page.querySelector('app-learning-scene')).not.toBeNull();
    fixture.componentRef.setInput('success', false);
    fixture.detectChanges();
    expect(page.querySelector('.success')).toBeNull();
    expect(page.querySelector('app-learning-scene')).toBeNull();
    expect(page.textContent).toContain('Prova ancora, puoi farcela!');
    expect(page.textContent).toContain('Hai finito.');
  });
});
