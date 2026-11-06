import axios, { CanceledError } from "axios";
import type { AxiosInstance, AxiosResponse, InternalAxiosRequestConfig } from "axios";

/** Supplies application-owned JWT storage, refresh behavior, and native interceptor callbacks. */
export interface AxiosSession {
  getToken(): string | undefined;
  refreshToken(signal?: AbortSignal): Promise<void>;
  beforeRequest?(request: InternalAxiosRequestConfig): void | Promise<void>;
  afterResponse?(response: AxiosResponse): void | Promise<void>;
}

/** Marks a single request so another 401 cannot recursively refresh and replay it again. */
type RefreshRequest = InternalAxiosRequestConfig & { valioRefreshAttempted?: boolean };

/** Creates a native Axios instance with dynamic JWT headers and one bounded refresh/replay. */
export function createAuthenticatedAxios(baseUrl: string, session: AxiosSession): AxiosInstance {
  const client = axios.create({ baseURL: baseUrl });
  client.interceptors.request.use(async request => {
    if (request.signal?.aborted) throw new CanceledError("Request aborted before dispatch");
    const token = session.getToken();
    if (token !== undefined) request.headers.set("Authorization", `Bearer ${token}`);
    await session.beforeRequest?.(request);
    return request;
  });
  const receive = async (response: AxiosResponse): Promise<AxiosResponse> => {
    await session.afterResponse?.(response);
    const request = response.config as RefreshRequest;
    if (response.status !== 401 || request.valioRefreshAttempted) return response;
    request.valioRefreshAttempted = true;
    if (request.signal?.aborted) throw new CanceledError("Request aborted before refresh");
    await session.refreshToken(request.signal instanceof AbortSignal ? request.signal : undefined);
    if (request.signal?.aborted) throw new CanceledError("Request aborted before replay");
    return client.request(request);
  };
  client.interceptors.response.use(receive, error => {
    if (axios.isAxiosError(error) && error.response?.status === 401) return receive(error.response);
    return Promise.reject(error);
  });
  return client;
}
