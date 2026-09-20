import RadioTab from './RadioTab';
import PageHeader from '../../../components/Client/Primitives/PageHeader';

export default function RadioPage() {
    return (
        <div className="min-h-full pb-16">
            <PageHeader
                title="Radio"
                subtitle="Live stations by country and genre."
            />
            <div className="px-8 pt-6">
                <RadioTab />
            </div>
        </div>
    );
}
