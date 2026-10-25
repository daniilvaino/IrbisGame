import { useEffect, useMemo, useRef, useState } from 'react';
import { api } from './api';
import { EnginesPage } from './components/EnginesPage';
import { LevelList } from './components/LevelList';
import { LevelPanel } from './components/LevelPanel';
import { Sandbox } from './components/Sandbox';
import { SchemaPanel } from './components/SchemaPanel';
import type { WorkbenchHandle } from './components/Workbench';
import { setKustoSchema } from './monaco/setup';
import { useProgress } from './progress';
import { ENGINES, type EngineInfo, type EngineName, type LevelsResponse, type SchemaResponse } from './types';

type Mode = 'levels' | 'sandbox' | 'engines';

export function App() {
    const { progress, setProgress, updateLevel, exportFile, importText, reset } = useProgress();
    const [data, setData] = useState<LevelsResponse | null>(null);
    const [schema, setSchema] = useState<SchemaResponse | null>(null);
    const [info, setInfo] = useState<EngineInfo | null>(null);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [mode, setMode] = useState<Mode>('levels');
    const [current, setCurrent] = useState<string | null>(null);
    const workbench = useRef<WorkbenchHandle>(null);
    const fileInput = useRef<HTMLInputElement>(null);

    useEffect(() => {
        Promise.all([api.levels(), api.schema(), api.engine()])
            .then(([levels, s, engineInfo]) => {
                setKustoSchema(s);
                setData(levels);
                setSchema(s);
                setInfo(engineInfo);
            })
            .catch((e) => setLoadError(e instanceof Error ? e.message : String(e)));
    }, []);

    const engine: EngineName = progress.engine ?? info?.active ?? 'BabyKusto';
    const setEngine = (e: EngineName) => setProgress((p) => ({ ...p, engine: e }));

    const levels = useMemo(() => data?.levels ?? [], [data]);
    const firstOpen = levels.find((l) => !progress.levels[l.id]?.solved && l.engines.includes(engine)) ?? levels[0];
    const level = levels.find((l) => l.id === current) ?? firstOpen;

    const totalPoints = Object.values(progress.levels).reduce((sum, l) => sum + (l.solved ? l.points : 0), 0);
    const maxPoints = levels.reduce((sum, l) => sum + l.points, 0);
    const solved = levels.filter((l) => progress.levels[l.id]?.solved).length;

    const next = level ? levels[levels.indexOf(level) + 1] : undefined;

    // Pin the starting level: otherwise solving it would immediately switch the view to the next unsolved one.
    useEffect(() => {
        if (current === null && firstOpen) setCurrent(firstOpen.id);
    }, [current, firstOpen]);

    const importProgress = async (file: File) => {
        try {
            importText(await file.text());
        } catch (e) {
            window.alert(e instanceof Error ? e.message : String(e));
        }
    };

    if (loadError) {
        return (
            <div className="fatal">
                <h2>Сервер не ответил</h2>
                <pre>{loadError}</pre>
                <p>Запустите сервер: <code>dotnet run --project src/KustoGame.Server</code></p>
            </div>
        );
    }

    return (
        <div className="app">
            <header className="topbar">
                <div className="brand">
                    <span className="logo">▲</span>
                    <span>
                        Дело «Ирбис» <span className="muted">· учебник KQL</span>
                    </span>
                </div>
                <nav className="tabs">
                    {(
                        [
                            ['levels', 'Уровни'],
                            ['sandbox', 'Песочница'],
                            ['engines', 'Движки'],
                        ] as const
                    ).map(([m, title]) => (
                        <button key={m} className={mode === m ? 'tab active' : 'tab'} onClick={() => setMode(m)}>
                            {title}
                        </button>
                    ))}
                </nav>
                <span className="spacer" />
                <label className="engine-select">
                    Движок
                    <select value={engine} onChange={(e) => setEngine(e.target.value as EngineName)}>
                        {ENGINES.map((e) => (
                            <option key={e} value={e} disabled={info?.engines.find((s) => s.name === e)?.loaded === false}>
                                {e}
                                {info?.active === e ? ' (по умолчанию)' : ''}
                            </option>
                        ))}
                    </select>
                </label>
                <div className="score-box" title="Меньше подсказок — больше очков">
                    ★ {totalPoints} <span className="muted">/ {maxPoints}</span>
                    <span className="muted small"> · {solved}/{levels.length}</span>
                </div>
                <div className="menu">
                    <button onClick={exportFile} title="Скачать прогресс в JSON">Экспорт</button>
                    <button onClick={() => fileInput.current?.click()} title="Загрузить прогресс из JSON">Импорт</button>
                    <button
                        className="ghost"
                        onClick={() => window.confirm('Сбросить весь прогресс?') && reset()}
                        title="Удалить прогресс из этого браузера"
                    >
                        Сброс
                    </button>
                    <input
                        ref={fileInput}
                        type="file"
                        accept="application/json,.json"
                        hidden
                        onChange={(e) => {
                            const file = e.target.files?.[0];
                            if (file) void importProgress(file);
                            e.target.value = '';
                        }}
                    />
                </div>
            </header>

            {!data ? (
                <div className="loading">Генерирую мир и загружаю движки…</div>
            ) : (
                <main className={`layout layout-${mode}`}>
                    {mode === 'levels' && (
                        <LevelList
                            chapters={data.chapters}
                            levels={levels}
                            progress={progress}
                            engine={engine}
                            current={level?.id ?? null}
                            onSelect={(id) => setCurrent(id)}
                        />
                    )}

                    <section className="center">
                        {mode === 'levels' && level && (
                            <LevelPanel
                                key={level.id}
                                level={level}
                                chapter={data.chapters.find((c) => c.number === level.chapter)}
                                engine={engine}
                                progress={progress.levels[level.id]}
                                updateLevel={updateLevel}
                                onSwitchEngine={setEngine}
                                workbenchRef={workbench}
                                onNext={next ? () => setCurrent(next.id) : undefined}
                            />
                        )}
                        {mode === 'sandbox' && (
                            <Sandbox
                                engine={engine}
                                query={progress.sandboxQuery}
                                onQueryChange={(q) => setProgress((p) => ({ ...p, sandboxQuery: q }))}
                                workbenchRef={workbench}
                            />
                        )}
                        {mode === 'engines' && (
                            <EnginesPage
                                info={info}
                                engine={engine}
                                levels={levels}
                                onSwitch={setEngine}
                                onTry={(q) => {
                                    setProgress((p) => ({ ...p, sandboxQuery: q }));
                                    setMode('sandbox');
                                }}
                            />
                        )}
                    </section>

                    {mode !== 'engines' && <SchemaPanel schema={schema} engine={engine} onInsert={(t) => workbench.current?.insert(t)} />}
                </main>
            )}
        </div>
    );
}
