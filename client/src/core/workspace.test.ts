import { describe, expect, it } from 'vitest';
import { allowedPath } from './workspace';

describe('allowedPath', () => {
  it('normalizes safe relative paths', () => {
    expect(allowedPath('./src\\core/file.ts')).toEqual(['src', 'core', 'file.ts']);
  });

  it.each(['../secret', '/etc/passwd', 'C:\\secret', '.env', 'src/.git/config', 'id_rsa', 'cert.pem'])('rejects unsafe path %s', value => {
    expect(() => allowedPath(value)).toThrow();
  });
});
