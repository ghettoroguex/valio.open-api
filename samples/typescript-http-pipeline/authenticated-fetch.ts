/** Supplies application-owned JWT storage and refresh behavior to a native fetch wrapper. */
export interface FetchSession {
  getToken(): string | undefined;
  refreshToken(signal: AbortSignal): Promise<void>;
  beforeRequest?(request: Request): void | Promise<void>;
  afterResponse?(response: Response): void | Promise<void>;
}

/**
 * Creates an injectable native fetch implementation with one refresh and replay at most.
 * The initial Request is cloned before dispatch so JSON, FormData, and other replayable bodies survive the retry.
 * Hooks must clone a Response before inspecting its body because the generated operation still reads that body.
 */
export function createAuthenticatedFetch(
  baseUrl: string,
  session: FetchSession,
  nativeFetch: typeof fetch = globalThis.fetch,
): typeof fetch {
  return async (input, init) => {
    const address = typeof input === "string" || input instanceof URL ? new URL(input, baseUrl) : input;
    const request = new Request(address, init);
    request.signal.throwIfAborted();
    const replay = request.clone();
    const send = async (source: Request): Promise<Response> => {
      source.signal.throwIfAborted();
      const headers = new Headers(source.headers);
      const token = session.getToken();
      if (token !== undefined) headers.set("Authorization", `Bearer ${token}`);
      const authenticated = new Request(source, { headers });
      await session.beforeRequest?.(authenticated);
      authenticated.signal.throwIfAborted();
      const response = await nativeFetch(authenticated);
      await session.afterResponse?.(response);
      return response;
    };
    const response = await send(request);
    if (response.status !== 401) return response;
    request.signal.throwIfAborted();
    await response.body?.cancel();
    await session.refreshToken(request.signal);
    replay.signal.throwIfAborted();
    return send(replay);
  };
}
