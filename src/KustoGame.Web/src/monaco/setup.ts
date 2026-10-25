import * as monaco from 'monaco-editor/esm/vs/editor/editor.api.js';
import 'monaco-editor/esm/vs/editor/editor.all.js';
import EditorWorker from 'monaco-editor/esm/vs/editor/editor.worker.js?worker';
import { getKustoWorker, kustoDefaults } from '@kusto/monaco-kusto/release/esm/monaco.contribution';
import type { EngineSchema } from '@kusto/monaco-kusto/release/esm/monaco.contribution';
import KustoWorker from './kusto.worker?worker';
import { api } from '../api';
import type { SchemaResponse } from '../types';

export { monaco };

// Monaco cancels pending language requests when an editor is disposed (e.g. on level switch); that is not an error.
window.addEventListener('unhandledrejection', (event) => {
    if (event.reason?.name === 'Canceled' || event.reason?.message === 'Canceled') event.preventDefault();
});

self.MonacoEnvironment = {
    getWorker(_workerId: string, label: string) {
        return label === 'kusto' ? new KustoWorker() : new EditorWorker();
    },
};

kustoDefaults.setLanguageSettings({
    includeControlCommands: false,
    newlineAfterPipe: true,
    enableHover: true,
    enableQueryWarnings: true,
    enableQuerySuggestions: false,
    completionOptions: { includeExtendedSyntax: false },
});

// monaco-kusto has no signature help; the server builds it from Kusto.Language's syntax tree and symbols.
monaco.languages.registerSignatureHelpProvider('kusto', {
    signatureHelpTriggerCharacters: ['(', ','],
    signatureHelpRetriggerCharacters: [','],
    async provideSignatureHelp(model, position) {
        const help = await api.signature(model.getValue(), model.getOffsetAt(position)).catch(() => null);
        if (!help) return null;
        return {
            value: {
                signatures: help.signatures.map((s) => ({
                    label: s.label,
                    documentation: s.documentation,
                    parameters: s.parameters.map((p) => ({ label: p.label, documentation: p.documentation })),
                })),
                activeSignature: help.activeSignature,
                activeParameter: help.activeParameter,
            },
            dispose() {},
        };
    },
});

let pendingSchema: SchemaResponse | null = null;
let schemaApplied = false;

/** Remembers the schema; it is pushed to the language worker once an editor (and so the worker) exists. */
export function setKustoSchema(schema: SchemaResponse) {
    pendingSchema = schema;
    schemaApplied = false;
}

/** Called by every editor after its model is created; applies the schema to the worker once. */
export async function ensureSchema(model: monaco.editor.ITextModel) {
    if (!pendingSchema || schemaApplied) return;
    schemaApplied = true;
    const database = {
        name: pendingSchema.database,
        tables: pendingSchema.tables.map((t) => ({
            name: t.name,
            docstring: t.description,
            columns: t.columns.map((c) => ({ name: c.name, type: c.type, docstring: c.description })),
        })),
        functions: [],
        graphs: [],
        entityGroups: [],
        majorVersion: 1,
        minorVersion: 0,
    };
    const schema: EngineSchema = {
        clusterType: 'Engine',
        cluster: { connectionString: 'https://mayak.local', databases: [database] },
        database,
    };
    try {
        const accessor = await getKustoWorker();
        const worker = await accessor(model.uri);
        await worker.setSchema(schema);
    } catch (e) {
        schemaApplied = false;
        console.error('Не удалось передать схему языковому сервису Kusto', e);
    }
}
