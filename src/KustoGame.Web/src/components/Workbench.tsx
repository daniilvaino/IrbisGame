import { forwardRef, useCallback, useImperativeHandle, useMemo, useRef, useState, type ReactNode } from 'react';
import { api } from '../api';
import type { EngineName, QueryResponse, StepsResponse } from '../types';
import { ErrorPanel } from './ErrorPanel';
import { QueryEditor, type QueryEditorHandle } from './QueryEditor';
import { ResultTable } from './ResultTable';
import { StepsRibbon } from './StepsRibbon';

export interface WorkbenchHandle {
    /** Runs the given query (or the current one). */
    run(query?: string): void;
    insert(text: string): void;
    /** Replaces the editor text; the change flows back through onQueryChange. */
    setQuery(text: string): void;
    /** Text currently in the editor (use this, not React state, when acting on a keystroke or click). */
    getQuery(): string;
}

interface Props {
    engine: EngineName;
    query: string;
    onQueryChange(query: string): void;
    /** Extra toolbar buttons (check, compare engines…). */
    actions?: ReactNode;
    /** Latest successful full result, e.g. for the side-by-side comparison with the reference. */
    onResult?(result: QueryResponse | null): void;
    editorHeight?: number;
}

const STEP_MODE_KEY = 'kusto-game.stepMode';

function readStepMode() {
    try {
        return localStorage.getItem(STEP_MODE_KEY) === '1';
    } catch {
        return false;
    }
}

/** Editor + run + step-by-step pipeline view + results; shared by levels and the sandbox. */
export const Workbench = forwardRef<WorkbenchHandle, Props>(function Workbench(
    { engine, query, onQueryChange, actions, onResult, editorHeight },
    ref,
) {
    const editor = useRef<QueryEditorHandle>(null);

    const [stepMode, setStepMode] = useState(readStepMode);
    const [running, setRunning] = useState(false);
    const [result, setResult] = useState<QueryResponse | null>(null);
    const [steps, setSteps] = useState<StepsResponse | null>(null);
    const [selected, setSelected] = useState(0);
    const [failure, setFailure] = useState<string | null>(null);

    const run = useCallback(
        async (text?: string) => {
            // Ctrl+Enter can arrive before React re-renders after the last keystroke: read the editor, not state.
            const q = text ?? editor.current?.getValue() ?? query;
            setRunning(true);
            setFailure(null);
            try {
                if (stepMode) {
                    const response = await api.steps(q, engine);
                    setSteps(response);
                    setResult(null);
                    const last = response.stages.length - 1;
                    setSelected(Math.max(0, last));
                    const final = response.stages[last]?.result ?? null;
                    onResult?.(final && !final.error ? final : null);
                } else {
                    const response = await api.query(q, engine);
                    setResult(response);
                    setSteps(null);
                    onResult?.(response.error ? null : response);
                }
            } catch (e) {
                setFailure(e instanceof Error ? e.message : String(e));
            } finally {
                setRunning(false);
            }
        },
        [engine, stepMode, onResult, query],
    );

    useImperativeHandle(ref, () => ({
        run: (text?: string) => void run(text),
        insert: (text: string) => editor.current?.insert(text),
        setQuery: (text: string) => editor.current?.setValue(text),
        getQuery: () => editor.current?.getValue() ?? query,
    }));

    const toggleStepMode = () => {
        setStepMode((m) => {
            try {
                localStorage.setItem(STEP_MODE_KEY, m ? '0' : '1');
            } catch {
                // storage unavailable: the toggle simply is not remembered
            }
            return !m;
        });
    };

    const stage = steps?.stages[selected] ?? null;
    const shown = stage?.result ?? result;
    const error = steps?.error ?? shown?.error ?? null;
    // Stable identity: the editor re-applies its marker whenever this object changes, and it clears the marker on typing.
    const editorError = useMemo(
        () => (error?.position ? { message: error.message, position: error.position } : null),
        [error],
    );

    return (
        <div className="workbench">
            <QueryEditor
                ref={editor}
                initialValue={query}
                onChange={onQueryChange}
                onRun={() => void run()}
                error={editorError}
                highlight={stage?.position ?? null}
                height={editorHeight}
            />
            <div className="toolbar">
                <button className="primary" onClick={() => void run()} disabled={running} title="Ctrl+Enter">
                    {running ? 'Выполняется…' : '▶ Выполнить'}
                    <kbd>Ctrl+Enter</kbd>
                </button>
                <label className="toggle" title="Выполнить каждый префикс конвейера и показать, как меняется число строк">
                    <input type="checkbox" checked={stepMode} onChange={toggleStepMode} />
                    Пошагово
                </label>
                {actions}
                <span className="spacer" />
                <span className="muted">движок: {engine}</span>
            </div>

            {failure && <div className="error-panel"><pre className="error-text">{failure}</pre></div>}

            {steps && steps.stages.length > 0 && (
                <>
                    <StepsRibbon stages={steps.stages} selected={selected} onSelect={setSelected} />
                    {stage && (
                        <div className="stage-caption">
                            Стадия {selected + 1}: <code>{stage.label}</code>
                            {selected > 0 && !stage.result.error && (
                                <span className="muted">
                                    {' '}
                                    — было {steps.stages[selected - 1].result.totalRows.toLocaleString('ru-RU')}, стало{' '}
                                    {stage.result.totalRows.toLocaleString('ru-RU')} строк
                                </span>
                            )}
                        </div>
                    )}
                </>
            )}

            {error && <ErrorPanel error={error} engine={shown?.engine ?? steps?.engine ?? engine} />}

            {shown && !shown.error && (
                <ResultTable
                    columns={shown.columns}
                    rows={shown.rows}
                    totalRows={shown.totalRows}
                    elapsedMs={shown.elapsedMs}
                    engine={shown.engine}
                />
            )}
            {!shown && !error && !failure && (
                <div className="placeholder">Нажмите «Выполнить» или Ctrl+Enter — результат появится здесь.</div>
            )}
        </div>
    );
});
