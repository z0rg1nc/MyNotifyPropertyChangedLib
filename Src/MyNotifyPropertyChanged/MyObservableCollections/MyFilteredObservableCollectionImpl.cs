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
    /* Filtered proxy*/
    public interface IObservableCollectionProxyFilter<TItem>
    {
        Func<TItem, Task<bool>> Predicate { get; set; }
        IObservable<Func<TItem, Task<bool>>> PredicateChangedObservable { get; }
    }

    public class ObservableCollectionProxyFilter<TItem>
        : IObservableCollectionProxyFilter<TItem>
    {
        public ObservableCollectionProxyFilter(Func<TItem, Task<bool>> predicate)
        {
            Assert.NotNull(predicate);
            _predicate = predicate;
        }

        private Func<TItem, Task<bool>> _predicate;
        public Func<TItem, Task<bool>> Predicate
        {
            get
            {
                return _predicate;
            }
            set
            {
                Assert.NotNull(value);
                _predicate = value;
                _predicateChangedSubject.OnNext(value);
            }
        }

        private readonly Subject<Func<TItem, Task<bool>>> _predicateChangedSubject
            = new Subject<Func<TItem, Task<bool>>>();
        public IObservable<Func<TItem, Task<bool>>> PredicateChangedObservable
            => _predicateChangedSubject;
    }

    public static class MyFilteredObservableCollectionImpl
    {
        public static async Task<MyFilteredObservableCollectionImpl<TItem>> CreateInstance<TItem>(
            IMyNotifyCollectionChanged<TItem> collection,
            IObservableCollectionProxyFilter<TItem> filter
            )
        {
            return await MyFilteredObservableCollectionImpl<TItem>.CreateInstance(
                collection,
                filter
            ).ConfigureAwait(false);
        }
    }
    public class MyFilteredObservableCollectionImpl<TItem>
        : IMyNotifyCollectionChanged<TItem>, IMyAsyncDisposable
    {
        private MyFilteredObservableCollectionImpl()
        {
        }

        private IMyNotifyCollectionChanged<TItem> _originCollection;
        private Func<TItem, Task<bool>> _predicateFilter;
        public static async Task<MyFilteredObservableCollectionImpl<TItem>> CreateInstance(
            IMyNotifyCollectionChanged<TItem> collection,
            IObservableCollectionProxyFilter<TItem> filter
        )
        {
            Assert.NotNull(collection);
            Assert.NotNull(filter);
            Assert.NotNull(filter.Predicate);
            var result = new MyFilteredObservableCollectionImpl<TItem>();
            result._originCollection = collection;
            result._predicateFilter = filter.Predicate;
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
                filter.PredicateChangedObservable.Subscribe(
                    result.UpdatePredicateFilter
                )
            );
            using (await result._lockSem.GetDisposable().ConfigureAwait(false))
            {
                await result.ResetActionAsync().ConfigureAwait(false);
            }
            result.ProcessNewChangedArgs();
            return await Task.FromResult(result).ConfigureAwait(false);
        }
        /**/
        private async Task ResetActionAsync()
        {
            await _resultCollection.ClearAsync().ConfigureAwait(false);
            _indicesCorrelation.Clear();
            var deepCopyArgs = await _originCollection.GetDeepCopyAsync().ConfigureAwait(false);
            _changedArgsDict[deepCopyArgs.ChangesNum] = deepCopyArgs;
            _prevChangesCounter = deepCopyArgs.ChangesNum - 1;
        }

        private async void UpdatePredicateFilter(Func<TItem, Task<bool>> newPredicate)
        {
            try
            {
                using (_stateHelper.GetFuncWrapper())
                {
                    Assert.NotNull(newPredicate);
                    using (await _lockSem.GetDisposable().ConfigureAwait(false))
                    {
                        _predicateFilter = newPredicate;
                        await ResetActionAsync().ConfigureAwait(false);
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

        private readonly List<int> _indicesCorrelation = new List<int>(); // -1 if not presented in new
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
                                var currentFilterPredicate = _predicateFilter;
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
                                        int resultCollectionInsertingIndex = 0;
                                        for (int i = 0; i < insertIndex; i++)
                                        {
                                            if (_indicesCorrelation[i] != -1)
                                            {
                                                resultCollectionInsertingIndex = _indicesCorrelation[i] + 1;
                                            }
                                        }
                                        _indicesCorrelation.InsertRange(
                                            insertIndex,
                                            Enumerable.Repeat(-1, insertCount)
                                        );
                                        var filteredNewItems = new List<TItem>();
                                        int prevIndexSurplus = 0;
                                        for (int i = 0; i < insertCount; i++)
                                        {
                                            if (await currentFilterPredicate(nextArgs.NewItems[i]).ConfigureAwait(false))
                                            {
                                                filteredNewItems.Add(nextArgs.NewItems[i]);
                                                _indicesCorrelation[insertIndex + i] = resultCollectionInsertingIndex +
                                                                                       prevIndexSurplus;
                                                prevIndexSurplus++;
                                            }
                                        }
                                        if (filteredNewItems.Any())
                                        {
                                            var filteredNewItemsCount = filteredNewItems.Count;
                                            await _resultCollection.InsertRangeAtAsync(
                                                resultCollectionInsertingIndex,
                                                filteredNewItems
                                                ).ConfigureAwait(false);
                                            for (int i = insertIndex + insertCount; i < _indicesCorrelation.Count; i++)
                                            {
                                                if (_indicesCorrelation[i] != -1)
                                                    _indicesCorrelation[i] += filteredNewItemsCount;
                                            }
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
                                        int resultStartIndexToRemove = -1;
                                        int resultCountToRemove = 0;
                                        for (int i = removeIndexStart; i < removeIndexStart + removeCount; i++)
                                        {
                                            if (_indicesCorrelation[i] != -1)
                                            {
                                                if (resultStartIndexToRemove == -1)
                                                {
                                                    resultStartIndexToRemove = _indicesCorrelation[i];
                                                }
                                                resultCountToRemove++;
                                            }
                                        }
                                        if (resultStartIndexToRemove != -1 && resultCountToRemove > 0)
                                        {
                                            await _resultCollection.RemoveRangeAsync(
                                                resultStartIndexToRemove,
                                                resultCountToRemove
                                            ).ConfigureAwait(false);
                                            for (
                                                int i = removeIndexStart + removeCount;
                                                i < _indicesCorrelation.Count;
                                                i++
                                            )
                                            {
                                                if (_indicesCorrelation[i] != -1)
                                                {
                                                    _indicesCorrelation[i] -= resultCountToRemove;
                                                }
                                            }
                                        }
                                        _indicesCorrelation.RemoveRange(
                                            removeIndexStart,
                                            removeCount
                                        );
                                    }
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                {
                                    int count = nextArgs.NewItems.Count;
                                    if (count > 0)
                                    {
                                        var insertNewItemsIs = new List<int>();
                                        var removeNewItemsIs = new List<int>();
                                        var updateNewItemsIs = new List<int>();
                                        for (int i = 0; i < count; i++)
                                        {
                                            var newItemOriginIndex = nextArgs.NewItemsIndices[i];
                                            var newItem = nextArgs.NewItems[i];
                                            var newItemFilterTrue =
                                                await currentFilterPredicate(newItem).ConfigureAwait(false);
                                            if (
                                                !newItemFilterTrue
                                                && _indicesCorrelation[newItemOriginIndex] != -1
                                                )
                                            {
                                                removeNewItemsIs.Add(i);
                                            }
                                            else if (
                                                newItemFilterTrue
                                                && _indicesCorrelation[newItemOriginIndex] == -1
                                                )
                                            {
                                                insertNewItemsIs.Add(i);
                                            }
                                            else if (
                                                newItemFilterTrue
                                                && _indicesCorrelation[newItemOriginIndex] != -1
                                                )
                                            {
                                                updateNewItemsIs.Add(i);
                                            }
                                        }
                                        foreach (var i in removeNewItemsIs)
                                        {
                                            var itemIndex = nextArgs.NewItemsIndices[i];
                                            var filteredItemIndex = _indicesCorrelation[itemIndex];
                                            await _resultCollection.RemoveRangeAsync(
                                                filteredItemIndex,
                                                1
                                                ).ConfigureAwait(false);
                                            _indicesCorrelation[itemIndex] = -1;
                                            for (int j = itemIndex + 1; j < _indicesCorrelation.Count; j++)
                                            {
                                                if (_indicesCorrelation[j] != -1)
                                                {
                                                    _indicesCorrelation[j]--;
                                                }
                                            }
                                        }
                                        foreach (var i in insertNewItemsIs)
                                        {
                                            var itemIndex = nextArgs.NewItemsIndices[i];
                                            var insertIndex = 0;
                                            for (int j = 0; j < itemIndex; j++)
                                            {
                                                if (_indicesCorrelation[j] != -1)
                                                {
                                                    insertIndex = _indicesCorrelation[j] + 1;
                                                }
                                            }
                                            await _resultCollection.InsertRangeAtAsync(
                                                insertIndex,
                                                new[] {nextArgs.NewItems[i]}
                                                ).ConfigureAwait(false);
                                            _indicesCorrelation[itemIndex] = insertIndex;
                                            for (int j = itemIndex + 1; j < _indicesCorrelation.Count; j++)
                                            {
                                                if (_indicesCorrelation[j] != -1)
                                                {
                                                    _indicesCorrelation[j]++;
                                                }
                                            }
                                        }
                                        /**/
                                        var bulkReplaceArgs = new List<Tuple<int, TItem>>();
                                        foreach (var i in updateNewItemsIs)
                                        {
                                            var newItemIndex = nextArgs.NewItemsIndices[i];
                                            var newItem = nextArgs.NewItems[i];
                                            bulkReplaceArgs.Add(
                                                Tuple.Create(
                                                    _indicesCorrelation[newItemIndex],
                                                    newItem
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
            = new DisposableObjectStateHelper("MyFilteredObservableCollectionImpl");
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
