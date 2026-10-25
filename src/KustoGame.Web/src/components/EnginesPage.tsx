import { useEffect, useState } from 'react';
import { api } from '../api';
import type { EngineInfo, EngineName, Level, MatrixResponse } from '../types';

interface Props {
    info: EngineInfo | null;
    engine: EngineName;
    levels: Level[];
    onSwitch(engine: EngineName): void;
    onTry(query: string): void;
}

const ICON = { Supported: '✅', WrongResult: '⚠️', Failed: '❌' } as const;

/** Both engines side by side: load status, operator support matrix, level availability. */
export function EnginesPage({ info, engine, levels, onSwitch, onTry }: Props) {
    const [matrix, setMatrix] = useState<MatrixResponse | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        api.matrix().then(setMatrix, (e) => setError(String(e)));
    }, []);

    return (
        <div className="engines-page">
            <section className="card">
                <h3>Движки</h3>
                <p>
                    Оба движка загружены в один процесс с одинаковыми данными. По умолчанию (appsettings <code>"Engine"</code>) активен{' '}
                    <b>{info?.active ?? '…'}</b>; здесь можно переключиться для себя — выбор запоминается в браузере.
                </p>
                <div className="engine-cards">
                    {info?.engines.map((e) => (
                        <div key={e.name} className={`engine-card ${engine === e.name ? 'active' : ''}`}>
                            <div className="engine-name">{e.name}</div>
                            <div className="muted small">
                                {e.loaded ? `данные загружены за ${Math.round(e.loadMs)} мс` : `не загрузился: ${e.error}`}
                            </div>
                            <div className="small">
                                Уровней доступно: {levels.filter((l) => l.engines.includes(e.name)).length} из {levels.length}
                            </div>
                            {engine === e.name ? (
                                <span className="badge">выбран</span>
                            ) : (
                                <button onClick={() => onSwitch(e.name)} disabled={!e.loaded}>
                                    Выбрать
                                </button>
                            )}
                        </div>
                    ))}
                </div>
                <p className="muted small">
                    Таймаут запроса {info?.timeoutSeconds ?? 10} с, в ответе не более {info?.maxRows ?? 500} строк (подсчёт строк — полный).
                </p>
            </section>

            <section className="card">
                <h3>Таблица поддержки операторов</h3>
                <p className="muted">
                    Каждая строка — маленький запрос на тестовых данных с заранее известным ответом. ✅ — результат верный, ⚠️ — запрос
                    выполнился, но ответ неверный, ❌ — движок вернул ошибку (наведите курсор, чтобы увидеть её).
                </p>
                {error && <div className="error-panel"><pre className="error-text">{error}</pre></div>}
                {!matrix && !error && <div className="muted">Прогоняю матрицу на обоих движках…</div>}
                {matrix && (
                    <div className="table-wrap" style={{ maxHeight: 'none' }}>
                        <table className="matrix">
                            <thead>
                                <tr>
                                    <th>Возможность</th>
                                    <th>Запрос</th>
                                    {matrix.engines.map((e) => (
                                        <th key={e}>
                                            {e}{' '}
                                            <span className="muted small">
                                                {matrix.rows.filter((r) => r.results[e].status === 'Supported').length}/{matrix.rows.length}
                                            </span>
                                        </th>
                                    ))}
                                </tr>
                            </thead>
                            <tbody>
                                {matrix.rows.map((row) => (
                                    <tr key={row.feature}>
                                        <td>{row.feature}</td>
                                        <td>
                                            <code className="matrix-query">{row.query}</code>
                                        </td>
                                        {matrix.engines.map((e) => (
                                            <td key={e} className="matrix-cell" title={row.results[e].detail || 'работает'}>
                                                {ICON[row.results[e].status]}
                                            </td>
                                        ))}
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
                <p className="muted small">
                    Матрица использует свои таблицы <code>M</code> и <code>N</code>, их нет в мире игры. Чтобы пощупать конструкцию на
                    настоящих данных, откройте песочницу:{' '}
                    <button className="link" onClick={() => onTry('NetworkLogs\n| where Url has "helpdesk"\n| take 10')}>
                        пример с has
                    </button>
                    .
                </p>
            </section>
        </div>
    );
}
