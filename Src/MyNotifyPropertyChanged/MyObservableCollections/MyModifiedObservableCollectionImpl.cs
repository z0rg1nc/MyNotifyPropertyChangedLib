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
    public class MyModifiedObservableCollectionImpl<TItemFrom,TItemTo>
        : IMyNotifyCollectionChanged<TItemTo>, IMyAsyncDisposable
    {
        private MyModifiedObservableCollectionImpl()
        {
        }
        private IMyNotifyCollectionChanged<TItemFrom> _originCollection;
        private Func<TItemFrom, Task<TItemTo>> _modificationFunc;

        public static async Task<MyModifiedObservableCollectionImpl<TItemFrom, TItemTo>> CreateInstance(
            IMyNotifyCollectionChanged<TItemFrom> collection,
            Func<TItemFrom, Task<TItemTo>> modificationFunc
            )
        {
            Assert.NotNull(collection);
            Assert.NotNull(modificationFunc);
            var result = new MyModifiedObservableCollectionImpl<TItemFrom, TItemTo>();
            result._originCollection = collection;
            result._modificationFunc = modificationFunc;
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
                                MyNotifyCollectionChangedArgs<TItemFrom> removedArgs;
                                _changedArgsDict.TryRemove(key, out removedArgs);
                            }
                            MyNotifyCollectionChangedArgs<TItemFrom> nextArgs;
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
                                        var modifiedRange = new List<TItemTo>(insertCount);
                                        for (int i = 0; i < insertCount; i++)
                                        {
                                            modifiedRange.Add(
                                                await _modificationFunc(nextArgs.NewItems[i]).ConfigureAwait(false)
                                                );
                                        }
                                        await _resultCollection.InsertRangeAtAsync(
                                            insertIndex,
                                            modifiedRange
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
                                    }
                                    _prevChangesCounter++;
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                {
                                    var count = nextArgs.NewItems.Count;
                                    var bulkUpdate = new List<Tuple<int, TItemTo>>(count);
                                    for (int i = 0; i < count; i++)
                                    {
                                        bulkUpdate.Add(
                                            Tuple.Create(
                                                nextArgs.NewItemsIndices[i],
                                                await _modificationFunc(nextArgs.NewItems[i]).ConfigureAwait(false)
                                            )
                                        );
                                    }
                                    if (bulkUpdate.Any())
                                    {
                                        await
                                            _resultCollection.ReplaceBulkAsync(bulkUpdate).ConfigureAwait(false);
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
        private readonly MyObservableCollectionSafeAsyncImpl<TItemTo> _resultCollection
            = new MyObservableCollectionSafeAsyncImpl<TItemTo>();
        public IObservable<MyNotifyCollectionChangedArgs<TItemTo>> CollectionChangedObservable
            => _resultCollection.CollectionChangedObservable;
        public async Task<MyNotifyCollectionChangedArgs<TItemTo>> GetDeepCopyAsync()
        {
            return await _resultCollection.GetDeepCopyAsync().ConfigureAwait(false);
        }
        private readonly ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItemFrom>> _changedArgsDict
            = new ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItemFrom>>();
        private long _prevChangesCounter = -2;
        private readonly Logger _log = LogManager.GetCurrentClassLogger();
        private readonly DisposableObjectStateHelper _stateHelper
            = new DisposableObjectStateHelper("MyModifiedNotifyCollectionChangedImpl");
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
