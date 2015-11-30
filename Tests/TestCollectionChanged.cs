using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BtmI2p.MiscUtils;
using BtmI2p.MyNotifyPropertyChanged.MyObservableCollections;
using NLog;
using Xunit;
using Xunit.Abstractions;

namespace BtmI2p.MyNotifyPropertyChanged.Tests
{
    public class CollectionChangedTests
    {
        private readonly ITestOutputHelper _outputHelper;
        public CollectionChangedTests(
            ITestOutputHelper outputHelper
            )
        {
            Assert.NotNull(outputHelper);
            _outputHelper = outputHelper;
        }
        [Fact]
        public async Task TestCollectionChanged()
        {
            var collectionChanged = new MyObservableCollectionSafeAsyncImpl<string>();
            MyNotifyCollectionChangedArgs<string> lastObservableCollectionChangedArgs = null;
            using (
                collectionChanged.CollectionChangedObservable.Subscribe(
                    args => lastObservableCollectionChangedArgs = args
                )
            )
            {
                var listToCompare = new List<string>();
                var range1 = new[]
                {
                    "str0",
                    "str1",
                    "str2",
                    "str3",
                    "str4",
                    "str5",
                    "str6",
                    "str7",
                    "str8",
                    "str9"
                };
                await collectionChanged.AddRangeAsync(range1).ConfigureAwait(false);
                listToCompare.AddRange(range1);
                await Task.Delay(10).ConfigureAwait(false);
                /* Check deep copy type */
                var deepCopy1 = await collectionChanged.GetDeepCopyAsync().ConfigureAwait(false);
                Assert.Equal(
                    deepCopy1.ChangedAction,
                    EMyCollectionChangedAction.NewItemsRangeInserted
                );
                Assert.Equal(
                    deepCopy1.NewItems,
                    listToCompare
                );
                /**/
                Assert.NotNull(lastObservableCollectionChangedArgs);
                Assert.True(
                    lastObservableCollectionChangedArgs.ChangedAction == EMyCollectionChangedAction.NewItemsRangeInserted
                    && lastObservableCollectionChangedArgs.NewItemsIndices.Count == 1
                    && lastObservableCollectionChangedArgs.NewItems.Count == range1.Length
                    && lastObservableCollectionChangedArgs.NewItemsIndices[0] == 0
                    && lastObservableCollectionChangedArgs.NewItems.SequenceEqual(range1)
                );
                /**/
                await collectionChanged.RemoveRangeAsync(1, 2).ConfigureAwait(false);
                listToCompare.RemoveRange(1,2);
                await Task.Delay(10).ConfigureAwait(false);
                Assert.True(
                    lastObservableCollectionChangedArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved
                    && lastObservableCollectionChangedArgs.OldItems.Count == 2
                    && lastObservableCollectionChangedArgs.OldItems.SequenceEqual(range1.Skip(1).Take(2))
                    && lastObservableCollectionChangedArgs.OldItemsIndices.Count == 1
                    && lastObservableCollectionChangedArgs.OldItemsIndices[0] == 1
                );
                Assert.Equal(
                    (await collectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems,
                    listToCompare
                );
                /**/
                await collectionChanged.ReplaceBulkAsync(
                    new[]
                    {
                        Tuple.Create(2, "NewString1"),
                        Tuple.Create(4, "NewString2")
                    }
                ).ConfigureAwait(false);
                var oldItems = new[] {listToCompare[2], listToCompare[4]};
                listToCompare[2] = "NewString1";
                listToCompare[4] = "NewString2";
                await Task.Delay(10).ConfigureAwait(false);
                Assert.True(
                    lastObservableCollectionChangedArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged
                    && lastObservableCollectionChangedArgs.NewItemsIndices.Count == 2
                    && lastObservableCollectionChangedArgs.OldItemsIndices.Count == 2
                    && lastObservableCollectionChangedArgs.NewItems.Count == 2
                    && lastObservableCollectionChangedArgs.OldItems.Count == 2
                    && lastObservableCollectionChangedArgs.NewItems.SequenceEqual(new[] { "NewString1", "NewString2" })
                    && lastObservableCollectionChangedArgs.NewItemsIndices.SequenceEqual(new[] { 2, 4 })
                    && lastObservableCollectionChangedArgs.OldItems.SequenceEqual(oldItems)
                    && lastObservableCollectionChangedArgs.OldItemsIndices.SequenceEqual(new[] { 2, 4 })
                );
                Assert.Equal(
                    (await collectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems,
                    listToCompare
                );
                /**/
                await collectionChanged.ClearAsync().ConfigureAwait(false);
                listToCompare.Clear();
                await Task.Delay(10).ConfigureAwait(false);
                Assert.True(
                    lastObservableCollectionChangedArgs.ChangedAction == EMyCollectionChangedAction.Reset
                );
                Assert.Equal(
                    (await collectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems,
                    listToCompare
                );
            }
        }
        
        [Fact]
        public async Task TestFilteredCollectionChanged()
        {
            var collectionChanged = new MyObservableCollectionSafeAsyncImpl<string>();
            Func<string, Task<bool>> predicate = async _ => await Task.FromResult(
                _.StartsWith("1") || _.EndsWith("eb")
            ).ConfigureAwait(false);
            var proxyFilter = new ObservableCollectionProxyFilter<string>(predicate);
            var filteredCollectionChanged = await MyFilteredObservableCollectionImpl<string>.CreateInstance(
                collectionChanged,
                proxyFilter
            ).ConfigureAwait(false);
            MyNotifyCollectionChangedArgs<string> lastObservableCollectionChangedArgs = null;
            MyNotifyCollectionChangedArgs<string> lastFilteredObservableCollectionChangedArgs = null;
            var originElementCopy = new List<string>();
            try
            {
                using (collectionChanged.CollectionChangedObservable.Subscribe(
                    args => lastObservableCollectionChangedArgs = args))
                {
                    using (filteredCollectionChanged.CollectionChangedObservable.Subscribe(
                        args => lastFilteredObservableCollectionChangedArgs = args))
                    {
                        var addRange1 = new[]
                        {
                            "La11a",
                            "1asqqqq",
                            "bs123444",
                            "1bar",
                            "cat",
                            "leb",
                            "1sp11133eb"
                        };
                        await collectionChanged.AddRangeAsync(addRange1).ConfigureAwait(false);
                        originElementCopy.AddRange(addRange1);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.True(
                            lastFilteredObservableCollectionChangedArgs.ChangedAction == EMyCollectionChangedAction.NewItemsRangeInserted
                            && lastFilteredObservableCollectionChangedArgs.NewItems.SequenceEqual(await addRange1.WhereAsync(predicate).ConfigureAwait(false))
                        );
                        Assert.Equal(
                            await originElementCopy.WhereAsync(predicate).ConfigureAwait(false),
                            (await filteredCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /**/
                        await collectionChanged.RemoveRangeAsync(1, 2).ConfigureAwait(false);
                        originElementCopy.RemoveRange(1, 2);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.True(
                            lastFilteredObservableCollectionChangedArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved
                            && lastFilteredObservableCollectionChangedArgs.OldItems.SequenceEqual(await addRange1.Skip(1).Take(2).WhereAsync(predicate).ConfigureAwait(false))
                        );
                        Assert.Equal(
                            await originElementCopy.WhereAsync(predicate).ConfigureAwait(false),
                            (await filteredCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /**/
                        var insertRange1 = new[]
                        {
                            "insert",
                            "1mmm"
                        };
                        await collectionChanged.InsertRangeAtAsync(
                            2,
                            insertRange1
                        ).ConfigureAwait(false);
                        originElementCopy.InsertRange(
                            2,
                            insertRange1
                        );
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.True(
                            lastFilteredObservableCollectionChangedArgs.ChangedAction == EMyCollectionChangedAction.NewItemsRangeInserted
                            && lastFilteredObservableCollectionChangedArgs.NewItems.SequenceEqual(await insertRange1.WhereAsync(predicate).ConfigureAwait(false))
                        );
                        Assert.Equal(
                            await originElementCopy.WhereAsync(predicate).ConfigureAwait(false),
                            (await filteredCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /**/
                        var currentOriginCount = originElementCopy.Count;
                        var bulkReplaceArgs = new List<Tuple<int, string>>();
                        for (int i = 0; i < currentOriginCount; i++)
                        {
                            var newItem = (await predicate(originElementCopy[i]).ConfigureAwait(false))
                                ? "2" + originElementCopy[i]
                                : "1" + originElementCopy[i];
                            bulkReplaceArgs.Add(Tuple.Create(i, newItem));
                            originElementCopy[i] = newItem;
                        }
                        await collectionChanged.ReplaceBulkAsync(
                            bulkReplaceArgs
                        ).ConfigureAwait(false);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            await originElementCopy.WhereAsync(predicate).ConfigureAwait(false),
                            (await filteredCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /*Predicate changing*/
                        predicate = async _ => await Task.FromResult(_.Contains("a"));
                        proxyFilter.Predicate = predicate;
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            await originElementCopy.WhereAsync(predicate).ConfigureAwait(false),
                            (await filteredCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    }
                }
            }
            finally
            {
                await filteredCollectionChanged.MyDisposeAsync().ConfigureAwait(false);
            }
        }

        private static int CompareDinosByLength(string x, string y)
        {
            if (x == null)
            {
                if (y == null)
                {
                    // If x is null and y is null, they're
                    // equal. 
                    return 0;
                }
                else
                {
                    // If x is null and y is not null, y
                    // is greater. 
                    return -1;
                }
            }
            else
            {
                // If x is not null...
                //
                if (y == null)
                    // ...and y is null, x is greater.
                {
                    return 1;
                }
                else
                {
                    // ...and y is not null, compare the 
                    // lengths of the two strings.
                    //
                    int retval = x.Length.CompareTo(y.Length);

                    if (retval != 0)
                    {
                        // If the strings are not of equal length,
                        // the longer string is greater.
                        //
                        return retval;
                    }
                    else
                    {
                        // If the strings are of equal length,
                        // sort them with ordinary string comparison.
                        //
                        return x.CompareTo(y);
                    }
                }
            }
        }
        [Fact]
        public async Task TestOrderedCollectionChanged()
        {
            var collectionChanged = new MyObservableCollectionSafeAsyncImpl<string>();
            var lengthComparer = Comparer<string>.Create(
                CompareDinosByLength
                );
            var proxyOrdered = new MyObservableCollectionProxyComparer<string>(
                lengthComparer
            );
            var orderedCollection = await MyOrderedObservableCollection<string>.CreateInstance(
                collectionChanged,
                proxyOrdered
            ).ConfigureAwait(false);
            try
            {
                MyNotifyCollectionChangedArgs<string> lastOrderedObservableCollectionChangedArgs = null;
                using (orderedCollection.CollectionChangedObservable.Subscribe(
                    args => lastOrderedObservableCollectionChangedArgs = args))
                {
                    var insertRange1 = new[]
                    {
                        "Pachycephalosaurus",
                        "Amargasaurus",
                        "",
                        null,
                        "Mamenchisaurus",
                        "Deinonychus"
                    }.ToList();
                    var originCopy = new List<string>();
                    await collectionChanged.AddRangeAsync(
                        insertRange1
                    ).ConfigureAwait(false);
                    originCopy.AddRange(insertRange1);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.NotNull(lastOrderedObservableCollectionChangedArgs);
                    Assert.Equal(
                        originCopy.OrderBy(_ => _, lengthComparer),
                        (await orderedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                    );
                    /**/
                    var bulkUpdateArgs = new List<Tuple<int, string>>()
                    {
                        Tuple.Create(2, "Velociraptor"),
                        Tuple.Create(3, "Tsaagan"),
                        Tuple.Create(5, (string)null)
                    };
                    await collectionChanged.ReplaceBulkAsync(
                        bulkUpdateArgs
                    ).ConfigureAwait(false);
                    foreach (Tuple<int, string> arg in bulkUpdateArgs)
                    {
                        originCopy[arg.Item1] = arg.Item2;
                    }
                    await Task.Delay(10).ConfigureAwait(false);
                    var orderedOrigin1 = originCopy.OrderBy(_ => _, lengthComparer).ToList();
                    var changedItemsIndices = bulkUpdateArgs.Select(
                        _ => orderedOrigin1.IndexOf(_.Item2)
                    ).ToList();
                    Assert.True(changedItemsIndices.All(_ => _ >= 0));
                    Assert.Equal(
                        originCopy.OrderBy(_ => _, lengthComparer),
                        (await orderedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                    );
                    /**/
                    var insertRange2 = new[]
                    {
                        null,
                        null,
                        null,
                        "",
                        "Amargasaurus",
                        "Amargasaurus",
                        "Amargasaurus",
                        "Mamenchisaurus",
                        "Mamenchisaurus",
                        "Mamenchisaurus",
                        null,
                        ""
                    };
                    await collectionChanged.InsertRangeAtAsync(
                        3,
                        insertRange2
                    ).ConfigureAwait(false);
                    originCopy.InsertRange(
                        3,
                        insertRange2
                    );
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originCopy.OrderBy(_ => _, lengthComparer),
                        (await orderedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                    );
                    /**/
                    await collectionChanged.RemoveRangeAsync(3, 2).ConfigureAwait(false);
                    originCopy.RemoveRange(3, 2);
                    await collectionChanged.RemoveRangeAsync(2, 3).ConfigureAwait(false);
                    originCopy.RemoveRange(2, 3);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originCopy.OrderBy(_ => _, lengthComparer),
                        (await orderedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                    );
                    /* Change comparer */
                    var defaultComparer = Comparer<string>.Default;
                    proxyOrdered.Comparer = defaultComparer;
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originCopy.OrderBy(_ => _, defaultComparer),
                        (await orderedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                    );
                }
            }
            finally
            {
                await orderedCollection.MyDisposeAsync().ConfigureAwait(false);
            }
        }

        private IList<T1> LastN<T1>(IList<T1> collection, int n)
        {
            Assert.NotNull(collection);
            return collection.Reverse().Take(n).Reverse().ToList();
        }

        [Fact]
        public async Task TestNLastCollectionChanged()
        {
            var collectionChanged = new MyObservableCollectionSafeAsyncImpl<int>();
            var proxyLastN = new MyObservableCollectionProxyN(1);
            var lastNCollection = await MyNLastObservableCollectionImpl<int>.CreateInstance(
                collectionChanged,
                proxyLastN
            ).ConfigureAwait(false);
            MyNotifyCollectionChangedArgs<int> lastLastNObservableCollectionChangedArgs = null;
            var originCopy = new List<int>();
            try
            {
                using (lastNCollection.CollectionChangedObservable.Subscribe(
                    args => lastLastNObservableCollectionChangedArgs = args))
                {
                    foreach (var n in new[] {3, 500, 1, 1000, 200, 2, 2})
                    {
                        proxyLastN.N = n;
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            LastN(originCopy, n),
                            (await lastNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        await collectionChanged.ClearAsync().ConfigureAwait(false);
                        originCopy.Clear();
                        /**/
                        var insertRange1 = Enumerable.Range(0, 10).ToList();
                        await collectionChanged.AddRangeAsync(insertRange1).ConfigureAwait(false);
                        originCopy.AddRange(insertRange1);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.NotNull(lastLastNObservableCollectionChangedArgs);
                        Assert.True(
                            lastLastNObservableCollectionChangedArgs.ChangedAction == EMyCollectionChangedAction.NewItemsRangeInserted
                            && lastLastNObservableCollectionChangedArgs.NewItems.Count == Math.Min(insertRange1.Count, n)
                            && lastLastNObservableCollectionChangedArgs.NewItems.SequenceEqual(LastN(insertRange1, n))
                            && lastLastNObservableCollectionChangedArgs.NewItemsIndices[0] == 0
                        );
                        Assert.Equal(
                            LastN(originCopy, n),
                            (await lastNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /* Insert not affected */
                        var lastLastNCollectionChangedArgs1 = lastLastNObservableCollectionChangedArgs;
                        if (originCopy.Count > n)
                        {
                            var insertRange2 = Enumerable.Range(20, 3).ToList();
                            await collectionChanged.InsertRangeAtAsync(0, insertRange2).ConfigureAwait(false);
                            originCopy.InsertRange(0, insertRange2);
                            await Task.Delay(10).ConfigureAwait(false);
                            Assert.Equal(
                                lastLastNObservableCollectionChangedArgs,
                                lastLastNCollectionChangedArgs1
                            );
                            Assert.Equal(
                                LastN(originCopy, n),
                                (await lastNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                            );
                        }
                        /**/
                        var insertRange3 = Enumerable.Range(30, 3).ToList();
                        await collectionChanged.AddRangeAsync(insertRange3).ConfigureAwait(false);
                        originCopy.AddRange(insertRange3);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.NotEqual(
                            lastLastNObservableCollectionChangedArgs,
                            lastLastNCollectionChangedArgs1
                        );
                        Assert.Equal(
                            LastN(originCopy, n),
                            (await lastNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /* First not in N last */
                        if (originCopy.Count > n)
                        {
                            var insertRange4 = Enumerable.Range(40, 3).ToList();
                            var newInsertIndex = originCopy.Count - 1 - n;
                            await
                                collectionChanged.InsertRangeAtAsync(newInsertIndex, insertRange4).ConfigureAwait(false);
                            originCopy.InsertRange(newInsertIndex, insertRange4);
                            await Task.Delay(10).ConfigureAwait(false);
                            Assert.Equal(
                                LastN(originCopy, n),
                                (await lastNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                            );
                        }
                        /**/
                        var lastLastNCollectionChangedArgs2 = lastLastNObservableCollectionChangedArgs;
                        if (originCopy.Count >= (n + 3))
                        {
                            await collectionChanged.RemoveRangeAsync(1, 2).ConfigureAwait(false);
                            originCopy.RemoveRange(1, 2);
                            await Task.Delay(10).ConfigureAwait(false);
                            Assert.Equal(
                                lastLastNObservableCollectionChangedArgs,
                                lastLastNCollectionChangedArgs2
                            );
                            Assert.Equal(
                                LastN(originCopy, n),
                                (await lastNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                            );
                        }
                        /**/
                        var indexToRemove1 = originCopy.Count - 1;
                        await collectionChanged.RemoveRangeAsync(indexToRemove1, 1).ConfigureAwait(false);
                        originCopy.RemoveRange(indexToRemove1, 1);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.NotEqual(
                            lastLastNObservableCollectionChangedArgs,
                            lastLastNCollectionChangedArgs2
                        );
                        Assert.Equal(
                            LastN(originCopy, n),
                            (await lastNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /**/
                        var indexToRemove2 = originCopy.Count - 1 - 4;
                        await collectionChanged.RemoveRangeAsync(indexToRemove2, 3).ConfigureAwait(false);
                        originCopy.RemoveRange(indexToRemove2,3);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            LastN(originCopy, n),
                            (await lastNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /**/
                        var lastLastNCollectionChangedArgs3 = lastLastNObservableCollectionChangedArgs;
                        if (originCopy.Count >= (n + 2))
                        {
                            await collectionChanged.ReplaceItemAtAsync(1, 105).ConfigureAwait(false);
                            originCopy[1] = 105;
                            await Task.Delay(10).ConfigureAwait(false);
                            Assert.Equal(
                                lastLastNObservableCollectionChangedArgs,
                                lastLastNCollectionChangedArgs3
                                );
                            Assert.Equal(
                                LastN(originCopy, n),
                                (await lastNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                                );
                        }
                        /**/
                        var lastIndex = originCopy.Count - 1;
                        await collectionChanged.ReplaceItemAtAsync(lastIndex, 107).ConfigureAwait(false);
                        originCopy[lastIndex] = 107;
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.NotEqual(
                            lastLastNObservableCollectionChangedArgs,
                            lastLastNCollectionChangedArgs3
                        );
                        Assert.Equal(
                            LastN(originCopy, n),
                            (await lastNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    }
                }
            }
            finally
            {
                await lastNCollection.MyDisposeAsync().ConfigureAwait(false);
            }
        }

        [Fact]
        public async Task TestReversedCollectionChanged()
        {
            var collectionChanged = new MyObservableCollectionSafeAsyncImpl<int>();
            var reversedCollectionChanged = await MyReversedObservableCollection<int>.CreateInstance(
                collectionChanged).ConfigureAwait(false);
            var originCopy = new List<int>();
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    await collectionChanged.ClearAsync().ConfigureAwait(false);
                    originCopy.Clear();
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        Enumerable.Reverse(originCopy),
                        (await reversedCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    var addRange1 = Enumerable.Range(2, 35).ToList();
                    await collectionChanged.AddRangeAsync(addRange1).ConfigureAwait(false);
                    originCopy.AddRange(addRange1);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        Enumerable.Reverse(originCopy),
                        (await reversedCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    await collectionChanged.RemoveRangeAsync(25, 3).ConfigureAwait(false);
                    originCopy.RemoveRange(25, 3);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        Enumerable.Reverse(originCopy),
                        (await reversedCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    await collectionChanged.ReplaceItemAtAsync(1, 155).ConfigureAwait(false);
                    originCopy[1] = 155;
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        Enumerable.Reverse(originCopy),
                        (await reversedCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                }
            }
            finally
            {
                await reversedCollectionChanged.MyDisposeAsync().ConfigureAwait(false);
            }
        }

        [Fact]
        public async Task TestHotSwapCollectionChanged()
        {
            var collectionChanged1 = new MyObservableCollectionSafeAsyncImpl<int>();
            var collectionChanged2 = new MyObservableCollectionSafeAsyncImpl<int>();
            var hotSwapCollectionChanged = await MyHotSwapObservableCollectionImpl<int>.CreateInstance(
                collectionChanged1
            ).ConfigureAwait(false);
            var originCopy = new List<int>();
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    await collectionChanged1.ClearAsync().ConfigureAwait(false);
                    await collectionChanged2.ClearAsync().ConfigureAwait(false);
                    originCopy.Clear();
                    await hotSwapCollectionChanged.ReplaceOriginCollectionChanged(
                        collectionChanged1
                        ).ConfigureAwait(false);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originCopy,
                        (await hotSwapCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    var insertRange1 = Enumerable.Range(10, 15).ToList();
                    await collectionChanged1.AddRangeAsync(
                        insertRange1
                    ).ConfigureAwait(false);
                    originCopy.AddRange(insertRange1);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originCopy,
                        (await hotSwapCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    var insertRange2 = Enumerable.Range(100, 13).ToList();
                    await collectionChanged1.InsertRangeAtAsync(
                        2,
                        insertRange2
                        ).ConfigureAwait(false);
                    originCopy.InsertRange(
                        2,
                        insertRange2
                        );
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originCopy,
                        (await hotSwapCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    var insertRange3 = Enumerable.Range(823, 42).ToList();
                    await collectionChanged2.AddRangeAsync(insertRange3).ConfigureAwait(false);
                    await
                        hotSwapCollectionChanged.ReplaceOriginCollectionChanged(collectionChanged2)
                            .ConfigureAwait(false);
                    originCopy.Clear();
                    originCopy.AddRange(insertRange3);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originCopy,
                        (await hotSwapCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    await collectionChanged2.RemoveRangeAsync(3, 5).ConfigureAwait(false);
                    originCopy.RemoveRange(3, 5);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originCopy,
                        (await hotSwapCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    await
                        hotSwapCollectionChanged.ReplaceOriginCollectionChanged(collectionChanged1)
                            .ConfigureAwait(false);
                    originCopy.Clear();
                    originCopy.AddRange(
                        (await collectionChanged1.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originCopy,
                        (await hotSwapCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                }
            }
            finally
            {
                await hotSwapCollectionChanged.MyDisposeAsync().ConfigureAwait(false);
            }
        }
        private IList<T1> FirstN<T1>(IList<T1> collection, int n)
        {
            Assert.NotNull(collection);
            return collection.Take(n).ToList();
        }
        [Fact]
        public async Task TestNFirstCollection()
        {
            var collectionChanged = new MyObservableCollectionSafeAsyncImpl<int>();
            var proxyLastN = new MyObservableCollectionProxyN(1);
            var firstNCollection = await MyNFirstObservableCollectionImpl<int>.CreateInstance(
                collectionChanged,
                proxyLastN
            ).ConfigureAwait(false);
            MyNotifyCollectionChangedArgs<int> lastLastNObservableCollectionChangedArgs = null;
            var originCopy = new List<int>();
            try
            {
                using (firstNCollection.CollectionChangedObservable.Subscribe(
                    args => lastLastNObservableCollectionChangedArgs = args))
                {
                    foreach (var n in new[] { 3, 500, 1, 1000, 200, 2, 2 })
                    {
                        proxyLastN.N = n;
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            FirstN(originCopy, n),
                            (await firstNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        await collectionChanged.ClearAsync().ConfigureAwait(false);
                        originCopy.Clear();
                        /**/
                        var insertRange1 = Enumerable.Range(0, 10).ToList();
                        await collectionChanged.AddRangeAsync(insertRange1).ConfigureAwait(false);
                        originCopy.AddRange(insertRange1);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.NotNull(lastLastNObservableCollectionChangedArgs);
                        Assert.Equal(
                            FirstN(originCopy, n),
                            (await firstNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /* Insert not affected */
                        var lastLastNCollectionChangedArgs1 = lastLastNObservableCollectionChangedArgs;
                        if (originCopy.Count > n)
                        {
                            var insertRange2 = Enumerable.Range(20, 3).ToList();
                            await collectionChanged.InsertRangeAtAsync(0, insertRange2).ConfigureAwait(false);
                            originCopy.InsertRange(0, insertRange2);
                            await Task.Delay(10).ConfigureAwait(false);
                            Assert.Equal(
                                FirstN(originCopy, n),
                                (await firstNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                            );
                        }
                        /**/
                        var insertRange3 = Enumerable.Range(30, 3).ToList();
                        await collectionChanged.AddRangeAsync(insertRange3).ConfigureAwait(false);
                        originCopy.AddRange(insertRange3);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            FirstN(originCopy, n),
                            (await firstNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /* First not in N last */
                        if (originCopy.Count > n)
                        {
                            var insertRange4 = Enumerable.Range(40, 3).ToList();
                            var newInsertIndex = originCopy.Count - 1 - n;
                            await
                                collectionChanged.InsertRangeAtAsync(newInsertIndex, insertRange4).ConfigureAwait(false);
                            originCopy.InsertRange(newInsertIndex, insertRange4);
                            await Task.Delay(10).ConfigureAwait(false);
                            Assert.Equal(
                                FirstN(originCopy, n),
                                (await firstNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                            );
                        }
                        /**/
                        var lastLastNCollectionChangedArgs2 = lastLastNObservableCollectionChangedArgs;
                        if (originCopy.Count >= (n + 3))
                        {
                            await collectionChanged.RemoveRangeAsync(1, 2).ConfigureAwait(false);
                            originCopy.RemoveRange(1, 2);
                            await Task.Delay(10).ConfigureAwait(false);
                            Assert.Equal(
                                FirstN(originCopy, n),
                                (await firstNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                            );
                        }
                        /**/
                        var indexToRemove1 = originCopy.Count - 1;
                        await collectionChanged.RemoveRangeAsync(indexToRemove1, 1).ConfigureAwait(false);
                        originCopy.RemoveRange(indexToRemove1, 1);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            FirstN(originCopy, n),
                            (await firstNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /**/
                        var indexToRemove2 = originCopy.Count - 1 - 4;
                        await collectionChanged.RemoveRangeAsync(indexToRemove2, 3).ConfigureAwait(false);
                        originCopy.RemoveRange(indexToRemove2, 3);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            FirstN(originCopy, n),
                            (await firstNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                        /**/
                        var lastLastNCollectionChangedArgs3 = lastLastNObservableCollectionChangedArgs;
                        if (originCopy.Count >= (n + 2))
                        {
                            await collectionChanged.ReplaceItemAtAsync(1, 105).ConfigureAwait(false);
                            originCopy[1] = 105;
                            await Task.Delay(10).ConfigureAwait(false);
                            Assert.Equal(
                                FirstN(originCopy, n),
                                (await firstNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                                );
                        }
                        /**/
                        var lastIndex = originCopy.Count - 1;
                        await collectionChanged.ReplaceItemAtAsync(lastIndex, 107).ConfigureAwait(false);
                        originCopy[lastIndex] = 107;
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            FirstN(originCopy, n),
                            (await firstNCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    }
                }
            }
            finally
            {
                await firstNCollection.MyDisposeAsync().ConfigureAwait(false);
            }
        }
        [Fact]
        public async Task TestModifiedCollection()
        {
            var collectionChanged = new MyObservableCollectionSafeAsyncImpl<int>();
            var originElementsCopy = new List<int>();
            Func<int, Task<decimal>> modificationFunc = async i => await Task.FromResult((i + 3)*1.344m);
            var modifiedCollection = await MyModifiedObservableCollectionImpl<int, decimal>.CreateInstance(
                collectionChanged,
                modificationFunc
            ).ConfigureAwait(false);
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    await collectionChanged.ClearAsync().ConfigureAwait(false);
                    originElementsCopy.Clear();
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        await originElementsCopy.SelectAsync(modificationFunc).ConfigureAwait(false),
                        (await modifiedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    var addRange1 = Enumerable.Range(3, 21).ToList();
                    await collectionChanged.AddRangeAsync(
                        addRange1
                        ).ConfigureAwait(false);
                    originElementsCopy.AddRange(addRange1);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        await originElementsCopy.SelectAsync(modificationFunc).ConfigureAwait(false),
                        (await modifiedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    var removeIndex1 = 2;
                    var removeCount1 = 5;
                    await collectionChanged.RemoveRangeAsync(removeIndex1, removeCount1).ConfigureAwait(false);
                    originElementsCopy.RemoveRange(removeIndex1, removeCount1);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        await originElementsCopy.SelectAsync(modificationFunc).ConfigureAwait(false),
                        (await modifiedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    var insertIndex1 = 5;
                    var insertRange1 = Enumerable.Range(105, 15).ToList();
                    await collectionChanged.InsertRangeAtAsync(
                        insertIndex1,
                        insertRange1
                        ).ConfigureAwait(false);
                    originElementsCopy.InsertRange(insertIndex1, insertRange1);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        await originElementsCopy.SelectAsync(modificationFunc).ConfigureAwait(false),
                        (await modifiedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    var newElementIndex1 = 7;
                    var newElement1 = 537;
                    await collectionChanged.ReplaceItemAtAsync(newElementIndex1, newElement1).ConfigureAwait(false);
                    originElementsCopy[newElementIndex1] = newElement1;
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        await originElementsCopy.SelectAsync(modificationFunc).ConfigureAwait(false),
                        (await modifiedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                }
            }
            finally
            {
                await modifiedCollection.MyDisposeAsync().ConfigureAwait(false);
            }
        }

        [Fact]
        public async Task TestUnionCollection()
        {
            var collectionChanged1 = new MyObservableCollectionSafeAsyncImpl<int>();
            var collectionChanged2 = new MyObservableCollectionSafeAsyncImpl<int>();
            var collectionChanged3 = new MyObservableCollectionSafeAsyncImpl<int>();
            var originElementsCopy1= new List<int>();
            var originElementsCopy2 = new List<int>();
            var originElementsCopy3 = new List<int>();
            var unionCollectionChanged = await MyConcatObservableCollectionImpl<int>.CreateInstance(
                new IMyNotifyCollectionChanged<int>[]
                {
                    collectionChanged1,
                    collectionChanged2,
                    collectionChanged3
                }
            ).ConfigureAwait(false);
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    await collectionChanged1.ClearAsync().ConfigureAwait(false);
                    await collectionChanged2.ClearAsync().ConfigureAwait(false);
                    await collectionChanged3.ClearAsync().ConfigureAwait(false);
                    originElementsCopy1.Clear();
                    originElementsCopy2.Clear();
                    originElementsCopy3.Clear();
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originElementsCopy1.Concat(originElementsCopy2).Concat(originElementsCopy3),
                        (await unionCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    var addRange1 = Enumerable.Range(0, 10).ToList();
                    var addRange2 = Enumerable.Range(20, 14).ToList();
                    var addRange3 = Enumerable.Range(40, 4).ToList();
                    await collectionChanged1.AddRangeAsync(addRange1).ConfigureAwait(false);
                    originElementsCopy1.AddRange(addRange1);
                    await collectionChanged2.AddRangeAsync(addRange2).ConfigureAwait(false);
                    originElementsCopy2.AddRange(addRange2);
                    await collectionChanged3.AddRangeAsync(addRange3).ConfigureAwait(false);
                    originElementsCopy3.AddRange(addRange3);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originElementsCopy1.Concat(originElementsCopy2).Concat(originElementsCopy3),
                        (await unionCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    int removeIndex1 = 1, removeCount1 = 3, removeIndex2 = 3, removeCount2 = 10;
                    await collectionChanged1.RemoveRangeAsync(removeIndex1, removeCount1).ConfigureAwait(false);
                    originElementsCopy1.RemoveRange(removeIndex1, removeCount1);
                    await collectionChanged2.RemoveRangeAsync(removeIndex2, removeCount2).ConfigureAwait(false);
                    originElementsCopy2.RemoveRange(removeIndex2, removeCount2);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originElementsCopy1.Concat(originElementsCopy2).Concat(originElementsCopy3),
                        (await unionCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                    /**/
                    int replaceIndex1 = 1, replaceItem1 = 142555, replaceIndex3 = 3, replaceItem3 = -1914;
                    await collectionChanged1.ReplaceItemAtAsync(replaceIndex1, replaceItem1).ConfigureAwait(false);
                    originElementsCopy1[replaceIndex1] = replaceItem1;
                    await collectionChanged3.ReplaceItemAtAsync(replaceIndex3, replaceItem3).ConfigureAwait(false);
                    originElementsCopy3[replaceIndex3] = replaceItem3;
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        originElementsCopy1.Concat(originElementsCopy2).Concat(originElementsCopy3),
                        (await unionCollectionChanged.GetDeepCopyAsync().ConfigureAwait(false)).NewItems
                        );
                }
            }
            finally
            {
                await unionCollectionChanged.MyDisposeAsync().ConfigureAwait(false);
            }
        }
        [Fact]
        public async Task TestIndexedCollection()
        {
            var collectionChanged = new MyObservableCollectionSafeAsyncImpl<long>();
            var collectionCopy = new List<long>();
            var indexedCollection = await MyIndexedObservableCollectionImpl<long>.CreateInstance(
                collectionChanged
                ).ConfigureAwait(false);
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    await collectionChanged.ClearAsync().ConfigureAwait(false);
                    collectionCopy.Clear();
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        collectionCopy.Select((_, idx) => MutableTuple.Create(idx, _)).ToList().WriteObjectToJson(),
                        (await indexedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()
                        );
                    /**/
                    var addRange1 = Enumerable.Range(10, 15).Select(_ => (long) _).ToList();
                    await collectionChanged.AddRangeAsync(addRange1).ConfigureAwait(false);
                    collectionCopy.AddRange(addRange1);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        collectionCopy.Select((_, idx) => MutableTuple.Create(idx, _)).ToList().WriteObjectToJson(),
                        (await indexedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()
                        );
                    /**/
                    var insertRange1 = Enumerable.Range(50, 11).Select(_ => (long) _).ToList();
                    await collectionChanged.InsertRangeAtAsync(4, insertRange1).ConfigureAwait(false);
                    collectionCopy.InsertRange(4, insertRange1);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        collectionCopy.Select((_, idx) => MutableTuple.Create(idx, _)).ToList().WriteObjectToJson(),
                        (await indexedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()
                        );
                    /**/
                    await collectionChanged.RemoveRangeAsync(3, 7).ConfigureAwait(false);
                    collectionCopy.RemoveRange(3, 7);
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        collectionCopy.Select((_, idx) => MutableTuple.Create(idx, _)).ToList().WriteObjectToJson(),
                        (await indexedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()
                        );
                    /**/
                    await collectionChanged.ReplaceItemAtAsync(5, 1567).ConfigureAwait(false);
                    collectionCopy[5] = 1567;
                    await Task.Delay(10).ConfigureAwait(false);
                    Assert.Equal(
                        collectionCopy.Select((_, idx) => MutableTuple.Create(idx, _)).ToList().WriteObjectToJson(),
                        (await indexedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson()
                        );
                }
            }
            finally
            {
                await indexedCollection.MyDisposeAsync().ConfigureAwait(false);
            }
        }

        private IList<MutableTuple<TItem1, TItem2>> TestJoin<TItem1, TItem2>(
            IList<TItem1> list1,
            IList<TItem2> list2,
            JoinType joinType,
            Func<TItem1,TItem2,bool> comparer 
        )
            where TItem1 : class
            where TItem2 : class
        {
            Assert.NotNull(list1);
            Assert.NotNull(list2);
            Assert.NotNull(comparer);
            var fullOuterJoin = new List<MutableTuple<TItem1, TItem2>>();
            var indexedList2 = list2.WithIndex().ToList();
            var exceptList2Indices = new List<int>();
            for (int i = 0; i < list1.Count; i++)
            {
                var correspondentItem2 = indexedList2.FirstOrDefault(
                    _ => comparer(list1[i], _.Item2)
                );
                fullOuterJoin.Add(
                    MutableTuple.Create(
                        list1[i],
                        correspondentItem2?.Item2
                    )
                );
                if(correspondentItem2 != null)
                    exceptList2Indices.Add(correspondentItem2.Item1);
            }
            fullOuterJoin.AddRange(
                Enumerable.Range(0, list2.Count)
                    .Except(exceptList2Indices.Distinct())
                    .Select(i => MutableTuple.Create(default(TItem1), list2[i])
                )
            );
            var result = fullOuterJoin.Where(
                tuple =>
                {
                    if (joinType == JoinType.FullOuterJoin)
                        return true;
                    else if (joinType == JoinType.InnerJoin)
                        return tuple.Item1 != null && tuple.Item2 != null;
                    else if (joinType == JoinType.LeftJoin)
                        return tuple.Item1 != null;
                    else if (joinType == JoinType.RightJoin)
                        return tuple.Item2 != null;
                    return false; //Unreachable code
                }
                ).ToList();
            return result;
        }

        [Fact]
        public async Task TestJoinedCollection()
        {
            foreach (JoinType joinType in Enum.GetValues(typeof(JoinType)))
            {
                var collection1 = new MyObservableCollectionSafeAsyncImpl<MutableTuple<int,string>>();
                var collection1Copy = new List<MutableTuple<int, string>>();
                var collection2 = new MyObservableCollectionSafeAsyncImpl<MutableTuple<int,Guid>>();
                var collection2Copy = new List<MutableTuple<int, Guid>>();
                Func<MutableTuple<int, string>, MutableTuple<int, Guid>, bool> comparisonPredicate =
                    (a, b) => a.Item1 == b.Item1;
                var joinedCollection = await MyJoinedObservableCollectionImpl<
                    MutableTuple<int, string>,
                    MutableTuple<int, Guid>
                    >.CreateInstance(
                        collection1,
                        collection2,
                        comparisonPredicate,
                        joinType
                    ).ConfigureAwait(false);
                try
                {
                    for (int k = 0; k < 10; k++)
                    {
                        await collection1.ClearAsync().ConfigureAwait(false);
                        collection1Copy.Clear();
                        await collection2.ClearAsync().ConfigureAwait(false);
                        collection2Copy.Clear();
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        var addRange1 = new[]
                        {
                            MutableTuple.Create(1, "A1"),
                            MutableTuple.Create(3, "A3"),
                            MutableTuple.Create(7, "A7"),
                            MutableTuple.Create(8, "A8"),
                            MutableTuple.Create(15, "A15"),
                            MutableTuple.Create(23, "A23")
                        };
                        await collection1.AddRangeAsync(
                            addRange1
                            ).ConfigureAwait(false);
                        collection1Copy.AddRange(addRange1);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        var addRange2 = new[]
                        {
                            MutableTuple.Create(2, Guid.NewGuid()),
                            MutableTuple.Create(3, Guid.NewGuid()),
                            MutableTuple.Create(15, Guid.NewGuid()),
                            MutableTuple.Create(8, Guid.NewGuid()),
                        };
                        await collection2.AddRangeAsync(addRange2).ConfigureAwait(false);
                        collection2Copy.AddRange(addRange2);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        await collection1.RemoveRangeAsync(1, 2).ConfigureAwait(false);
                        collection1Copy.RemoveRange(1, 2);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        await collection2.RemoveRangeAsync(2, 2).ConfigureAwait(false);
                        collection2Copy.RemoveRange(2, 2);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        await collection1.InsertRangeAtAsync(
                            2,
                            addRange1
                            ).ConfigureAwait(false);
                        collection1Copy.InsertRange(2, addRange1);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        await collection2.InsertRangeAtAsync(1, addRange2).ConfigureAwait(false);
                        collection2Copy.InsertRange(1, addRange2);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        var bulkUpdate1 = new List<Tuple<int, MutableTuple<int, string>>>()
                        {
                            Tuple.Create(2, MutableTuple.Create(4, "A4")),
                            Tuple.Create(5, MutableTuple.Create(9, "A9"))
                        };
                        await collection1.ReplaceBulkAsync(bulkUpdate1).ConfigureAwait(false);
                        collection1Copy[2] = MutableTuple.Create(4, "A4");
                        collection1Copy[5] = MutableTuple.Create(9, "A9");
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        var g1 = Guid.NewGuid();
                        var g2 = Guid.NewGuid();
                        var bulkUpdate2 = new List<Tuple<int, MutableTuple<int, Guid>>>()
                        {
                            Tuple.Create(2, MutableTuple.Create(21, g1)),
                            Tuple.Create(5, MutableTuple.Create(95, g2))
                        };
                        await collection2.ReplaceBulkAsync(bulkUpdate2).ConfigureAwait(false);
                        collection2Copy[2] = MutableTuple.Create(21, g1);
                        collection2Copy[5] = MutableTuple.Create(95, g2);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        await collection1.ClearAsync().ConfigureAwait(false);
                        collection1Copy.Clear();
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        await collection1.AddRangeAsync(addRange1).ConfigureAwait(false);
                        collection1Copy.AddRange(addRange1);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        await collection2.ClearAsync().ConfigureAwait(false);
                        collection2Copy.Clear();
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                        /**/
                        var addRange22 = addRange2.Concat(new[] {MutableTuple.Create(100, Guid.NewGuid())}).ToList();
                        await collection2.AddRangeAsync(addRange22).ConfigureAwait(false);
                        collection2Copy.AddRange(addRange22);
                        await Task.Delay(10).ConfigureAwait(false);
                        Assert.Equal(
                            TestJoin(collection1Copy, collection2Copy, joinType, comparisonPredicate)
                                .WriteObjectToJson(),
                            (await joinedCollection.GetDeepCopyAsync().ConfigureAwait(false)).NewItems.WriteObjectToJson
                                ()
                            );
                    }
                }
                finally
                {
                    await joinedCollection.MyDisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private readonly Logger _log = LogManager.GetCurrentClassLogger();
    }
}
