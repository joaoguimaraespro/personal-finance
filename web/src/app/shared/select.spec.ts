import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SelectComponent, SelectOption } from './select';

@Component({
  imports: [SelectComponent],
  template: `
    <app-select
      inputId="s"
      [options]="options"
      [value]="value()"
      [resetOnPick]="reset()"
      (valueChange)="picked.push($event); reset() || value.set($event)"
    />
  `,
})
class Host {
  readonly options: SelectOption[] = [
    { value: '', label: 'All' },
    { value: 'a', label: 'Alpha' },
    { value: 'b', label: 'Beta' },
  ];
  readonly value = signal('a');
  readonly reset = signal(false);
  readonly picked: string[] = [];
}

async function setup(reset = false) {
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.reset.set(reset);
  fixture.detectChanges();
  await fixture.whenStable();
  const trigger = () => fixture.nativeElement.querySelector('#s') as HTMLButtonElement;
  const choose = async (label: string) => {
    trigger().click();
    fixture.detectChanges();
    await fixture.whenStable();
    const item = [...document.querySelectorAll<HTMLElement>('[role=option]')].find(
      (o) => o.textContent?.trim() === label,
    )!;
    item.click();
    fixture.detectChanges();
    await fixture.whenStable();
  };
  return { fixture, trigger, choose };
}

describe('SelectComponent', () => {
  // jsdom lacks scrollIntoView (highlighted item) and ResizeObserver (trigger width).
  beforeAll(() => {
    Element.prototype.scrollIntoView ??= () => {};
    globalThis.ResizeObserver ??= class {
      observe() {}
      unobserve() {}
      disconnect() {}
    };
  });

  it('shows the label of the bound value', async () => {
    const { trigger } = await setup();
    expect(trigger().textContent?.trim()).toBe('Alpha');
  });

  it('emits the picked value and shows its label', async () => {
    const { fixture, trigger, choose } = await setup();
    await choose('Beta');
    expect(fixture.componentInstance.picked).toEqual(['b']);
    expect(trigger().textContent?.trim()).toBe('Beta');
  });

  it("treats '' as a real option", async () => {
    const { fixture, trigger, choose } = await setup();
    await choose('All');
    expect(fixture.componentInstance.picked).toEqual(['']);
    expect(trigger().textContent?.trim()).toBe('All');
  });

  it('snaps back to [value] with resetOnPick', async () => {
    const { fixture, trigger, choose } = await setup(true);
    await choose('Beta');
    expect(fixture.componentInstance.picked).toEqual(['b']);
    expect(trigger().textContent?.trim()).toBe('Alpha');
  });
});
