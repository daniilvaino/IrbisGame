// Mirrors of the server's JSON contracts (camelCase).

export type EngineName = 'BabyKusto' | 'KustoLoco';
export const ENGINES: EngineName[] = ['BabyKusto', 'KustoLoco'];

export interface TextPosition {
    start: number;
    length: number;
    line: number;
    column: number;
}

export interface ResultColumn {
    name: string;
    type: string;
}

export type Cell = string | number | boolean | null | object;

export interface QueryError {
    message: string;
    source: 'engine' | 'command' | 'timeout' | 'input';
    position: TextPosition | null;
}

export interface QueryDiagnostic {
    severity: string;
    code: string;
    message: string;
    position: TextPosition;
}

export interface QueryResponse {
    engine: EngineName;
    columns: ResultColumn[];
    rows: Cell[][];
    totalRows: number;
    truncated: boolean;
    elapsedMs: number;
    error: QueryError | null;
    diagnostics: QueryDiagnostic[];
}

export interface StageResponse {
    index: number;
    label: string;
    query: string;
    position: TextPosition;
    result: QueryResponse;
}

export interface StepsResponse {
    engine: EngineName;
    stages: StageResponse[];
    error: QueryError | null;
}

export interface ColumnMatch {
    expected: string;
    actual: string | null;
}

export interface ResultDiff {
    columns: string[];
    missingRows: Cell[][];
    extraRows: Cell[][];
    missingCount: number;
    extraCount: number;
    missingColumns: string[];
    extraColumns: string[];
    columnMapping: ColumnMatch[];
    expectedRowCount: number;
    actualRowCount: number;
    expectedValue: Cell;
    actualValue: Cell;
    firstOrderMismatch: number | null;
}

export interface SolutionView {
    query: string;
    columns: ResultColumn[];
    rows: Cell[][];
    totalRows: number;
}

export interface CheckResponse {
    levelId: string;
    engine: EngineName;
    passed: boolean;
    message: string;
    diff: ResultDiff | null;
    error: QueryError | null;
    solution: SolutionView | null;
}

export interface Chapter {
    number: number;
    title: string;
    description: string;
}

export type CheckMode = 'set' | 'ordered' | 'scalar' | 'shape';

export interface Level {
    id: string;
    chapter: number;
    order: number;
    title: string;
    topic: string;
    engines: EngineName[];
    theory: string;
    example: string;
    task: string;
    hints: string[];
    points: number;
    checkMode: CheckMode;
}

export interface WorldInfo {
    today: string;
    days: number;
}

export interface LevelsResponse {
    world: WorldInfo;
    chapters: Chapter[];
    levels: Level[];
}

export interface SchemaColumn {
    name: string;
    type: string;
    description: string;
}

export interface SchemaTable {
    name: string;
    description: string;
    rowCount: number;
    columns: SchemaColumn[];
}

export interface SchemaResponse {
    engine: EngineName;
    database: string;
    world: WorldInfo;
    tables: SchemaTable[];
}

export interface EngineStatus {
    name: EngineName;
    loaded: boolean;
    error: string | null;
    loadMs: number;
}

export interface EngineInfo {
    active: EngineName;
    engines: EngineStatus[];
    timeoutSeconds: number;
    maxRows: number;
}

export type SupportStatus = 'Supported' | 'WrongResult' | 'Failed';

export interface MatrixResponse {
    engines: EngineName[];
    rows: {
        feature: string;
        query: string;
        results: Record<EngineName, { status: SupportStatus; detail: string }>;
    }[];
}

export interface SignatureHelp {
    signatures: { label: string; documentation: string; parameters: { label: string; documentation: string }[] }[];
    activeSignature: number;
    activeParameter: number;
}
