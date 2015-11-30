using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BtmI2p.MiscClientForms;
using BtmI2p.MiscUtils;
using BtmI2p.MyNotifyPropertyChanged.MyObservableCollections;
using NLog;
using ObjectStateLib;
using Xunit;

namespace BtmI2p.MyNotifyPropertyChanged.Winforms
{
    public static class ListViewCollectionChangedOneWayBinding
    {
        public static async Task<ListViewCollectionChangedOneWayBinding<TItem>> CreateInstance<TItem>(
            ListView listView,
            IMyNotifyCollectionChanged<TItem> collection,
            Func<ListView, ListViewItem> newItemConstructor,
            Action<TItem, ListViewItem> editItemAction // Old value, new value, listViewItem
            )
        {
            return await ListViewCollectionChangedOneWayBinding<TItem>.CreateInstance(
                listView,
                collection,
                newItemConstructor,
                editItemAction
            ).ConfigureAwait(false);
        }
    }
    public class ListViewCollectionChangedOneWayBinding<TItem> : IMyAsyncDisposable
    {
        private ListViewCollectionChangedOneWayBinding()
        {
        }

        public static async Task<ListViewCollectionChangedOneWayBinding<TItem>> CreateInstance(
            ListView listView,
            IMyNotifyCollectionChanged<TItem> collection,
            Func<ListView,ListViewItem> newItemConstructor,
            Action<TItem,ListViewItem> editItemAction // Old value, new value, listViewItem
        )
        {
            Assert.NotNull(listView);
            Assert.Equal(listView.View, View.Details);
            Assert.NotNull(collection);
            if (newItemConstructor == null)
                newItemConstructor = lv =>
                {
                    var newItem = new ListViewItem()
                    {
                        Font = lv.Font
                    };
                    newItem.SubItems.AddRange(
                        Enumerable.Repeat("", lv.Columns.Count - 1).ToArray()
                        );
                    return newItem;
                };
            Assert.NotNull(editItemAction);
            var result = new ListViewCollectionChangedOneWayBinding<TItem>();
            result._listView = listView;
            result._collection = collection;
            result._newItemConstructor = newItemConstructor;
            result._editItemAction = editItemAction;
            result._stateHelper.SetInitializedState();
            result._subscriptions.Add(
                collection.CollectionChangedObservable.Subscribe(
                    _ =>
                    {
                        if(result._changedArgsDict.TryAdd(_.ChangesNum, _))
                            result.ProcessNewChangedArgs();
                    }
                )
            );
            var deepCopyArgs = await collection.GetDeepCopyAsync().ConfigureAwait(false);
            result._changedArgsDict[deepCopyArgs.ChangesNum] = deepCopyArgs;
            Interlocked.Exchange(
                ref result._prevChangesCounter, 
                deepCopyArgs.ChangesNum - 1
            );
            result.ProcessNewChangedArgs();
            return result;
        }

        private Action<TItem, ListViewItem> _editItemAction;
        private Func<ListView, ListViewItem> _newItemConstructor;
        private IMyNotifyCollectionChanged<TItem> _collection;
        /**/
        private readonly SemaphoreSlim _processNewChangedArgsLockSem = new SemaphoreSlim(1);
        private async void ProcessNewChangedArgs()
        {
            try
            {
                using (_stateHelper.GetFuncWrapper())
                {
                    var lockSemCalledWrapper = _processNewChangedArgsLockSem.GetCalledWrapper();
                    lockSemCalledWrapper.Called = true;
                    using (await _processNewChangedArgsLockSem.GetDisposable(true).ConfigureAwait(false))
                    {
                        while (!_cts.IsCancellationRequested && lockSemCalledWrapper.Called)
                        {
                            lockSemCalledWrapper.Called = false;
                            var prevChangesNum = Interlocked.Read(ref _prevChangesCounter);
                            var currentChangesNumInDictToRemove =
                                _changedArgsDict.Keys.Where(_ => _ <= prevChangesNum).ToList();
                            foreach (long key in currentChangesNumInDictToRemove)
                            {
                                MyNotifyCollectionChangedArgs<TItem> removedArgs;
                                _changedArgsDict.TryRemove(key, out removedArgs);
                            }
                            MyNotifyCollectionChangedArgs<TItem> nextArgs;
                            if (
                                _changedArgsDict.TryGetValue(
                                    prevChangesNum + 1,
                                    out nextArgs
                                )
                            )
                            {
                                lockSemCalledWrapper.Called = true;
                                if (nextArgs.ChangedAction == EMyCollectionChangedAction.Reset)
                                {
                                    await _listView.InvokeAsync(
                                        () => _listView.Items.Clear()
                                    ).ConfigureAwait(false);
                                    var currentDataCopy =
                                        await _collection.GetDeepCopyAsync().ConfigureAwait(false);
                                    _changedArgsDict[currentDataCopy.ChangesNum] = currentDataCopy;
                                    Interlocked.Exchange(
                                        ref _prevChangesCounter, 
                                        currentDataCopy.ChangesNum - 1
                                    );
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.NewItemsRangeInserted)
                                {
                                    var insertIndex = nextArgs.NewItemsIndices[0];
                                    await _listView.InvokeAsync(
                                        () =>
                                        {
                                            _listView.BeginUpdate();
                                            try
                                            {
                                                for (int i = 0; i < nextArgs.NewItems.Count; i++)
                                                {
                                                    var newListViewItem = _newItemConstructor(_listView);
                                                    _listView.Items.Insert(
                                                        insertIndex,
                                                        newListViewItem
                                                        );
                                                }
                                                for (int i = 0; i < nextArgs.NewItems.Count; i++)
                                                {
                                                    _editItemAction(
                                                        nextArgs.NewItems[i],
                                                        _listView.Items[insertIndex + i]
                                                        );
                                                }
                                            }
                                            finally
                                            {
                                                _listView.EndUpdate();
                                            }
                                        }
                                    ).ConfigureAwait(false);
                                    Interlocked.Exchange(ref _prevChangesCounter, prevChangesNum + 1);
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsRangeRemoved)
                                {
                                    int removeIndexStart = nextArgs.OldItemsIndices[0];
                                    int removeCount = nextArgs.OldItems.Count;
                                    if (removeCount > 0)
                                    {
                                        await _listView.InvokeAsync(() =>
                                        {
                                            _listView.BeginUpdate();
                                            try
                                            {
                                                for (int i = 0; i < removeCount; i++)
                                                {
                                                    _listView.Items.RemoveAt(removeIndexStart);
                                                }
                                            }
                                            finally
                                            {
                                                _listView.EndUpdate();
                                            }
                                        }).ConfigureAwait(false);
                                    }
                                    Interlocked.Exchange(ref _prevChangesCounter, prevChangesNum + 1);
                                }
                                else if (nextArgs.ChangedAction == EMyCollectionChangedAction.ItemsChanged)
                                {
                                    await _listView.InvokeAsync(() =>
                                    {
                                        _listView.BeginUpdate();
                                        try
                                        {
                                            int count = nextArgs.NewItems.Count;
                                            for (int i = 0; i < count; i++)
                                            {
                                                var listViewItem = _listView.Items[nextArgs.NewItemsIndices[i]];
                                                var newItem = nextArgs.NewItems[i];
                                                _editItemAction(newItem, listViewItem);
                                            }
                                        }
                                        finally
                                        {
                                            _listView.EndUpdate();
                                        }
                                    });
                                    Interlocked.Exchange(ref _prevChangesCounter, prevChangesNum + 1);
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
                MiscFuncs.HandleUnexpectedError(exc,_log);
            }
        }

        private readonly Logger _log = LogManager.GetCurrentClassLogger();
        private long _prevChangesCounter = -2;
        private ListView _listView;
        private readonly ConcurrentDictionary<long,MyNotifyCollectionChangedArgs<TItem>> _changedArgsDict
            = new ConcurrentDictionary<long,MyNotifyCollectionChangedArgs<TItem>>();
        private readonly DisposableObjectStateHelper _stateHelper 
            = new DisposableObjectStateHelper("ListViewCollectionChangedOneWayBinding");
        private readonly List<IDisposable> _subscriptions 
            = new List<IDisposable>(); 
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        public async Task MyDisposeAsync()
        {
            _cts.Cancel();
            await _stateHelper.MyDisposeAsync().ConfigureAwait(false);
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
            _subscriptions.Clear();
            _cts.Dispose();
        }
    }
    
    public static partial class TwoWayBindings
    {
        public static IDisposable BindTextBox<T1, T2, TProp>(
            T1 impl,
            Expression<Func<T1, TProp>> expression,
            T2 propValue,
            TextBox tBox,
            Func<T2,string> toStringFunc,
            Func<string,T2> parseFunc
        )
            where T1 : IMyNotifyPropertyChanged
        {
            Assert.NotNull(impl);
            Assert.NotNull(expression);
            var propName = impl.MyNameOfProperty(expression);
            Assert.NotNull(propName);
            Assert.False(string.IsNullOrWhiteSpace(propName));
            Assert.NotNull(tBox);
            Assert.NotNull(toStringFunc);
            Assert.NotNull(parseFunc);
            var obs1 = impl.PropertyChangedSubject
                .Where(x => x.PropertyName == propName)
                .ObserveOn(new ControlScheduler(tBox))
                .Subscribe(
                    x =>
                    {
                        try
                        {
                            tBox.Text = toStringFunc((T2) x.CastedNewProperty);
                            tBox.ResetBackColor();
                        }
                        catch
                        {
                            tBox.BackColor = Color.Red;
                        }
                    }
                );
            try
            {
                var obs2 = Observable.FromEventPattern(
                    ev => tBox.TextChanged += ev,
                    ev => tBox.TextChanged -= ev
                ).Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(new ControlScheduler(tBox))
                .Subscribe(x =>
                {
                    try
                    {
                        typeof(T1).GetProperty(propName).SetValue(
                            impl, parseFunc(tBox.Text));
                        tBox.ResetBackColor();
                    }
                    catch
                    {
                        tBox.BackColor = Color.Red;
                    }
                });
                return new CompositeDisposable(
                    obs1,
                    obs2
                );
            }
            catch
            {
                obs1.Dispose();
                throw;
            }
        }
    }
}
