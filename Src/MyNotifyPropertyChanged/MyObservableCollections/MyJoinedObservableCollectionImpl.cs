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
    public enum JoinType
    {
        LeftJoin,
        RightJoin,
        InnerJoin,
        FullOuterJoin
    }

    public static class MyJoinedObservableCollectionImpl
    {
        public static async Task<MyJoinedObservableCollectionImpl<TItem1, TItem2>> CreateInstance<TItem1, TItem2>(
            IMyNotifyCollectionChanged<TItem1> collectionChanged1,
            IMyNotifyCollectionChanged<TItem2> collectionChanged2,
            Func<TItem1, TItem2, bool> comparisonPredicate,
            JoinType joinType
            )
        where TItem1 : class
        where TItem2 : class
        {
            return await MyJoinedObservableCollectionImpl<TItem1, TItem2>.CreateInstance(
                collectionChanged1,
                collectionChanged2,
                comparisonPredicate,
                joinType
            ).ConfigureAwait(false);
        }
    }
    public class MyJoinedObservableCollectionImpl<TItem1,TItem2>
        : IMyNotifyCollectionChanged<MutableTuple<TItem1,TItem2>>, IMyAsyncDisposable
    where TItem1 : class
    where TItem2 : class
    {
        private MyJoinedObservableCollectionImpl()
        {
        }

        private IMyNotifyCollectionChanged<TItem1> _originCollectionChanged1;
        private IMyNotifyCollectionChanged<TItem2> _originCollectionChanged2;
        private Func<TItem1, TItem2, bool> _comparisonPredicate;
        private JoinType _joinType;
        public static async Task<MyJoinedObservableCollectionImpl<TItem1, TItem2>> CreateInstance(
            IMyNotifyCollectionChanged<TItem1> collectionChanged1,
            IMyNotifyCollectionChanged<TItem2> collectionChanged2,
            Func<TItem1, TItem2, bool> comparisonPredicate,
            JoinType joinType
            )
        {
            Assert.NotNull(collectionChanged1);
            Assert.NotNull(collectionChanged2);
            Assert.NotNull(comparisonPredicate);
            var result = new MyJoinedObservableCollectionImpl<TItem1, TItem2>();
            result._originCollectionChanged1 = collectionChanged1;
            result._originCollectionChanged2 = collectionChanged2;
            result._comparisonPredicate = comparisonPredicate;
            result._joinType = joinType;
            /**/
            result._secondChunkIndexed = await MyIndexedObservableCollectionImpl<TItem2>.CreateInstance(
                result._secondChunkCollection
            ).ConfigureAwait(false);
            result._secondChunkFiltered = await MyFilteredObservableCollectionImpl<MutableTuple<int, TItem2>>.CreateInstance(
                result._secondChunkIndexed,
                new ObservableCollectionProxyFilter<MutableTuple<int,TItem2>>(
                    async _ =>
                    {
                        using (await result._correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
                        {
                            if (_.Item1 >= result._correlation2To1.Count)
                                return false;
                            return result._correlation2To1[_.Item1].Count == 0;
                        }
                    }
                )
            ).ConfigureAwait(false);
            result._secondChunkModifiedObservable =
                await MyModifiedObservableCollectionImpl<MutableTuple<int, TItem2>, MutableTuple<TItem1, TItem2>>.CreateInstance(
                    result._secondChunkFiltered,
                    async _ => await Task.FromResult(MutableTuple.Create(default(TItem1), _.Item2))
                ).ConfigureAwait(false);
            result._mergedFullOuterJoin =
                await MyConcatObservableCollectionImpl<MutableTuple<TItem1, TItem2>>.CreateInstance(
                    new IMyNotifyCollectionChanged<MutableTuple<TItem1, TItem2>>[]
                    {
                        result._firstChunkCollection,
                        result._secondChunkModifiedObservable
                    }
                ).ConfigureAwait(false);
            result._resultCollection =
                await MyFilteredObservableCollectionImpl<MutableTuple<TItem1, TItem2>>.CreateInstance(
                    result._mergedFullOuterJoin,
                    new ObservableCollectionProxyFilter<MutableTuple<TItem1, TItem2>>(
                        async tuple =>
                        {
                            if (joinType == JoinType.FullOuterJoin)
                                return true;
                            else if (joinType == JoinType.InnerJoin)
                                return tuple.Item1 != null && tuple.Item2 != null;
                            else if (joinType == JoinType.LeftJoin)
                                return tuple.Item1 != null;
                            else if (joinType == JoinType.RightJoin)
                                return tuple.Item2 != null;
                            return await Task.FromResult(false); //Unreachable code
                        }
                    )
                ).ConfigureAwait(false);
            result._stateHelper.SetInitializedState();
            result._subscriptions.Add(
                collectionChanged1.CollectionChangedObservable.Subscribe(
                    _ =>
                    {
                        if (result._changedArgsDict1.TryAdd(_.ChangesNum, _))
                            result.ProcessNewChangedArgs();
                    }
                )
            );
            result._subscriptions.Add(
                collectionChanged2.CollectionChangedObservable.Subscribe(
                    _ =>
                    {
                        if (result._changedArgsDict2.TryAdd(_.ChangesNum, _))
                            result.ProcessNewChangedArgs();
                    }
                )
            );
            result._asyncSubscriptions.Add(
                new CompositeMyAsyncDisposable(
                    result._resultCollection,
                    result._mergedFullOuterJoin,
                    result._secondChunkModifiedObservable,
                    result._secondChunkFiltered,
                    result._secondChunkIndexed
                )
            );
            using (await result._lockSem.GetDisposable().ConfigureAwait(false))
            {
                await result.ResetActionAsync1().ConfigureAwait(false);
                await result.ResetActionAsync2().ConfigureAwait(false);
            }
            result.ProcessNewChangedArgs();
            return result;
        }
        /**/

        private async Task ResetActionAsync1()
        {
            _collectionChanged1Copy.Clear();
            await _firstChunkCollection.ClearAsync().ConfigureAwait(false);
            /**/
            var deepCopyArgs1 = await _originCollectionChanged1.GetDeepCopyAsync().ConfigureAwait(false);
            _changedArgsDict1[deepCopyArgs1.ChangesNum] = deepCopyArgs1;
            _prevChangesCounter1 = deepCopyArgs1.ChangesNum - 1;
            /**/
            _correlation1To2.Clear();
            using (await _correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
            {
                foreach (var corList in _correlation2To1)
                {
                    corList.Clear();
                }
            }
            await _secondChunkCollection.TouchBulkAsync(
                Enumerable.Range(0,_collectionChanged2Copy.Count).ToList()
            ).ConfigureAwait(false);
        }
        private async Task ResetActionAsync2()
        {
            _collectionChanged2Copy.Clear();
            for (int i = 0; i < _correlation1To2.Count; i++)
            {
                _correlation1To2[i] = -1;
            }
            using (await _correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
            {
                _correlation2To1.Clear();
            }
            await _secondChunkCollection.ClearAsync().ConfigureAwait(false);
            await SearchCorrespondentItems2ForCollection1Range(
                Enumerable.Range(0, _collectionChanged1Copy.Count).ToList(), 
                true
            ).ConfigureAwait(false);
            /**/
            var deepCopyArgs2 = await _originCollectionChanged2.GetDeepCopyAsync().ConfigureAwait(false);
            _changedArgsDict2[deepCopyArgs2.ChangesNum] = deepCopyArgs2;
            _prevChangesCounter2 = deepCopyArgs2.ChangesNum - 1;
        }
        private readonly SemaphoreSlim _lockSem = new SemaphoreSlim(1);
        private readonly List<TItem1> _collectionChanged1Copy = new List<TItem1>();
        private readonly List<TItem2> _collectionChanged2Copy = new List<TItem2>();
        private readonly List<List<int>> _correlation2To1 = new List<List<int>>();
        private readonly List<int> _correlation1To2 = new List<int>();

        private async Task SearchCorrespondentItems2ForCollection1Range(
            IList<int> range,
            bool replace2ToNew = false,
            List<int> listTouch2Indices = null,
            List<Tuple<int, TItem2>> bulkUpdate2Args = null
        )
        {
            var indexedCollection2 = _collectionChanged2Copy.WithIndex().ToList();
            var bulkUpdateArgs = new List<Tuple<int, MutableTuple<TItem1, TItem2>>>();
            listTouch2Indices = listTouch2Indices ?? new List<int>();
            foreach (int i in range)
            {
                Assert.True(i >= 0 && i < _collectionChanged1Copy.Count);
                if (_correlation1To2[i] == -1)
                {
                    var correspondentItem2 = indexedCollection2
                        .FirstOrDefault(_ => _comparisonPredicate(_collectionChanged1Copy[i], _.Item2));
                    if (correspondentItem2 != null)
                    {
                        _correlation1To2[i] = correspondentItem2.Item1;
                        using (await _correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
                        {
                            _correlation2To1[correspondentItem2.Item1].Add(i);
                            if (_correlation2To1[correspondentItem2.Item1].Count == 1)
                                listTouch2Indices.Add(correspondentItem2.Item1);
                        }
                        bulkUpdateArgs.Add(
                            Tuple.Create(
                                i,
                                MutableTuple.Create(
                                    _collectionChanged1Copy[i],
                                    correspondentItem2.Item2
                                )
                            )
                        );
                    }
                    else if (replace2ToNew)
                    {
                        bulkUpdateArgs.Add(
                            Tuple.Create(
                                i,
                                MutableTuple.Create(
                                    _collectionChanged1Copy[i],
                                    default(TItem2)
                                )
                            )
                        );
                    }
                }
            }
            if (bulkUpdateArgs.Any())
            {
                await _firstChunkCollection.ReplaceBulkAsync(
                    bulkUpdateArgs
                    ).ConfigureAwait(false);
            }
            if (bulkUpdate2Args != null && bulkUpdate2Args.Any())
            {
                await _secondChunkCollection.ReplaceBulkAsync(
                    bulkUpdate2Args
                    ).ConfigureAwait(false);
                listTouch2Indices = listTouch2Indices.Except(bulkUpdate2Args.Select(_ => _.Item1).ToList()).ToList();
            }
            if (listTouch2Indices.Any())
                await _secondChunkCollection.TouchBulkAsync(
                    listTouch2Indices.Distinct().ToList()
                ).ConfigureAwait(false);
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
                            /* First collection */
                            {
                                var currentChangesNumInDictToRemove =
                                    _changedArgsDict1.Keys.Where(_ => _ <= _prevChangesCounter1).ToList();
                                foreach (long key in currentChangesNumInDictToRemove)
                                {
                                    MyNotifyCollectionChangedArgs<TItem1> removedArgs;
                                    _changedArgsDict1.TryRemove(key, out removedArgs);
                                }
                                MyNotifyCollectionChangedArgs<TItem1> nextArgs;
                                if (
                                    _changedArgsDict1.TryRemove(
                                        _prevChangesCounter1 + 1,
                                        out nextArgs
                                    )
                                )
                                {
                                    lockSemCalledWrapper.Called = true;
                                    if (nextArgs.ChangedAction == EMyCollectionChangedAction.Reset)
                                    {
                                        await ResetActionAsync1().ConfigureAwait(false);
                                    }
                                    else if (nextArgs.ChangedAction == EMyCollectionChangedAction.NewItemsRangeInserted)
                                    {
                                        var insertIndex = nextArgs.NewItemsIndices[0];
                                        var insertCount = nextArgs.NewItems.Count;
                                        if (insertCount > 0)
                                        {
                                            _collectionChanged1Copy.InsertRange(
                                                insertIndex,
                                                nextArgs.NewItems
                                            );
                                            _correlation1To2.InsertRange(
                                                insertIndex,
                                                Enumerable.Repeat(-1, insertCount)
                                            );
                                            using (await _correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
                                            {
                                                foreach (List<int> list in _correlation2To1)
                                                {
                                                    for (int m = 0; m < list.Count; m++)
                                                    {
                                                        if (list[m] >= insertIndex)
                                                            list[m] += insertCount;
                                                    }
                                                }
                                            }
                                            await _firstChunkCollection.InsertRangeAtAsync(
                                                insertIndex,
                                                nextArgs.NewItems.Select(_ => MutableTuple.Create(_,default(TItem2))).ToList()
                                            ).ConfigureAwait(false);
                                            await SearchCorrespondentItems2ForCollection1Range(
                                                Enumerable.Range(insertIndex,insertCount).ToList()
                                            ).ConfigureAwait(false);
                                        }
                                        _prevChangesCounter1++;
                                    }
                                    else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved)
                                    {
                                        var removeIndex = nextArgs.OldItemsIndices[0];
                                        var removeCount = nextArgs.OldItems.Count;
                                        if (removeCount > 0)
                                        {
                                            _collectionChanged1Copy.RemoveRange(
                                                removeIndex,
                                                removeCount
                                            );
                                            _correlation1To2.RemoveRange(
                                                removeIndex,
                                                removeCount
                                            );
                                            var listTouch2 = new List<int>();
                                            using (await _correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
                                            {
                                                for (int listI = 0; listI < _correlation2To1.Count; listI++)
                                                {
                                                    var list = _correlation2To1[listI];
                                                    if (
                                                        list.RemoveAll(
                                                            _ =>
                                                                _ >= removeIndex
                                                                && _ < removeIndex + removeCount
                                                        ) > 0 
                                                        && list.Count == 0
                                                    )
                                                    {
                                                        listTouch2.Add(listI);
                                                    }
                                                    for (int m = 0; m < list.Count; m++)
                                                    {
                                                        if (list[m] >= removeIndex + removeCount)
                                                            list[m] -= removeCount;
                                                    }
                                                }
                                            }
                                            await _firstChunkCollection.RemoveRangeAsync(
                                                removeIndex,
                                                removeCount
                                            ).ConfigureAwait(false);
                                            await _secondChunkCollection.TouchBulkAsync(
                                                listTouch2.Distinct().ToList()
                                            ).ConfigureAwait(false);
                                        }
                                        _prevChangesCounter1++;
                                    }
                                    else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                    {
                                        var count = nextArgs.NewItems.Count;
                                        if (count > 0)
                                        {
                                            var touch2List = new List<int>();
                                            for (int i = 0; i < count; i++)
                                            {
                                                var newItemIndex = nextArgs.NewItemsIndices[i];
                                                var newItem = nextArgs.NewItems[i];
                                                /**/
                                                _collectionChanged1Copy[newItemIndex] = newItem;
                                                var oldCorrelationIndex = _correlation1To2[newItemIndex];
                                                if (oldCorrelationIndex != -1)
                                                {
                                                    using (await _correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
                                                    {
                                                        _correlation2To1[oldCorrelationIndex].Remove(newItemIndex);
                                                        if(_correlation2To1[oldCorrelationIndex].Count == 0)
                                                            touch2List.Add(oldCorrelationIndex);
                                                    }
                                                    _correlation1To2[newItemIndex] = -1;
                                                }
                                            }
                                            // _firstCollection autoupdates cause of _correlation1To2[newItemIndex] == -1
                                            await SearchCorrespondentItems2ForCollection1Range(
                                                nextArgs.NewItemsIndices,
                                                true,
                                                touch2List
                                            ).ConfigureAwait(false);
                                        }
                                        _prevChangesCounter1++;
                                    }
                                    else
                                    {
                                        throw new NotImplementedException();
                                    }
                                }
                            }
                            /* Second collection */
                            {
                                var currentChangesNumInDictToRemove =
                                    _changedArgsDict2.Keys.Where(_ => _ <= _prevChangesCounter2).ToList();
                                foreach (long key in currentChangesNumInDictToRemove)
                                {
                                    MyNotifyCollectionChangedArgs<TItem2> removedArgs;
                                    _changedArgsDict2.TryRemove(key, out removedArgs);
                                }
                                MyNotifyCollectionChangedArgs<TItem2> nextArgs;
                                if (
                                    _changedArgsDict2.TryRemove(
                                        _prevChangesCounter2 + 1,
                                        out nextArgs
                                        )
                                    )
                                {
                                    lockSemCalledWrapper.Called = true;
                                    if (nextArgs.ChangedAction == EMyCollectionChangedAction.Reset)
                                    {
                                        await ResetActionAsync2().ConfigureAwait(false);
                                    }
                                    else if (nextArgs.ChangedAction == EMyCollectionChangedAction.NewItemsRangeInserted)
                                    {
                                        var insertIndex = nextArgs.NewItemsIndices[0];
                                        var insertCount = nextArgs.NewItems.Count;
                                        if (insertCount > 0)
                                        {
                                            _collectionChanged2Copy.InsertRange(
                                                insertIndex,
                                                nextArgs.NewItems
                                            );
                                            for (int i = 0; i < _correlation1To2.Count; i++)
                                            {
                                                if (_correlation1To2[i] >= insertIndex)
                                                    _correlation1To2[i] += insertCount;
                                            }
                                            using (await _correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
                                            {
                                                for (int i = 0; i < insertCount; i++)
                                                {
                                                    _correlation2To1.Insert(insertIndex, new List<int>());
                                                }
                                            }
                                            await _secondChunkCollection.InsertRangeAtAsync(
                                                insertIndex,
                                                nextArgs.NewItems
                                            ).ConfigureAwait(false);
                                            await SearchCorrespondentItems2ForCollection1Range(
                                                Enumerable.Range(0, _collectionChanged1Copy.Count).ToList(),
                                                listTouch2Indices: Enumerable.Range(insertIndex,insertCount).ToList()
                                            ).ConfigureAwait(false);
                                        }
                                        _prevChangesCounter2++;
                                    }
                                    else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved)
                                    {
                                        var removeIndex = nextArgs.OldItemsIndices[0];
                                        var removeCount = nextArgs.OldItems.Count;
                                        if (removeCount > 0)
                                        {
                                            var updateCollection1Indices = new List<int>();
                                            using (await _correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
                                            {
                                                for (int i = removeIndex; i < (removeIndex + removeCount); i++)
                                                {
                                                    foreach (int j in _correlation2To1[i])
                                                    {
                                                        _correlation1To2[j] = -1;
                                                        updateCollection1Indices.Add(j);
                                                    }
                                                    _correlation2To1[i].Clear();
                                                }
                                                _correlation2To1.RemoveRange(
                                                    removeIndex,
                                                    removeCount
                                                    );
                                            }
                                            _collectionChanged2Copy.RemoveRange(
                                                removeIndex,
                                                removeCount
                                                );
                                            await _secondChunkCollection.RemoveRangeAsync(
                                                removeIndex,
                                                removeCount
                                            ).ConfigureAwait(false);
                                            if (updateCollection1Indices.Any())
                                                await SearchCorrespondentItems2ForCollection1Range(
                                                    updateCollection1Indices,
                                                    true
                                                ).ConfigureAwait(false);
                                        }
                                        _prevChangesCounter2++;
                                    }
                                    else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                    {
                                        var count = nextArgs.NewItems.Count;
                                        if (count > 0)
                                        {
                                            var bulkUpdate2Args = new List<Tuple<int, TItem2>>();
                                            var updateCollection1Indices = new List<int>();
                                            for (int i = 0; i < count; i++)
                                            {
                                                bulkUpdate2Args.Add(
                                                    Tuple.Create(
                                                        nextArgs.NewItemsIndices[i],
                                                        nextArgs.NewItems[i]
                                                        )
                                                    );
                                                _collectionChanged2Copy[nextArgs.NewItemsIndices[i]] =
                                                    nextArgs.NewItems[i];
                                                using (await _correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
                                                {
                                                    foreach (var j in _correlation2To1[nextArgs.NewItemsIndices[i]])
                                                    {
                                                        _correlation1To2[j] = -1;
                                                        updateCollection1Indices.Add(j);
                                                    }
                                                    _correlation2To1[nextArgs.NewItemsIndices[i]].Clear();
                                                }
                                            }
                                            if (updateCollection1Indices.Any())
                                                await SearchCorrespondentItems2ForCollection1Range(
                                                    updateCollection1Indices,
                                                    true,
                                                    bulkUpdate2Args: bulkUpdate2Args
                                                ).ConfigureAwait(false);
                                        }
                                        _prevChangesCounter2++;
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
            catch (OperationCanceledException){}
            catch (WrongDisposableObjectStateException){}
            catch (Exception exc)
            {
                MiscFuncs.HandleUnexpectedError(exc, _log);
            }
        }
        /*
        public async Task DumpToLog()
        {
            using (_stateHelper.GetFuncWrapper())
            {
                using (await _lockSem.GetDisposable().ConfigureAwait(false))
                {
                    _log.Trace($"MyJoinedCollection Dump");
                    _log.Trace($"_firstChunkCollection {(await _firstChunkCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()}");
                    _log.Trace($"_secondChunkCollection {(await _secondChunkCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()}");
                    _log.Trace($"_secondChunkIndexed {(await _secondChunkIndexed.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()}");
                    _log.Trace($"_secondChunkFiltered {(await _secondChunkFiltered.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()}");
                    _log.Trace($"_secondChunkModifiedObservable {(await _secondChunkModifiedObservable.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()}");
                    _log.Trace($"_mergedFullOuterJoin {(await _mergedFullOuterJoin.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()}");
                    _log.Trace($"_resultCollection {(await _resultCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()}");
                    _log.Trace($"_correlation1To2 {_correlation1To2.WriteObjectToJson()}");
                    using (await _correlation2To1.GetLockSem().GetDisposable().ConfigureAwait(false))
                    {
                        _log.Trace($"_correlation2To1 {_correlation2To1.WriteObjectToJson()}");
                    }
                }
            }
        }
        */
        private readonly MyObservableCollectionSafeAsyncImpl<MutableTuple<TItem1, TItem2>> _firstChunkCollection
            = new MyObservableCollectionSafeAsyncImpl<MutableTuple<TItem1, TItem2>>();
        /**/
        private readonly MyObservableCollectionSafeAsyncImpl<TItem2> _secondChunkCollection
            = new MyObservableCollectionSafeAsyncImpl<TItem2>();
        private MyIndexedObservableCollectionImpl<TItem2> _secondChunkIndexed;
        private MyFilteredObservableCollectionImpl<MutableTuple<int, TItem2>> _secondChunkFiltered;
        private MyModifiedObservableCollectionImpl<
            MutableTuple<int, TItem2>, 
            MutableTuple<TItem1,TItem2>
        > _secondChunkModifiedObservable;
        /**/
        private MyConcatObservableCollectionImpl<MutableTuple<TItem1, TItem2>> _mergedFullOuterJoin; 
        /**/
        private MyFilteredObservableCollectionImpl<MutableTuple<TItem1, TItem2>> _resultCollection;
        public IObservable<MyNotifyCollectionChangedArgs<MutableTuple<TItem1, TItem2>>> CollectionChangedObservable
            => _resultCollection.CollectionChangedObservable;
        public async Task<MyNotifyCollectionChangedArgs<MutableTuple<TItem1, TItem2>>> GetDeepCopyAsync()
        {
            return await _resultCollection.GetDeepCopyAsync().ConfigureAwait(false);
        }
        private readonly ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem1>> _changedArgsDict1
            = new ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem1>>();
        private long _prevChangesCounter1 = -2;
        /**/
        private readonly ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem2>> _changedArgsDict2
            = new ConcurrentDictionary<long, MyNotifyCollectionChangedArgs<TItem2>>();
        private long _prevChangesCounter2 = -2;
        private readonly Logger _log = LogManager.GetCurrentClassLogger();
        private readonly DisposableObjectStateHelper _stateHelper
            = new DisposableObjectStateHelper("MyJoinedObservableCollectionImpl");
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private readonly List<IMyAsyncDisposable> _asyncSubscriptions = new List<IMyAsyncDisposable>();
        public async Task MyDisposeAsync()
        {
            _cts.Cancel();
            await _stateHelper.MyDisposeAsync().ConfigureAwait(false);
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _subscriptions.Clear();
            foreach (var asyncSubscription in _asyncSubscriptions)
            {
                await asyncSubscription.MyDisposeAsync().ConfigureAwait(false);
            }
            _asyncSubscriptions.Clear();
            _cts.Dispose();
        }
    }
}
