import { ChangeDetectionStrategy, Component, ElementRef, effect, input, output, viewChild } from '@angular/core';

/** Native <dialog>: focus trapping, Esc to close and backdrop come from the platform. */
@Component({
  selector: 'app-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog
      #dialog
      class="m-auto w-[min(100%-2rem,var(--w))] rounded-2xl border border-slate-200 bg-white p-0 text-slate-900 shadow-2xl backdrop:bg-slate-950/50 backdrop:backdrop-blur-sm dark:border-slate-800 dark:bg-slate-900 dark:text-slate-100"
      [style.--w]="width()"
      (close)="closed.emit()"
      (cancel)="closed.emit()"
    >
      <div class="flex items-center justify-between border-b border-slate-100 px-5 py-4 dark:border-slate-800">
        <h2 class="text-base font-semibold">{{ title() }}</h2>
        <button class="btn btn-ghost !p-1.5" (click)="dialog.close()" aria-label="Close">✕</button>
      </div>
      <div class="max-h-[75vh] overflow-y-auto p-5">
        <ng-content />
      </div>
    </dialog>
  `,
})
export class ModalComponent {
  readonly open = input(false);
  readonly title = input('');
  readonly width = input('32rem');
  readonly closed = output<void>();
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const el = this.dialog().nativeElement;
      if (this.open() && !el.open) el.showModal();
      if (!this.open() && el.open) el.close();
    });
  }
}
