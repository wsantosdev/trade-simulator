import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class InstrumentNames {
  private readonly cache = new Map<string, string>();

  async nameOf(ticker: string): Promise<string> {
    const cached = this.cache.get(ticker);
    if (cached !== undefined) return cached;
    try {
      const response = await fetch(`/api/instruments/${encodeURIComponent(ticker)}`);
      if (!response.ok) return '';
      const body = (await response.json()) as { name?: string };
      const name = body.name?.trim() ?? '';
      this.cache.set(ticker, name);
      return name;
    } catch {
      return '';
    }
  }
}
