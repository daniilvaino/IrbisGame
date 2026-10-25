import { useMemo, useState } from 'react';
import type { Cell, ResultColumn } from '../types';

interface Props {
    columns: ResultColumn[];
    rows: Cell[][];
    totalRows: number;
    elapsedMs?: number;
    engine?: string;
    /** Tone of the rows: used by the diff view to color missing/extra rows. */
    tone?: 'missing' | 'extra';
    maxHeight?: number;
    caption?: string;
}

type Sort = { column: number; desc: boolean } | null;

export function formatCell(value: Cell): string {
    if (value === null || value === undefined) return '';
    if (typeof value === 'object') return JSON.stringify(value);
    return String(value);
}

function compare(a: Cell, b: Cell): number {
    if (a === b) return 0;
    if (a === null || a === undefined) return 1;
    if (b === null || b === undefined) return -1;
    if (typeof a === 'number' && typeof b === 'number') return a - b;
    if (typeof a === 'boolean' && typeof b === 'boolean') return Number(a) - Number(b);
    return formatCell(a).localeCompare(formatCell(b), 'ru', { numeric: true });
}

export function CellView({ value }: { value: Cell }) {
    if (value === null || value === undefined) return <span className="cell-null">∅</span>;
    if (typeof value === 'object') {
        const text = JSON.stringify(value);
        return (
            <span className="cell-dynamic" title={JSON.stringify(value, null, 2)}>
                {text.length > 160 ? text.slice(0, 160) + '…' : text}
            </span>
        );
    }
    if (typeof value === 'boolean') return <span className={value ? 'cell-true' : 'cell-false'}>{String(value)}</span>;
    if (typeof value === 'number') return <span className="cell-number">{value.toLocaleString('ru-RU', { maximumFractionDigits: 6 })}</span>;
    return <>{value}</>;
}

/** Result grid: column types in the header, row counter, timing, click-to-sort (client side, on loaded rows). */
export function ResultTable({ columns, rows, totalRows, elapsedMs, engine, tone, maxHeight = 420, caption }: Props) {
    const [sort, setSort] = useState<Sort>(null);

    const sorted = useMemo(() => {
        if (!sort) return rows;
        const copy = [...rows];
        copy.sort((x, y) => compare(x[sort.column], y[sort.column]) * (sort.desc ? -1 : 1));
        return copy;
    }, [rows, sort]);

    const toggle = (column: number) =>
        setSort((s) => (s?.column !== column ? { column, desc: false } : s.desc ? null : { column, desc: true }));

    const shown = rows.length;
    return (
        <div className={`result ${tone ? 'result-' + tone : ''}`}>
            <div className="result-status">
                {caption && <span className="result-caption">{caption}</span>}
                <span>
                    <b>{totalRows.toLocaleString('ru-RU')}</b> {plural(totalRows, 'строка', 'строки', 'строк')}
                    {shown < totalRows && <> · показано {shown.toLocaleString('ru-RU')}</>}
                </span>
                {elapsedMs !== undefined && <span>{elapsedMs < 1 ? '<1' : Math.round(elapsedMs)} мс</span>}
                {engine && <span className="badge">{engine}</span>}
                {sort && shown < totalRows && <span className="muted">сортировка — только по показанным строкам</span>}
            </div>
            {columns.length > 0 ? (
                <div className="table-wrap" style={{ maxHeight }}>
                    <table>
                        <thead>
                            <tr>
                                <th className="rownum">#</th>
                                {columns.map((c, i) => (
                                    <th key={i} onClick={() => toggle(i)} title="Сортировать">
                                        <span className="col-name">{c.name}</span>
                                        <span className={`col-type type-${c.type}`}>{c.type}</span>
                                        {sort?.column === i && <span className="sort-mark">{sort.desc ? '▼' : '▲'}</span>}
                                    </th>
                                ))}
                            </tr>
                        </thead>
                        <tbody>
                            {sorted.map((row, r) => (
                                <tr key={r}>
                                    <td className="rownum">{r + 1}</td>
                                    {row.map((v, c) => (
                                        <td key={c} className={typeof v === 'number' ? 'num' : undefined}>
                                            <CellView value={v} />
                                        </td>
                                    ))}
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            ) : (
                <div className="empty">Нет колонок</div>
            )}
        </div>
    );
}

export function plural(n: number, one: string, few: string, many: string) {
    const mod10 = n % 10;
    const mod100 = n % 100;
    if (mod10 === 1 && mod100 !== 11) return one;
    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return few;
    return many;
}
