import type { Progress } from '../progress';
import type { Chapter, EngineName, Level } from '../types';

interface Props {
    chapters: Chapter[];
    levels: Level[];
    progress: Progress;
    engine: EngineName;
    current: string | null;
    onSelect(id: string): void;
}

export function LevelList({ chapters, levels, progress, engine, current, onSelect }: Props) {
    return (
        <nav className="level-list">
            {chapters.map((chapter) => {
                const items = levels.filter((l) => l.chapter === chapter.number);
                const done = items.filter((l) => progress.levels[l.id]?.solved).length;
                return (
                    <div key={chapter.number} className="chapter">
                        <div className="chapter-title">
                            <span>
                                {chapter.number}. {chapter.title}
                            </span>
                            <span className="muted small">
                                {done}/{items.length}
                            </span>
                        </div>
                        <ul>
                            {items.map((level) => {
                                const state = progress.levels[level.id];
                                const locked = !level.engines.includes(engine);
                                const status = state?.solved ? 'solved' : state?.revealed ? 'revealed' : locked ? 'locked' : 'open';
                                return (
                                    <li key={level.id}>
                                        <button
                                            className={`level-item ${status} ${current === level.id ? 'current' : ''}`}
                                            onClick={() => onSelect(level.id)}
                                        >
                                            <span className="level-status">
                                                {status === 'solved' ? '✔' : status === 'revealed' ? '👁' : status === 'locked' ? '🔒' : '○'}
                                            </span>
                                            <span className="level-name">
                                                {locked ? <span className="needs">нужен {level.engines.join('/')}</span> : level.title}
                                                <span className="level-topic">{level.topic}</span>
                                            </span>
                                            {state?.solved && <span className="level-points">{state.points}</span>}
                                        </button>
                                    </li>
                                );
                            })}
                        </ul>
                    </div>
                );
            })}
        </nav>
    );
}
