export interface LocalMetadataModeOption {
    value: number;
    label: string;
    help: string;
}

// The numbers are the server's LocalMetadataMode values.
export const LOCAL_METADATA_MODES: readonly LocalMetadataModeOption[] = [
    {
        value: 0,
        label: 'Off',
        help: 'NFO files are ignored. Titles are matched from their file and folder names.',
    },
    {
        value: 1,
        label: 'For matching only',
        help: 'An NFO beside a title tells Vora which movie or show it is. Every detail still comes from the metadata provider.',
    },
    {
        value: 2,
        label: 'Prefer NFO details',
        help: 'The NFO says which title it is and its details are used first. The metadata provider fills in whatever the NFO leaves out.',
    },
];
