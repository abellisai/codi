import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
  type Signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  ActivatedRouteSnapshot,
  isActive,
  NavigationEnd,
  Router,
  RouterLink,
  RouterOutlet,
} from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLogOut } from '@ng-icons/lucide';
import { AdminSessionService } from '@core/api';
import { GlobalSpinner } from '@shared/components';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmToaster } from '@spartan-ng/helm/sonner';
import { filter, map } from 'rxjs';
import { AdminSection, AdminSectionIcons, AdminSections } from '../admin-sections';

/** Voce del menu: la sezione del catalogo più lo stato attivo, che dipende dal router. */
interface ShellSection extends AdminSection {
  readonly active: Signal<boolean>;
}

/** Gruppo di voci con un titolo proprio. */
interface ShellSectionGroup {
  readonly label: string;
  readonly sections: readonly ShellSection[];
}

/**
 * La sezione comprende anche le route figlie: scuola, corso e lezione mantengono la voce attiva.
 * Il router confronta segmenti, non prefissi testuali; query e frammenti non cambiano la sezione.
 */
const ActiveMatch = { paths: 'subset', queryParams: 'ignored', fragment: 'ignored' } as const;

/** Titolo dichiarato dalla route più profonda: serve alle pagine che non stanno nel menu. */
function routeTitle(root: ActivatedRouteSnapshot): string {
  let snapshot: ActivatedRouteSnapshot | null = root;
  let title = '';

  while (snapshot !== null) {
    if (snapshot.title !== undefined && snapshot.title !== '') {
      title = snapshot.title;
    }

    snapshot = snapshot.firstChild;
  }

  return title;
}

/**
 * Guscio dell'area docente (§14.1, §14.2): il menu delle sezioni resta montato mentre cambia la
 * pagina, così ogni superficie di configurazione è raggiungibile con un clic e sta sempre nello
 * stesso posto, anche nelle pagine che non si collegano fra loro.
 *
 * Il menu dichiara soltanto destinazioni: non decide i permessi, che restano sul server, e non
 * conserva lo stato delle pagine, che ognuna rilegge dal server. L'accesso vive fuori da questo
 * guscio, perché senza sessione non c'è nulla da navigare.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, RouterLink, RouterOutlet, HlmSidebarImports, HlmToaster, GlobalSpinner],
  providers: [provideIcons({ ...AdminSectionIcons, lucideLogOut })],
  selector: 'app-admin-shell',
  templateUrl: './admin-shell.html',
})
export class AdminShell {
  private readonly router = inject(Router);
  private readonly session = inject(AdminSessionService);

  /** Le sezioni del catalogo con lo stato attivo, calcolato dal router. */
  protected readonly groups: readonly ShellSectionGroup[] = AdminSections.map((group) => ({
    label: group.label,
    sections: group.sections.map((section) => ({
      ...section,
      active: isActive(section.url, this.router, ActiveMatch),
    })),
  }));

  /**
   * Fotografia delle route all'ultima navigazione conclusa. È la dipendenza del titolo quando la
   * pagina non sta nel menu; il valore iniziale copre il primo disegno, prima che la navigazione
   * sia conclusa.
   */
  private readonly activated = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(() => this.router.routerState.snapshot),
    ),
    { initialValue: this.router.routerState.snapshot },
  );

  /** Sezione corrente, riconosciuta dalla voce attiva del menu. */
  private readonly currentSection = computed(() => {
    for (const group of this.groups) {
      for (const section of group.sections) {
        if (section.active()) {
          return section.label;
        }
      }
    }

    return '';
  });

  /**
   * Titolo della barra: la sezione corrente, o il titolo dichiarato dalla route per le pagine
   * fuori menu (il prototipo dell'autore). Dice in quale sezione si sta lavorando.
   */
  protected readonly pageTitle = computed(() => {
    const section = this.currentSection();

    return section !== '' ? section : routeTitle(this.activated().root);
  });

  /** Uscita in corso: il pulsante si disabilita per non ripetere la revoca. */
  protected readonly signingOut = signal(false);

  /** Esce dall'area docente e riporta all'accesso. È il server a revocare la credenziale. */
  protected async signOut(): Promise<void> {
    this.signingOut.set(true);

    try {
      await this.session.signOut();
      await this.router.navigateByUrl('/login');
    } finally {
      this.signingOut.set(false);
    }
  }
}
