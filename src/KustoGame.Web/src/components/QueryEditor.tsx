import { forwardRef, useEffect, useImperativeHandle, useRef } from 'react';
import { ensureSchema, monaco } from '../monaco/setup';
import type { TextPosition } from '../types';

export interface QueryEditorHandle {
    insert(text: string): void;
    setValue(text: string): void;
    /** Current text straight from the model: React state may lag behind the last keystroke. */
    getValue(): string;
    focus(): void;
}

interface Props {
    /**
     * Initial text only. The editor owns its content afterwards: syncing a controlled value back would race
     * with fast typing (a stale render resets the text and the cursor). Change it from outside via setValue().
     */
    initialValue: string;
    onChange(value: string): void;
    onRun(): void;
    /** Engine error to underline (owner "engine"); monaco-kusto adds its own syntax/semantic markers. */
    error?: { message: string; position: TextPosition } | null;
    /** Range of the pipeline stage selected in step mode. */
    highlight?: TextPosition | null;
    height?: number;
}

export const QueryEditor = forwardRef<QueryEditorHandle, Props>(function QueryEditor(
    { initialValue, onChange, onRun, error, highlight, height = 220 },
    ref,
) {
    const container = useRef<HTMLDivElement>(null);
    const editor = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
    const decorations = useRef<monaco.editor.IEditorDecorationsCollection | null>(null);
    const onRunRef = useRef(onRun);
    const onChangeRef = useRef(onChange);
    onRunRef.current = onRun;
    onChangeRef.current = onChange;

    useEffect(() => {
        const model = monaco.editor.createModel(initialValue, 'kusto');
        const instance = monaco.editor.create(container.current!, {
            model,
            theme: 'kusto-dark',
            minimap: { enabled: false },
            fontSize: 14,
            fontFamily: "'JetBrains Mono', 'Cascadia Code', Consolas, monospace",
            lineNumbersMinChars: 3,
            scrollBeyondLastLine: false,
            automaticLayout: true,
            wordWrap: 'on',
            fixedOverflowWidgets: true,
            suggest: { showStatusBar: true },
            parameterHints: { enabled: true },
            tabSize: 4,
            renderLineHighlight: 'gutter',
            padding: { top: 8, bottom: 8 },
            // Monaco 0.53+ reads keyboard input through the experimental EditContext API in Chromium; with some
            // Windows keyboard layouts / IMEs it drops keys (space). The classic hidden textarea is reliable.
            editContext: false,
        });
        editor.current = instance;
        decorations.current = instance.createDecorationsCollection();
        instance.addAction({
            id: 'kusto-game.run',
            label: 'Выполнить запрос',
            keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter],
            run: () => onRunRef.current(),
        });
        const sub = model.onDidChangeContent(() => {
            onChangeRef.current(model.getValue());
            monaco.editor.setModelMarkers(model, 'engine', []);
        });
        void ensureSchema(model);
        return () => {
            sub.dispose();
            instance.dispose();
            model.dispose();
        };
        // Created once per mount; see initialValue.
    }, []);

    useEffect(() => {
        const model = editor.current?.getModel();
        if (!model) return;
        if (!error) {
            monaco.editor.setModelMarkers(model, 'engine', []);
            return;
        }
        const start = model.getPositionAt(error.position.start);
        const end = model.getPositionAt(error.position.start + Math.max(1, error.position.length));
        monaco.editor.setModelMarkers(model, 'engine', [
            {
                severity: monaco.MarkerSeverity.Error,
                message: `Движок: ${error.message}`,
                startLineNumber: start.lineNumber,
                startColumn: start.column,
                endLineNumber: end.lineNumber,
                endColumn: end.column,
            },
        ]);
    }, [error]);

    useEffect(() => {
        const model = editor.current?.getModel();
        if (!model || !decorations.current) return;
        if (!highlight) {
            decorations.current.clear();
            return;
        }
        const start = model.getPositionAt(highlight.start);
        const end = model.getPositionAt(highlight.start + highlight.length);
        decorations.current.set([
            {
                range: new monaco.Range(start.lineNumber, start.column, end.lineNumber, end.column),
                options: { inlineClassName: 'stage-highlight', isWholeLine: false },
            },
        ]);
    }, [highlight]);

    useImperativeHandle(ref, () => ({
        insert(text: string) {
            const instance = editor.current;
            if (!instance) return;
            const selection = instance.getSelection();
            if (selection) instance.executeEdits('schema', [{ range: selection, text, forceMoveMarkers: true }]);
            instance.focus();
        },
        setValue(text: string) {
            // Through an edit (not model.setValue) so that Ctrl+Z can bring the previous query back.
            const instance = editor.current;
            const model = instance?.getModel();
            if (!instance || !model) return;
            instance.executeEdits('replace', [{ range: model.getFullModelRange(), text }]);
            instance.setPosition(model.getPositionAt(text.length));
            instance.focus();
        },
        getValue() {
            return editor.current?.getModel()?.getValue() ?? '';
        },
        focus() {
            editor.current?.focus();
        },
    }));

    return <div className="editor" style={{ height }} ref={container} />;
});
