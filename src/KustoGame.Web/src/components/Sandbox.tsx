import { useState, type RefObject } from 'react';
import { api } from '../api';
import { ENGINES, type EngineName, type QueryResponse } from '../types';
import { ErrorPanel } from './ErrorPanel';
import { formatCell, ResultTable } from './ResultTable';
import { Workbench, type WorkbenchHandle } from './Workbench';

interface Props {
    engine: EngineName;
    query: string;
    onQueryChange(query: string): void;
    workbenchRef: RefObject<WorkbenchHandle>;
}

const SAMPLES: { title: string; query: string }[] = [
    { title: 'Кто сколько отправил', query: 'NetworkLogs\n| summarize Sent = sum(BytesSent) by UserId\n| sort by Sent desc\n| take 10' },
    { title: 'Письма по дням', query: 'EmailEvents\n| summarize Letters = count() by Day = bin(Timestamp, 1d)\n| sort by Day asc' },
    { title: 'Отказы дверей', query: 'BadgeEvents\n| where Result == "denied"\n| summarize Denied = count() by Door\n| sort by Denied desc' },
    { title: 'has vs contains', query: 'EmailEvents\n| where Subject has "пароль"\n| summarize count() by Sender' },
    { title: 'Теги платежей', query: 'Transactions\n| mv-expand Tag = Details.tags\n| summarize count() by tostring(Tag)' },
];

/** Free queries with the same step mode, plus running one query on both engines side by side. */
export function Sandbox({ engine, query, onQueryChange, workbenchRef }: Props) {
    const [comparison, setComparison] = useState<Record<EngineName, QueryResponse> | null>(null);
    const [comparing, setComparing] = useState(false);

    const compare = async () => {
        setComparing(true);
        try {
            const text = workbenchRef.current?.getQuery() ?? query;
            const results = await Promise.all(ENGINES.map((e) => api.query(text, e)));
            setComparison(Object.fromEntries(ENGINES.map((e, i) => [e, results[i]])) as Record<EngineName, QueryResponse>);
        } finally {
            setComparing(false);
        }
    };

    return (
        <div className="sandbox">
            <div className="samples">
                <span className="muted">Примеры:</span>
                {SAMPLES.map((s) => (
                    <button
                        key={s.title}
                        className="chip-button"
                        onClick={() => {
                            workbenchRef.current?.setQuery(s.query);
                            workbenchRef.current?.run(s.query);
                        }}
                    >
                        {s.title}
                    </button>
                ))}
            </div>
            <Workbench
                ref={workbenchRef}
                engine={engine}
                query={query}
                onQueryChange={onQueryChange}
                editorHeight={260}
                actions={
                    <button onClick={() => void compare()} disabled={comparing} title="Выполнить запрос на BabyKusto и KustoLoco">
                        {comparing ? 'Сравниваю…' : '⇄ Сравнить движки'}
                    </button>
                }
            />
            {comparison && (
                <section className="compare">
                    <div className="compare-head">
                        <h4>Один запрос — два движка</h4>
                        <span className="verdict">{verdict(comparison)}</span>
                    </div>
                    <div className="compare-grid">
                        {ENGINES.map((e) => {
                            const r = comparison[e];
                            return (
                                <div key={e}>
                                    <div className="compare-title">{e}</div>
                                    {r.error ? (
                                        <ErrorPanel error={r.error} engine={e} />
                                    ) : (
                                        <ResultTable columns={r.columns} rows={r.rows} totalRows={r.totalRows} elapsedMs={r.elapsedMs} maxHeight={320} />
                                    )}
                                </div>
                            );
                        })}
                    </div>
                </section>
            )}
        </div>
    );
}

function verdict(c: Record<EngineName, QueryResponse>) {
    const [a, b] = ENGINES.map((e) => c[e]);
    if (a.error || b.error) return a.error && b.error ? 'ошибка в обоих' : 'один из движков вернул ошибку';
    const key = (r: QueryResponse) => r.rows.map((row) => row.map(formatCell).join('\u001f')).sort().join('\u001e');
    const sameColumns = a.columns.map((x) => `${x.name}:${x.type}`).join() === b.columns.map((x) => `${x.name}:${x.type}`).join();
    if (a.totalRows !== b.totalRows) return `разное число строк: ${a.totalRows} и ${b.totalRows}`;
    if (!sameColumns) return 'колонки или их типы различаются';
    return key(a) === key(b) ? 'результаты совпадают' : 'строки различаются';
}
