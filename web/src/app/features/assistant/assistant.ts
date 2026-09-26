import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { problemMessage } from '../../core/toast';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';

interface Turn {
  role: 'user' | 'assistant';
  text: string;
  tools?: { tool: string; status: number }[];
}

/** Conversation lives only in this page: it is not stored on the server and not remembered across visits. */
@Component({
  selector: 'app-assistant',
  imports: [HlmInputImports, HlmButtonImports, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.assistant' | translate }}</h1>
        <p class="text-sm text-muted-foreground">{{ 'assistant.subtitle' | translate }}</p>
      </div>
      @if (turns().length) {
        <button hlmBtn variant="ghost" size="sm" (click)="turns.set([])">
          {{ 'assistant.clear' | translate }}
        </button>
      }
    </div>

    @if (status.value(); as s) {
      @if (!s.enabled) {
        <section class="card max-w-2xl space-y-2 text-sm">
          <p class="font-medium">{{ 'assistant.disabled' | translate }}</p>
          <p class="text-muted-foreground">{{ 'assistant.enableHelp' | translate }}</p>
        </section>
      } @else {
        <section class="card flex min-h-[60vh] flex-col !p-0">
          <div #log class="flex-1 space-y-4 overflow-y-auto p-5">
            @for (t of turns(); track $index) {
              <div
                [class]="
                  t.role === 'user'
                    ? 'ml-auto max-w-[80%] rounded-2xl rounded-br-sm bg-primary px-4 py-2 text-primary-foreground'
                    : 'max-w-[85%] rounded-2xl rounded-bl-sm bg-muted px-4 py-2'
                "
              >
                <p class="text-sm whitespace-pre-wrap">{{ t.text }}</p>
                @if (t.tools?.length) {
                  <p class="mt-2 text-[11px] text-muted-foreground">
                    {{ 'assistant.used' | translate }}
                    @for (c of t.tools; track $index) {
                      <span class="mr-1 font-mono" [class.text-rose-600]="c.status !== 200">{{
                        c.tool
                      }}</span>
                    }
                  </p>
                }
              </div>
            } @empty {
              <div class="grid h-full place-items-center text-center text-sm text-muted-foreground">
                <div class="space-y-2">
                  <p>{{ 'assistant.try' | translate }}</p>
                  @for (q of suggestions; track q) {
                    <button class="chip mx-1" (click)="askKey(q)">{{ q | translate }}</button>
                  }
                </div>
              </div>
            }
            @if (busy()) {
              <p class="text-sm text-muted-foreground">{{ 'assistant.thinking' | translate }}</p>
            }
          </div>
          <form
            class="flex gap-2 border-t border-border p-3"
            (submit)="$event.preventDefault(); ask(input())"
          >
            <input
              hlmInput
              maxlength="2000"
              [placeholder]="'assistant.placeholder' | translate"
              [value]="input()"
              (input)="input.set($any($event.target).value)"
              [disabled]="busy()"
            />
            <button hlmBtn [disabled]="busy() || !input().trim()">
              {{ 'assistant.send' | translate }}
            </button>
          </form>
        </section>
        <p class="mt-2 text-xs text-muted-foreground">
          {{ 'assistant.privacy' | translate: { model: s.model, scopes: s.scopes.join(', ') } }}
        </p>
      }
    }
  `,
})
export class AssistantComponent {
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(TranslateService);
  private readonly log = viewChild<ElementRef<HTMLDivElement>>('log');
  protected readonly status = rxResource({
    stream: () =>
      this.http.get<{ enabled: boolean; model: string | null; scopes: string[] }>(
        '/api/assistant/status',
      ),
  });
  protected readonly turns = signal<Turn[]>([]);
  protected readonly input = signal('');
  protected readonly busy = signal(false);
  protected readonly suggestions = ['assistant.q1', 'assistant.q2', 'assistant.q3'];

  protected askKey(key: string) {
    void this.ask(this.i18n.instant(key));
  }

  protected async ask(question: string) {
    const text = question.trim();
    if (!text || this.busy()) return;
    this.input.set('');
    this.turns.update((t) => [...t, { role: 'user', text }]);
    this.busy.set(true);
    try {
      const history = this.turns()
        .slice(-20)
        .map((t) => ({ role: t.role, text: t.text }));
      const res = await firstValueFrom(
        this.http.post<{ reply: string; toolCalls: { tool: string; status: number }[] }>(
          '/api/assistant/chat',
          { messages: history },
        ),
      );
      this.turns.update((t) => [
        ...t,
        { role: 'assistant', text: res.reply || '…', tools: res.toolCalls },
      ]);
    } catch (err) {
      this.turns.update((t) => [
        ...t,
        { role: 'assistant', text: `${this.i18n.instant('common.error')}: ${problemMessage(err)}` },
      ]);
    } finally {
      this.busy.set(false);
      setTimeout(() => this.log()?.nativeElement.scrollTo({ top: 1e9, behavior: 'smooth' }));
    }
  }
}
