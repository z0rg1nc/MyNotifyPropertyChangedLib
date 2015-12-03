using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using BtmI2p.MiscUtils;
using BtmI2p.ObjectStateLib;
using NLog;
using Xunit;

namespace BtmI2p.MyNotifyPropertyChanged.MyObservableCollections
{
    public interface IMyObservableCollectionProxyN
    {
        int N { get; set; }
        IObservable<int> NChangedObservable { get; } 
    }

    public class MyObservableCollectionProxyN : IMyObservableCollectionProxyN
    {
        public MyObservableCollectionProxyN(int n)
        {
            Assert.True(n >= 0);
            _n = n;
        }

        private int _n;
        public int N {
            get
            {
                return _n;
            }
            set
            {
                Assert.True(value >= 0);
                _n = value;
                _nChangedSubject.OnNext(value);
            }
        }
        private readonly Subject<int> _nChangedSubject = new Subject<int>();
        public IObservable<int> NChangedObservable => _nChangedSubject;
    }

    public class MyNLastObservableCollectionImpl<TItem>
        : IMyNotifyCollectionChanged<TItem>, IMyAsyncDisposable
    {
        private MyNLastObservableCollectionImpl()
        {
        }
        private IMyNotifyCollectionChanged<TItem> _originCollection;
        private int _n;
        private readonly List<TItem> _originElementsCopy = new List<TItem>(); 
        public static async Task<MyNLastObservableCollectionImpl<TItem>> CreateInstance(
            IMyNotifyCollectionChanged<TItem> collection,
            IMyObservableCollectionProxyN proxyN
            )
        {
            Assert.NotNull(collection);
            Assert.NotNull(proxyN);
            var result = new MyNLastObservableCollectionImpl<TItem>();
            result._originCollection = collection;
            result._n = proxyN.N;
            result._stateHelper.SetInitializedState();
            result._subscriptions.Add(
                collection.CollectionChangedObservable.Subscribe(
                    _ =>
                    {
                        if(result._changedArgsDict.TryAdd(_.ChangesNum,_))
                            result.ProcessNewChangedArgs();
                    }
                )
            );
            result._subscriptions.Add(
                proxyN.NChangedObservable.Subscribe(
                    result.UpdateN
                )
            );
            using (await result._lockSem.GetDisposable().ConfigureAwait(false))
            {
                await result.ResetActionAsync().ConfigureAwait(false);
            }
            result.ProcessNewChangedArgs();
            return result;
        }
        /**/
        private async Task ResetActionAsync()
        {
            await _resultCollection.ClearAsync().ConfigureAwait(false);
            _originElementsCopy.Clear();
            var deepCopyArgs = await _originCollection.GetDeepCopyAsync().ConfigureAwait(false);
            _changedArgsDict[deepCopyArgs.ChangesNum] = deepCopyArgs;
            _prevChangesCounter = deepCopyArgs.ChangesNum - 1;
        }
        private async void UpdateN(int newN)
        {
            try
            {
                using (_stateHelper.GetFuncWrapper())
                {
                    Assert.True(newN >= 0);
                    using (await _lockSem.GetDisposable().ConfigureAwait(false))
                    {
                        var oldN = _n;
                        _n = newN;
                        var originCount = _originElementsCopy.Count;
                        var oldResultCount = originCount > oldN ? oldN : originCount;
                        var newResultCount = originCount > newN ? newN : originCount;
                        if (newResultCount < oldResultCount)
                        {
                            await _resultCollection.RemoveRangeAsync(
                                0,
                                oldResultCount - newResultCount
                            ).ConfigureAwait(false);
                        }
                        else if (newResultCount > oldResultCount)
                        {
                            await _resultCollection.InsertRangeAtAsync(
                                0,
                                _originElementsCopy
                                    .Skip(originCount - newResultCount)
                                    .Take(newResultCount - oldResultCount)
                                    .ToList()
                            ).ConfigureAwait(false);
                        }
                    }
                    ProcessNewChangedArgs();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WrongDisposableObjectStateException)
            {
            }
            catch (Exception exc)
            {
                MiscFuncs.HandleUnexpectedError(exc, _log);
            }
        }
        private readonly SemaphoreSlim _lockSem = new SemaphoreSlim(1);
        private async void ProcessNewChangedArgs()
        {
            try
            {
                using (_stateHelper.GetFuncWrapper())
                {
                    var lockSemCalledWrapper = _lockSem.GetCalledWrapper();
                    lockSemCalledWrapper.Called = true;
                    using (await _lockSem.GetDisposable(true).ConfigureAwait(false))
                    {
                        while (!_cts.IsCancellationRequested && lockSemCalledWrapper.Called)
                        {
                            lockSemCalledWrapper.Called = false;
                            var currentChangesNumInDictToRemove =
                                _changedArgsDict.Keys.Where(_ => _ <= _prevChangesCounter).ToList();
                            foreach (long key in currentChangesNumInDictToRemove)
                            {
                                MyNotifyCollectionChangedArgs<TItem> removedArgs;
                                _changedArgsDict.TryRemove(key, out removedArgs);
                            }
                            MyNotifyCollectionChangedArgs<TItem> nextArgs;
                            if (
                                _changedArgsDict.TryRemove(
                                    _prevChangesCounter + 1,
                                    out nextArgs
                                    )
                                )
                            {
                                lockSemCalledWrapper.Called = true;
                                var currentN = _n;
                                if (nextArgs.ChangedAction == EMyCollectionChangedAction.Reset)
                                {
                                    await ResetActionAsync().ConfigureAwait(false);
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.NewItemsRangeInserted)
                                {
                                    var insertIndex = nextArgs.NewItemsIndices[0];
                                    var insertCount = nextArgs.NewItems.Count;
                                    if (insertCount > 0)
                                    {
                                        _originElementsCopy.InsertRange(insertIndex, nextArgs.NewItems);
                                        /* Update in result */
                                        var currentResultCollectionCount =
                                            await _resultCollection.CountAsync().ConfigureAwait(false);
                                        if (currentResultCollectionCount > 0)
                                        {
                                            int startResultIndex = (_originElementsCopy.Count - 1) -
                                                                   (currentResultCollectionCount - 1);
                                            var bulkReplaceArgs = new List<Tuple<int, TItem>>();
                                            for (int i = startResultIndex; i < insertIndex + insertCount; i++)
                                            {
                                                bulkReplaceArgs.Add(
                                                    Tuple.Create(
                                                        i - startResultIndex,
                                                        _originElementsCopy[i]
                                                    )
                                                );
                                            }
                                            if (bulkReplaceArgs.Any())
                                            {
                                                await _resultCollection.ReplaceBulkAsync(
                                                    bulkReplaceArgs
                                                ).ConfigureAwait(false);
                                            }
                                        }
                                        /* Insert start if presented */
                                        var newResultCollectionCount = _originElementsCopy.Count.With(
                                            _ => _ < currentN ? _ : currentN
                                        );
                                        if (newResultCollectionCount > currentResultCollectionCount)
                                        {
                                            var newResultCollectionStartIndex = 
                                                (_originElementsCopy.Count - 1) -
                                                (newResultCollectionCount - 1);
                                            Assert.True(newResultCollectionStartIndex >= 0);
                                            int insertRangeCount = newResultCollectionCount -
                                                                   currentResultCollectionCount;
                                            await _resultCollection.InsertRangeAtAsync(
                                                0,
                                                _originElementsCopy
                                                    .Skip(newResultCollectionStartIndex)
                                                    .Take(insertRangeCount)
                                                    .ToList()
                                            ).ConfigureAwait(false);
                                        }
                                    }
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved)
                                {
                                    int removeIndexStart = nextArgs.OldItemsIndices[0];
                                    int removeCount = nextArgs.OldItems.Count;
                                    if (removeCount > 0)
                                    {
                                        _originElementsCopy.RemoveRange(removeIndexStart, removeCount);
                                        /*Remove if needed*/
                                        var currentResultCollectionCount =
                                            await _resultCollection.CountAsync().ConfigureAwait(false);
                                        var newResultCollectionCount = _originElementsCopy.Count.With(
                                            _ => _ < currentN ? _ : currentN
                                        );
                                        if (newResultCollectionCount < currentResultCollectionCount)
                                        {
                                            await _resultCollection.RemoveRangeAsync(
                                                0,
                                                currentResultCollectionCount - newResultCollectionCount
                                            ).ConfigureAwait(false);
                                        }
                                        /*Replace*/
                                        if (newResultCollectionCount > 0)
                                        {
                                            var newResultCollectionStartIndex =
                                                (_originElementsCopy.Count - 1) -
                                                (newResultCollectionCount - 1);
                                            var bulkReplaceArgs = new List<Tuple<int, TItem>>();
                                            for (
                                                int i = newResultCollectionStartIndex;
                                                i < removeIndexStart;
                                                i++
                                            )
                                            {
                                                bulkReplaceArgs.Add(
                                                    Tuple.Create(
                                                        i - newResultCollectionStartIndex,
                                                        _originElementsCopy[i]
                                                    )
                                                );
                                            }
                                            if (bulkReplaceArgs.Any())
                                            {
                                                await _resultCollection.ReplaceBulkAsync(
                                                    bulkReplaceArgs
                                                ).ConfigureAwait(false);
                                            }
                                        }
                                    }
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                {
                                    /**/
                                    for (int i = 0; i < nextArgs.NewItems.Count; i++)
                                    {
                                        _originElementsCopy[nextArgs.NewItemsIndices[i]] = nextArgs.NewItems[i];
                                    }
                                    var currentResultCollectionCount =
                                        await _resultCollection.CountAsync().ConfigureAwait(false);
                                    if (currentResultCollectionCount > 0)
                                    {
                                        int startResultIndex = (_originElementsCopy.Count - 1) -
                                                                (currentResultCollectionCount - 1);
                                        var bulkReplaceArgs = new List<Tuple<int, TItem>>();
                                        for (int i = 0; i < nextArgs.NewItems.Count; i++)
                                        {
                                            if (nextArgs.NewItemsIndices[i] >= startResultIndex)
                                            {
                                                bulkReplaceArgs.Add(
                                                    Tuple.Create(
                                                        nextArgs.NewItemsIndices[i] - startResultIndex,
                                                        _originElementsCopy[nextArgs.NewItemsIndices[i]]
                                                    )
                                                );
                                            }
                                        }
                                        if (bulkReplaceArgs.Any())
                                        {
                                            await _resultCollection.ReplaceBulkAsync(
                                                bulkReplaceArgs
                                            ).ConfigureAwait(false);
                                        }
                                    }
                                    _prevChangesCounter++;
                                }
                                else
                                {
                                    throw new NotImplementedException();
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WrongDisposableObjectStateException)
            {
            }
            catch (Exception exc)
            {
                MiscFuncs.HandleUnexpectedError(exc, _log);
            }
        }

        /**/
        private readonly MyObservableCollectionSafeAsyncImpl<TItem> _resultCollection
            = new MyObservableCollectionSafeAsyncImpl<TItem>();
        public IObservable<MyNotifyCollectionChangedArgs<TItem>> CollectionChangedObservable
            => _resultCollection.CollectionChangedObservable;
        public async Task<MyNotifyCollectionChangedArgs<TItem>> GetDeepCopyAsync()
        {
            return await _resultCollection.GetDeepCopyAsync().ConfigureAwait(false);
        }
        private readonly ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem>> _changedArgsDict
            = new ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem>>();
        private long _prevChangesCounter = -2;
        private readonly Logger _log = LogManager.GetCurrentClassLogger();
        private readonly DisposableObjectStateHelper _stateHelper
            = new DisposableObjectStateHelper("MyNLastObservableCollectionImpl");
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        public async Task MyDisposeAsync()
        {
            _cts.Cancel();
            await _stateHelper.MyDisposeAsync();
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _subscriptions.Clear();
            _cts.Dispose();
        }
    }
}
