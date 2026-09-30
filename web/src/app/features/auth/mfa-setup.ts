import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth';
import { problemMessage } from '../../core/toast';
import { AuthLayoutComponent } from './auth-layout';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { APP_ICONS } from '../../shared/icons';

/** MFA is mandatory: a password-only session can do nothing but finish this enrolment. */
@Component({
  selector: 'app-mfa-setup',
  imports: [NgIcon, HlmInputImports, HlmButtonImports, AuthLayoutComponent, TranslatePipe],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-auth-layout
      [title]="'auth.mfaTitle' | translate"
      [subtitle]="'auth.mfaSubtitle' | translate"
    >
      @if (recoveryCodes().length === 0) {
        <div class="space-y-4">
          @if (qr()) {
            <img
              class="mx-auto h-48 w-48 rounded-lg bg-white p-2"
              [src]="qr()"
              alt="QR code for your authenticator app"
            />
          }
          <p class="text-center text-xs text-muted-foreground">
            {{ 'auth.mfaManual' | translate }}
          </p>
          <p class="rounded-lg bg-muted p-2 text-center font-mono text-sm break-all">{{ key() }}</p>
          <form class="space-y-3" (submit)="$event.preventDefault(); enable()">
            <input
              hlmInput
              class="num text-center h-12 text-2xl md:text-2xl tracking-[0.4em]"
              inputmode="numeric"
              autocomplete="one-time-code"
              maxlength="6"
              (input)="code.set($any($event.target).value)"
            />
            @if (error()) {
              <p class="tone-neg flex items-center gap-1.5 text-sm" role="alert">
                <ng-icon name="lucideCircleAlert" class="shrink-0" aria-hidden="true" />{{
                  error()
                }}
              </p>
            }
            <button hlmBtn class="w-full" [disabled]="busy()">
              <ng-icon name="lucideShieldCheck" />{{ 'auth.enableMfa' | translate }}
            </button>
          </form>
        </div>
      } @else {
        <div class="space-y-4">
          <p class="text-sm text-muted-foreground">{{ 'auth.recoveryIntro' | translate }}</p>
          <ul class="grid grid-cols-2 gap-2 rounded-lg bg-muted p-3 font-mono text-sm">
            @for (c of recoveryCodes(); track c) {
              <li>{{ c }}</li>
            }
          </ul>
          <button hlmBtn class="w-full" (click)="done()">
            <ng-icon name="lucideCheck" />{{ 'auth.savedCodes' | translate }}
          </button>
        </div>
      }
    </app-auth-layout>
  `,
})
export class MfaSetupComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly key = signal('');
  protected readonly qr = signal('');
  protected readonly code = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly recoveryCodes = signal<string[]>([]);

  async ngOnInit() {
    try {
      const setup = await this.auth.mfaSetup();
      this.key.set(setup.sharedKey);
      // qrcode is CommonJS: the production bundle exposes it under `default`, the dev server does not.
      const mod = await import('qrcode');
      const { toDataURL } = mod.default ?? mod;
      this.qr.set(await toDataURL(setup.otpAuthUri, { margin: 1, width: 384 }));
    } catch (err) {
      this.error.set(problemMessage(err));
    }
  }

  protected async enable() {
    this.busy.set(true);
    this.error.set('');
    try {
      this.recoveryCodes.set(await this.auth.enableMfa(this.code().trim()));
    } catch (err) {
      this.error.set(problemMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected done() {
    void this.router.navigateByUrl('/dashboard');
  }
}
