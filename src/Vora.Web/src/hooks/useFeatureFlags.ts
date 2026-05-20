import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { featureFlagsService, type FeatureFlagsVM, DEFAULT_FEATURE_FLAGS } from '../api/System/featureFlagsService';
import { serverVault } from '../utils/serverVault';

export function useFeatureFlags(): FeatureFlagsVM {
    const { serverId } = useParams<{ serverId?: string }>();
    const [flags, setFlags] = useState<FeatureFlagsVM>(DEFAULT_FEATURE_FLAGS);

    useEffect(() => {
        let cancelled = false;
        const targetServerId = serverId ?? serverVault.getActiveServerId() ?? undefined;
        if (!targetServerId) {
            setFlags(DEFAULT_FEATURE_FLAGS);
            return;
        }
        featureFlagsService.getFeatureFlags(targetServerId)
            .then(data => {
                if (!cancelled) setFlags(data);
            })
            .catch(err => {
                console.error('Failed to load feature flags', err);
                if (!cancelled) setFlags(DEFAULT_FEATURE_FLAGS);
            });
        return () => { cancelled = true; };
    }, [serverId]);

    return flags;
}
