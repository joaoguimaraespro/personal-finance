import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth';
import { problemMessage } from '../../core/toast';
import { AuthLayoutComponent } from './auth-layout';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';

@Component({
  selector: 'app-login',
  imports: [HlmInputImports, HlmButtonImports, AuthLayoutComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-auth-layout
      [title]="'auth.signIn' | translate"
      [subtitle]="'auth.privateNotice' | translate"
    >
      @if (step() === 'password') {
        <form class="space-y-4" (submit)="$event.preventDefault(); signIn()">
          <div>
            <label class="label" for="email">{{ 'auth.email' | translate }}</label>
            <input
              id="email"
              hlmInput
              type="email"
              autocomplete="username"
              required
              [value]="email()"
              (input)="email.set($any($event.target).value)"
            />
          </div>
          <div>
            <label class="label" for="password">{{ 'auth.password' | translate }}</label>
            <input
              id="password"
              hlmInput
              type="password"
              autocomplete="current-password"
              required
              (input)="password.set($any($event.target).value)"
            />
          </div>
          <label class="flex items-center gap-2 text-sm text-muted-foreground">
            <input type="checkbox" (change)="remember.set($any($event.target).checked)" />
            {{ 'auth.remember' | translate }}
          </label>
          @if (error()) {
            <p class="text-sm text-rose-600">{{ error() }}</p>
          }
          <button hlmBtn class="w-full" [disabled]="busy()">
            {{ 'auth.continue' | translate }}
          </button>
        </form>
      } @else {
        <form class="space-y-4" (submit)="$event.preventDefault(); verify()">
          <p class="text-sm text-muted-foreground">
            {{ (useRecovery() ? 'auth.recoveryPrompt' : 'auth.codePrompt') | translate }}
          </p>
          <input
            hlmInput
            class="num text-center h-12 text-2xl md:text-2xl tracking-[0.4em]"
            [attr.inputmode]="useRecovery() ? 'text' : 'numeric'"
            autocomplete="one-time-code"
            autofocus
            [attr.maxlength]="useRecovery() ? 20 : 6"
            (input)="code.set($any($event.target).value)"
          />
          @if (error()) {
            <p class="text-sm text-rose-600">{{ error() }}</p>
          }
          <button hlmBtn class="w-full" [disabled]="busy()">{{ 'auth.verify' | translate }}</button>
          <button
            type="button"
            hlmBtn
            variant="ghost"
            size="sm"
            class="w-full"
            (click)="useRecovery.set(!useRecovery())"
          >
            {{ (useRecovery() ? 'auth.useCode' : 'auth.useRecovery') | translate }}
          </button>
        </form>
      }
    </app-auth-layout>
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly i18n = inject(TranslateService);

  protected readonly step = signal<'password' | 'mfa'>('password');
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly remember = signal(false);
  protected readonly code = signal('');
  protected readonly useRecovery = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async signIn() {
    await this.run(async () => {
      const status = await this.auth.login(this.email(), this.password(), this.remember());
      if (status === 'mfa_required') this.step.set('mfa');
      else await this.router.navigateByUrl('/mfa-setup');
    });
  }

  protected async verify() {
    await this.run(async () => {
      const value = this.code().trim();
      await this.auth.verifyMfa(
        this.useRecovery() ? null : value,
        this.useRecovery() ? value : null,
        this.remember(),
      );
      await this.router.navigateByUrl('/dashboard');
    });
  }

  private async run(action: () => Promise<void>) {
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
    } catch (err) {
      const status = (err as { status?: number }).status;
      this.error.set(status === 429 ? this.i18n.instant('auth.lockedOut') : problemMessage(err));
    } finally {
      this.busy.set(false);
    }
  }
}
