using System.Collections.Concurrent;
using Vora.Application.Streaming;

namespace Vora.Infrastructure.Transcoding;

/// <summary>The recording conversions the DVR post-processor is running.</summary>
public sealed class DvrEncodes : IEncodeSource
{
    private readonly ConcurrentDictionary<Guid, Encode> _running = new();

    /// <summary>Records a conversion as running until the returned handle is disposed.</summary>
    public Encode Begin(Guid key, bool onGpu)
    {
        var encode = new Encode(this, key, onGpu);
        _running[key] = encode;
        return encode;
    }

    public IReadOnlyList<RunningEncode> RunningEncodes(Guid except) =>
        _running.Where(running => running.Key != except).Select(running => new RunningEncode(running.Value.OnGpu)).ToList();

    public sealed class Encode : IDisposable
    {
        private readonly DvrEncodes _owner;
        private readonly Guid _key;
        private volatile bool _onGpu;

        internal Encode(DvrEncodes owner, Guid key, bool onGpu)
        {
            _owner = owner;
            _key = key;
            _onGpu = onGpu;
        }

        public bool OnGpu => _onGpu;

        /// <summary>The conversion carries on on the CPU after the GPU failed it.</summary>
        public void MovedToCpu() => _onGpu = false;

        public void Dispose() => _owner._running.TryRemove(new KeyValuePair<Guid, Encode>(_key, this));
    }
}
