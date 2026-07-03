using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AppleInterop;

internal static class BlockStateManager
{
    private static readonly Dictionary<long, TaskCompletionSource<bool>> _pending = new();
    private static long _nextId;
    private static readonly Lock _lock = new();

    public static long Add(TaskCompletionSource<bool> tcs)
    {
        lock (_lock)
        {
            var id = ++_nextId;
            _pending[id] = tcs;
            return id;
        }
    }

    public static TaskCompletionSource<bool>? Remove(long id)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(id, out var tcs))
            {
                _pending.Remove(id);
                return tcs;
            }
            return null;
        }
    }
}
