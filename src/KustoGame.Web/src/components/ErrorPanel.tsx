import type { QueryError } from '../types';

const SOURCES: Record<QueryError['source'], string> = {
    engine: 'Ошибка движка',
    command: 'Запрещено',
    timeout: 'Таймаут',
    input: 'Запрос',
};

/** Engine error exactly as the engine reported it, plus where it points in the query. */
export function ErrorPanel({ error, engine }: { error: QueryError; engine?: string }) {
    const unsupported = /не реализовано|not implemented|not supported/i.test(error.message);
    return (
        <div className="error-panel">
            <div className="error-head">
                <span className="error-kind">{SOURCES[error.source]}</span>
                {engine && <span className="badge">{engine}</span>}
                {error.position && (
                    <span className="muted">
                        строка {error.position.line}, столбец {error.position.column}
                    </span>
                )}
            </div>
            <pre className="error-text">{error.message}</pre>
            {unsupported && engine && (
                <div className="muted">
                    Движок {engine} не поддерживает эту конструкцию. Посмотрите вкладку «Движки» или переключитесь на другой движок.
                </div>
            )}
        </div>
    );
}
