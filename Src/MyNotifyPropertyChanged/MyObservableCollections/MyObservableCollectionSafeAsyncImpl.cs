using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using BtmI2p.MiscUtils;
using BtmI2p.Newtonsoft.Json;
using Nito.AsyncEx;
using Xunit;

namespace BtmI2p.MyNotifyPropertyChanged.MyObservableCollections
{
    public enum EMyCollectionChangedAction
    {
        Reset,
        ItemsChanged,
        ItemsRangeRemoved,
        NewItemsRangeInserted
    }

    public class MyObservableCollectionChangedArgsComparer<TItem> : IComparer<MyNotifyCollectionChangedArgs<TItem>>
    {
        public int Compare(MyNotifyCollectionChangedArgs<TItem> x, MyNotifyCollectionChangedArgs<TItem> y)
        {
            Assert.NotNull(x);
            Assert.NotNull(y);
            return x.ChangesNum.CompareTo(y.ChangesNum);
        }
    }

    public class MyNotifyCollectionChangedArgs<TItem>
    {
        public EMyCollectionChangedAction ChangedAction { get; private set; }
        public long ChangesNum { get; private set; }
        /// <summary>
        /// Data copy, not origin
        /// </summary>
        public IList<TItem> OldItems { get; private set; } = null;
        public IList<int> OldItemsIndices { get; private set; } = null;
        /// <summary>
        /// Data copy, not origin
        /// </summary>
        public IList<TItem> NewItems { get; private set; } = null;
        public IList<int> NewItemsIndices { get; private set; } = null;

        public static MyNotifyCollectionChangedArgs<TItem> CreateReset(long changesNum)
        {
            return new MyNotifyCollectionChangedArgs<TItem>()
            {
                ChangedAction = EMyCollectionChangedAction.Reset,
                ChangesNum = changesNum
            };
        }

        public static MyNotifyCollectionChangedArgs<TItem> CreateItemChanged(
            long changesNum,
            int changedItemIndex,
            TItem oldItemDeepCopy,
            TItem newItemDeepCopy
            )
        {
            return new MyNotifyCollectionChangedArgs<TItem>()
            {
                ChangedAction = EMyCollectionChangedAction.ItemsChanged,
                ChangesNum = changesNum,
                OldItemsIndices = new [] { changedItemIndex},
                NewItemsIndices = new [] { changedItemIndex},
                OldItems = new [] { oldItemDeepCopy },
                NewItems = new [] { newItemDeepCopy }
            };
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="changedNum"></param>
        /// <param name="changedItemsEnumerable">index, old, new</param>
        /// <returns></returns>
        public static MyNotifyCollectionChangedArgs<TItem> CreateMultipleItemsChanged(
            long changedNum,
            IList<Tuple<int, TItem, TItem>> changedItemsEnumerable
            )
        {
            Assert.NotNull(changedItemsEnumerable);
            Assert.False(changedItemsEnumerable.Any(_ => _ == null));
            var indices = changedItemsEnumerable.Select(_ => _.Item1).ToList();
            Assert.False(indices.Any(_ => _ < 0));
            Assert.Equal(indices, indices.Distinct());
            var oldItemsIndices = new List<int>(changedItemsEnumerable.Count);
            var newItemsIndices = new List<int>(changedItemsEnumerable.Count);
            var oldItems = new List<TItem>(changedItemsEnumerable.Count);
            var newItems = new List<TItem>(changedItemsEnumerable.Count);
            foreach (Tuple<int, TItem, TItem> changedItemTuple in changedItemsEnumerable)
            {
                Assert.NotNull(changedItemTuple);
                oldItemsIndices.Add(changedItemTuple.Item1);
                newItemsIndices.Add(changedItemTuple.Item1);
                oldItems.Add(MiscFuncs.GetDeepCopy(changedItemTuple.Item2));
                newItems.Add(MiscFuncs.GetDeepCopy(changedItemTuple.Item3));
            }
            return new MyNotifyCollectionChangedArgs<TItem>()
            {
                ChangesNum = changedNum,
                ChangedAction = EMyCollectionChangedAction.ItemsChanged,
                NewItems = newItems,
                NewItemsIndices = newItemsIndices,
                OldItemsIndices = oldItemsIndices,
                OldItems = oldItems
            };
        }

        public static MyNotifyCollectionChangedArgs<TItem> CreateRangeRemoved(
            long changesNum,
            int rangeStartIndex,
            IList<TItem> oldItems
            )
        {
            return new MyNotifyCollectionChangedArgs<TItem>()
            {
                ChangedAction = EMyCollectionChangedAction.ItemsRangeRemoved,
                ChangesNum = changesNum,
                OldItems = oldItems,
                OldItemsIndices = new[] { rangeStartIndex }
            };
        }

        public static MyNotifyCollectionChangedArgs<TItem> CreateNewItemsInserted(
            long changesNum,
            int insertIndex,
            IList<TItem> newItemsDeepCopy
        )
        {
            return new MyNotifyCollectionChangedArgs<TItem>()
            {
                ChangedAction = EMyCollectionChangedAction.NewItemsRangeInserted,
                ChangesNum = changesNum,
                NewItemsIndices = new[] {insertIndex},
                NewItems = newItemsDeepCopy
            };
        }
    }
    /**/
    public interface IMyNotifyCollectionChanged<TItem>
    {
        /**/
        [JsonIgnore]
        IObservable<MyNotifyCollectionChangedArgs<TItem>> CollectionChangedObservable { get; }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>Pair of currentChangesNum, current data copy</returns>
        Task<MyNotifyCollectionChangedArgs<TItem>> GetDeepCopyAsync();
    }
    /**/
    public interface IMySafeCollectionAsync<TItem>
    {
        Task<IList<int>> IndexesOfAsync(Func<TItem,bool> predicate);
        Task<TItem> FirstOrDefaultDeepCopyAsync(Func<TItem, bool> predicate = null);
        Task<TItem> LastOrDefaultDeepCopyAsync(Func<TItem, bool> predicate = null);
        Task ClearAsync();
        Task ChangeAtAsync(int index, Action<TItem> changeAction);
        Task ReplaceItemAtAsync(int index, TItem newItem);
        Task ReplaceBulkAsync(IList<Tuple<int, TItem>> newItems);
        Task AddRangeAsync(IList<TItem> newItems);
        Task InsertRangeAtAsync(int index, IList<TItem> newItems);
        Task<TItem> GetItemDeepCopyAtAsync(int index);
        Task RemoveRangeAsync(int index, int count);
        Task<int> RemoveWhereAsync(Func<TItem, bool> predicate);
        Task<int> CountAsync();
        Task<IList<TItem>> WhereAsync(Func<TItem, bool> predicate);
        Task<int> InsertBinaryAsync(TItem item, IComparer<TItem> comparer);
    }
    /**/
    public class MyObservableCollectionSafeAsyncImpl<TItem>
        : IMyNotifyCollectionChanged<TItem>, IMySafeCollectionAsync<TItem>
    {
        private readonly Subject<MyNotifyCollectionChangedArgs<TItem>> _collectionChangedSubject
            = new Subject<MyNotifyCollectionChangedArgs<TItem>>();

        public IObservable<MyNotifyCollectionChangedArgs<TItem>> CollectionChangedObservable 
            => _collectionChangedSubject;

        private readonly AsyncReaderWriterLock _lockSem
            = new AsyncReaderWriterLock();

        private long _changesCounter = 0;
        private long NowChangesCounter => Interlocked.Read(ref _changesCounter);
        private long NextChangesCounter => Interlocked.Increment(ref _changesCounter);
        private readonly List<TItem> _list = new List<TItem>(); 
        public async Task<MyNotifyCollectionChangedArgs<TItem>> GetDeepCopyAsync()
        {
            using (await _lockSem.ReaderLockAsync().ConfigureAwait(false))
            {
                return MyNotifyCollectionChangedArgs<TItem>.CreateNewItemsInserted(
                    NowChangesCounter,
                    0,
                    MiscFuncs.GetDeepCopy(_list)
                );
            }
        }

        public async Task<IList<int>> IndexesOfAsync(Func<TItem, bool> predicate)
        {
            using (await _lockSem.ReaderLockAsync().ConfigureAwait(false))
            {
                var result = new List<int>();
                for (var i = 0; i < _list.Count; i++)
                {
                    if(predicate(_list[i]))
                        result.Add(i);
                }
                return result;
            }
        }

        public async Task<TItem> FirstOrDefaultDeepCopyAsync(Func<TItem, bool> predicate = null)
        {
            using (await _lockSem.ReaderLockAsync().ConfigureAwait(false))
            {
                return MiscFuncs.GetDeepCopy(predicate == null 
                    ? _list.FirstOrDefault() 
                    : _list.FirstOrDefault(predicate));
            }
        }

        public async Task<TItem> LastOrDefaultDeepCopyAsync(Func<TItem, bool> predicate = null)
        {
            using (await _lockSem.ReaderLockAsync().ConfigureAwait(false))
            {
                return MiscFuncs.GetDeepCopy(predicate == null 
                    ? _list.LastOrDefault() 
                    : _list.LastOrDefault(predicate));
            }
        }

        public async Task ClearAsync()
        {
            long nextCounter;
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                _list.Clear();
                nextCounter = NextChangesCounter;
            }
            _collectionChangedSubject.OnNext(
                MyNotifyCollectionChangedArgs<TItem>.CreateReset(nextCounter)
            );
        }

        public async Task ChangeAtAsync(int index, Action<TItem> changeAction)
        {
            TItem oldItemDeepCopy, newItemDeepCopy;
            long nextCounter;
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                if (index < 0 || index >= _list.Count)
                    throw new IndexOutOfRangeException();
                oldItemDeepCopy = MiscFuncs.GetDeepCopy(_list[index]);
                changeAction(_list[index]);
                newItemDeepCopy = MiscFuncs.GetDeepCopy(_list[index]);
                nextCounter = NextChangesCounter;
            }
            _collectionChangedSubject.OnNext(
                MyNotifyCollectionChangedArgs<TItem>.CreateItemChanged(
                    nextCounter,
                    index,
                    oldItemDeepCopy,
                    newItemDeepCopy
                )
            );
        }

        public async Task ReplaceItemAtAsync(int index, TItem newItem)
        {
            TItem oldItemDeepCopy, newItemDeepCopy;
            long nextCounter;
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                if (index < 0 || index >= _list.Count)
                    throw new IndexOutOfRangeException();
                oldItemDeepCopy = MiscFuncs.GetDeepCopy(_list[index]);
                _list[index] = newItem;
                newItemDeepCopy = MiscFuncs.GetDeepCopy(_list[index]);
                nextCounter = NextChangesCounter;
            }
            _collectionChangedSubject.OnNext(
                MyNotifyCollectionChangedArgs<TItem>.CreateItemChanged(
                    nextCounter,
                    index,
                    oldItemDeepCopy,
                    newItemDeepCopy
                )
            );
        }

        public async Task AddRangeAsync(IList<TItem> newItems)
        {
            Assert.NotNull(newItems);
            int insertIndex;
            long nextCounter;
            List<TItem> newItemsDeepCopy;
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                insertIndex = _list.Count;
                newItemsDeepCopy = MiscFuncs.GetDeepCopy(newItems.ToList());
                _list.AddRange(newItems);
                nextCounter = NextChangesCounter;
            }
            _collectionChangedSubject.OnNext(
                MyNotifyCollectionChangedArgs<TItem>.CreateNewItemsInserted(
                    nextCounter,
                    insertIndex,
                    newItemsDeepCopy
                )
            );
        }

        public async Task InsertRangeAtAsync(int index, IList<TItem> newItems)
        {
            long nextCounter;
            List<TItem> newItemsDeepCopy;
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                if (index < 0 || index > _list.Count)
                    throw new IndexOutOfRangeException();
                newItemsDeepCopy = MiscFuncs.GetDeepCopy(newItems.ToList());
                _list.InsertRange(index,newItems);
                nextCounter = NextChangesCounter;
            }
            _collectionChangedSubject.OnNext(
                MyNotifyCollectionChangedArgs<TItem>.CreateNewItemsInserted(
                    nextCounter,
                    index,
                    newItemsDeepCopy
                )
            );
        }

        public async Task<TItem> GetItemDeepCopyAtAsync(int index)
        {
            using (await _lockSem.ReaderLockAsync().ConfigureAwait(false))
            {
                if (index < 0 || index >= _list.Count)
                    throw new IndexOutOfRangeException();
                return MiscFuncs.GetDeepCopy(_list[index]);
            }
        }

        public async Task RemoveRangeAsync(int index, int count)
        {
            long nextCounter;
            List<TItem> oldItems;
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                Assert.True(count > 0);
                if (index < 0 || index >= _list.Count)
                    throw new IndexOutOfRangeException();
                if(index + (count - 1) >= _list.Count)
                    throw new IndexOutOfRangeException();
                oldItems = _list.GetRange(index, count);
                _list.RemoveRange(index, count);
                nextCounter = NextChangesCounter;
            }
            _collectionChangedSubject.OnNext(
                MyNotifyCollectionChangedArgs<TItem>.CreateRangeRemoved(
                    nextCounter,
                    index,
                    oldItems
                )
            );
        }

        public async Task<int> RemoveWhereAsync(Func<TItem, bool> predicate)
        {
            Assert.NotNull(predicate);
            var changedArgsList = new List<MyNotifyCollectionChangedArgs<TItem>>();
            int result = 0;
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                var indicesToRemove = new List<int>();
                foreach (
                    var source in _list
                        .Select((item,index) => Tuple.Create(item,index))
                        .Reverse()
                        .Where(_ => predicate(_.Item1))
                        .ToList()
                )
                {
                    indicesToRemove.Add(source.Item2);
                    changedArgsList.Add(
                        MyNotifyCollectionChangedArgs<TItem>.CreateRangeRemoved(
                            NextChangesCounter,
                            source.Item2,
                            new[] { source.Item1 }
                        )
                    );
                    result++;
                }
                foreach (var i in indicesToRemove)
                {
                    _list.RemoveAt(i);
                }
            }
            foreach (var changedArgs in changedArgsList)
            {
               _collectionChangedSubject.OnNext(changedArgs);
            }
            return result;
        }

        public async Task<int> CountAsync()
        {
            using (await _lockSem.ReaderLockAsync().ConfigureAwait(false))
            {
                return _list.Count;
            }
        }

        public async Task<IList<TItem>> WhereAsync(Func<TItem, bool> predicate)
        {
            Assert.NotNull(predicate);
            using (await _lockSem.ReaderLockAsync().ConfigureAwait(false))
            {
                return MiscFuncs.GetDeepCopy(_list.Where(predicate).ToList());
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        /// <param name="comparer"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<int> InsertBinaryAsync(TItem item, IComparer<TItem> comparer)
        {
            Assert.NotNull(comparer);
            long nextCounter;
            IList<TItem> newItemsDeepCopy;
            int insertIndex = -1;
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                if (_list.Count == 0)
                {
                    insertIndex = 0;
                }
                else if (_list.Count == 1)
                {
                    if (comparer.Compare(item, _list[0]) < 0)
                    {
                        insertIndex = 0;
                    }
                    else
                    {
                        insertIndex = 1;
                    }
                }
                else
                {
                    var lastIndex = _list.Count - 1;
                    if (comparer.Compare(item, _list[0]) < 0)
                    {
                        insertIndex = 0;
                    }
                    else if (comparer.Compare(item, _list[lastIndex]) >= 0)
                    {
                        insertIndex = lastIndex + 1;
                    }
                    else
                    {
                        int min = 0;
                        int max = _list.Count - 2;
                        Func<int,int> indexComparator = index =>
                        {
                            var t1 = comparer.Compare(item, _list[index]);
                            var t2 = comparer.Compare(item, _list[index + 1]);
                            if (
                                t1 >= 0
                                && t2 < 0
                            )
                                return 0; //Found it;
                            if (t1 >= 0 && t2 >= 0)
                                return -1;
                            return 1;
                        };
                        while (min <= max)
                        {
                            int mid = (min + max) / 2;
                            int comparison = indexComparator(mid);
                            if (comparison == 0)
                            {
                                insertIndex = (mid + 1);
                                break;
                            }
                            if (comparison < 0)
                            {
                                min = mid + 1;
                            }
                            else
                            {
                                max = mid - 1;
                            }
                        }
                    }
                }
                if(insertIndex == -1)
                    throw new InvalidOperationException();
                newItemsDeepCopy = new[] { MiscFuncs.GetDeepCopy(item) };
                _list.Insert(insertIndex, item);
                nextCounter = NextChangesCounter;
            }
            _collectionChangedSubject.OnNext(
                MyNotifyCollectionChangedArgs<TItem>.CreateNewItemsInserted(
                    nextCounter,
                    insertIndex,
                    newItemsDeepCopy
                )
            );
            return insertIndex;
        }

        public async Task ReplaceBulkAsync(IList<Tuple<int, TItem>> newItems)
        {
            long nextCounter;
            Assert.NotNull(newItems);
            Assert.False(newItems.Any(_ => _ == null));
            var indices = newItems.Select(_ => _.Item1).ToList();
            Assert.Equal(indices, indices.Distinct());
            var changedArgsParam = new List<Tuple<int, TItem, TItem>>();
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                var currentCount = _list.Count;
                Assert.True(newItems.All(_ => _.Item1 >= 0 && _.Item1 < currentCount));
                changedArgsParam.AddRange(
                    newItems.Select(
                        newItem => Tuple.Create(
                            newItem.Item1, 
                            MiscFuncs.GetDeepCopy(_list[newItem.Item1]), 
                            MiscFuncs.GetDeepCopy(newItem.Item2)
                        )
                    )
                );
                foreach (var newItem in newItems)
                {
                    _list[newItem.Item1] = newItem.Item2;
                }
                nextCounter = NextChangesCounter;
            }
            _collectionChangedSubject.OnNext(
                MyNotifyCollectionChangedArgs<TItem>.CreateMultipleItemsChanged(
                    nextCounter,
                    changedArgsParam
                )
            );
        }

        public async Task TouchBulkAsync(IList<int> indexes)
        {
            Assert.NotNull(indexes);
            if (!indexes.Any())
                return;
            Assert.Equal(indexes,indexes.Distinct());
            long nextCounter;
            var changedArgsParam = new List<Tuple<int, TItem, TItem>>();
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                foreach (int i in indexes)
                {
                    Assert.InRange(i, 0, _list.Count-1);
                    var copy1 = MiscFuncs.GetDeepCopy(_list[i]);
                    var copy2 = MiscFuncs.GetDeepCopy(_list[i]);
                    changedArgsParam.Add(
                        Tuple.Create(
                            i,
                            copy1,
                            copy2
                        )
                    );
                }
                nextCounter = NextChangesCounter;
            }
            _collectionChangedSubject.OnNext(
                MyNotifyCollectionChangedArgs<TItem>.CreateMultipleItemsChanged(
                    nextCounter,
                    changedArgsParam
                )
            );
        }

        public async Task ResetAsync()
        {
            long nextCounter;
            using (await _lockSem.WriterLockAsync().ConfigureAwait(false))
            {
                nextCounter = NextChangesCounter;
            }
            _collectionChangedSubject.OnNext(
                MyNotifyCollectionChangedArgs<TItem>.CreateReset(nextCounter)
            );
        }
    }
}