import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe } from '@ngx-translate/core';
import { APP_ICONS, PAGE_ICONS } from './icons';

/**
 * Settings and the pages used now and then (import, export, AI access) share one menu entry; these tabs move
 * between them, so the main menu stays short.
 */
@Component({
  selector: 'app-settings-tabs',
  imports: [RouterLink, RouterLinkActive, TranslatePipe, NgIcon],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav
      class="-mx-4 mb-5 overflow-x-auto px-4 sm:mx-0 sm:px-0"
      [attr.aria-label]="'nav.settings' | translate"
    >
      <div class="segmented">
        @for (t of tabs; track t.path) {
          <a [routerLink]="t.path" routerLinkActive="active" ariaCurrentWhenActive="page">
            <ng-icon [name]="t.icon" class="mr-1.5" aria-hidden="true" />{{ t.label | translate }}
          </a>
        }
      </div>
    </nav>
  `,
})
export class SettingsTabsComponent {
  protected readonly tabs = [
    { path: '/settings', label: 'nav.general', icon: PAGE_ICONS.settings },
    { path: '/import', label: 'nav.import', icon: PAGE_ICONS.import },
    { path: '/export', label: 'nav.export', icon: PAGE_ICONS.export },
    { path: '/ai', label: 'nav.ai', icon: PAGE_ICONS.ai },
  ];
}

/** Routes that belong to the Settings entry of the main menu. */
export const SETTINGS_ROUTES = ['/settings', '/import', '/export', '/ai'];
