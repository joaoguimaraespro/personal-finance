import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { BACKGROUND } from './activity';
import { DataEvents } from './data-events';
import {
  NotificationItem,
  Notifications,
  NotificationsResponse,
  allocationPrefill,
  arrivals,
  pruneSeen,
  unseenItems,
} from './notifications';

function item(id: string, over: Partial<NotificationItem> = {}): NotificationItem {
  return {
    id,
    kind: 'RecurringDue',
    severity: 'Info',
    date: '2026-10-05',
    link: '/recurring',
    targetId: id.split(':')[1] ?? null,
    args: { name: id, amount: 10, currency: 'EUR', overdue: false },
    actions: ['confirm', 'skip'],
    ...over,
  };
}

describe('notification seen logic', () => {
  const a = item('recurring:a');
  const b = item('recurring:b');

  it('counts only items not seen yet', () => {
    expect(unseenItems([a, b], new Set()).length).toBe(2);
    expect(unseenItems([a, b], new Set(['recurring:a'])).map((i) => i.id)).toEqual(['recurring:b']);
  });

  it('forgets seen ids of resolved items so a recurrence counts as new', () => {
    const pruned = pruneSeen(new Set(['recurring:a', 'recurring:gone']), [a, b]);
    expect([...pruned]).toEqual(['recurring:a']);
  });

  it('reports arrivals only after the first load', () => {
    expect(arrivals(null, [a, b])).toEqual([]);
    expect(arrivals(new Set(['recurring:a']), [a, b])).toEqual([b]);
  });
});

describe('allocation prefill', () => {
  const due = (month: string, investment: boolean) =>
    item(`allocation:${month}:b1:Partial`, {
      kind: 'AllocationDue',
      targetId: 'b1',
      args: { bucket: 'ETFs', investment, month, remaining: 179.6, target: 300, partial: true },
      actions: ['record', 'skip'],
    });

  it('records what is left of an investment bucket today', () => {
    expect(
      allocationPrefill(due('2026-10', true), 'Allocation: ETFs', new Date(2026, 9, 6)),
    ).toEqual({
      type: 'InvestmentContribution',
      amount: 179.6,
      bucketId: 'b1',
      occurredOn: '2026-10-06',
      description: 'Allocation: ETFs',
    });
  });

  it("dates a past month's savings on its last day", () => {
    const p = allocationPrefill(due('2026-09', false), 'x', new Date(2026, 9, 6));
    expect(p.type).toBe('Savings');
    expect(p.occurredOn).toBe('2026-09-30');
  });
});

describe('Notifications', () => {
  let http: HttpTestingController;
  let service: Notifications;

  const respond = (items: NotificationItem[]) => {
    const req = http.expectOne('/api/notifications');
    // Background polling never flashes the global loading bar.
    expect(req.request.context.get(BACKGROUND)).toBe(true);
    req.flush({ items, partial: false, generatedAtUtc: '' } satisfies NotificationsResponse);
  };
  const settle = () => new Promise((r) => setTimeout(r));

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(Notifications);
  });

  afterEach(() => {
    service.stop();
    http.verify();
  });

  it('badges unseen items, clears on open and remembers it across reloads', async () => {
    service.start();
    respond([
      item('recurring:a'),
      item('broker:x:needsAttention:0', {
        severity: 'Error',
        kind: 'BrokerAttention',
        actions: [],
      }),
    ]);
    await settle();
    expect(service.unseenCount()).toBe(2);
    expect(service.hasError()).toBe(true);

    service.markAllSeen();
    expect(service.unseenCount()).toBe(0);
    expect(JSON.parse(localStorage.getItem('pf.notifications.seen')!)).toHaveLength(2);

    // A new item arrives: only it counts.
    void service.refresh();
    respond([item('recurring:a'), item('recurring:b')]);
    await settle();
    expect(service.unseen().map((i) => i.id)).toEqual(['recurring:b']);
    expect(service.hasError()).toBe(false);
    // The resolved broker item is forgotten.
    expect(JSON.parse(localStorage.getItem('pf.notifications.seen')!)).toEqual(['recurring:a']);
  });

  it('refreshes when data changes', async () => {
    service.start();
    respond([]);
    await settle();
    TestBed.inject(DataEvents).bump();
    TestBed.inject(ApplicationRef).tick();
    respond([item('recurring:a')]);
    await settle();
    expect(service.items().length).toBe(1);
  });

  it('confirms inline: the item leaves at once and data views refresh', async () => {
    service.start();
    respond([item('recurring:a'), item('recurring:b')]);
    await settle();
    const version = TestBed.inject(DataEvents).version();

    const done = service.act(service.items()[0], 'confirm');
    expect(service.items().map((i) => i.id)).toEqual(['recurring:b']);
    const req = http.expectOne('/api/expected/a/confirm');
    expect(req.request.method).toBe('POST');
    req.flush({ transactionId: 't' });
    await done;
    expect(TestBed.inject(DataEvents).version()).toBe(version + 1);
    TestBed.inject(ApplicationRef).tick();
    respond([item('recurring:b')]);
  });

  it('puts the item back when the server refuses', async () => {
    service.start();
    respond([item('recurring:a')]);
    await settle();

    const done = service.act(service.items()[0], 'skip');
    expect(service.items()).toEqual([]);
    http
      .expectOne('/api/expected/a/skip')
      .flush({ title: 'Already resolved' }, { status: 409, statusText: 'Conflict' });
    await done;
    expect(service.items().map((i) => i.id)).toEqual(['recurring:a']);
  });

  it('confirms an interest estimate through reconcile', async () => {
    service.start();
    respond([item('interest:m1', { kind: 'InterestToReconcile', actions: ['confirm'] })]);
    await settle();
    const done = service.act(service.items()[0], 'confirm');
    http.expectOne('/api/interest/m1/reconcile').flush({ transactionId: null });
    await done;
    TestBed.inject(ApplicationRef).tick();
    respond([]);
  });
});
