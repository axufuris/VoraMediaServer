// OpenAI's standard list prices in US dollars per million tokens, as
// [input, output], read from https://developers.openai.com/api/docs/pricing on
// 12 October 2026. Batch, cached-input and long-context rates are not applied,
// so this is an estimate that runs a little high.
//
// Looked up by exact name, not by "contains": gpt-5 used to match nothing and
// show as free, and a contains rule would price gpt-5.5 as gpt-5 and a local
// "text-embedding-…" model as OpenAI's.
const PRICES: Record<string, readonly [number, number]> = {
    'gpt-5': [1.25, 10],
    'gpt-5-mini': [0.25, 2],
    'gpt-5-nano': [0.05, 0.4],
    'gpt-5.1': [1.25, 10],
    'gpt-5.2': [1.75, 14],
    'gpt-5.4': [2.5, 15],
    'gpt-5.4-mini': [0.75, 4.5],
    'gpt-5.4-nano': [0.2, 1.25],
    'gpt-5.5': [5, 30],
    'gpt-4.1': [2, 8],
    'gpt-4.1-mini': [0.4, 1.6],
    'gpt-4.1-nano': [0.1, 0.4],
    'gpt-4o': [2.5, 10],
    'gpt-4o-2024-05-13': [5, 15],
    'gpt-4o-mini': [0.15, 0.6],
    'o3': [2, 8],
    'o4-mini': [1.1, 4.4],
    'text-embedding-3-small': [0.02, 0],
    'text-embedding-3-large': [0.13, 0],
    'text-embedding-ada-002': [0.1, 0],
};

const SNAPSHOT_DATE = /-\d{4}-\d{2}-\d{2}$/;
const PER_MILLION = 1_000_000;

// The price of a model by the name it was called with. A dated snapshot costs
// what its model does unless it has a price of its own, and a vendor prefix
// ("openai/gpt-5", as OpenRouter names it) is ignored. Null for anything else:
// another company's model, or one that runs on the admin's own computer.
export function aiModelPrice(model: string): readonly [number, number] | null {
    const name = model.trim().toLowerCase();
    const bare = name.slice(name.lastIndexOf('/') + 1);
    return PRICES[bare] ?? PRICES[bare.replace(SNAPSHOT_DATE, '')] ?? null;
}

export interface AiUsage {
    modelUsed: string;
    promptTokens: number;
    completionTokens: number;
}

export interface AiCostEstimate {
    cost: number;
    // Models that used tokens and have no price here, so are not in the cost.
    unpriced: string[];
}

export function estimateAiCost(usage: AiUsage[]): AiCostEstimate {
    let cost = 0;
    const unpriced = new Set<string>();

    for (const row of usage) {
        const price = aiModelPrice(row.modelUsed);
        if (price) {
            cost += (row.promptTokens * price[0] + row.completionTokens * price[1]) / PER_MILLION;
        } else if (row.promptTokens + row.completionTokens > 0) {
            unpriced.add(row.modelUsed);
        }
    }

    return { cost, unpriced: [...unpriced].sort((a, b) => a.localeCompare(b)) };
}
