using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using BlockCache = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Autodesk.AutoCAD.Geometry2.TxEntity>>;

namespace TX_TOOLBOX
{
    public static class FileCache
    {
        private const int CACHE_SIZE = 10;
        private static SynchronizedCache<string, BlockCache> cacheLoad = new SynchronizedCache<string, BlockCache>(CACHE_SIZE);

        private static SemaphoreSlim fileSemaphore = new SemaphoreSlim(0, CACHE_SIZE);    //设置文件读取信号量,最大为缓存池大小,信号量数即为文件数
        private static LinkedList<string> fileToLoad = new LinkedList<string>();       //用双向链表模拟双端队列,增加一个读写锁保证线程安全
        private static ReaderWriterLockSlim fileQueueLocker = new ReaderWriterLockSlim();

        private static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(2, 2);    //设置两个信号量，同时读取文件限制为两个线程
        private static AutoResetEvent resetEvent = new AutoResetEvent(true);  //设置一个自动锁,完成一个读取时拨动一次
        private static string lastLoadCache = string.Empty;
        private static ReaderWriterLockSlim lastLoadLocker = new ReaderWriterLockSlim();

        private static Task wokingLoadTask = null;
        private static HashSet<string> workingLoadFiles = new HashSet<string>();

        private static void LoadFileToCache()
        {
            fileSemaphore.Wait();  //等待直到有文件加载请求

            string fileName;
            fileQueueLocker.EnterWriteLock();
            try
            {
                fileName = fileToLoad.First.Value;
                fileToLoad.RemoveFirst();
            }
            finally
            {
                fileQueueLocker.ExitWriteLock();
            }

            //先尝试读取
            if (cacheLoad.Contains(fileName) || !File.Exists(fileName))
            {
                semaphoreSlim.Release();   //释放当前线程信号量
                return;
            }

            //再尝试加载
            bool loadState = false;
            try
            {
                workingLoadFiles.Add(fileName);
                var blockDataResult = new BlockWithOutData();
                var dxfReader = new ReadDXF();
                var dtEntsResult = dxfReader.ImportDXFFile(fileName, blockDataResult);
                BlockCache cache = dxfReader.blocks;
                cacheLoad.Add(fileName, cache);
                loadState = true;
            }
            finally
            {
                workingLoadFiles.Remove(fileName);
            }

            if (loadState)
            {
                //加载保护机制，通知加载成功
                lastLoadLocker.EnterWriteLock();
                try
                {
                    lastLoadCache = fileName;
                    resetEvent.Set();
                }
                finally
                {
                    lastLoadLocker.ExitWriteLock();
                }
            }
            semaphoreSlim.Release();   //释放当前线程信号量
        }

        /// <summary>
        /// 处理加载请求,需要以单独的线程启动
        /// </summary>
        private static void DoLoadFileWork()
        {
            while (true)
            {
                semaphoreSlim.Wait();
                Task.Factory.StartNew(() => LoadFileToCache());
            }
        }

        private static bool CheckExistOrLoadingFile(string fileName)
        {
            fileQueueLocker.EnterReadLock();
            try
            {
                if (workingLoadFiles.Contains(fileName) || cacheLoad.Contains(fileName) || fileToLoad.Contains(fileName))
                {
                    return true;
                }
            }
            finally
            {
                fileQueueLocker.ExitReadLock();
            }
            return false;
        }


        public static void PostReadFileRequest(string fileName)
        {
            if (CheckExistOrLoadingFile(fileName)) { return; }

            //最后添加的加载请求均放在队头
            fileQueueLocker.EnterWriteLock();
            try
            {
                fileToLoad.AddFirst(fileName);
                fileSemaphore.Release();

                //每增加一个请求验证一次,超出队列容量时移除队尾元素
                if (fileSemaphore.CurrentCount == CACHE_SIZE)
                {
                    fileToLoad.RemoveLast();
                    fileSemaphore.Wait(1);  //这里扣除信号量但不等待
                }
            }
            finally
            {
                fileQueueLocker.ExitWriteLock();
            }

            //启动加载线程
            if (wokingLoadTask == null || wokingLoadTask.IsCompleted)
            {
                wokingLoadTask = Task.Factory.StartNew(() => DoLoadFileWork());
            }
        }

        public static BlockCache GetFileCache(string fileName)
        {
            BlockCache cache;
            if (cacheLoad.Read(fileName, out cache))
            {
                return cache;
            }

            //没有缓存的情况下,需要发出请求，并等待请求返回
            PostReadFileRequest(fileName);

            //等待
            while (resetEvent.WaitOne(20000))  //设置最多等待20秒，这里是采用的主线程等待的
            {
                lastLoadLocker.EnterReadLock();
                try
                {
                    if (lastLoadCache == fileName)
                    {
                        if (cacheLoad.Read(fileName, out cache))
                        {
                            return cache;
                        }
                        else
                        {
                            throw new FileNotFoundException("");
                        }
                    }
                }
                finally
                {
                    lastLoadLocker.ExitReadLock();
                }
            }

            return null;
        }
    }
}
