interface ArtMosaicProps {
    urls: string[];
    className?: string;
}

export default function ArtMosaic({ urls, className = '' }: ArtMosaicProps) {
    const tile = 'h-full w-full object-cover';
    if (urls.length >= 4) {
        return (
            <div className={`grid h-full w-full grid-cols-2 grid-rows-2 ${className}`}>
                {urls.slice(0, 4).map((url, i) => <img key={i} src={url} alt="" className={tile} />)}
            </div>
        );
    }
    if (urls.length === 3) {
        return (
            <div className={`flex h-full w-full ${className}`}>
                <img src={urls[0]} alt="" className="h-full w-1/2 object-cover" />
                <div className="flex h-full w-1/2 flex-col">
                    <img src={urls[1]} alt="" className="h-1/2 w-full object-cover" />
                    <img src={urls[2]} alt="" className="h-1/2 w-full object-cover" />
                </div>
            </div>
        );
    }
    return (
        <div className={`flex h-full w-full ${className}`}>
            {urls.slice(0, 2).map((url, i) => <img key={i} src={url} alt="" className="h-full w-1/2 object-cover" />)}
        </div>
    );
}
