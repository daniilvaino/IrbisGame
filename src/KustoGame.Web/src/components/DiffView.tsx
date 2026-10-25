import type { CheckResponse, ResultColumn } from '../types';
import { CellView, ResultTable } from './ResultTable';
import { ErrorPanel } from './ErrorPanel';

/** Check outcome: verdict, column mismatches, missing / extra rows, scalar comparison. */
export function DiffView({ check }: { check: CheckResponse }) {
    const diff = check.diff;
    const columns: ResultColumn[] = (diff?.columns ?? []).map((name) => ({ name, type: '' }));
    const renamed = diff?.columnMapping.filter((m) => m.actual && m.actual !== m.expected) ?? [];

    return (
        <div className={`check ${check.passed ? 'passed' : 'failed'}`}>
            <div className="check-head">
                <span className="check-icon">{check.passed ? '✔' : '✖'}</span>
                <span>{check.message}</span>
            </div>

            {check.error && <ErrorPanel error={check.error} engine={check.engine} />}

            {diff && !check.passed && (
                <div className="diff">
                    {(diff.missingColumns.length > 0 || diff.extraColumns.length > 0) && (
                        <div className="diff-columns">
                            {diff.missingColumns.map((c) => (
                                <span key={'m' + c} className="chip missing">не хватает колонки {c}</span>
                            ))}
                            {diff.extraColumns.map((c) => (
                                <span key={'e' + c} className="chip extra">лишняя колонка {c}</span>
                            ))}
                        </div>
                    )}

                    {diff.expectedValue !== null && diff.expectedValue !== undefined && (
                        <div className="scalar-diff">
                            <div>
                                <div className="muted">ожидалось</div>
                                <div className="scalar"><CellView value={diff.expectedValue} /></div>
                            </div>
                            <div>
                                <div className="muted">получено</div>
                                <div className="scalar"><CellView value={diff.actualValue} /></div>
                            </div>
                        </div>
                    )}

                    {(diff.missingCount > 0 || diff.extraCount > 0) && (
                        <div className="muted">
                            Ожидалось строк: {diff.expectedRowCount.toLocaleString('ru-RU')}, у вас: {diff.actualRowCount.toLocaleString('ru-RU')}.
                        </div>
                    )}
                    {diff.missingCount > 0 && (
                        <ResultTable
                            caption={`Не хватает (${diff.missingCount.toLocaleString('ru-RU')})`}
                            columns={columns}
                            rows={diff.missingRows}
                            totalRows={diff.missingCount}
                            tone="missing"
                            maxHeight={240}
                        />
                    )}
                    {diff.extraCount > 0 && (
                        <ResultTable
                            caption={`Лишние (${diff.extraCount.toLocaleString('ru-RU')})`}
                            columns={columns}
                            rows={diff.extraRows}
                            totalRows={diff.extraCount}
                            tone="extra"
                            maxHeight={240}
                        />
                    )}
                    {diff.firstOrderMismatch !== null && (
                        <div className="muted">Первое расхождение порядка — строка {diff.firstOrderMismatch + 1}.</div>
                    )}
                </div>
            )}

            {check.passed && renamed.length > 0 && (
                <div className="muted">
                    Колонки сопоставлены по содержимому: {renamed.map((m) => `${m.actual} → ${m.expected}`).join(', ')}.
                </div>
            )}
        </div>
    );
}
