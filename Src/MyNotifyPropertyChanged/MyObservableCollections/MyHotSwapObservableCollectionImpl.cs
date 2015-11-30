using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BtmI2p.MiscUtils;
using NLog;
using ObjectStateLib;
using Xunit;

namespace BtmI2p.MyNotifyPropertyChanged.MyObservableCollections
{
    public static class MyHotSwapObservableCollectionImpl
    {
        public static async Task<MyHotSwapObservableCollectionImpl<TItem>> CreateInstance<TItem>(
            IMyNotifyCollectionChanged<TItem> firstCollection
            )
        {
            return await MyHotSwapObservableCollectionImpl<TItem>.CreateInstance(firstCollection).ConfigureAwait(false);
        }
    }

    public class MyHotSwapObservableCollectionImpl<TItem>
        : IMyNotifyCollectionChanged<TItem>, IMyAsyncDisposable
    {
        private MyHotSwapObservableCollectionImpl()
        {
        }
        private IMyNotifyCollectionChanged<TItem> _originCollection;
        public static async Task<MyHotSwapObservableCollectionImpl<TItem>> CreateInstance(
            IMyNotifyCollectionChanged<TItem> firstCollection
        )
        {
            Assert.NotNull(firstCollection);
            var result = new MyHotSwapObservableCollectionImpl<TItem>();
            result._stateHelper.SetInitializedState();
            result._originCollection = firstCollection;
            result._subscriptions.Add(
                firstCollection.CollectionChangedObservable.Subscribe(
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
        public async Task ReplaceOriginCollectionChanged(
            IMyNotifyCollectionChanged<TItem> newCollection,
            bool updateSoftly = false
        )
        {
            Assert.NotNull(newCollection);
            using (_stateHelper.GetFuncWrapper())
            {
                using (await _lockSem.GetDisposable().ConfigureAwait(false))
                {
                    foreach (var subscription in _subscriptions)
                    {
                        subscription.Dispose();
                    }
                    _subscriptions.Clear();
                    _changedArgsDict.Clear();
                    /**/
                    _originCollection = newCollection;
                    _subscriptions.Add(
                        newCollection.CollectionChangedObservable.Subscribe(
                            _ =>
                            {
                                if (_changedArgsDict.TryAdd(_.ChangesNum, _))
                                    ProcessNewChangedArgs();
                            }
                        )
                    );
                    await ResetActionAsync(updateSoftly).ConfigureAwait(false);
                }
                ProcessNewChangedArgs();
            }
        }

        /**/
        private async Task ResetActionAsync(bool updateSoftly = false)
        {
            if (!updateSoftly)
            {
                await _resultCollection.ClearAsync().ConfigureAwait(false);
                var deepCopyArgs = await _originCollection.GetDeepCopyAsync().ConfigureAwait(false);
                _changedArgsDict[deepCopyArgs.ChangesNum] = deepCopyArgs;
                _prevChangesCounter = deepCopyArgs.ChangesNum - 1;
            }
            else
            {
                var deepCopyArgs = await _originCollection.GetDeepCopyAsync().ConfigureAwait(false);
                var currentCount = await _resultCollection.CountAsync();
                if (currentCount < deepCopyArgs.NewItems.Count)
                    await _resultCollection.AddRangeAsync(
                        deepCopyArgs.NewItems.Skip(currentCount).ToList()
                    ).ConfigureAwait(false);
                else if (currentCount > deepCopyArgs.NewItems.Count)
                    await _resultCollection.RemoveRangeAsync(
                        deepCopyArgs.NewItems.Count, currentCount - deepCopyArgs.NewItems.Count
                    ).ConfigureAwait(false);
                await _resultCollection.ReplaceBulkAsync(
                    Enumerable.Range(0, Math.Min(currentCount, deepCopyArgs.NewItems.Count))
                        .Select(_ => Tuple.Create(_, deepCopyArgs.NewItems[_]))
                        .ToList()
                ).ConfigureAwait(false);
                _prevChangesCounter = deepCopyArgs.ChangesNum;
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
                                        await _resultCollection.InsertRangeAtAsync(
                                            insertIndex,
                                            nextArgs.NewItems
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
                                        await _resultCollection.RemoveRangeAsync(
                                            removeIndexStart,
                                            removeCount
                                        ).ConfigureAwait(false);
                                    }
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                {
                                    var count = nextArgs.NewItems.Count;
                                    var bulkReplaceArgs = new List<Tuple<int, TItem>>();
                                    for (int i = 0; i < count; i++)
                                    {
                                        bulkReplaceArgs.Add(
                                            Tuple.Create(
                                                nextArgs.NewItemsIndices[i],
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
            = new DisposableObjectStateHelper("MyHotSwapObservableCollectionImpl");
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
