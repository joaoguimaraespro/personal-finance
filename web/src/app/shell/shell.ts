import { ChangeDetectionStrategy, Component, HostListener, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../core/auth';
import { QuickAdd } from '../core/data-events';
import { Prefs } from '../core/prefs';
import { QuickAddComponent } from '../features/transactions/quick-add';

interface NavItem {
  path: string;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, QuickAddComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-dvh">
      <aside
        class="fixed inset-y-0 left-0 z-30 w-64 -translate-x-full border-r border-slate-200 bg-white transition-transform lg:static lg:translate-x-0 dark:border-slate-800 dark:bg-slate-900"
        [class.translate-x-0]="menuOpen()"
      >
        <div class="flex h-16 items-center gap-2 px-5">
          <div class="grid h-8 w-8 place-items-center rounded-lg bg-brand-600 text-sm font-bold text-white">€</div>
          <span class="font-semibold">Personal Finance</span>
        </div>
        <nav class="space-y-6 px-3 py-2">
          @for (section of nav; track section.title) {
            <div>
              <p class="px-3 pb-1 text-[11px] font-semibold tracking-wider text-slate-400 uppercase">{{ section.title | translate }}</p>
              @for (item of section.items; track item.path) {
                <a
                  [routerLink]="item.path"
                  routerLinkActive="!bg-brand-50 !text-brand-700 dark:!bg-brand-500/15 dark:!text-brand-100"
                  class="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800"
                  (click)="menuOpen.set(false)"
                >
                  <span class="w-4 text-center" aria-hidden="true">{{ item.icon }}</span>{{ item.label | translate }}
                </a>
              }
            </div>
          }
        </nav>
      </aside>

      @if (menuOpen()) {
        <div class="fixed inset-0 z-20 bg-slate-950/40 lg:hidden" (click)="menuOpen.set(false)"></div>
      }

      <div class="flex min-w-0 flex-1 flex-col">
        <header class="sticky top-0 z-10 flex h-16 items-center gap-3 border-b border-slate-200 bg-white/80 px-4 backdrop-blur lg:px-8 dark:border-slate-800 dark:bg-slate-950/80">
          <button class="btn btn-ghost !p-2 lg:hidden" (click)="menuOpen.set(true)" aria-label="Menu">☰</button>
          <div class="flex-1"></div>
          <button class="btn btn-primary" (click)="quick.add()">
            <span aria-hidden="true">＋</span>{{ 'tx.add' | translate }}
            <kbd class="ml-1 hidden rounded bg-white/20 px-1.5 text-[10px] sm:inline">N</kbd>
          </button>
          <select class="input !w-auto !py-1.5" [value]="prefs.lang()" (change)="prefs.lang.set($any($event.target).value)" aria-label="Language">
            <option value="en">EN</option>
            <option value="pt-PT">PT</option>
          </select>
          <button class="btn btn-ghost !p-2" (click)="toggleTheme()" [attr.aria-label]="'settings.theme' | translate">
            {{ prefs.theme() === 'dark' ? '☾' : prefs.theme() === 'light' ? '☀' : '◐' }}
          </button>
          <button class="btn btn-ghost !px-2 text-xs" (click)="logout()">{{ 'auth.logout' | translate }}</button>
        </header>
        <main class="mx-auto w-full max-w-7xl flex-1 px-4 py-6 lg:px-8">
          <router-outlet />
        </main>
      </div>
    </div>
    <app-quick-add />
  `,
})
export class ShellComponent {
  protected readonly quick = inject(QuickAdd);
  protected readonly prefs = inject(Prefs);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly menuOpen = signal(false);

  protected readonly nav: { title: string; items: NavItem[] }[] = [
    {
      title: 'nav.overview',
      items: [
        { path: '/dashboard', label: 'nav.dashboard', icon: '◧' },
        { path: '/monthly', label: 'nav.monthly', icon: '▦' },
        { path: '/annual', label: 'nav.annual', icon: '▤' },
      ],
    },
    {
      title: 'nav.money',
      items: [
        { path: '/transactions', label: 'nav.transactions', icon: '≡' },
        { path: '/recurring', label: 'nav.recurring', icon: '↻' },
        { path: '/budgets', label: 'nav.budgets', icon: '◔' },
        { path: '/goals', label: 'nav.goals', icon: '◎' },
      ],
    },
    {
      title: 'nav.wealth',
      items: [
        { path: '/portfolio', label: 'nav.portfolio', icon: '◆' },
        { path: '/net-worth', label: 'nav.netWorth', icon: '△' },
      ],
    },
    {
      title: 'nav.setup',
      items: [
        { path: '/connections', label: 'nav.connections', icon: '⇄' },
        { path: '/accounts', label: 'nav.accounts', icon: '▭' },
        { path: '/categories', label: 'nav.categories', icon: '#' },
        { path: '/import', label: 'nav.import', icon: '⇪' },
        { path: '/export', label: 'nav.export', icon: '⇩' },
        { path: '/settings', label: 'nav.settings', icon: '⚙' },
      ],
    },
  ];

  /** "N" opens quick-add from anywhere, unless the user is typing in a field. */
  @HostListener('document:keydown', ['$event'])
  onKey(event: KeyboardEvent) {
    const target = event.target as HTMLElement;
    const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) || target.isContentEditable;
    if (!typing && !event.ctrlKey && !event.metaKey && !event.altKey && event.key.toLowerCase() === 'n') {
      event.preventDefault();
      this.quick.add();
    }
  }

  protected toggleTheme() {
    const order = ['system', 'light', 'dark'] as const;
    this.prefs.theme.set(order[(order.indexOf(this.prefs.theme()) + 1) % order.length]);
  }

  protected async logout() {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
