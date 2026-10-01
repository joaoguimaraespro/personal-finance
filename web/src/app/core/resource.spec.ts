import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { liveResource } from './resource';

describe('liveResource', () => {
  function setup() {
    const version = signal(0);
    const responses: Subject<string>[] = [];
    const res = TestBed.runInInjectionContext(() =>
      liveResource({
        params: () => version(),
        stream: () => {
          const s = new Subject<string>();
          responses.push(s);
          return s;
        },
      }),
    );
    // The loader resolves on a microtask; tick, let it settle, tick again.
    const flush = async () => {
      TestBed.inject(ApplicationRef).tick();
      await new Promise((r) => setTimeout(r));
      TestBed.inject(ApplicationRef).tick();
    };
    return { version, responses, res, flush };
  }

  it('keeps the previous value while new params load', async () => {
    const { version, responses, res, flush } = setup();
    await flush();
    expect(res.value()).toBeUndefined();
    expect(res.hasValue()).toBe(false);

    responses[0].next('first');
    await flush();
    expect(res.value()).toBe('first');

    version.set(1);
    await flush();
    expect(res.isLoading()).toBe(true);
    expect(res.value()).toBe('first');
    expect(res.hasValue()).toBe(true);

    responses[1].next('second');
    await flush();
    expect(res.value()).toBe('second');
    expect(res.isLoading()).toBe(false);
  });

  it('clears the value when the request fails', async () => {
    const { version, responses, res, flush } = setup();
    await flush();
    responses[0].next('first');
    await flush();
    version.set(1);
    await flush();
    responses[1].error(new Error('boom'));
    await flush();
    expect(res.status()).toBe('error');
    expect(res.value()).toBeUndefined();
  });
});
