import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth';
import { Notifications } from '../../core/notifications';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { DataEvents } from '../../core/data-events';
import { ModalComponent } from '../../shared/modal';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { StatusBadgeComponent } from '../../shared/status-badge';

import { SettingsTabsComponent } from '../../shared/settings-tabs';
@Component({
  selector: 'app-settings',
  // Narrow content, centred in the main area like a document rather than pinned to the left.
  host: { class: 'mx-auto block w-full max-w-3xl' },
  imports: [
    SettingsTabsComponent,
    NgIcon,
    PageHeaderComponent,
    StatusBadgeComponent,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    ModalComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-settings-tabs />
    <app-page-header [icon]="icons.settings" [title]="'nav.settings' | translate" />
    <div class="grid gap-4">
      <section class="card space-y-4">
        <h2 class="card-title">
          <ng-icon name="lucidePalette" />{{ 'settings.appearance' | translate }}
        </h2>
        <div class="flex flex-wrap gap-6">
          <div>
            <span class="label flex items-center gap-1.5"
              ><ng-icon name="lucideLanguages" aria-hidden="true" />{{
                'settings.language' | translate
              }}</span
            >
            <div class="segmented">
              <button [class.active]="prefs.lang() === 'en'" (click)="prefs.lang.set('en')">
                English
              </button>
              <button [class.active]="prefs.lang() === 'pt-PT'" (click)="prefs.lang.set('pt-PT')">
                Português
              </button>
            </div>
          </div>
          <div>
            <span class="label flex items-center gap-1.5"
              ><ng-icon name="lucideSunMoon" aria-hidden="true" />{{
                'settings.theme' | translate
              }}</span
            >
            <div class="segmented">
              @for (t of themes; track t) {
                <button
                  class="gap-1.5"
                  [class.active]="prefs.theme() === t"
                  (click)="prefs.theme.set(t)"
                >
                  <ng-icon [name]="themeIcon[t]" aria-hidden="true" />
                  {{ 'settings.themes.' + t | translate }}
                </button>
              }
            </div>
          </div>
          <div>
            <span class="label">{{ 'settings.currency' | translate }}</span>
            <div class="segmented">
              @for (c of currencies; track c) {
                <button
                  [class.active]="prefs.displayCurrency() === c"
                  (click)="prefs.displayCurrency.set(c)"
                >
                  {{ c === 'EUR' ? '€ EUR' : '$ USD' }}
                </button>
              }
            </div>
            <p class="text-muted-foreground mt-1 max-w-xs text-[11px]">
              {{ 'settings.currencyHint' | translate }}
            </p>
          </div>
        </div>
      </section>

      <section class="card space-y-3">
        <h2 class="card-title">
          <ng-icon name="lucideBell" />{{ 'settings.notifications' | translate }}
        </h2>
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="max-w-md min-w-0">
            <p id="s-browser-alerts" class="text-sm font-medium">
              {{ 'settings.browserAlerts' | translate }}
            </p>
            <p class="text-muted-foreground mt-1 text-xs leading-relaxed">
              {{ 'settings.browserAlertsHint' | translate }}
            </p>
          </div>
          <div class="segmented" role="group" aria-labelledby="s-browser-alerts">
            <button
              [class.active]="!notifications.browserAlerts()"
              [attr.aria-pressed]="!notifications.browserAlerts()"
              (click)="setBrowserAlerts(false)"
            >
              {{ 'settings.off' | translate }}
            </button>
            <button
              [class.active]="notifications.browserAlerts()"
              [attr.aria-pressed]="notifications.browserAlerts()"
              [disabled]="!browserSupported"
              (click)="setBrowserAlerts(true)"
            >
              {{ 'settings.on' | translate }}
            </button>
          </div>
        </div>
        @if (blocked()) {
          <p class="text-xs text-amber-700 dark:text-amber-300" role="status">
            {{ 'settings.browserBlocked' | translate }}
          </p>
        }
      </section>

      <section class="card">
        <h2 class="card-title">
          <ng-icon name="lucideLock" />{{ 'settings.security' | translate }}
        </h2>
        <div class="text-muted-foreground mb-4 flex flex-wrap items-center gap-2 text-sm">
          <ng-icon name="lucideUserRound" aria-hidden="true" />
          <span class="min-w-0 break-all">{{
            'settings.signedInAs' | translate: { email: auth.me()?.email }
          }}</span>
          <app-status-badge tone="success" icon="lucideShieldCheck">{{
            'settings.mfaOn' | translate
          }}</app-status-badge>
        </div>
        <form
          class="grid gap-3 sm:grid-cols-2"
          (submit)="$event.preventDefault(); changePassword()"
        >
          <div>
            <label class="label" for="s-cur">{{ 'settings.currentPassword' | translate }}</label>
            <input
              id="s-cur"
              hlmInput
              type="password"
              autocomplete="current-password"
              (input)="current.set($any($event.target).value)"
            />
          </div>
          <div>
            <label class="label" for="s-new">{{ 'auth.newPassword' | translate }}</label>
            <input
              id="s-new"
              hlmInput
              type="password"
              autocomplete="new-password"
              minlength="12"
              (input)="next.set($any($event.target).value)"
            />
          </div>
          <div class="sm:col-span-2">
            <button hlmBtn variant="outline">
              <ng-icon name="lucideKeyRound" />{{ 'settings.changePassword' | translate }}
            </button>
          </div>
        </form>
      </section>

      <section class="card text-sm text-muted-foreground">
        <h2 class="card-title">
          <ng-icon name="lucideShieldCheck" />{{ 'settings.privacy' | translate }}
        </h2>
        <p class="leading-relaxed">{{ 'settings.privacyText' | translate }}</p>
      </section>

      <!-- Start fresh: everything entered or imported goes; broker connections and categories stay. -->
      <section class="card border-destructive/40 space-y-3">
        <h2 class="card-title text-destructive">
          <ng-icon name="lucideTriangleAlert" />{{ 'settings.reset.title' | translate }}
        </h2>
        <p class="text-sm text-muted-foreground">{{ 'settings.reset.text' | translate }}</p>
        <button hlmBtn variant="destructive" (click)="resetOpen.set(true)">
          <ng-icon name="lucideTrash2" />{{ 'settings.reset.open' | translate }}
        </button>
      </section>
    </div>

    <app-modal
      [open]="resetOpen()"
      [title]="'settings.reset.title' | translate"
      (closed)="resetOpen.set(false); resetWord.set('')"
    >
      <div class="space-y-3 text-sm">
        <p>{{ 'settings.reset.deletes' | translate }}</p>
        <p>{{ 'settings.reset.keeps' | translate }}</p>
        <a hlmBtn variant="outline" size="sm" href="/api/exports/json" download>
          <ng-icon name="lucideCloudDownload" />{{ 'settings.reset.backup' | translate }}
        </a>
        <label class="label block" for="reset-word">{{
          'settings.reset.type' | translate: { word: confirmWord() }
        }}</label>
        <input
          id="reset-word"
          hlmInput
          class="w-full"
          autocomplete="off"
          [value]="resetWord()"
          (input)="resetWord.set($any($event.target).value)"
        />
        <div class="flex justify-end gap-2">
          <button hlmBtn variant="outline" (click)="resetOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button
            hlmBtn
            variant="destructive"
            [disabled]="resetWord().trim().toUpperCase() !== confirmWord() || resetting()"
            (click)="reset()"
          >
            <ng-icon name="lucideTrash2" />{{ 'settings.reset.confirm' | translate }}
          </button>
        </div>
      </div>
    </app-modal>
  `,
})
export class SettingsComponent {
  protected readonly prefs = inject(Prefs);
  protected readonly auth = inject(AuthService);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly currencies = ['EUR', 'USD'] as const;
  protected readonly themes = ['system', 'light', 'dark'] as const;
  protected readonly icons = PAGE_ICONS;
  protected readonly themeIcon = {
    system: 'lucideMonitor',
    light: 'lucideSun',
    dark: 'lucideMoon',
  };
  protected readonly notifications = inject(Notifications);
  protected readonly browserSupported = 'Notification' in window;
  protected readonly blocked = signal(false);
  protected readonly current = signal('');
  protected readonly next = signal('');

  protected async setBrowserAlerts(on: boolean) {
    this.blocked.set(!(await this.notifications.setBrowserAlerts(on)));
  }

  private readonly http = inject(HttpClient);
  private readonly events = inject(DataEvents);
  protected readonly resetOpen = signal(false);
  protected readonly resetWord = signal('');
  protected readonly resetting = signal(false);
  protected readonly confirmWord = () => (this.prefs.lang() === 'pt-PT' ? 'APAGAR' : 'DELETE');

  protected async reset() {
    this.resetting.set(true);
    try {
      await firstValueFrom(this.http.post('/api/data/reset', { confirm: 'reset-all-data' }));
      this.resetOpen.set(false);
      this.resetWord.set('');
      this.events.bump();
      this.toasts.show(this.i18n.instant('settings.reset.done'));
    } catch (err) {
      this.toasts.error(err);
    } finally {
      this.resetting.set(false);
    }
  }

  protected async changePassword() {
    try {
      await this.auth.changePassword(this.current(), this.next());
      this.toasts.show(this.i18n.instant('settings.passwordChanged'));
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
