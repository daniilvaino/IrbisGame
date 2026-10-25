import { useCallback, useEffect, useState } from 'react';
import type { EngineName, Level } from './types';

const STORAGE_KEY = 'kusto-game.progress.v1';

export interface LevelProgress {
    solved: boolean;
    /** Hints opened (the penalty applies to the score). */
    hintsUsed: number;
    /** The player gave up and looked at the reference: the level counts as passed for 0 points. */
    revealed: boolean;
    points: number;
    /** Last query typed on this level. */
    query: string;
    solvedAt?: string;
    solvedOn?: EngineName;
}

export interface Progress {
    version: 1;
    engine?: EngineName;
    sandboxQuery: string;
    levels: Record<string, LevelProgress>;
}

export const HINT_PENALTY = 0.25;
export const MIN_SHARE = 0.25;

export function emptyLevelProgress(): LevelProgress {
    return { solved: false, hintsUsed: 0, revealed: false, points: 0, query: '' };
}

/** Fewer hints — more points: every hint costs 25% of the base, at least 25% is always kept. */
export function scoreFor(level: Level, hintsUsed: number): number {
    return Math.round(level.points * Math.max(MIN_SHARE, 1 - HINT_PENALTY * hintsUsed));
}

function initial(): Progress {
    return { version: 1, sandboxQuery: 'EmailEvents\n| summarize Letters = count() by Direction', levels: {} };
}

function load(): Progress {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (!raw) return initial();
        return parseProgress(raw);
    } catch {
        return initial();
    }
}

/** Validates an imported/stored document; throws with a readable message on garbage. */
export function parseProgress(raw: string): Progress {
    const data = JSON.parse(raw);
    if (!data || typeof data !== 'object' || data.version !== 1 || typeof data.levels !== 'object')
        throw new Error('Это не файл прогресса игры (ожидается JSON с version: 1).');
    const levels: Record<string, LevelProgress> = {};
    for (const [id, value] of Object.entries<Record<string, unknown>>(data.levels)) {
        levels[id] = {
            solved: Boolean(value.solved),
            hintsUsed: Math.max(0, Number(value.hintsUsed) || 0),
            revealed: Boolean(value.revealed),
            points: Math.max(0, Number(value.points) || 0),
            query: typeof value.query === 'string' ? value.query : '',
            solvedAt: typeof value.solvedAt === 'string' ? value.solvedAt : undefined,
            solvedOn: value.solvedOn === 'BabyKusto' || value.solvedOn === 'KustoLoco' ? value.solvedOn : undefined,
        };
    }
    return {
        version: 1,
        engine: data.engine === 'BabyKusto' || data.engine === 'KustoLoco' ? data.engine : undefined,
        sandboxQuery: typeof data.sandboxQuery === 'string' ? data.sandboxQuery : initial().sandboxQuery,
        levels,
    };
}

export function useProgress() {
    const [progress, setProgress] = useState<Progress>(load);

    useEffect(() => {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(progress));
        } catch {
            // private mode / storage disabled: progress lives only in memory
        }
    }, [progress]);

    const updateLevel = useCallback((id: string, change: (p: LevelProgress) => LevelProgress) => {
        setProgress((p) => ({ ...p, levels: { ...p.levels, [id]: change(p.levels[id] ?? emptyLevelProgress()) } }));
    }, []);

    const exportFile = useCallback(() => {
        const blob = new Blob([JSON.stringify(progress, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `kql-irbis-progress-${new Date().toISOString().slice(0, 10)}.json`;
        a.click();
        URL.revokeObjectURL(url);
    }, [progress]);

    const importText = useCallback((text: string) => setProgress(parseProgress(text)), []);
    const reset = useCallback(() => setProgress((p) => ({ ...initial(), engine: p.engine })), []);

    return { progress, setProgress, updateLevel, exportFile, importText, reset };
}
