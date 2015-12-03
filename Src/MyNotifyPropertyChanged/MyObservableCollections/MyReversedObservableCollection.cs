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
    public class MyReversedObservableCollection<TItem>
        : IMyNotifyCollectionChanged<TItem>, IMyAsyncDisposable
    {
        private MyReversedObservableCollection()
        {
        }
        private IMyNotifyCollectionChanged<TItem> _originCollection;

        public static async Task<MyReversedObservableCollection<TItem>> CreateInstance(
            IMyNotifyCollectionChanged<TItem> collection
            )
        {
            Assert.NotNull(collection);
            var result = new MyReversedObservableCollection<TItem>();
            result._originCollection = collection;
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
            var deepCopyArgs = await _originCollection.GetDeepCopyAsync().ConfigureAwait(false);
            _changedArgsDict[deepCopyArgs.ChangesNum] = deepCopyArgs;
            _prevChangesCounter = deepCopyArgs.ChangesNum - 1;
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
                                        var currentResultCount =
                                            await _resultCollection.CountAsync().ConfigureAwait(false);
                                        var revertedInsertIndex = currentResultCount - insertIndex;
                                        await _resultCollection.InsertRangeAtAsync(
                                            revertedInsertIndex,
                                            nextArgs.NewItems.Reverse().ToList()
                                            ).ConfigureAwait(false);
                                    }
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved)
                                {
                                    var removeIndexStart = nextArgs.OldItemsIndices[0];
                                    var removeCount = nextArgs.OldItems.Count;
                                    if (removeCount > 0)
                                    {
                                        var removeIndexLast = removeIndexStart + (removeCount - 1);
                                        var revertedIndexFirst =
                                            (await _resultCollection.CountAsync().ConfigureAwait(false)) - 1 -
                                            removeIndexLast;
                                        await _resultCollection.RemoveRangeAsync(
                                            revertedIndexFirst,
                                            removeCount
                                        ).ConfigureAwait(false);
                                    }
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                {
                                    int count = nextArgs.NewItems.Count;
                                    var currentResultCollectionCount =
                                        await _resultCollection.CountAsync().ConfigureAwait(false);
                                    var bulkReplaceArgs = new List<Tuple<int, TItem>>();
                                    for (int i = 0; i < count; i++)
                                    {
                                        var revertedIndex = currentResultCollectionCount - 1 -
                                                            nextArgs.NewItemsIndices[i];
                                        bulkReplaceArgs.Add(
                                            Tuple.Create(
                                                revertedIndex,
                                                nextArgs.NewItems[i]
                                            )
                                        );
                                    }
                                    if (bulkReplaceArgs.Any())
                                    {
                                        await _resultCollection.ReplaceBulkAsync(
                                            bulkReplaceArgs
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
            catch(OperationCanceledException)
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
            = new DisposableObjectStateHelper("MyReversedNotifyCollectionChangedImpl");
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
