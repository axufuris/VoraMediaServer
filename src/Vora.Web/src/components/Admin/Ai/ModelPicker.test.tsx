import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import ModelPicker from './ModelPicker';

function Picker({ initial = '', options, emptyLabel, onPicked }: { initial?: string; options: string[]; emptyLabel?: string; onPicked?: (value: string) => void }) {
    const [value, setValue] = useState(initial);
    return (
        <>
            <label htmlFor="model">Model</label>
            <ModelPicker id="model" value={value} onChange={v => { setValue(v); onPicked?.(v); }} options={options} emptyLabel={emptyLabel} />
        </>
    );
}

describe('ModelPicker', () => {
    it('takes a typed name when the server has not listed its models', () => {
        const onPicked = vi.fn();
        render(<Picker options={[]} onPicked={onPicked} />);

        fireEvent.change(screen.getByRole('textbox', { name: 'Model' }), { target: { value: 'llama3.1' } });

        expect(onPicked).toHaveBeenCalledWith('llama3.1');
        expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    });

    it('offers the models the server has', () => {
        const onPicked = vi.fn();
        render(<Picker initial="llama3.1" options={['llama3.1', 'qwen3:14b']} onPicked={onPicked} />);
        const list = screen.getByRole('combobox', { name: 'Model' });

        expect(list).toHaveValue('llama3.1');
        fireEvent.change(list, { target: { value: 'qwen3:14b' } });

        expect(onPicked).toHaveBeenCalledWith('qwen3:14b');
        expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    });

    it('asks for a choice when one has to be made and none has', () => {
        render(<Picker options={['llama3.1']} />);

        expect(screen.getByRole('option', { name: 'Choose a model…' })).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: 'Model' })).toHaveValue('');
    });

    it('offers what an empty choice means first, where one is allowed', () => {
        const onPicked = vi.fn();
        render(<Picker initial="llama3.1" options={['llama3.1']} emptyLabel="Same as recommendations" onPicked={onPicked} />);

        fireEvent.change(screen.getByRole('combobox', { name: 'Model' }), { target: { value: '' } });

        expect(onPicked).toHaveBeenCalledWith('');
        expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual(['Same as recommendations', 'llama3.1', 'Another model…']);
    });

    it('lets a model the list does not show be typed', () => {
        const onPicked = vi.fn();
        render(<Picker initial="llama3.1" options={['llama3.1']} onPicked={onPicked} />);

        fireEvent.change(screen.getByRole('combobox', { name: 'Model' }), { target: { value: '__other__' } });
        fireEvent.change(screen.getByRole('textbox', { name: 'Model name' }), { target: { value: 'my-finetune' } });

        expect(onPicked).toHaveBeenLastCalledWith('my-finetune');
        expect(screen.getByRole('combobox', { name: 'Model' })).toHaveValue('__other__');
    });

    it('shows a saved name the server no longer lists instead of dropping it', () => {
        render(<Picker initial="gpt-4o-mini" options={['llama3.1']} />);

        expect(screen.getByRole('textbox', { name: 'Model name' })).toHaveValue('gpt-4o-mini');
    });
});
