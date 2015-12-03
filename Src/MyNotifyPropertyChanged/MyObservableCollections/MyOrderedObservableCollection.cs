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
    public interface IMyObservableCollectionProxyComparer<TItem>
    {
        IComparer<TItem> Comparer { get; set; } 
        IObservable<IComparer<TItem>> ComparerChangedObservable { get; }
    }

    public class MyObservableCollectionProxyComparer<TItem>
        : IMyObservableCollectionProxyComparer<TItem>
    {
        public MyObservableCollectionProxyComparer(
            IComparer<TItem> initComparer)
        {
            Assert.NotNull(initComparer);
            _comparer = initComparer;
        }

        private IComparer<TItem> _comparer;
        public IComparer<TItem> Comparer {
            get
            {
                return _comparer;
            }
            set
            {
                Assert.NotNull(value);
                _comparer = value;
                _comparerChangedSubject.OnNext(value);
            }
        }
        private readonly Subject<IComparer<TItem>> _comparerChangedSubject
            = new Subject<IComparer<TItem>>();
        public IObservable<IComparer<TItem>> ComparerChangedObservable
            => _comparerChangedSubject;
    }

    public static class MyOrderedObservableCollection
    {
        public static async Task<MyOrderedObservableCollection<TItem>> CreateInstance<TItem>(
            IMyNotifyCollectionChanged<TItem> collection,
            IMyObservableCollectionProxyComparer<TItem> proxyComparer
            )
        {
            return await MyOrderedObservableCollection<TItem>.CreateInstance(
                collection,
                proxyComparer
                ).ConfigureAwait(false);
        }
    }

    public class MyOrderedObservableCollection<TItem>
        : IMyNotifyCollectionChanged<TItem>, IMyAsyncDisposable
    {
        private MyOrderedObservableCollection()
        {
        }
        private IMyNotifyCollectionChanged<TItem> _originCollection;
        private IComparer<TItem> _proxyComparer;
        public static async Task<MyOrderedObservableCollection<TItem>> CreateInstance(
            IMyNotifyCollectionChanged<TItem> collection,
            IMyObservableCollectionProxyComparer<TItem> proxyComparer
            )
        {
            Assert.NotNull(collection);
            Assert.NotNull(proxyComparer);
            Assert.NotNull(proxyComparer.Comparer);
            var result = new MyOrderedObservableCollection<TItem>();
            result._originCollection = collection;
            result._proxyComparer = proxyComparer.Comparer;
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
                proxyComparer.ComparerChangedObservable.Subscribe(
                    result.UpdateProxyComparer
                )
            );
            using (await result._lockSem.GetDisposable().ConfigureAwait(false))
            {
                await result.ResetActionAsync().ConfigureAwait(false);
            }
            result.ProcessNewChangedArgs();
            return result;
        }
        private async Task ResetActionAsync()
        {
            await _resultCollection.ClearAsync().ConfigureAwait(false);
            _indicesCorrelation.Clear();
            var deepCopyArgs = await _originCollection.GetDeepCopyAsync().ConfigureAwait(false);
            _changedArgsDict[deepCopyArgs.ChangesNum] = deepCopyArgs;
            _prevChangesCounter = deepCopyArgs.ChangesNum - 1;
        }
        private async void UpdateProxyComparer(IComparer<TItem> newComparer)
        {
            try
            {
                using (_stateHelper.GetFuncWrapper())
                {
                    Assert.NotNull(newComparer);
                    using (await _lockSem.GetDisposable().ConfigureAwait(false))
                    {
                        _proxyComparer = newComparer;
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
                                var currentComparer = _proxyComparer;
                                Assert.NotNull(currentComparer);
                                if (nextArgs.ChangedAction == EMyCollectionChangedAction.Reset)
                                {
                                    await ResetActionAsync().ConfigureAwait(false);
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.NewItemsRangeInserted)
                                {
                                    var insertIndex = nextArgs.NewItemsIndices[0];
                                    _indicesCorrelation.InsertRange(
                                        insertIndex,
                                        Enumerable.Repeat(-1, nextArgs.NewItems.Count)
                                    );
                                    for (int i = 0; i < nextArgs.NewItems.Count; i++)
                                    {
                                        var orderedInsertIndex = await _resultCollection.InsertBinaryAsync(
                                            nextArgs.NewItems[i],
                                            currentComparer
                                        ).ConfigureAwait(false);
                                        for (int j = 0; j < _indicesCorrelation.Count; j++)
                                        {
                                            if (_indicesCorrelation[j] >= orderedInsertIndex)
                                                _indicesCorrelation[j]++;
                                        }
                                        _indicesCorrelation[insertIndex + i] = orderedInsertIndex;
                                    }
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved)
                                {
                                    int removeIndexStart = nextArgs.OldItemsIndices[0];
                                    int removeCount = nextArgs.OldItems.Count;
                                    if (removeCount > 0)
                                    {
                                        for (
                                            int i = removeIndexStart; 
                                            i < removeIndexStart + removeCount; 
                                            i++
                                        )
                                        {
                                            var newIndex = _indicesCorrelation[i];
                                            await _resultCollection.RemoveRangeAsync(
                                                newIndex,
                                                1
                                            ).ConfigureAwait(false);
                                            _indicesCorrelation[i] = -1;
                                            for (int j = 0; j < _indicesCorrelation.Count; j++)
                                            {
                                                if (_indicesCorrelation[j] > newIndex)
                                                {
                                                    _indicesCorrelation[j]--;
                                                }
                                            }
                                        }
                                        _indicesCorrelation.RemoveRange(removeIndexStart,removeCount);
                                    }
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                {
                                    int count = nextArgs.NewItems.Count;
                                    _prevChangesCounter++;
                                    var bulkUpdateArgs = new List<Tuple<int, TItem>>();
                                    for (int i = 0; i < count; i++)
                                    {
                                        if (
                                            currentComparer.Compare(
                                                nextArgs.OldItems[i], 
                                                nextArgs.NewItems[i]
                                            ) == 0
                                        )
                                        {
                                            var resultIndex = _indicesCorrelation[nextArgs.NewItemsIndices[i]];
                                            bulkUpdateArgs.Add(
                                                Tuple.Create(
                                                    resultIndex,
                                                    nextArgs.NewItems[i]
                                                )
                                            );
                                        }
                                        else { 
                                            _prevChangesCounter -= 2;
                                            _changedArgsDict[_prevChangesCounter + 1] = MyNotifyCollectionChangedArgs<TItem>.CreateRangeRemoved(
                                                _prevChangesCounter + 1,
                                                nextArgs.OldItemsIndices[i],
                                                new[] { nextArgs.OldItems[i] }
                                            );
                                            _changedArgsDict[_prevChangesCounter + 2] = MyNotifyCollectionChangedArgs<TItem>.CreateNewItemsInserted(
                                                _prevChangesCounter + 2,
                                                nextArgs.NewItemsIndices[i],
                                                new[] { nextArgs.NewItems[i] }
                                            );
                                        }
                                    }
                                    if (bulkUpdateArgs.Any())
                                    {
                                        await _resultCollection.ReplaceBulkAsync(
                                            bulkUpdateArgs
                                        ).ConfigureAwait(false);
                                    }
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
        private readonly List<int> _indicesCorrelation = new List<int>(); // -1 if not presented in new
        private readonly SemaphoreSlim _lockSem = new SemaphoreSlim(1);
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
            = new DisposableObjectStateHelper("MyOrderedObservableCollection");
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
