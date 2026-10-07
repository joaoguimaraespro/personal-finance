import { AllocationStatus, BucketLine } from '../../core/models';

/** Same rule as the server's AllocationProgress: what was set aside against the bucket's target this month. */
export function autoStatus(line: Pick<BucketLine, 'target' | 'actual'>): AllocationStatus | null {
  if (line.target === null || line.target <= 0) return null;
  if (line.actual >= line.target) return 'Done';
  return line.actual > 0 ? 'Partial' : 'Todo';
}

/** A status picked by hand wins; otherwise it follows the figures. */
export function effectiveStatus(
  manual: AllocationStatus | undefined,
  line: Pick<BucketLine, 'target' | 'actual'>,
): AllocationStatus | null {
  return manual ?? autoStatus(line);
}

/** What is still to set aside (never negative). */
export function remaining(line: Pick<BucketLine, 'target' | 'actual'>): number {
  return line.target === null
    ? 0
    : Math.max(Math.round((line.target - line.actual) * 100) / 100, 0);
}

/** The movement that sets aside the rest is offered while something is left and the month is not closed by hand. */
export function canRecord(
  manual: AllocationStatus | undefined,
  line: Pick<BucketLine, 'target' | 'actual'>,
) {
  const status = effectiveStatus(manual, line);
  return remaining(line) > 0 && status !== 'Done' && status !== 'NotApplicable';
}
