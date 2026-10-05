import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { Injectable, NgZone, computed, effect, inject, signal, untracked } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { Observable, firstValueFrom } from 'rxjs';
import { BACKGROUND } from './activity';
import { Api } from './api';
import { DataEvents } from './data-events';
import { Toasts } from './toast';

export type NotificationKind =
  | 'RecurringDue'
  | 'InterestToReconcile'
  | 'BrokerAttention'
  | 'BudgetOver'
  | 'BudgetNear'
  | 'GoalReached'
  | 'AiWrites';

export type NotificationSeverity = 'Info' | 'Warning' | 'Error';

export type NotificationAction = 'confirm' | 'skip';

/** One thing that needs the owner's attention; computed by the server, gone once resolved. */
export interface NotificationItem {
  id: string;
  kind: NotificationKind;
  severity: NotificationSeverity;
  date: string;
  link: string;
  targetId: string | null;
  args: Record<string, string | number | boolean | null>;
  actions: NotificationAction[];
}

export interface NotificationsResponse {
  items: NotificationItem[];
  partial: boolean;
  generatedAtUtc: string;
}

/** Re-check this often while the app is open (the server only reads; it is cheap). */
export const POLL_EVERY_MS = 3 * 60_000;
const SEEN_KEY = 'pf.notifications.seen';
const BROWSER_KEY = 'pf.notifications.browser';
/** Upper bound for the remembered ids; resolved items are pruned anyway. */
const MAX_SEEN = 500;

/** Items the owner has not seen yet. Pure, so the badge logic is testable. */
export function unseenItems(items: readonly NotificationItem[], seen: ReadonlySet<string>) {
  return items.filter((i) => !seen.has(i.id));
}

/**
 * Seen ids that still matter: only ids of items that still exist. A resolved item's id is forgotten, so the
 * stored list never grows without bound and a situation that comes back later counts as new.
 */
export function pruneSeen(seen: ReadonlySet<string>, items: readonly NotificationItem[]): Set<string> {
  const current = new Set(items.map((i) => i.id));
  return new Set([...seen].filter((id) => current.has(id)).slice(-MAX_SEEN));
}

/** Items that appeared since the previous load (none on the first load: nothing "appeared", it was there). */
export function arrivals(
  previous: ReadonlySet<string> | null,
  items: readonly NotificationItem[],
): NotificationItem[] {
  return previous ? items.filter((i) => !previous.has(i.id)) : [];
}

function readSeen(): Set<string> {
  try {
    const raw = localStorage.getItem(SEEN_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    return new Set(Array.isArray(parsed) ? parsed.filter((x) => typeof x === 'string') : []);
  } catch {
    return new Set();
  }
}

function store(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage unavailable (private mode): the badge simply resets on reload.
  }
}

/**
 * The notification centre's state: the server's list, which items the owner has already seen (ids only, in
 * localStorage — no financial data), and inline actions that update the list at once. Refreshes after every
 * mutation (DataEvents), when the window regains focus and every few minutes, always as background requests so
 * the global loading bar stays quiet.
 */
@Injectable({ providedIn: 'root' })
export class Notifications {
  private readonly http = inject(HttpClient);
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly zone = inject(NgZone);

  readonly items = signal<NotificationItem[]>([]);
  readonly loaded = signal(false);
  readonly seen = signal<ReadonlySet<string>>(readSeen());
  /** Ids with an inline action in flight (buttons disabled). */
  readonly busy = signal<ReadonlySet<string>>(new Set());
  readonly unseen = computed(() => unseenItems(this.items(), this.seen()));
  readonly unseenCount = computed(() => this.unseen().length);
  readonly hasError = computed(() => this.items().some((i) => i.severity === 'Error'));
  /** Opt-in: a system notification when a new item arrives while the tab is open but not focused. */
  readonly browserAlerts = signal(this.readBrowserPref());

  private running = false;
  private timer: ReturnType<typeof setInterval> | undefined;
  private known: Set<string> | null = null;
  private inFlight = false;
  private queued = false;
  private readonly background = { context: new HttpContext().set(BACKGROUND, true) };
  private readonly onFocus = () => this.zone.run(() => void this.refresh());

  constructor() {
    // Every mutation anywhere (and the session monitor's return-to-tab bump) re-checks the list.
    let version = this.events.version();
    effect(() => {
      const next = this.events.version();
      if (next === version) return; // the effect's first run is not a change
      version = next;
      untracked(() => {
        if (this.running) void this.refresh();
      });
    });
    effect(() => store(BROWSER_KEY, this.browserAlerts() ? '1' : '0'));
  }

  start() {
    if (this.running) return;
    this.running = true;
    this.zone.runOutsideAngular(() => {
      window.addEventListener('focus', this.onFocus);
      this.timer = setInterval(this.onFocus, POLL_EVERY_MS);
    });
    void this.refresh();
  }

  stop() {
    this.running = false;
    clearInterval(this.timer);
    this.timer = undefined;
    window.removeEventListener('focus', this.onFocus);
    this.known = null;
  }

  async refresh() {
    if (this.inFlight) {
      this.queued = true;
      return;
    }
    this.inFlight = true;
    try {
      const res = await firstValueFrom(
        this.http.get<NotificationsResponse>('/api/notifications', this.background),
      );
      this.apply(res.items);
    } catch (err) {
      // Offline or signed out: keep what is shown; the session monitor handles 401s.
      if (!(err instanceof HttpErrorResponse)) throw err;
    } finally {
      this.inFlight = false;
      if (this.queued) {
        this.queued = false;
        void this.refresh();
      }
    }
  }

  /** Opening the centre counts as seeing everything in it. */
  markAllSeen() {
    const all = new Set([...this.seen(), ...this.items().map((i) => i.id)]);
    this.saveSeen(pruneSeen(all, this.items()));
  }

  /** Confirm or skip a recurring item, or confirm an interest estimate, straight from the list. */
  async act(item: NotificationItem, action: NotificationAction) {
    if (!item.targetId || this.busy().has(item.id)) return;
    const request: Observable<unknown> | null =
      item.kind === 'RecurringDue'
        ? action === 'confirm'
          ? this.api.confirmExpected(item.targetId)
          : this.api.skipExpected(item.targetId)
        : item.kind === 'InterestToReconcile' && action === 'confirm'
          ? this.api.reconcileInterest(item.targetId)
          : null;
    if (!request) return;

    const before = this.items();
    this.busy.update((s) => new Set(s).add(item.id));
    // Optimistic: the item leaves the list at once and comes back only if the server refuses.
    this.items.set(before.filter((i) => i.id !== item.id));
    try {
      await firstValueFrom(request);
      this.toasts.show(
        this.i18n.instant(action === 'skip' ? 'notifications.skipped' : 'recurring.confirmed'),
      );
      this.events.bump();
    } catch (err) {
      this.items.set(before);
      this.toasts.error(err);
    } finally {
      this.busy.update((s) => {
        const next = new Set(s);
        next.delete(item.id);
        return next;
      });
    }
  }

  /** Turning alerts on asks the browser for permission; refusing leaves them off. */
  async setBrowserAlerts(on: boolean): Promise<boolean> {
    if (!on) {
      this.browserAlerts.set(false);
      return true;
    }
    if (!('Notification' in window)) return false;
    const permission =
      Notification.permission === 'default'
        ? await Notification.requestPermission()
        : Notification.permission;
    this.browserAlerts.set(permission === 'granted');
    return permission === 'granted';
  }

  /** Text for one item: [title, detail]. Shared by the list and the browser alert. */
  describe(item: NotificationItem, format: (key: string, params?: object) => string): [string, string] {
    const a = item.args;
    switch (item.kind) {
      case 'RecurringDue':
        return [String(a['name'] ?? ''), format(a['overdue'] ? 'notifications.overdue' : 'notifications.dueToday')];
      case 'InterestToReconcile':
        return [
          format('notifications.interestTitle', { account: a['account'] }),
          format('notifications.interestDetail'),
        ];
      case 'BrokerAttention':
        return [
          format(`notifications.broker.${a['reason']}`, { name: a['name'] }),
          format('notifications.brokerDetail'),
        ];
      case 'BudgetOver':
        return [format('notifications.budgetOver', { category: a['category'] }), ''];
      case 'BudgetNear':
        return [format('notifications.budgetNear', { category: a['category'] }), ''];
      case 'GoalReached':
        return [format('notifications.goalReached', { name: a['name'] }), format('notifications.goalDetail')];
      case 'AiWrites':
        return [
          format('notifications.aiWrites', { client: a['client'], count: a['count'] }),
          format('notifications.aiDetail'),
        ];
    }
  }

  private apply(items: NotificationItem[]) {
    const fresh = arrivals(this.known, items).filter((i) => !this.seen().has(i.id));
    this.known = new Set(items.map((i) => i.id));
    this.items.set(items);
    this.loaded.set(true);
    // Forget seen ids of resolved items.
    const pruned = pruneSeen(this.seen(), items);
    if (pruned.size !== this.seen().size) this.saveSeen(pruned);
    if (fresh.length) this.alert(fresh);
  }

  private alert(fresh: NotificationItem[]) {
    if (!this.browserAlerts() || !('Notification' in window)) return;
    if (Notification.permission !== 'granted' || document.hasFocus()) return;
    const format = (key: string, params?: object) => this.i18n.instant(key, params);
    const [title, body] =
      fresh.length === 1
        ? this.describe(fresh[0], format)
        : [format('notifications.manyNew', { count: fresh.length }), ''];
    try {
      // Same tag: a burst replaces the previous alert instead of stacking.
      const n = new Notification(title, { body, tag: 'pf-notifications', icon: '/favicon.svg' });
      n.onclick = () => window.focus();
    } catch {
      // Some browsers only allow notifications from a service worker; the bell still shows everything.
    }
  }

  private saveSeen(seen: Set<string>) {
    this.seen.set(seen);
    store(SEEN_KEY, JSON.stringify([...seen]));
  }

  private readBrowserPref(): boolean {
    try {
      return (
        localStorage.getItem(BROWSER_KEY) === '1' &&
        'Notification' in window &&
        Notification.permission === 'granted'
      );
    } catch {
      return false;
    }
  }
}
