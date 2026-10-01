import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { StatusBadgeComponent } from '../../shared/status-badge';

@Component({
  selector: 'app-settings',
  // Narrow content, centred in the main area like a document rather than pinned to the left.
  host: { class: 'mx-auto block w-full max-w-3xl' },
  imports: [
    NgIcon,
    PageHeaderComponent,
    StatusBadgeComponent,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
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
        </div>
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
    </div>
  `,
})
export class SettingsComponent {
  protected readonly prefs = inject(Prefs);
  protected readonly auth = inject(AuthService);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly themes = ['system', 'light', 'dark'] as const;
  protected readonly icons = PAGE_ICONS;
  protected readonly themeIcon = {
    system: 'lucideMonitor',
    light: 'lucideSun',
    dark: 'lucideMoon',
  };
  protected readonly current = signal('');
  protected readonly next = signal('');

  protected async changePassword() {
    try {
      await this.auth.changePassword(this.current(), this.next());
      this.toasts.show(this.i18n.instant('settings.passwordChanged'));
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
