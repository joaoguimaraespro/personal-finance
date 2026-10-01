import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { liveResource } from '../../core/resource';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { problemMessage } from '../../core/toast';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { EmptyStateComponent } from '../../shared/empty-state';

interface Turn {
  role: 'user' | 'assistant';
  text: string;
  tools?: { tool: string; status: number }[];
}

/** Conversation lives only in this page: it is not stored on the server and not remembered across visits. */
@Component({
  selector: 'app-assistant',
  // Narrow content, centred in the main area like a document rather than pinned to the left.
  host: { class: 'mx-auto block w-full max-w-3xl' },
  imports: [
    NgIcon,
    PageHeaderComponent,
    EmptyStateComponent,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      class="!mb-4"
      [icon]="icons.assistant"
      [title]="'nav.assistant' | translate"
      [subtitle]="'assistant.subtitle' | translate"
    >
      @if (turns().length) {
        <button
          hlmBtn
          variant="ghost"
          size="sm"
          class="self-start sm:self-auto"
          (click)="turns.set([])"
        >
          <ng-icon name="lucideRotateCcw" />{{ 'assistant.clear' | translate }}
        </button>
      }
    </app-page-header>

    @if (status.value(); as s) {
      @if (!s.enabled) {
        <section class="card !p-0">
          <app-empty-state
            icon="lucideBot"
            [title]="'assistant.disabled' | translate"
            [text]="'assistant.enableHelp' | translate"
          />
        </section>
      } @else {
        <section class="card flex min-h-[60vh] flex-col !p-0">
          <div #log class="flex-1 space-y-4 overflow-y-auto p-5">
            @for (t of turns(); track $index) {
              <div class="flex items-end gap-2" [class.justify-end]="t.role === 'user'">
                @if (t.role === 'assistant') {
                  <span
                    class="bg-primary/10 text-primary dark:bg-primary/20 flex size-7 shrink-0 items-center justify-center rounded-full"
                    aria-hidden="true"
                  >
                    <ng-icon name="lucideSparkles" class="text-sm" />
                  </span>
                }
                <div
                  [class]="
                    t.role === 'user'
                      ? 'max-w-[80%] rounded-2xl rounded-br-sm bg-primary px-4 py-2 text-primary-foreground'
                      : 'max-w-[85%] rounded-2xl rounded-bl-sm bg-muted px-4 py-2'
                  "
                >
                  <p class="text-sm whitespace-pre-wrap">{{ t.text }}</p>
                  @if (t.tools?.length) {
                    <p class="mt-2 text-[11px] text-muted-foreground">
                      {{ 'assistant.used' | translate }}
                      @for (c of t.tools; track $index) {
                        <span class="mr-1 font-mono" [class.tone-neg]="c.status !== 200">{{
                          c.tool
                        }}</span>
                      }
                    </p>
                  }
                </div>
              </div>
            } @empty {
              <div class="grid h-full place-items-center">
                <app-empty-state icon="lucideSparkles" [text]="'assistant.try' | translate">
                  <div class="flex flex-wrap justify-center gap-2">
                    @for (q of suggestions; track q) {
                      <button class="chip" (click)="askKey(q)">
                        <ng-icon name="lucideSparkles" aria-hidden="true" />{{ q | translate }}
                      </button>
                    }
                  </div>
                </app-empty-state>
              </div>
            }
            @if (busy()) {
              <p class="text-muted-foreground flex items-center gap-2 text-sm" role="status">
                <ng-icon
                  name="lucideLoaderCircle"
                  class="motion-safe:animate-spin"
                  aria-hidden="true"
                />{{ 'assistant.thinking' | translate }}
              </p>
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
            <button
              hlmBtn
              [disabled]="busy() || !input().trim()"
              [attr.aria-label]="'assistant.send' | translate"
            >
              <ng-icon name="lucideSend" /><span class="hidden sm:inline">{{
                'assistant.send' | translate
              }}</span>
            </button>
          </form>
        </section>
        <p class="text-muted-foreground mt-2 flex gap-1.5 text-xs">
          <ng-icon
            name="lucideShieldCheck"
            class="text-primary mt-px shrink-0"
            aria-hidden="true"
          />
          {{ 'assistant.privacy' | translate: { model: s.model, scopes: s.scopes.join(', ') } }}
        </p>
      }
    }
  `,
})
export class AssistantComponent {
  protected readonly icons = PAGE_ICONS;
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(TranslateService);
  private readonly log = viewChild<ElementRef<HTMLDivElement>>('log');
  protected readonly status = liveResource({
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
