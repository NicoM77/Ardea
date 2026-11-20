import { groupMetrics, metricLabel, notifiedAt } from './operations';

describe('groupMetrics', () => {
  it('groups by group in the operator order, unknown groups last, rows by name', () => {
    const groups = groupMetrics([
      { group: 'zeta', name: 'a', value: 1 },
      { group: 'sources', name: 'osv.lag', value: 12, unit: 'min' },
      { group: 'worker', name: 'alive', value: '2' },
      { group: 'sources', name: 'cve.lag', value: 3, unit: 'min' },
    ]);

    expect(groups.map((g) => g.group)).toEqual(['worker', 'sources', 'zeta']);
    expect(groups.map((g) => g.label)).toEqual([
      'admin.operations.group.worker',
      'admin.operations.group.sources',
      null,
    ]);
    expect(groups[1].rows).toEqual([
      {
        name: 'cve.lag',
        label: { subject: 'cve', key: 'admin.operations.metric.sourceLag' },
        value: 3,
        unit: 'min',
      },
      {
        name: 'osv.lag',
        label: { subject: 'osv', key: 'admin.operations.metric.sourceLag' },
        value: 12,
        unit: 'min',
      },
    ]);
    expect(groups[2].rows[0].label).toBeNull();
    expect(groups[0].rows[0].value).toBe(2);
  });

  it('is empty without figures', () => {
    expect(groupMetrics([])).toEqual([]);
  });
});

describe('metricLabel', () => {
  it('names the measure and keeps the job kind, source or provider as its subject', () => {
    expect(metricLabel('worker', 'alive')).toEqual({
      subject: null,
      key: 'admin.operations.metric.workersAlive',
    });
    expect(metricLabel('jobs', 'platform.clean-up-jobs.oldest_wait')).toEqual({
      subject: 'platform.clean-up-jobs',
      key: 'admin.operations.metric.jobOldestWait',
    });
    expect(metricLabel('ai', 'ollama.tokens_in')).toEqual({
      subject: 'ollama',
      key: 'admin.operations.metric.aiTokensIn',
    });
    expect(metricLabel('deliveries', 'dead')).toEqual({
      subject: null,
      key: 'admin.operations.metric.deliveryDead',
    });
    expect(metricLabel('repositories', 'partial')?.key).toBe(
      'admin.operations.metric.repositoryPartial',
    );
  });

  it('leaves what it does not know to the raw name', () => {
    expect(metricLabel('zeta', 'a')).toBeNull();
    expect(metricLabel('jobs', 'pending')).toBeNull();
    expect(metricLabel('sources', 'osv.unheard')).toBeNull();
  });
});

describe('notifiedAt', () => {
  const notified = [
    { code: 'source_stale', subject: 'osv', at: '2026-11-19T08:00:00Z' },
    { code: 'worker_stalled', subject: null, at: '2026-11-19T09:00:00Z' },
  ];
  const warning = (code: string, subject?: string) => ({
    code,
    subject,
    severity: 'warning',
    message: '',
  });

  it('matches a warning by code and subject', () => {
    expect(notifiedAt(notified, warning('source_stale', 'osv'))).toBe('2026-11-19T08:00:00Z');
    expect(notifiedAt(notified, warning('worker_stalled'))).toBe('2026-11-19T09:00:00Z');
  });

  it('is null for a warning the administrators were not mailed about', () => {
    expect(notifiedAt(notified, warning('source_stale', 'nvd'))).toBeNull();
    expect(notifiedAt([], warning('worker_stalled'))).toBeNull();
  });
});
