using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace TX_TOOLBOX
{
    /// <summary>
    /// 线程同步的缓存
    /// </summary>
    public class SynchronizedCache<TKey, TCache>
    {
        public SynchronizedCache() : this(4) { }

        public SynchronizedCache(int capacity)
        {
            if (capacity <= 0) { throw new ArgumentException("capacity must be greater than zero!"); }

            this.capacity = capacity;
            innerCache = new LruCache<TKey, TCache>(capacity);
        }

        private ReaderWriterLockSlim locker = new ReaderWriterLockSlim();  //LruCache是非线程安全的，读写时加锁
        private int capacity = 0;
        private LruCache<TKey, TCache> innerCache;

        public bool Contains(TKey key)
        {
            locker.EnterReadLock();
            try
            {
                return innerCache.Contains(key, false);
            }
            finally
            {
                locker.ExitReadLock();
            }
        }

        public bool Read(TKey key, out TCache cache)
        {
            locker.EnterReadLock();
            try
            {
                return innerCache.Get(key, out cache);
            }
            finally
            {
                locker.ExitReadLock();
            }
        }

        public void Add(TKey key, TCache cache)
        {
            locker.EnterWriteLock();
            try
            {
                innerCache.Put(key, cache);
            }
            finally
            {
                locker.ExitWriteLock();
            }
        }

        public void AddOrUpdate(TKey key, TCache cache)
        {
            locker.EnterUpgradeableReadLock();
            try
            {
                innerCache.Put(key, cache);
            }
            finally
            {
                locker.ExitUpgradeableReadLock();
            }
        }

        ~SynchronizedCache()
        {
            if (locker != null)
            {
                locker.Dispose();
            }
        }

    }
}
