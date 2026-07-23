import MusicTab from './MusicTab';
import PageHeader from '../../../components/Client/Primitives/PageHeader';

export default function MusicPage() {
    return (
        <div className="min-h-full pb-16">
            <PageHeader
                title="Music"
                subtitle="Your artists, albums, mixes, and stations."
            />
            <div className="px-8 pt-6">
                <MusicTab />
            </div>
        </div>
    );
}
