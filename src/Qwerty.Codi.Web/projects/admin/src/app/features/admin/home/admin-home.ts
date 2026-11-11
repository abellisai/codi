import { ChangeDetectionStrategy, Component } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { RouterLink } from '@angular/router';
import { AdminSectionIcons, AdminSections } from '../admin-sections';

/**
 * Pagina iniziale dell'area docente (§14.1): elenca le sezioni con una riga di spiegazione e
 * porta a ciascuna con un clic.
 *
 * Prima della radice portava all'accesso, quindi chi entrava non trovava le sezioni se non
 * digitando un indirizzo. L'elenco viene dal catalogo condiviso con il menu del guscio: la pagina
 * non aggiunge destinazioni proprie e non mostra dati, perché non ne ha bisogno per orientare.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, RouterLink, HlmCardImports],
  providers: [provideIcons(AdminSectionIcons)],
  selector: 'app-admin-home',
  styleUrl: './admin-home.css',
  templateUrl: './admin-home.html',
})
export class AdminHome {
  protected readonly groups = AdminSections;
}
