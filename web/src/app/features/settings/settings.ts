import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';

@Component({
  selector: 'app-settings',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="mb-6 text-2xl font-semibold tracking-tight">{{ 'nav.settings' | translate }}</h1>
    <div class="grid max-w-3xl gap-4">
      <section class="card space-y-4">
        <h2 class="card-title">{{ 'settings.appearance' | translate }}</h2>
        <div class="flex flex-wrap gap-6">
          <div>
            <span class="label">{{ 'settings.language' | translate }}</span>
            <div class="segmented">
              <button [class.active]="prefs.lang() === 'en'" (click)="prefs.lang.set('en')">English</button>
              <button [class.active]="prefs.lang() === 'pt-PT'" (click)="prefs.lang.set('pt-PT')">Português</button>
            </div>
          </div>
          <div>
            <span class="label">{{ 'settings.theme' | translate }}</span>
            <div class="segmented">
              @for (t of themes; track t) {
                <button [class.active]="prefs.theme() === t" (click)="prefs.theme.set(t)">{{ 'settings.themes.' + t | translate }}</button>
              }
            </div>
          </div>
        </div>
      </section>

      <section class="card">
        <h2 class="card-title">{{ 'settings.security' | translate }}</h2>
        <p class="mb-4 text-sm text-slate-500">{{ 'settings.signedInAs' | translate: { email: auth.me()?.email } }} · {{ 'settings.mfaOn' | translate }}</p>
        <form class="grid gap-3 sm:grid-cols-2" (submit)="$event.preventDefault(); changePassword()">
          <div>
            <label class="label" for="s-cur">{{ 'settings.currentPassword' | translate }}</label>
            <input id="s-cur" class="input" type="password" autocomplete="current-password" (input)="current.set($any($event.target).value)" />
          </div>
          <div>
            <label class="label" for="s-new">{{ 'auth.newPassword' | translate }}</label>
            <input id="s-new" class="input" type="password" autocomplete="new-password" minlength="12" (input)="next.set($any($event.target).value)" />
          </div>
          <div class="sm:col-span-2"><button class="btn">{{ 'settings.changePassword' | translate }}</button></div>
        </form>
      </section>

      <section class="card text-sm text-slate-500">
        <h2 class="card-title">{{ 'settings.privacy' | translate }}</h2>
        <p>{{ 'settings.privacyText' | translate }}</p>
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
