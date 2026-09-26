import { Directive } from '@angular/core';
import { classes } from '@spartan-ng/helm/utils';

/**
 * Native <select> with the spartan/ui field look (same classes as hlm-native-select) and a chevron.
 * Native keeps the platform picker on mobile and works with the existing [value]/(change) bindings.
 */
@Directive({
  selector: 'select[uiSelect]',
  host: { 'data-slot': 'native-select' },
})
export class UiSelect {
  constructor() {
    classes(
      () =>
        'border-input dark:bg-input/30 dark:hover:bg-input/50 focus-visible:border-ring focus-visible:ring-ring/50 h-9 w-full min-w-0 cursor-pointer appearance-none rounded-md border bg-transparent py-1 ps-2.5 pe-8 text-sm shadow-xs transition-[color,box-shadow] outline-none focus-visible:ring-3 disabled:cursor-not-allowed disabled:opacity-50',
    );
  }
}
