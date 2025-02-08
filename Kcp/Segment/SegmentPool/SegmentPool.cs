using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Kcp.SegmentPool
{
    public sealed class SegmentPool<T> : IDisposable where T : class, new()
    {
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly object _lock = new object();
        
        public IReadOnlyCollection<T> SharedPool => _queue;

        public SegmentPool(uint initialSize)
        {
            for (var i = 0; i < initialSize; i++)
            {
                _queue.Enqueue(new T());
            }
        }

        private T CreateSegment() => new T();

        private void Create()
        {
            lock (_lock)
            {
                var segment = CreateSegment();
                _queue.Enqueue(segment);
            }
        } 

        public T Rent()
        {
            try
            {
                if(_queue.TryDequeue(out var segment)) return segment;
                
                Create();
                return Rent();

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
        }

        public void Return(T segment)
        {
            if (segment is null) throw new ArgumentNullException(nameof(segment));
            
            if (segment is IDisposable disposable) disposable.Dispose(); //通过dispose方法重置
            else throw new MissingMethodException("Missing Dispose Method");
            
            _queue.Enqueue(segment); //放入缓存池
        }

        public int GetPoolCount() => _queue.Count;
        public void Dispose()
        {
            _queue.Clear();
        }
    }
}