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
    public class MyIndexedObservableCollectionImpl<TItem>
        : IMyNotifyCollectionChanged<MutableTuple<int,TItem>>, IMyAsyncDisposable
    {
        private MyIndexedObservableCollectionImpl()
        {
        }
        private IMyNotifyCollectionChanged<TItem> _originObservableCollection;

        public static async Task<MyIndexedObservableCollectionImpl<TItem>> CreateInstance(
            IMyNotifyCollectionChanged<TItem> observableCollection
            )
        {
            Assert.NotNull(observableCollection);
            var result = new MyIndexedObservableCollectionImpl<TItem>();
            result._originObservableCollection = observableCollection;
            result._stateHelper.SetInitializedState();
            result._subscriptions.Add(
                observableCollection.CollectionChangedObservable.Subscribe(
                    _ =>
                    {
                        if (result._changedArgsDict.TryAdd(_.ChangesNum, _))
                            result.ProcessNewChangedArgs();
                    }
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
            _originCollectionCopy.Clear();
            await _resultCollection.ClearAsync().ConfigureAwait(false);
            var deepCopyArgs = await _originObservableCollection.GetDeepCopyAsync().ConfigureAwait(false);
            _changedArgsDict[deepCopyArgs.ChangesNum] = deepCopyArgs;
            _prevChangesCounter = deepCopyArgs.ChangesNum - 1;
        }
        private readonly SemaphoreSlim _lockSem = new SemaphoreSlim(1);
        private readonly List<TItem> _originCollectionCopy = new List<TItem>(); 
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
                                        var insertArgs = new List<MutableTuple<int, TItem>>();
                                        for (int i = 0; i < insertCount; i++)
                                        {
                                            insertArgs.Add(
                                                MutableTuple.Create(
                                                    i + insertIndex,
                                                    nextArgs.NewItems[i]
                                                )
                                            );
                                        }
                                        await _resultCollection.InsertRangeAtAsync(
                                            insertIndex,
                                            insertArgs
                                        ).ConfigureAwait(false);
                                        _originCollectionCopy.InsertRange(
                                            insertIndex,
                                            nextArgs.NewItems
                                        );
                                        /**/
                                        var currentResultCount = _originCollectionCopy.Count;
                                        var bulkUpdateArgs = new List<Tuple<int, MutableTuple<int, TItem>>>();
                                        for (int i = insertIndex + insertCount; i < currentResultCount; i++)
                                        {
                                            bulkUpdateArgs.Add(
                                                Tuple.Create(
                                                    i,
                                                    MutableTuple.Create(
                                                        i,
                                                        _originCollectionCopy[i]
                                                    )
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
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved)
                                {
                                    var removeIndex = nextArgs.OldItemsIndices[0];
                                    var removeCount = nextArgs.OldItems.Count;
                                    if (removeCount > 0)
                                    {
                                        await _resultCollection.RemoveRangeAsync(
                                            removeIndex,
                                            removeCount
                                            ).ConfigureAwait(false);
                                        _originCollectionCopy.RemoveRange(
                                            removeIndex,
                                            removeCount
                                        );
                                        /**/
                                        var currentResultCount = _originCollectionCopy.Count;
                                        var bulkUpdateArgs = new List<Tuple<int, MutableTuple<int, TItem>>>();
                                        for (int i = removeIndex; i < currentResultCount; i++)
                                        {
                                            bulkUpdateArgs.Add(
                                                Tuple.Create(
                                                    i,
                                                    MutableTuple.Create(
                                                        i,
                                                        _originCollectionCopy[i]
                                                    )
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
                                    var count = nextArgs.NewItems.Count;
                                    var bulkUpdateArgs = new List<Tuple<int, MutableTuple<int, TItem>>>();
                                    for (int i = 0; i < count; i++)
                                    {
                                        _originCollectionCopy[nextArgs.NewItemsIndices[i]] = nextArgs.NewItems[i];
                                        bulkUpdateArgs.Add(
                                            Tuple.Create(
                                                nextArgs.NewItemsIndices[i],
                                                MutableTuple.Create(
                                                    nextArgs.NewItemsIndices[i],
                                                    nextArgs.NewItems[i]
                                                )
                                            )
                                        );
                                    }
                                    if (bulkUpdateArgs.Any())
                                        await _resultCollection.ReplaceBulkAsync(
                                            bulkUpdateArgs
                                        ).ConfigureAwait(false);
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
        private readonly MyObservableCollectionSafeAsyncImpl<MutableTuple<int, TItem>> _resultCollection
            = new MyObservableCollectionSafeAsyncImpl<MutableTuple<int, TItem>>();
        public IObservable<MyNotifyCollectionChangedArgs<MutableTuple<int, TItem>>> CollectionChangedObservable
            => _resultCollection.CollectionChangedObservable;
        public async Task<MyNotifyCollectionChangedArgs<MutableTuple<int, TItem>>> GetDeepCopyAsync()
        {
            return await _resultCollection.GetDeepCopyAsync().ConfigureAwait(false);
        }
        private readonly ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem>> _changedArgsDict
            = new ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem>>();
        private long _prevChangesCounter = -2;
        private readonly Logger _log = LogManager.GetCurrentClassLogger();
        private readonly DisposableObjectStateHelper _stateHelper
            = new DisposableObjectStateHelper("MyIndexedObservableCollectionImpl");
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
