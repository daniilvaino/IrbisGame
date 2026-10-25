import { useState } from 'react';
import { api } from '../api';
import type { EngineName, QueryResponse, SchemaResponse } from '../types';
import { ErrorPanel } from './ErrorPanel';
import { ResultTable } from './ResultTable';

interface Props {
    schema: SchemaResponse | null;
    engine: EngineName;
    onInsert(text: string): void;
}

/** Tables, columns and types; click inserts the name into the editor, 👁 previews 20 rows. */
export function SchemaPanel({ schema, engine, onInsert }: Props) {
    const [open, setOpen] = useState<Record<string, boolean>>({});
    const [preview, setPreview] = useState<{ table: string; result: QueryResponse | null; error?: string } | null>(null);

    if (!schema) return <aside className="schema"><div className="muted">Загрузка схемы…</div></aside>;

    const showPreview = async (table: string) => {
        setPreview({ table, result: null });
        try {
            setPreview({ table, result: await api.preview(table, engine) });
        } catch (e) {
            setPreview({ table, result: null, error: e instanceof Error ? e.message : String(e) });
        }
    };

    return (
        <aside className="schema">
            <div className="schema-head">
                <h3>Схема · {schema.database}</h3>
                <div className="muted">Дата мира: {schema.world.today} (данные за {schema.world.days} дней до неё)</div>
            </div>
            {schema.tables.map((table) => (
                <div key={table.name} className="schema-table">
                    <div className="schema-table-row">
                        <button className="link caret" onClick={() => setOpen((o) => ({ ...o, [table.name]: !o[table.name] }))}>
                            {open[table.name] ? '▾' : '▸'}
                        </button>
                        <button className="link table-name" onClick={() => onInsert(table.name)} title={`Вставить ${table.name}`}>
                            {table.name}
                        </button>
                        <span className="muted small">{table.rowCount.toLocaleString('ru-RU')}</span>
                        <button className="icon" onClick={() => void showPreview(table.name)} title="Первые 20 строк">
                            👁
                        </button>
                    </div>
                    {open[table.name] && (
                        <>
                            <div className="schema-desc">{table.description}</div>
                            <ul className="columns">
                                {table.columns.map((c) => (
                                    <li key={c.name} title={c.description}>
                                        <button className="link" onClick={() => onInsert(c.name)}>
                                            {c.name}
                                        </button>
                                        <span className={`col-type type-${c.type}`}>{c.type}</span>
                                        <div className="col-desc">{c.description}</div>
                                    </li>
                                ))}
                            </ul>
                        </>
                    )}
                </div>
            ))}

            {preview && (
                <div className="modal-backdrop" onClick={() => setPreview(null)}>
                    <div className="modal" onClick={(e) => e.stopPropagation()}>
                        <div className="modal-head">
                            <h3>
                                {preview.table} <span className="muted">| take 20</span>
                            </h3>
                            <button className="icon" onClick={() => setPreview(null)} aria-label="Закрыть">
                                ✕
                            </button>
                        </div>
                        {preview.error && <div className="error-panel"><pre className="error-text">{preview.error}</pre></div>}
                        {preview.result?.error && <ErrorPanel error={preview.result.error} engine={preview.result.engine} />}
                        {preview.result && !preview.result.error && (
                            <ResultTable
                                columns={preview.result.columns}
                                rows={preview.result.rows}
                                totalRows={preview.result.totalRows}
                                elapsedMs={preview.result.elapsedMs}
                                engine={preview.result.engine}
                                maxHeight={520}
                            />
                        )}
                        {!preview.result && !preview.error && <div className="muted">Загрузка…</div>}
                    </div>
                </div>
            )}
        </aside>
    );
}
