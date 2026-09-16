import { describe, expect, it } from 'vitest';
import { normalizeAddress } from './backend';

describe('normalizeAddress', () => {
  it('adds HTTPS and removes the root slash', () => {
    expect(normalizeAddress('example.com', false)).toBe('https://example.com');
  });

  it('allows explicit HTTP only in development mode', () => {
    expect(normalizeAddress('http://127.0.0.1:5000', true)).toBe('http://127.0.0.1:5000');
    expect(() => normalizeAddress('http://127.0.0.1:5000', false)).toThrow();
  });

  it.each(['ftp://example.com', 'https://user:pass@example.com', 'https://example.com/api', 'https://example.com?q=1'])('rejects unsafe address %s', value => {
    expect(() => normalizeAddress(value, false)).toThrow();
  });
});
