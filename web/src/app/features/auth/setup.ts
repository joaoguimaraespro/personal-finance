import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth';
import { problemMessage } from '../../core/toast';
import { AuthLayoutComponent } from './auth-layout';

/** First run only: creates the single owner account. Needs the setup token from the server's .env file. */
@Component({
  selector: 'app-setup',
  imports: [AuthLayoutComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-auth-layout [title]="'auth.setupTitle' | translate" [subtitle]="'auth.setupSubtitle' | translate">
      <form class="space-y-4" (submit)="$event.preventDefault(); create()">
        <div>
          <label class="label" for="token">{{ 'auth.setupToken' | translate }}</label>
          <input id="token" class="input font-mono" autocomplete="off" required (input)="token.set($any($event.target).value)" />
        </div>
        <div>
          <label class="label" for="email">{{ 'auth.email' | translate }}</label>
          <input id="email" class="input" type="email" autocomplete="username" required (input)="email.set($any($event.target).value)" />
        </div>
        <div>
          <label class="label" for="password">{{ 'auth.newPassword' | translate }}</label>
          <input id="password" class="input" type="password" autocomplete="new-password" minlength="12" required (input)="password.set($any($event.target).value)" />
          <p class="mt-1 text-xs text-slate-400">{{ 'auth.passwordHint' | translate }}</p>
        </div>
        @if (error()) { <p class="text-sm text-rose-600">{{ error() }}</p> }
        <button class="btn btn-primary w-full" [disabled]="busy()">{{ 'auth.createOwner' | translate }}</button>
      </form>
    </app-auth-layout>
  `,
})
export class SetupComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly token = signal('');
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async create() {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.setup(this.email(), this.password(), this.token());
      await this.auth.login(this.email(), this.password(), false);
      await this.router.navigateByUrl('/mfa-setup');
    } catch (err) {
      this.error.set(problemMessage(err));
    } finally {
      this.busy.set(false);
    }
  }
}
