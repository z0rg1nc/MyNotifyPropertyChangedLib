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
    public class MyConcatObservableCollectionImpl<TItem>
        : IMyNotifyCollectionChanged<TItem>, IMyAsyncDisposable
    {
        private MyConcatObservableCollectionImpl()
        {
        }

        private IMyNotifyCollectionChanged<TItem>[] _originCollectionArray;
        private int _originCollectionChangedArrayLength;
        public static async Task<MyConcatObservableCollectionImpl<TItem>> CreateInstance(
            IMyNotifyCollectionChanged<TItem>[] collectionArray
        )
        {
            Assert.NotEmpty(collectionArray);
            var originCollectionCount = collectionArray.Length;
            var result = new MyConcatObservableCollectionImpl<TItem>();
            result._originCollectionArray = collectionArray;
            result._originCollectionChangedArrayLength = originCollectionCount;
            result._changedArgsDictArray = new ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem>>[originCollectionCount];
            result._prevChangesCounterArray = new long[originCollectionCount];
            result._startIndexesArray = new int[originCollectionCount];
            result._currentChunksLengthArray = new int[originCollectionCount];
            for (int i = 0; i < originCollectionCount; i++)
            {
                result._changedArgsDictArray[i] 
                    = new ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem>>();
                result._prevChangesCounterArray[i] = -2;
                result._startIndexesArray[i] = 0;
                result._currentChunksLengthArray[i] = 0;
            }
            result._stateHelper.SetInitializedState();
            for (int i = 0; i < originCollectionCount; i++)
            {
                int j = i;
                result._subscriptions.Add(
                    collectionArray[j].CollectionChangedObservable.Subscribe(
                        _ =>
                        {
                            if (result._changedArgsDictArray[j].TryAdd(_.ChangesNum, _))
                                result.ProcessNewChangedArgs();
                        }
                    )
                );
            }
            using (await result._lockSem.GetDisposable().ConfigureAwait(false))
            {
                for (int i = 0; i < originCollectionCount; i++)
                {
                    await result.ResetActionAsync(i).ConfigureAwait(false);
                }
            }
            result.ProcessNewChangedArgs();
            return result;
        }

        private async Task ResetActionAsync(int j)
        {
            var currentChunkLength = _currentChunksLengthArray[j];
            if (currentChunkLength > 0)
            {
                await _resultCollection.RemoveRangeAsync(
                    _startIndexesArray[j],
                    currentChunkLength
                    ).ConfigureAwait(false);
                _currentChunksLengthArray[j] = 0;
                for (int k = j + 1; k < _originCollectionChangedArrayLength; k++)
                {
                    _startIndexesArray[k] -= currentChunkLength;
                }
            }
            var deepCopyArgsI = await _originCollectionArray[j].GetDeepCopyAsync().ConfigureAwait(false);
            _changedArgsDictArray[j][deepCopyArgsI.ChangesNum] = deepCopyArgsI;
            _prevChangesCounterArray[j] = deepCopyArgsI.ChangesNum - 1;
        }

        private readonly SemaphoreSlim _lockSem = new SemaphoreSlim(1);
        private int[] _startIndexesArray;
        private int[] _currentChunksLengthArray;
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
                            for (int j = 0; j < _originCollectionChangedArrayLength; j++)
                            {
                                var currentChangesNumInDictToRemove =
                                _changedArgsDictArray[j].Keys.Where(_ => _ <= _prevChangesCounterArray[j]).ToList();
                                foreach (long key in currentChangesNumInDictToRemove)
                                {
                                    MyNotifyCollectionChangedArgs<TItem> removedArgs;
                                    _changedArgsDictArray[j].TryRemove(key, out removedArgs);
                                }
                                MyNotifyCollectionChangedArgs<TItem> nextArgs;
                                if (
                                    _changedArgsDictArray[j].TryRemove(
                                        _prevChangesCounterArray[j] + 1,
                                        out nextArgs
                                        )
                                    )
                                {
                                    lockSemCalledWrapper.Called = true;
                                    if (nextArgs.ChangedAction == EMyCollectionChangedAction.Reset)
                                    {
                                        await ResetActionAsync(j).ConfigureAwait(false);
                                    }
                                    else if (nextArgs.ChangedAction == EMyCollectionChangedAction.NewItemsRangeInserted)
                                    {
                                        var insertIndex = nextArgs.NewItemsIndices[0];
                                        var insertCount = nextArgs.NewItems.Count;
                                        if (insertCount > 0)
                                        {
                                            await _resultCollection.InsertRangeAtAsync(
                                                _startIndexesArray[j] + insertIndex,
                                                nextArgs.NewItems
                                                ).ConfigureAwait(false);
                                            for (int k = j + 1; k < _originCollectionChangedArrayLength; k++)
                                            {
                                                _startIndexesArray[k] += insertCount;
                                            }
                                            _currentChunksLengthArray[j] += insertCount;
                                        }
                                        _prevChangesCounterArray[j]++;
                                    }
                                    else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved)
                                    {
                                        var removeIndex = nextArgs.OldItemsIndices[0];
                                        var removeCount = nextArgs.OldItems.Count;
                                        if (removeCount > 0)
                                        {
                                            await _resultCollection.RemoveRangeAsync(
                                                _startIndexesArray[j] + removeIndex,
                                                removeCount
                                                ).ConfigureAwait(false);
                                            for (int k = j + 1; k < _originCollectionChangedArrayLength; k++)
                                            {
                                                _startIndexesArray[k] -= removeCount;
                                            }
                                            _currentChunksLengthArray[j] -= removeCount;
                                        }
                                        _prevChangesCounterArray[j]++;
                                    }
                                    else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                    {
                                        var count = nextArgs.NewItems.Count;
                                        var bulkUpdateArgs = new List<Tuple<int, TItem>>();
                                        for (int i = 0; i < count; i++)
                                        {
                                            bulkUpdateArgs.Add(
                                                Tuple.Create(
                                                    _startIndexesArray[j] + nextArgs.NewItemsIndices[i],
                                                    nextArgs.NewItems[i]
                                                )
                                            );
                                        }
                                        if (bulkUpdateArgs.Any())
                                        {
                                            await _resultCollection.ReplaceBulkAsync(
                                                bulkUpdateArgs
                                                ).ConfigureAwait(false);
                                        }
                                        _prevChangesCounterArray[j]++;
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
        private ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem>>[] _changedArgsDictArray;
        private long[] _prevChangesCounterArray;
        private readonly Logger _log = LogManager.GetCurrentClassLogger();
        private readonly DisposableObjectStateHelper _stateHelper
            = new DisposableObjectStateHelper("MyConcatObservableCollectionImpl");
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
