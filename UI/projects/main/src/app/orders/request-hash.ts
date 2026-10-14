export interface HashInput {
  accountId: string;
  ticker: string;
  quantity: number;
  price: number | null;
  sentAt: string;
}

export async function computeRequestHash(input: HashInput): Promise<string> {
  const text = [input.accountId, input.ticker, input.quantity, input.price ?? '', input.sentAt].join('|');
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join('');
}
