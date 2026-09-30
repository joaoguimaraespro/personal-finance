import { linkedSignal, ResourceRef } from '@angular/core';
import { rxResource, RxResourceOptions } from '@angular/core/rxjs-interop';

/**
 * `rxResource` that keeps showing the last loaded value while it reloads.
 *
 * A plain resource clears `value()` whenever its params change. Views re-fetch after every mutation and
 * while a sync runs (params include `DataEvents.version`), so the page blanked and redrew each time.
 * Here the previous data stays on screen until the new response arrives; `isLoading()` still reports the
 * request, so views can show a quiet indicator instead.
 */
export function liveResource<T, R>(opts: RxResourceOptions<T, R>): ResourceRef<T | undefined> {
  const res = rxResource(opts);
  const value = linkedSignal<T | undefined, T | undefined>({
    source: () => (res.status() === 'error' ? undefined : res.value()),
    computation: (next, prev) => (next === undefined && res.isLoading() ? prev?.value : next),
  });
  const hasValue = () => value() !== undefined;
  return new Proxy(res, {
    get(target, prop) {
      if (prop === 'value') return value;
      if (prop === 'hasValue') return hasValue;
      const member = Reflect.get(target, prop, target);
      return typeof member === 'function' ? member.bind(target) : member;
    },
  });
}
