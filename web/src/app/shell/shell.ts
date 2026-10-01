import { ChangeDetectionStrategy, Component, HostListener, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBriefcase,
  lucideCalendarDays,
  lucideChartColumn,
  lucideChartPie,
  lucideDownload,
  lucideKeyRound,
  lucideLayoutDashboard,
  lucideLink,
  lucideList,
  lucideLogOut,
  lucideMenu,
  lucideMonitor,
  lucideMoon,
  lucidePlus,
  lucideRepeat,
  lucideSettings,
  lucideSparkles,
  lucideSun,
  lucideTags,
  lucideTarget,
  lucideTrendingUp,
  lucideUpload,
  lucideWallet,
} from '@ng-icons/lucide';
import { TranslatePipe } from '@ngx-translate/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmKbdImports } from '@spartan-ng/helm/kbd';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { Activity } from '../core/activity';
import { AuthService } from '../core/auth';
import { QuickAdd } from '../core/data-events';
import { Prefs } from '../core/prefs';
import { QuickAddComponent } from '../features/transactions/quick-add';
import { LogoComponent } from '../shared/logo';

interface NavItem {
  path: string;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-shell',
  imports: [
    LogoComponent,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    TranslatePipe,
    QuickAddComponent,
    NgIcon,
    HlmButtonImports,
    HlmKbdImports,
    HlmToggleGroupImports,
    HlmTooltipImports,
  ],
  providers: [
    provideIcons({
      lucideBriefcase,
      lucideCalendarDays,
      lucideChartColumn,
      lucideChartPie,
      lucideDownload,
      lucideKeyRound,
      lucideLayoutDashboard,
      lucideLink,
      lucideList,
      lucideLogOut,
      lucideMenu,
      lucideMonitor,
      lucideMoon,
      lucidePlus,
      lucideRepeat,
      lucideSettings,
      lucideSparkles,
      lucideSun,
      lucideTags,
      lucideTarget,
      lucideTrendingUp,
      lucideUpload,
      lucideWallet,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="loading-bar" [class.active]="activity.busy()" aria-hidden="true"></div>
    <div class="flex min-h-dvh">
      <aside
        class="bg-sidebar text-sidebar-foreground fixed inset-y-0 left-0 z-30 flex w-64 -translate-x-full flex-col border-r border-sidebar-border transition-transform lg:sticky lg:top-0 lg:h-dvh lg:translate-x-0"
        [class.translate-x-0]="menuOpen()"
      >
        <div class="flex h-16 items-center gap-2.5 px-5">
          <app-logo [size]="36" />
          <div class="leading-tight">
            <div class="text-sm font-semibold">Personal Finance</div>
            <div class="text-muted-foreground text-[11px]">self-hosted</div>
          </div>
        </div>
        <nav class="flex-1 space-y-5 overflow-y-auto px-3 py-3">
          @for (section of nav; track section.title) {
            <div>
              <p
                class="text-muted-foreground px-3 pb-1.5 text-[11px] font-semibold tracking-wider uppercase"
              >
                {{ section.title | translate }}
              </p>
              @for (item of section.items; track item.path) {
                <a
                  [routerLink]="item.path"
                  routerLinkActive="nav-active"
                  class="nav-link text-sidebar-foreground/80 hover:bg-sidebar-accent hover:text-sidebar-accent-foreground flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors"
                  (click)="menuOpen.set(false)"
                >
                  <ng-icon [name]="item.icon" class="text-base opacity-80" aria-hidden="true" />{{
                    item.label | translate
                  }}
                </a>
              }
            </div>
          }
        </nav>
      </aside>

      @if (menuOpen()) {
        <div class="fixed inset-0 z-20 bg-black/40 lg:hidden" (click)="menuOpen.set(false)"></div>
      }

      <div class="flex min-w-0 flex-1 flex-col">
        <header
          class="bg-background/80 sticky top-0 z-10 flex h-14 items-center gap-1.5 border-b px-3 backdrop-blur sm:h-16 sm:gap-2 sm:px-4 lg:px-8"
        >
          <button
            hlmBtn
            variant="ghost"
            size="icon"
            class="lg:hidden"
            (click)="menuOpen.set(true)"
            aria-label="Menu"
          >
            <ng-icon name="lucideMenu" />
          </button>
          <div class="flex-1"></div>
          <!-- Phones: icon-only actions so the header never overflows at 320–400px. -->
          <button hlmBtn (click)="quick.add()" [attr.aria-label]="'tx.add' | translate">
            <ng-icon name="lucidePlus" /><span class="hidden sm:inline">{{
              'tx.add' | translate
            }}</span>
            <kbd hlmKbd class="ml-1 hidden bg-black/15 text-inherit lg:inline-flex">N</kbd>
          </button>
          <hlm-toggle-group
            class="hidden sm:flex"
            type="single"
            variant="outline"
            size="sm"
            [value]="prefs.lang()"
            (valueChange)="$event && prefs.lang.set($any($event))"
            aria-label="Language"
          >
            <button hlmToggleGroupItem value="en">EN</button>
            <button hlmToggleGroupItem value="pt-PT">PT</button>
          </hlm-toggle-group>
          <button
            hlmBtn
            variant="ghost"
            size="icon"
            (click)="toggleTheme()"
            [attr.aria-label]="'settings.theme' | translate"
            [hlmTooltip]="'settings.theme' | translate"
          >
            <ng-icon
              [name]="
                prefs.theme() === 'dark'
                  ? 'lucideMoon'
                  : prefs.theme() === 'light'
                    ? 'lucideSun'
                    : 'lucideMonitor'
              "
            />
          </button>
          <button
            hlmBtn
            variant="ghost"
            size="icon"
            (click)="logout()"
            [attr.aria-label]="'auth.logout' | translate"
            [hlmTooltip]="'auth.logout' | translate"
          >
            <ng-icon name="lucideLogOut" />
          </button>
        </header>
        <main class="mx-auto w-full max-w-7xl min-w-0 flex-1 px-4 py-5 sm:px-6 sm:py-6 lg:px-8">
          <router-outlet />
        </main>
      </div>
    </div>
    <app-quick-add />
  `,
})
export class ShellComponent {
  protected readonly quick = inject(QuickAdd);
  protected readonly activity = inject(Activity);
  protected readonly prefs = inject(Prefs);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly menuOpen = signal(false);

  protected readonly nav: { title: string; items: NavItem[] }[] = [
    {
      title: 'nav.overview',
      items: [
        { path: '/dashboard', label: 'nav.dashboard', icon: 'lucideLayoutDashboard' },
        { path: '/monthly', label: 'nav.monthly', icon: 'lucideCalendarDays' },
        { path: '/annual', label: 'nav.annual', icon: 'lucideChartColumn' },
      ],
    },
    {
      title: 'nav.money',
      items: [
        { path: '/transactions', label: 'nav.transactions', icon: 'lucideList' },
        { path: '/recurring', label: 'nav.recurring', icon: 'lucideRepeat' },
        { path: '/budgets', label: 'nav.budgets', icon: 'lucideChartPie' },
        { path: '/goals', label: 'nav.goals', icon: 'lucideTarget' },
      ],
    },
    {
      title: 'nav.wealth',
      items: [
        { path: '/portfolio', label: 'nav.portfolio', icon: 'lucideBriefcase' },
        { path: '/net-worth', label: 'nav.netWorth', icon: 'lucideTrendingUp' },
        { path: '/assistant', label: 'nav.assistant', icon: 'lucideSparkles' },
      ],
    },
    {
      title: 'nav.setup',
      items: [
        { path: '/connections', label: 'nav.connections', icon: 'lucideLink' },
        { path: '/accounts', label: 'nav.accounts', icon: 'lucideWallet' },
        { path: '/categories', label: 'nav.categories', icon: 'lucideTags' },
        { path: '/import', label: 'nav.import', icon: 'lucideUpload' },
        { path: '/export', label: 'nav.export', icon: 'lucideDownload' },
        { path: '/ai', label: 'nav.ai', icon: 'lucideKeyRound' },
        { path: '/settings', label: 'nav.settings', icon: 'lucideSettings' },
      ],
    },
  ];

  /** "N" opens quick-add from anywhere, unless the user is typing in a field. */
  @HostListener('document:keydown', ['$event'])
  onKey(event: KeyboardEvent) {
    const target = event.target as HTMLElement;
    const typing =
      ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) || target.isContentEditable;
    if (
      !typing &&
      !event.ctrlKey &&
      !event.metaKey &&
      !event.altKey &&
      event.key.toLowerCase() === 'n'
    ) {
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
