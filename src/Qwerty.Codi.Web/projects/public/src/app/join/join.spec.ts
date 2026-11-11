import { OverlayModule } from '@angular/cdk/overlay';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ApiError, AvailableClassroom, AvailableSchool, PlaySession } from '@codi/api';
import { JoinByCodeRequest, JoinRequest, ParticipantService } from '../core/participant-service';
import { Join } from './join';

/** Pagina di comodo per l'approdo dopo l'ingresso: il percorso si verifica dall'indirizzo. */
@Component({ selector: 'app-arrival', template: '<p>lezione</p>' })
class Arrival {}

/** Sostituto del trasporto pubblico: registra le chiamate e restituisce quello che serve. */
class ParticipantStub {
  readonly calls: string[] = [];
  readonly joins: { body: JoinRequest; key: string }[] = [];
  readonly codeJoins: { body: JoinByCodeRequest; key: string }[] = [];

  /** Presenza della lezione in play: la home la interroga all'apertura e poi periodicamente. */
  inPlay = false;

  schoolsList: readonly AvailableSchool[] = [{ id: 's1', name: 'Scuola Manzoni' }];

  classroomsList: readonly AvailableClassroom[] = [
    { id: 'c1', schoolId: 's1', name: 'Terza A', schoolYear: '2026/2027' },
  ];

  play: PlaySession = {
    schoolId: 's1',
    classroomId: 'c1',
    sessionId: 'sess1',
    lessonId: 'l1',
    courseId: 'co1',
    courseTitle: 'Informatica',
    lessonTitle: 'Prima lezione',
  };

  joinFailure: ApiError | null = null;

  async bootstrap(): Promise<void> {
    this.calls.push('bootstrap');
  }

  async lessonInPlay(): Promise<boolean> {
    this.calls.push('in-play');

    return this.inPlay;
  }

  async schools(): Promise<readonly AvailableSchool[]> {
    this.calls.push('schools');

    return this.schoolsList;
  }

  async classrooms(schoolId: string): Promise<readonly AvailableClassroom[]> {
    this.calls.push(`classrooms:${schoolId}`);

    return this.classroomsList;
  }

  async playSession(classroomId: string): Promise<PlaySession> {
    this.calls.push(`session:${classroomId}`);

    return this.play;
  }

  async joinByCode(body: JoinByCodeRequest, key: string): Promise<never> {
    this.calls.push('join-by-code');
    this.codeJoins.push({ body, key });

    if (this.joinFailure !== null) {
      throw this.joinFailure;
    }

    throw new Error('ingresso con codice non dovuto in questa prova');
  }

  async join(body: JoinRequest, key: string): Promise<never> {
    this.calls.push('join');
    this.joins.push({ body, key });

    if (this.joinFailure !== null) {
      throw this.joinFailure;
    }

    throw new Error('ingresso con catena non dovuto in questa prova');
  }

  async context(): Promise<never> {
    throw new Error('contesto non richiesto dall’ingresso');
  }

  async leave(): Promise<void> {
    this.calls.push('leave');
  }
}

describe('Ingresso dell’alunno', () => {
  let harness!: RouterTestingHarness;
  let participants: ParticipantStub;

  beforeEach(async () => {
    participants = new ParticipantStub();
    localStorage.clear();

    await TestBed.configureTestingModule({
      imports: [OverlayModule],
      providers: [
        { provide: ParticipantService, useValue: participants },
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'join', component: Join },
          { path: 'lezione', component: Arrival },
        ]),
      ],
    }).compileComponents();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('sente la lezione avviata: bootstrap, sorveglianza e invito a scrivere il codice', async () => {
    participants.inPlay = true;

    await mount();

    expect(participants.calls.slice(0, 2)).toEqual(['bootstrap', 'in-play']);
    // Le scuole non si caricano all'apertura: servono solo a chi non ha il codice.
    expect(participants.calls).not.toContain('schools');
    expect(pageText()).toContain('La lezione è iniziata');
    expect(pageText()).toContain('Codice della lezione');
  });

  it('senza lezione in corso lo dichiara invece di far digitare un codice che non esiste', async () => {
    participants.inPlay = false;

    await mount();

    expect(pageText()).toContain('Nessuna lezione in corso');
  });

  it('entra con il codice e il nome, senza dichiarare scuola, classe e sessione', async () => {
    participants.inPlay = true;
    await mount();
    typeCode('24681357');
    typeName('  Luca  ');
    await flush();

    button('Entra').click();
    await flush();

    expect(participants.codeJoins.length).toBe(1);
    expect(participants.codeJoins[0].body).toEqual({
      joinCode: '24681357',
      displayName: 'Luca',
    });
    expect(participants.codeJoins[0].key.length).toBeGreaterThan(10);
    expect(harness.routeNativeElement?.textContent).toContain('lezione');
  });

  it('tiene solo le cifre, non più di otto, e chiede il nome per entrare', async () => {
    await mount();
    typeCode('12a3456789');
    await flush();

    expect(codeField().value).toBe('12345678');
    // Senza nome non si entra: il maestro deve poter riconoscere chi è entrato.
    expect(button('Entra').disabled).toBe(true);

    typeName('Luca');
    await flush();

    expect(button('Entra').disabled).toBe(false);
  });

  it('dopo un codice errato un codice diverso è un tentativo nuovo, non la stessa richiesta', async () => {
    // La chiave di idempotenza resta la stessa finché il contenuto resta lo stesso; cambiando il
    // codice il tentativo è un altro, e riusare la chiave sarebbe un conflitto (§9.4).
    participants.joinFailure = new ApiError('InvalidJoinCode', 404, 'Il codice non è valido.');
    await mount();
    typeName('Luca');
    typeCode('11111111');
    await flush();

    button('Entra').click();
    await flush();
    expect(pageText()).toContain('Il codice non è valido.');

    typeCode('22222222');
    await flush();
    button('Entra').click();
    await flush();

    expect(participants.codeJoins.length).toBe(2);
    expect(participants.codeJoins[1].key).not.toBe(participants.codeJoins[0].key);
  });

  it('ripete con la stessa chiave un tentativo identico dopo un rifiuto', async () => {
    participants.joinFailure = new ApiError('AdmissionsClosed', 409, 'Ingressi chiusi.');
    await mount();
    typeName('Luca');
    typeCode('24681357');
    await flush();

    button('Entra').click();
    await flush();
    participants.joinFailure = null;
    button('Entra').click();
    await flush();

    expect(participants.codeJoins.length).toBe(2);
    expect(participants.codeJoins[1].key).toBe(participants.codeJoins[0].key);
  });

  it('non ho il codice: scopre la scelta di scuola e classe e carica le scuole solo allora', async () => {
    await mount();
    expect(participants.calls).not.toContain('schools');

    button('Non ho il codice').click();
    await flush();

    expect(participants.calls).toContain('schools');
    expect(pageText()).toContain('Scuola Manzoni');
  });

  it('con il ripiego sceglie scuola e classe ed entra dichiarando la catena', async () => {
    await mount();
    button('Non ho il codice').click();
    await flush();

    choose('scuola', 's1');
    await flush();
    expect(participants.calls).toContain('classrooms:s1');

    choose('classe', 'c1');
    await flush();
    expect(participants.calls).toContain('session:c1');
    expect(pageText()).toContain('Prima lezione');

    typeName('Luca');
    await flush();
    button('Entra con scuola e classe').click();
    await flush();

    expect(participants.joins.length).toBe(1);
    expect(participants.joins[0].body).toEqual({
      schoolId: 's1',
      classroomId: 'c1',
      sessionId: 'sess1',
      displayName: 'Luca',
    });
    expect(harness.routeNativeElement?.textContent).toContain('lezione');
  });

  it('ricorda il nome dell’ultimo ingresso per non riscriverlo', async () => {
    localStorage.setItem('codi:display-name', 'Luca');

    await mount();

    expect(nameField().value).toBe('Luca');
  });

  async function mount(): Promise<void> {
    harness = await RouterTestingHarness.create('/join');
    await flush();
  }

  function page(): HTMLElement {
    const element = harness.routeNativeElement;
    if (element === null) {
      throw new Error('La pagina dell’ingresso non è montata.');
    }

    return element;
  }

  function pageText(): string {
    return page().textContent ?? '';
  }

  function choose(id: string, value: string): void {
    const select = page().querySelector<HTMLSelectElement>(`select#${id}`);
    if (select === null) {
      throw new Error(`Elenco non trovato: ${id}`);
    }

    select.value = value;
    select.dispatchEvent(new Event('change'));
  }

  function typeName(value: string): void {
    write(nameField(), value);
  }

  function typeCode(value: string): void {
    write(codeField(), value);
  }

  function nameField(): HTMLInputElement {
    return field('nome');
  }

  function codeField(): HTMLInputElement {
    return field('codice');
  }

  function field(id: string): HTMLInputElement {
    const input = page().querySelector<HTMLInputElement>(`input#${id}`);
    if (input === null) {
      throw new Error(`Campo non trovato: ${id}`);
    }

    return input;
  }

  function write(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function button(label: string): HTMLButtonElement {
    const found = [...page().querySelectorAll('button')].find(
      (candidate) => (candidate.textContent ?? '').trim() === label,
    );
    if (found === undefined) {
      throw new Error(`Pulsante non trovato: ${label}`);
    }

    return found;
  }

  async function flush(): Promise<void> {
    harness.detectChanges();
    await harness.fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    harness.detectChanges();
  }
});
