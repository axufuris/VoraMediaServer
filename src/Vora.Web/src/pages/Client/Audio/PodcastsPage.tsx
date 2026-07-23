import PodcastsTab from './PodcastsTab';
import PageHeader from '../../../components/Client/Primitives/PageHeader';

export default function PodcastsPage() {
    return (
        <div className="min-h-full pb-16">
            <PageHeader
                title="Podcasts"
                subtitle="Your subscriptions and the latest episodes."
            />
            <div className="px-8 pt-6">
                <PodcastsTab />
            </div>
        </div>
    );
}
