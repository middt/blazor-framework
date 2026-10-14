import { describe, it, expect, vi, beforeEach } from 'vitest';
import { bffFetch, CSRF_HEADER, loginUrl } from './bff-fetch';
import { DEFAULT_MIDDT_CONFIG } from './config';

describe('bffFetch', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('generates correct loginUrl with returnUrl encoded', () => {
    const url = loginUrl(DEFAULT_MIDDT_CONFIG, '/dashboard?tab=1');
    expect(url).toBe('/Account/Login?returnUrl=%2Fdashboard%3Ftab%3D1');
  });

  it('attaches CSRF header and same-origin credentials', async () => {
    const mockResponse = new Response('ok', { status: 200 });
    const fetchSpy = vi.fn().mockResolvedValue(mockResponse);
    vi.stubGlobal('fetch', fetchSpy);

    const client = bffFetch(DEFAULT_MIDDT_CONFIG);
    const response = await client('/api/test', { method: 'GET' });

    expect(response.status).toBe(200);
    expect(fetchSpy).toHaveBeenCalledOnce();

    const [calledInput, calledInit] = fetchSpy.mock.calls[0];
    expect(calledInput).toBe('/api/test');
    expect(calledInit.credentials).toBe('same-origin');
    expect(new Headers(calledInit.headers).get(CSRF_HEADER)).toBe('1');
  });

  it('redirects to login on 401 response', async () => {
    const mockResponse = new Response('unauthorized', { status: 401 });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse));

    const assignMock = vi.fn();
    vi.stubGlobal('location', {
      pathname: '/secret',
      search: '',
      assign: assignMock,
    });

    const client = bffFetch(DEFAULT_MIDDT_CONFIG);
    await client('/api/protected');

    expect(assignMock).toHaveBeenCalledWith('/Account/Login?returnUrl=%2Fsecret');
  });
});
