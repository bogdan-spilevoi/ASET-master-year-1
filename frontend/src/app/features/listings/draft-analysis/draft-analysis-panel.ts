import { Component, computed, DestroyRef, inject, input, model, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { catchError, forkJoin, of, repeat, retry, Subscription, takeWhile } from 'rxjs';
import { apiError } from '../../../shared/api-error';
import { AnalysisKind, DraftAnalysis } from '../data-access/draft-analysis.models';
import { ListingsApi } from '../data-access/listings-api';

@Component({
  selector: 'sl-draft-analysis',
  imports: [ReactiveFormsModule],
  templateUrl: './draft-analysis-panel.html',
  styleUrl: './draft-analysis-panel.scss'
})
export class DraftAnalysisPanel implements OnInit {
  private readonly api = inject(ListingsApi);
  private readonly destroyRef = inject(DestroyRef);
  private polling?: Subscription;
  readonly resume = input(false);
  private checkMissingRequests = false;
  readonly listingId = input.required<string>();
  readonly savedTraits = input<readonly string[]>([]);
  readonly selected = model.required<string[]>();
  readonly traitText = new FormControl('', { nonNullable: true });
  readonly disabled = input(false);
  readonly operations = [
    { kind: 'traits', label: 'Suggested traits (Qwen)' },
    { kind: 'embedding', label: 'Photo embedding (DINO)' }
  ] as const;
  readonly snapshot = signal<DraftAnalysis | null>(null);
  readonly submitting = signal(false);
  readonly requestErrors = signal<Partial<Record<AnalysisKind, string>>>({});
  readonly readError = signal('');
  readonly phase = computed(() => {
    const snapshot = this.snapshot();
    if (snapshot?.traits.state === 'Ready' && snapshot.embedding.state === 'Ready') return 'ready';
    const needsAttention = this.operations.some(({ kind }) => {
      const state = snapshot?.[kind].state;
      return state === 'Failed' || (this.requestErrors()[kind] && (!state || state === 'NotRequested'));
    });
    return this.readError() || needsAttention ? 'blocked' : 'analyzing';
  });
  readonly choices = computed(() => {
    const unique = new Map(this.savedTraits().map(value => [value.toLowerCase(), value]));
    for (const item of this.snapshot()?.suggestions?.objects ?? []) {
      const values = [item.category, item.objectType, ...item.colors, item.material, item.shape,
        item.pattern, ...item.visibleText, ...item.distinctiveFeatures, ...item.tags];
      for (const value of values) {
        const text = value?.trim();
        if (text && !unique.has(text.toLowerCase())) unique.set(text.toLowerCase(), text);
      }
    }
    for (const value of this.selected()) {
      if (!unique.has(value.toLowerCase())) unique.set(value.toLowerCase(), value);
    }
    return [...unique.values()];
  });

  ngOnInit() {
    if (this.resume()) {
      this.checkMissingRequests = true;
      this.poll();
    } else {
      this.submit(['traits', 'embedding']);
    }
  }

  submit(kinds: readonly AnalysisKind[]) {
    if (this.submitting()) return;
    this.polling?.unsubscribe();
    this.submitting.set(true);
    const requests = kinds.map(kind => this.api.requestAnalysis(this.listingId(), kind).pipe(
      catchError(error => {
        this.requestErrors.update(errors => ({ ...errors, [kind]: apiError(error) }));
        return of(null);
      })
    ));
    this.requestErrors.update(errors => {
      const remaining = { ...errors };
      for (const kind of kinds) delete remaining[kind];
      return remaining;
    });
    forkJoin(requests).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.submitting.set(false);
      this.poll();
    });
  }

  poll() {
    this.polling?.unsubscribe();
    this.readError.set('');
    // Wait between completed reads so a slow API never creates overlapping requests.
    this.polling = this.api.analysis(this.listingId()).pipe(
      retry({ count: 2, delay: 3000 }),
      repeat({ delay: 3000 }),
      takeWhile(result => this.operations.some(({ kind }) => {
        const state = result[kind].state;
        return state === 'Pending' || state === 'Processing' || (state === 'NotRequested' && !this.requestErrors()[kind]);
      }), true),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: result => {
        this.snapshot.set(result);
        if (this.checkMissingRequests) {
          this.checkMissingRequests = false;
          const missing = this.operations.map(op => op.kind).filter(kind => result[kind].state === 'NotRequested');
          if (missing.length) this.submit(missing);
        }
      },
      error: error => this.readError.set(apiError(error))
    });
  }

  addTrait() {
    const text = this.traitText.value.trim();
    if (!text || this.disabled() || this.phase() !== 'ready') return;
    const existing = this.choices().find(value => value.toLowerCase() === text.toLowerCase());
    this.toggle(existing ?? text, true);
    this.traitText.reset();
  }

  toggle(value: string, checked: boolean) {
    this.selected.update(values => checked ? [...new Set([...values, value])] : values.filter(v => v !== value));
  }
}
