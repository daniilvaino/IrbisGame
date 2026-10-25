import type { StageResponse } from '../types';

interface Props {
    stages: StageResponse[];
    selected: number;
    onSelect(index: number): void;
}

/** Pipeline stages as a ribbon: each chip shows the operator and how the row count changed. */
export function StepsRibbon({ stages, selected, onSelect }: Props) {
    return (
        <div className="steps" role="tablist" aria-label="Стадии конвейера">
            {stages.map((stage, i) => {
                const before = i > 0 ? stages[i - 1].result.totalRows : null;
                const after = stage.result.totalRows;
                const failed = stage.result.error !== null;
                const trend = before === null || failed ? '' : after < before ? 'down' : after > before ? 'up' : 'same';
                return (
                    <div key={stage.index} className="step-wrap">
                        {i > 0 && <span className="step-arrow">│</span>}
                        <button
                            role="tab"
                            aria-selected={selected === i}
                            className={`step ${selected === i ? 'selected' : ''} ${failed ? 'failed' : ''}`}
                            onClick={() => onSelect(i)}
                            title={stage.query}
                        >
                            <span className="step-index">{i + 1}</span>
                            <span className="step-label">{stage.label}</span>
                            <span className={`step-count ${trend}`}>
                                {failed ? (
                                    'ошибка'
                                ) : before === null ? (
                                    <>{after.toLocaleString('ru-RU')}</>
                                ) : (
                                    <>
                                        {before.toLocaleString('ru-RU')} → {after.toLocaleString('ru-RU')}
                                    </>
                                )}
                            </span>
                        </button>
                    </div>
                );
            })}
        </div>
    );
}
