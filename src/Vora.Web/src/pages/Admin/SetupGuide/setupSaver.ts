import { createContext, useContext, useEffect } from 'react';

export type StepSaver = () => Promise<void>;
export type RegisterStepSaver = (saver: StepSaver) => () => void;

export const SetupSaverContext = createContext<RegisterStepSaver>(() => () => { });

export function useStepSaver(saver: StepSaver) {
    const register = useContext(SetupSaverContext);
    useEffect(() => register(saver), [register, saver]);
}
