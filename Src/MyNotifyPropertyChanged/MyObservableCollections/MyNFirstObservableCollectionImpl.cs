using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BtmI2p.MiscUtils;
using BtmI2p.ObjectStateLib;
using NLog;
using Xunit;

namespace BtmI2p.MyNotifyPropertyChanged.MyObservableCollections
{
    public static class MyNFirstObservableCollectionImpl
    {
        public static async Task<MyNFirstObservableCollectionImpl<TItem>> CreateInstance<TItem>(
            IMyNotifyCollectionChanged<TItem> collection,
            IMyObservableCollectionProxyN proxyN
            )
        {
            return await MyNFirstObservableCollectionImpl<TItem>.CreateInstance(
                collection,
                proxyN
                ).ConfigureAwait(false);
        }
    }

    public class MyNFirstObservableCollectionImpl<TItem>
        : IMyNotifyCollectionChanged<TItem>, IMyAsyncDisposable
    {
        private MyNFirstObservableCollectionImpl()
        {
        }
        private IMyNotifyCollectionChanged<TItem> _originCollection;
        private int _n;
        private readonly List<TItem> _originElementsCopy = new List<TItem>();

        public static async Task<MyNFirstObservableCollectionImpl<TItem>> CreateInstance(
            IMyNotifyCollectionChanged<TItem> collection,
            IMyObservableCollectionProxyN proxyN
            )
        {
            Assert.NotNull(collection);
            Assert.NotNull(proxyN);
            var result = new MyNFirstObservableCollectionImpl<TItem>();
            result._originCollection = collection;
            result._n = proxyN.N;
            result._stateHelper.SetInitializedState();
            result._subscriptions.Add(
                collection.CollectionChangedObservable.Subscribe(
                    _ =>
                    {
                        if (result._changedArgsDict.TryAdd(_.ChangesNum, _))
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
                        var originElementCount = _originElementsCopy.Count;
                        var oldResultCount = originElementCount > oldN ? oldN : originElementCount;
                        var newResultCount = originElementCount > newN ? newN : originElementCount;
                        if (newResultCount > oldResultCount)
                        {
                            await _resultCollection.AddRangeAsync(
                                _originElementsCopy.Skip(oldResultCount).Take(newResultCount - oldResultCount).ToList()
                            ).ConfigureAwait(false);
                        }
                        else if (newResultCount < oldResultCount)
                        {
                            await _resultCollection.RemoveRangeAsync(
                                newResultCount,
                                oldResultCount - newResultCount
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
                                        _originElementsCopy.InsertRange(
                                            insertIndex,
                                            nextArgs.NewItems
                                            );
                                        var currentResultCollectionCount =
                                            await _resultCollection.CountAsync().ConfigureAwait(false);
                                        var bulkUpdateArgs = new List<Tuple<int, TItem>>();
                                        for (int i = insertIndex; i < currentResultCollectionCount; i++)
                                        {
                                            bulkUpdateArgs.Add(
                                                Tuple.Create(
                                                    i,
                                                    _originElementsCopy[i]
                                                )
                                            );
                                        }
                                        if (bulkUpdateArgs.Any())
                                            await _resultCollection.ReplaceBulkAsync(
                                                bulkUpdateArgs
                                                ).ConfigureAwait(false);
                                        var newResultCollectionCount = _originElementsCopy.Count > currentN
                                            ? currentN
                                            : _originElementsCopy.Count;
                                        if (newResultCollectionCount > currentResultCollectionCount)
                                        {
                                            await _resultCollection.AddRangeAsync(
                                                _originElementsCopy.Skip(currentResultCollectionCount)
                                                    .Take(newResultCollectionCount - currentResultCollectionCount)
                                                    .ToList()
                                                ).ConfigureAwait(false);
                                        }
                                    }
                                    /**/
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved)
                                {
                                    var removeIndex = nextArgs.OldItemsIndices[0];
                                    var removeCount = nextArgs.OldItems.Count;
                                    if (removeCount > 0)
                                    {
                                        _originElementsCopy.RemoveRange(
                                            removeIndex,
                                            removeCount
                                        );
                                        var currentResultCollectionCount =
                                            await _resultCollection.CountAsync().ConfigureAwait(false);
                                        var newResultCollectionCount = _originElementsCopy.Count > currentN
                                            ? currentN
                                            : _originElementsCopy.Count;
                                        if (newResultCollectionCount < currentResultCollectionCount)
                                        {
                                            await _resultCollection.RemoveRangeAsync(
                                                removeIndex,
                                                (currentResultCollectionCount - newResultCollectionCount)
                                            ).ConfigureAwait(false);
                                        }
                                        var bulkUpdateArgs = new List<Tuple<int, TItem>>();
                                        for (int i = removeIndex; i < newResultCollectionCount; i++)
                                        {
                                            bulkUpdateArgs.Add(
                                                Tuple.Create(
                                                    i,
                                                    _originElementsCopy[i]
                                                )
                                            );
                                        }
                                        if (bulkUpdateArgs.Any())
                                            await _resultCollection.ReplaceBulkAsync(
                                                bulkUpdateArgs
                                                ).ConfigureAwait(false);
                                    }
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                {
                                    int count = nextArgs.NewItems.Count;
                                    if (count > 0)
                                    {
                                        var currentResultCollectionCount =
                                            await _resultCollection.CountAsync().ConfigureAwait(false);
                                        var bulkUpdateArgs = new List<Tuple<int, TItem>>();
                                        for (int i = 0; i < count; i++)
                                        {
                                            _originElementsCopy[nextArgs.NewItemsIndices[i]] = nextArgs.NewItems[i];
                                            if (nextArgs.NewItemsIndices[i] < currentResultCollectionCount)
                                            {
                                                bulkUpdateArgs.Add(Tuple.Create(nextArgs.NewItemsIndices[i], nextArgs.NewItems[i]));
                                            }
                                        }
                                        if (bulkUpdateArgs.Any())
                                            await _resultCollection.ReplaceBulkAsync(
                                                bulkUpdateArgs
                                            ).ConfigureAwait(false);
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
            private readonly
            MyObservableCollectionSafeAsyncImpl<TItem> _resultCollection
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
            = new DisposableObjectStateHelper("MyNFirstObservableCollectionImpl");
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
