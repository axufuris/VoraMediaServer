import { describe, it, expect } from 'vitest';
import { aiModelPrice, estimateAiCost } from './aiPricing';

const usage = (modelUsed: string, promptTokens: number, completionTokens = 0) => ({ modelUsed, promptTokens, completionTokens });

describe('aiModelPrice', () => {
    it.each([
        ['gpt-5', [1.25, 10]],
        ['gpt-5-mini', [0.25, 2]],
        ['gpt-5-nano', [0.05, 0.4]],
        ['gpt-4o-mini', [0.15, 0.6]],
        ['gpt-4o', [2.5, 10]],
        ['gpt-4.1', [2, 8]],
        ['gpt-4.1-mini', [0.4, 1.6]],
        ['gpt-4.1-nano', [0.1, 0.4]],
        ['text-embedding-3-small', [0.02, 0]],
        ['text-embedding-3-large', [0.13, 0]],
    ])('prices %s', (model, expected) => {
        expect(aiModelPrice(model)).toEqual(expected);
    });

    it('prices a dated snapshot as its model', () => {
        expect(aiModelPrice('gpt-5-2025-08-07')).toEqual([1.25, 10]);
        expect(aiModelPrice('gpt-4o-mini-2024-07-18')).toEqual([0.15, 0.6]);
    });

    it('keeps the price of a snapshot that has its own', () => {
        expect(aiModelPrice('gpt-4o-2024-05-13')).toEqual([5, 15]);
    });

    it('ignores the vendor in front of a name and how it is cased', () => {
        expect(aiModelPrice('openai/gpt-5-mini')).toEqual([0.25, 2]);
        expect(aiModelPrice(' GPT-5 ')).toEqual([1.25, 10]);
    });

    it('does not price a newer model as the one its name starts with', () => {
        expect(aiModelPrice('gpt-5.5')).toEqual([5, 30]);
        expect(aiModelPrice('gpt-5.9')).toBeNull();
        expect(aiModelPrice('gpt-5-pro')).toBeNull();
    });

    it.each(['llama3.1:8b', 'nomic-embed-text', 'text-embedding-nomic-embed-text-v1.5', 'anthropic/claude-sonnet-4', ''])(
        'has no price for %j',
        model => expect(aiModelPrice(model)).toBeNull(),
    );
});

describe('estimateAiCost', () => {
    it('charges input and output at their own rates', () => {
        const estimate = estimateAiCost([usage('gpt-5', 1_000_000, 100_000)]);

        expect(estimate.cost).toBeCloseTo(1.25 + 1, 6);
        expect(estimate.unpriced).toEqual([]);
    });

    it('adds up every model across the days', () => {
        const estimate = estimateAiCost([
            usage('gpt-4o-mini', 2_000_000, 500_000),
            usage('gpt-5', 400_000, 50_000),
            usage('gpt-5', 600_000, 50_000),
            usage('text-embedding-3-small', 5_000_000),
        ]);

        expect(estimate.cost).toBeCloseTo(0.3 + 0.3 + 1.25 + 1 + 0.1, 6);
    });

    it('leaves out a model with no price and says which', () => {
        const estimate = estimateAiCost([
            usage('gpt-5-mini', 1_000_000),
            usage('qwen3:14b', 900_000, 100_000),
            usage('nomic-embed-text', 4_000_000),
            usage('qwen3:14b', 10_000),
        ]);

        expect(estimate.cost).toBeCloseTo(0.25, 6);
        expect(estimate.unpriced).toEqual(['nomic-embed-text', 'qwen3:14b']);
    });

    it('does not name a model that used nothing', () => {
        expect(estimateAiCost([usage('llama3.1', 0, 0)])).toEqual({ cost: 0, unpriced: [] });
    });

    it('is nothing for no usage', () => {
        expect(estimateAiCost([])).toEqual({ cost: 0, unpriced: [] });
    });
});
