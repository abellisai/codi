import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

describe('Ingresso alunni', () => {
  it('apre il benvenuto senza richiedere accesso amministrativo', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe('/');
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('h1')?.textContent).toBe('Benvenuto in aula!');
    expect(page.querySelector('a[href="/join"]')).not.toBeNull();
  });

  it('apre l’ingresso con il modulo, senza accesso amministrativo', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/join');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(router.url).toBe('/join');
    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('h1')?.textContent).toBe('Entra nella lezione');
    expect(page.querySelector('input#nome')).not.toBeNull();
    expect(page.querySelector('input[type="password"]')).toBeNull();
    expect(page.textContent).not.toContain('Accesso docente');
  });
});
