import type {
    CheckResponse,
    EngineInfo,
    EngineName,
    LevelsResponse,
    MatrixResponse,
    QueryResponse,
    SchemaResponse,
    SignatureHelp,
    SolutionView,
    StepsResponse,
} from './types';

async function request<T>(url: string, body?: unknown): Promise<T> {
    const response = await fetch(url, {
        method: body === undefined ? 'GET' : 'POST',
        headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (response.status === 204) return null as T;
    // Query endpoints answer 400 with a regular response body (rejected command, empty query).
    if (!response.ok && response.status !== 400) {
        const text = await response.text();
        let message = text;
        try {
            message = JSON.parse(text).error ?? text;
        } catch {
            // not JSON
        }
        throw new Error(message || `HTTP ${response.status}`);
    }
    return (await response.json()) as T;
}

export const api = {
    query: (query: string, engine: EngineName) => request<QueryResponse>('/api/query', { query, engine }),
    steps: (query: string, engine: EngineName) => request<StepsResponse>('/api/query/steps', { query, engine }),
    check: (levelId: string, query: string, engine: EngineName) => request<CheckResponse>('/api/check', { levelId, query, engine }),
    reveal: (levelId: string, engine: EngineName) =>
        request<SolutionView>(`/api/levels/${encodeURIComponent(levelId)}/reveal?engine=${engine}`, {}),
    levels: () => request<LevelsResponse>('/api/levels'),
    schema: () => request<SchemaResponse>('/api/schema'),
    preview: (table: string, engine: EngineName) =>
        request<QueryResponse>(`/api/tables/${encodeURIComponent(table)}/preview?engine=${engine}`),
    engine: () => request<EngineInfo>('/api/engine'),
    matrix: () => request<MatrixResponse>('/api/engine/matrix'),
    signature: (query: string, offset: number) => request<SignatureHelp | null>('/api/signature', { query, offset }),
};
