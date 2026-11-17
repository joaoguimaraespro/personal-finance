import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AuthService } from './auth';
import { Me } from './models';

const signedIn: Me = {
  authenticated: true,
  setupRequired: false,
  email: 'owner@example.test',
  mfaEnabled: true,
  mfaSatisfied: true,
};

describe('AuthService.login', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  it('requests a fresh anti-forgery token before posting the password, even when a session is still remembered', async () => {
    // The login page calls this after the 12-hour ticket has died. The browser can still hold the
    // anti-forgery cookie minted while signed in; the POST is anonymous and that cookie no longer matches.
    auth.me.set(signedIn);

    const pending = auth.login('owner@example.test', 'long-enough-password', false);

    const token = http.expectOne('/api/auth/antiforgery');
    expect(token.request.method).toBe('GET');
    http.expectNone('/api/auth/login');
    token.flush(null);
    await Promise.resolve();

    const login = http.expectOne('/api/auth/login');
    expect(login.request.method).toBe('POST');
    expect(login.request.body).toEqual({
      email: 'owner@example.test',
      password: 'long-enough-password',
      rememberMe: false,
    });
    login.flush({ status: 'mfa_required' });
    await Promise.resolve();

    // Sign-in rotates the pair onto the new identity.
    http.expectOne('/api/auth/antiforgery').flush(null);
    await Promise.resolve();
    http.expectOne('/api/auth/me').flush(signedIn);

    await expect(pending).resolves.toBe('mfa_required');
  });
});
