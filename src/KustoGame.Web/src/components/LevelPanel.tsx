import { useCallback, useRef, useState, type RefObject } from 'react';
import { api } from '../api';
import { emptyLevelProgress, HINT_PENALTY, scoreFor, type LevelProgress } from '../progress';
import type { Chapter, CheckResponse, EngineName, Level, QueryResponse, SolutionView } from '../types';
import { DiffView } from './DiffView';
import { ResultTable } from './ResultTable';
import { RichText } from './RichText';
import { Workbench, type WorkbenchHandle } from './Workbench';

interface Props {
    level: Level;
    chapter: Chapter | undefined;
    engine: EngineName;
    progress: LevelProgress | undefined;
    updateLevel(id: string, change: (p: LevelProgress) => LevelProgress): void;
    onSwitchEngine(engine: EngineName): void;
    workbenchRef: RefObject<WorkbenchHandle>;
    onNext?: () => void;
}

const CHECK_LABELS: Record<Level['checkMode'], string> = {
    set: 'сравниваются строки (порядок и имена колонок не важны)',
    ordered: 'сравниваются строки с учётом порядка',
    scalar: 'ответ — одно значение',
    shape: 'проверяются колонки и число строк',
};

export function LevelPanel({ level, chapter, engine, progress, updateLevel, onSwitchEngine, workbenchRef, onNext }: Props) {
    const state = progress ?? emptyLevelProgress();
    const [query, setQuery] = useState(state.query || '');
    const [check, setCheck] = useState<CheckResponse | null>(null);
    const [checking, setChecking] = useState(false);
    const [solution, setSolution] = useState<SolutionView | null>(null);
    const [failure, setFailure] = useState<string | null>(null);
    const lastResult = useRef<QueryResponse | null>(null);
    const [playerResult, setPlayerResult] = useState<QueryResponse | null>(null);

    const available = level.engines.includes(engine);

    const changeQuery = useCallback(
        (q: string) => {
            setQuery(q);
            updateLevel(level.id, (p) => ({ ...p, query: q }));
        },
        [level.id, updateLevel],
    );

    const onResult = useCallback((r: QueryResponse | null) => {
        lastResult.current = r;
    }, []);

    if (!available) {
        return (
            <div className="level">
                <LevelHeader level={level} chapter={chapter} state={state} />
                <div className="locked">
                    <div className="locked-title">🔒 Нужен {level.engines.join(' или ')}</div>
                    <p>
                        Эталон этого уровня использует <code>{level.topic}</code> — движок {engine} эту возможность не поддерживает
                        (см. вкладку «Движки»). Переключите движок, чтобы открыть уровень.
                    </p>
                    {level.engines.map((e) => (
                        <button key={e} className="primary" onClick={() => onSwitchEngine(e)}>
                            Переключиться на {e}
                        </button>
                    ))}
                </div>
            </div>
        );
    }

    const runCheck = async () => {
        setChecking(true);
        setFailure(null);
        try {
            const response = await api.check(level.id, workbenchRef.current?.getQuery() ?? query, engine);
            setCheck(response);
            if (response.passed) {
                setSolution(response.solution);
                setPlayerResult(lastResult.current);
                updateLevel(level.id, (p) => ({
                    ...p,
                    solved: true,
                    points: p.revealed ? 0 : Math.max(p.points, scoreFor(level, p.hintsUsed)),
                    solvedAt: p.solvedAt ?? new Date().toISOString(),
                    solvedOn: engine,
                }));
            }
        } catch (e) {
            setFailure(e instanceof Error ? e.message : String(e));
        } finally {
            setChecking(false);
        }
    };

    const showHint = () => updateLevel(level.id, (p) => ({ ...p, hintsUsed: Math.min(level.hints.length, p.hintsUsed + 1) }));

    const giveUp = async () => {
        if (!window.confirm('Показать эталонное решение? За уровень будет 0 очков.')) return;
        try {
            setSolution(await api.reveal(level.id, engine));
            setPlayerResult(lastResult.current);
            updateLevel(level.id, (p) => ({ ...p, revealed: true, points: p.solved ? p.points : 0 }));
        } catch (e) {
            setFailure(e instanceof Error ? e.message : String(e));
        }
    };

    const insertExample = (run: boolean) => {
        workbenchRef.current?.setQuery(level.example);
        if (run) workbenchRef.current?.run(level.example);
    };

    const nextHintCost = Math.round(level.points * HINT_PENALTY);
    const potential = scoreFor(level, state.hintsUsed);

    return (
        <div className="level">
            <LevelHeader level={level} chapter={chapter} state={state} />

            <section className="card theory">
                <h4>Теория</h4>
                <RichText text={level.theory} />
                <div className="example">
                    <pre>{level.example}</pre>
                    <div className="example-actions">
                        <button onClick={() => insertExample(false)}>Вставить</button>
                        <button className="primary" onClick={() => insertExample(true)}>
                            Вставить и выполнить
                        </button>
                    </div>
                </div>
            </section>

            <section className="card task">
                <h4>Задача</h4>
                <RichText text={level.task} />
                <div className="muted small">Проверка: {CHECK_LABELS[level.checkMode]}.</div>
            </section>

            <section className="hints">
                {level.hints.slice(0, state.hintsUsed).map((hint, i) => (
                    <div key={i} className="hint">
                        <span className="hint-index">Подсказка {i + 1}</span>
                        <RichText text={hint} />
                    </div>
                ))}
                <div className="hint-actions">
                    {state.hintsUsed < level.hints.length && (
                        <button onClick={showHint}>
                            💡 Подсказка {state.hintsUsed + 1} из {level.hints.length}
                            {!state.solved && <span className="muted"> (−{nextHintCost} очков)</span>}
                        </button>
                    )}
                    {!state.solved && !state.revealed && (
                        <span className="muted">
                            Можно заработать: <b>{potential}</b> из {level.points}
                        </span>
                    )}
                    {!state.solved && !solution && (
                        <button className="ghost" onClick={() => void giveUp()}>
                            Сдаться и посмотреть эталон
                        </button>
                    )}
                </div>
            </section>

            <Workbench
                ref={workbenchRef}
                engine={engine}
                query={query}
                onQueryChange={changeQuery}
                onResult={onResult}
                actions={
                    <button className="accent" onClick={() => void runCheck()} disabled={checking}>
                        {checking ? 'Проверяю…' : '✓ Проверить ответ'}
                    </button>
                }
            />

            {failure && <div className="error-panel"><pre className="error-text">{failure}</pre></div>}
            {check && <DiffView check={check} />}

            {solution && (
                <section className="compare">
                    <h4>{check?.passed ? 'Ваш запрос и эталон' : 'Эталонное решение'}</h4>
                    <div className="compare-grid">
                        <div>
                            <div className="compare-title">Ваш запрос</div>
                            <pre className="code">{query.trim() || '—'}</pre>
                            {playerResult && (
                                <ResultTable columns={playerResult.columns} rows={playerResult.rows} totalRows={playerResult.totalRows} maxHeight={260} />
                            )}
                        </div>
                        <div>
                            <div className="compare-title">Эталон</div>
                            <pre className="code">{solution.query}</pre>
                            <ResultTable columns={solution.columns} rows={solution.rows} totalRows={solution.totalRows} maxHeight={260} />
                        </div>
                    </div>
                    {check?.passed && onNext && (
                        <button className="primary next" onClick={onNext}>
                            Следующий уровень →
                        </button>
                    )}
                </section>
            )}
        </div>
    );
}

function LevelHeader({ level, chapter, state }: { level: Level; chapter: Chapter | undefined; state: LevelProgress }) {
    return (
        <header className="level-head">
            {chapter && (
                <details className="chapter-intro" open={level.order === 1 && !state.solved}>
                    <summary>
                        Глава {chapter.number}. {chapter.title}
                    </summary>
                    <RichText text={chapter.description} />
                </details>
            )}
            <div className="level-title">
                <h2>{level.title}</h2>
                <span className="tag">{level.topic}</span>
                {level.engines.map((e) => (
                    <span key={e} className="badge">
                        {e}
                    </span>
                ))}
                <span className="spacer" />
                {state.solved && <span className="score done">✔ {state.points} очков</span>}
                {!state.solved && state.revealed && <span className="score">эталон просмотрен</span>}
                {!state.solved && !state.revealed && <span className="score">{level.points} очков</span>}
            </div>
        </header>
    );
}
