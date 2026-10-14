import { computeRequestHash } from './request-hash';

describe('computeRequestHash', () => {
  const input = {
    accountId: '01a13323-857f-754a-b009-6f8a317a186a',
    ticker: 'PETR4',
    quantity: 100,
    price: 50 as number | null,
    sentAt: '2026-01-01T00:00:00.000Z',
  };

  it('produces a 64 character hexadecimal SHA-256', async () => {
    expect(await computeRequestHash(input)).toMatch(/^[0-9a-f]{64}$/);
  });

  it('is stable for the same order and changes with the timestamp or price', async () => {
    const hash = await computeRequestHash(input);
    expect(await computeRequestHash({ ...input })).toBe(hash);
    expect(await computeRequestHash({ ...input, sentAt: '2026-01-01T00:00:01.000Z' })).not.toBe(hash);
    expect(await computeRequestHash({ ...input, price: null })).not.toBe(hash);
  });
});
