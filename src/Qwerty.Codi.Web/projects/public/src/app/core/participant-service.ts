import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  AvailableClassroom,
  AvailableSchool,
  ParticipantContext,
  ParticipantJoined,
  PlaySession,
  PUBLIC_API_PREFIX,
} from '@codi/api';
import { firstValueFrom } from 'rxjs';

/** Corpo dell'ingresso (§9.4): scuola, classe e sessione scelte insieme al nome dell'alunno. */
export interface JoinRequest {
  readonly schoolId: string;
  readonly classroomId: string;
  readonly sessionId: string;
  readonly displayName: string;
}

/** Corpo dell'ingresso con il codice detto dal docente (§9.4): otto cifre e nome, senza catena. */
export interface JoinByCodeRequest {
  readonly joinCode: string;
  readonly displayName: string;
}

/**
 * Trasporto HTTP dell'app alunni: gli **endpoint dedicati** di §10.3 — scoperta pubblica, bootstrap,
 * ingresso, profilo e uscita. Le letture del lavoro (`GetParticipantSnapshot`,
 * `GetAssignedExercise`, `GetAttempt`) passano dal dispatcher e le invoca la pagina con `ApiService`:
 * qui restano solo le rotte in cui il trasporto è parte del contratto, cioè quelle che emettono
 * cookie prima che esista un contesto applicativo.
 *
 * La pagina non usa `HttpClient`: lo fa questo servizio, così la costruzione degli indirizzi e
 * l'intestazione di idempotenza stanno in un punto solo.
 */
@Injectable({ providedIn: 'root' })
export class ParticipantService {
  static readonly DisplayNameStorageKey = 'codi:display-name';
  private readonly http = inject(HttpClient);

  rememberDisplayName(displayName: string): void {
    try {
      localStorage.setItem(ParticipantService.DisplayNameStorageKey, displayName);
    } catch {
      /* Il browser può impedire la memoria del nome. */
    }
  }

  /**
   * Bootstrap dell'area pubblica (§9.4): emette la coppia antiforgery e il **ticket di ammissione**
   * che rende idempotente l'ingresso. Va chiamato prima del primo ingresso della pagina.
   */
  async bootstrap(): Promise<void> {
    await firstValueFrom(this.http.get(`${PUBLIC_API_PREFIX}/bootstrap`));
  }

  /**
   * Vero se c'è una lezione in play (§10.3): la home lo chiede per accorgersi che l’insegnante ha
   * avviato la lezione. La risposta è un solo booleano: quale lezione sia non si sa da qui, serve
   * il codice.
   */
  async lessonInPlay(): Promise<boolean> {
    const presence = await firstValueFrom(
      this.http.get<{ readonly inPlay: boolean }>(`${PUBLIC_API_PREFIX}/in-play`),
    );

    return presence.inPlay;
  }

  /** Scuole con ingressi disponibili. */
  schools(): Promise<readonly AvailableSchool[]> {
    return firstValueFrom(
      this.http.get<readonly AvailableSchool[]>(`${PUBLIC_API_PREFIX}/schools`),
    );
  }

  /** Classi della scuola scelta che hanno una sessione in play. */
  classrooms(schoolId: string): Promise<readonly AvailableClassroom[]> {
    return firstValueFrom(
      this.http.get<readonly AvailableClassroom[]>(
        `${PUBLIC_API_PREFIX}/schools/${schoolId}/classrooms`,
      ),
    );
  }

  /** La sessione **in play** della classe scelta: è quella in cui l'alunno entra. */
  playSession(classroomId: string): Promise<PlaySession> {
    return firstValueFrom(
      this.http.get<PlaySession>(`${PUBLIC_API_PREFIX}/classrooms/${classroomId}/sessions`),
    );
  }

  /**
   * Ingresso idempotente (§9.4): la chiave accompagna la richiesta e resta la stessa finché
   * l'ingresso non riesce, così una risposta persa recupera la stessa partecipazione invece di
   * creare un secondo alunno.
   */
  join(body: JoinRequest, idempotencyKey: string): Promise<ParticipantJoined> {
    const headers = new HttpHeaders({ 'Idempotency-Key': idempotencyKey });

    return firstValueFrom(
      this.http.post<ParticipantJoined>(`${PUBLIC_API_PREFIX}/join`, body, { headers }),
    );
  }

  /**
   * Ingresso con il codice di ingresso (§9.4): la via ordinaria dell'aula. La chiave accompagna la
   * richiesta e resta la stessa finché l'ingresso non riesce **con lo stesso contenuto**: cambiare
   * il codice o il nome è un'altra richiesta, e riusare la chiave sarebbe un conflitto.
   */
  joinByCode(body: JoinByCodeRequest, idempotencyKey: string): Promise<ParticipantJoined> {
    const headers = new HttpHeaders({ 'Idempotency-Key': idempotencyKey });

    return firstValueFrom(
      this.http.post<ParticipantJoined>(`${PUBLIC_API_PREFIX}/join`, body, { headers }),
    );
  }

  /** Profilo del partecipante autenticato: chi è, la sua ammissione e la vista voluta (§10.3). */
  context(): Promise<ParticipantContext> {
    return firstValueFrom(this.http.get<ParticipantContext>(`${PUBLIC_API_PREFIX}/auth/me`));
  }

  /** Uscita volontaria (§10.3): revoca la credenziale e cancella i cookie del partecipante. */
  async leave(): Promise<void> {
    await firstValueFrom(this.http.post(`${PUBLIC_API_PREFIX}/participant/leave`, {}));
  }
}
