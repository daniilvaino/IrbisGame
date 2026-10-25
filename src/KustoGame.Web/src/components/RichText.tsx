import { Fragment } from 'react';

/** Level texts use a tiny subset of markdown: `code`, **bold** and line breaks. */
export function RichText({ text, className }: { text: string; className?: string }) {
    const lines = text.trim().split('\n');
    return (
        <div className={className}>
            {lines.map((line, i) => (
                <p key={i}>{inline(line)}</p>
            ))}
        </div>
    );
}

function inline(line: string) {
    const parts = line.split(/(`[^`]+`|\*\*[^*]+\*\*)/g);
    return parts.map((part, i) => {
        if (part.startsWith('`') && part.endsWith('`')) return <code key={i}>{part.slice(1, -1)}</code>;
        if (part.startsWith('**') && part.endsWith('**')) return <strong key={i}>{part.slice(2, -2)}</strong>;
        return <Fragment key={i}>{part}</Fragment>;
    });
}
