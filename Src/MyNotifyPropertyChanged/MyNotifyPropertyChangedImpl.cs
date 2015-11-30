using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BtmI2p.MiscUtils;
using BtmI2p.Newtonsoft.Json;
using LinFu.DynamicProxy;
using NLog;
using ObjectStateLib;
using Xunit;

namespace BtmI2p.MyNotifyPropertyChanged
{
    public static class MyNotifyPropertyChangedImpl
    {
        public static T1 GetProxy<T1>(T1 impl)
            where T1 : class, IMyNotifyPropertyChanged
        {
            return new MyNotifyPropertyChangedImpl<T1>(
                impl
            ).GetProxy();
        }
        public readonly static ConditionalWeakTable<
            IMyNotifyPropertyChanged,
            Subject<MyNotifyPropertyChangedArgs>
        > WeakTableOfPropChangedSubjects = new ConditionalWeakTable<
            IMyNotifyPropertyChanged,
            Subject<MyNotifyPropertyChangedArgs>
        >();
    }
    // T1 must be interface
    public class MyNotifyPropertyChangedImpl<T1> : IInvokeWrapper, IMyAsyncDisposable
        where T1 : class, IMyNotifyPropertyChanged
    {
        private readonly T1 _impl;
        private readonly DisposableObjectStateHelper _stateHelper
            = new DisposableObjectStateHelper("MyNotifyPropertyChangedImpl");
        private readonly List<string> _propNames = new List<string>();
        private readonly Subject<MyNotifyPropertyChangedArgs> _propertyChangedSubject;
        public MyNotifyPropertyChangedImpl(T1 impl)
        {
            if (impl == null)
                throw new ArgumentNullException(
                    MyNameof.GetLocalVarName(() => impl));
            _impl = impl;
            var t1Type = typeof(T1);
            if (!t1Type.IsInterface)
                throw new ArgumentException("T1 is not interface");
            var realImplType = impl.GetType();
            if (realImplType.Name.EndsWith("Proxy") && realImplType.BaseType == typeof(ProxyDummy))
            {
                throw new ArgumentException("impl is proxy already");
            }
            _propertyChangedSubject = MyNotifyPropertyChangedImpl
                .WeakTableOfPropChangedSubjects.GetOrCreateValue(impl);
            /**/
            foreach (
                var propInfo 
                in t1Type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                )
            )
            {
                var propName = propInfo.Name;
                if (
                    propInfo.PropertyType.GetInterfaces().Any(
                        x => x == typeof(IMyNotifyPropertyChanged)
                    )
                )
                {
                    if (!propInfo.PropertyType.IsInterface)
                        throw new ArgumentException("Property type is not interface");
                    if (!propInfo.CanRead || !propInfo.CanWrite)
                    {
                        throw new ArgumentException(
                            string.Format(
                                "IMyNotifyPropertyChanged property '{0}' must have public getter and setter",
                                propName
                            )
                        );
                    }
                    var propValue = propInfo.GetValue(_impl) as IMyNotifyPropertyChanged;
                    ProcessMyNotifyPropertyChangedProperty(
                        propInfo,
                        propValue
                    );
                }
                _propNames.Add(propName);
            }
            _stateHelper.SetInitializedState();
            var factory = new ProxyFactory();
            _proxy = factory.CreateProxy<T1>(this);
        }

        public void SubscribePropertyChangedArgs(
            Subject<MyNotifyPropertyChangedArgs> changedSubj,
            string propName
        )
        {
            changedSubj.ObserveOn(TaskPoolScheduler.Default).Subscribe(
                i => _propertyChangedSubject.OnNext(
                    new MyNotifyPropertyChangedArgs
                    {
                        Sender = this,
                        PropertyName = propName,
                        InnerArgs = i,
                        CastedNewProperty = typeof(T1).GetProperty(propName).GetValue(_impl)
                    }
                )
            );
        }
        private readonly Dictionary<string,IDisposable> _subscriptions
            = new Dictionary<string, IDisposable>();

        // Return - Suppress set_ method execution
        private bool ProcessMyNotifyPropertyChangedProperty(
            PropertyInfo propInfo,
            IMyNotifyPropertyChanged newPropValue
        )
        {
            if(propInfo.PropertyType.GetInterfaces().All(x => x != typeof (IMyNotifyPropertyChanged)))
                throw new Exception("Property is not IMyNotifyPropertyChanged type");
            if (_subscriptions.ContainsKey(propInfo.Name))
            {
                IDisposable curSubscription = _subscriptions[propInfo.Name];
                curSubscription.Dispose();
                _subscriptions.Remove(propInfo.Name);
            }
            /**/
            if (newPropValue != null)
            {
                /**/
                try
                {
                    var propValueRealType = newPropValue.GetType();
                    if (
                        !(
                            propValueRealType.Name.EndsWith("Proxy")
                            && propValueRealType.BaseType == typeof (ProxyDummy)
                            )
                        )
                    {
                        var proxyVarType = typeof (MyNotifyPropertyChangedImpl<>)
                            .MakeGenericType(propInfo.PropertyType);
                        var proxyVar = Activator.CreateInstance(
                            proxyVarType,
                            newPropValue
                        );
                        var proxyVarProxy =
                            (IMyNotifyPropertyChanged) proxyVarType.GetMethod(
                                this.MyNameOfMethod(e => e.GetProxy())
                            ).Invoke(
                                proxyVar,
                                new object[] {}
                            );

                        propInfo.SetValue(
                            _impl,
                            proxyVarProxy
                        );
                        newPropValue = proxyVarProxy;
                        return true;
                    }
                }
                finally
                {
                    _subscriptions.Add(
                        propInfo.Name,
                        newPropValue.PropertyChangedSubject.ObserveOn(TaskPoolScheduler.Default).Subscribe(
                            i => _propertyChangedSubject.OnNext(
                                new MyNotifyPropertyChangedArgs
                                {
                                    Sender = _proxy,
                                    PropertyName = propInfo.Name,
                                    InnerArgs = i,
                                    CastedNewProperty = propInfo.GetValue(_impl)
                                }
                            )
                        )
                    );
                }
            }
            return false;
        }

        
        /**/
        private readonly T1 _proxy;
        public T1 GetProxy()
        {
            return _proxy;
        }

        public void BeforeInvoke(InvocationInfo info)
        {
        }

        public static bool IsEqual<T2>(T2 a, T2 b)
        {
            return EqualityComparer<T2>.Default.Equals(a, b);
        }

        public object DoInvoke(InvocationInfo info)
        {
            using (_stateHelper.GetFuncWrapper())
            {
                var targetMthdName = info.TargetMethod.Name;
                if (targetMthdName == "get_PropertyChangedSubject")
                {
                    return _propertyChangedSubject;
                }
                if (targetMthdName.StartsWith("set_"))
                {
                    var propName = targetMthdName.Substring(4, targetMthdName.Length - 4);
                    if (
                            _propNames.Contains(propName) &&
                            typeof (T1).GetProperty(propName).SetMethod == info.TargetMethod
                        )
                    {
                        var propType = info.TargetMethod.GetParameters()[0].ParameterType;
                        var newValue = info.Arguments[0];
                        var oldValue = typeof (T1).GetProperty(propName).GetValue(_impl);
                        if (propType.GetInterfaces().Contains(typeof (IMyNotifyPropertyChanged)))
                        {
                            lock (_subscriptions)
                            {
                                var newValuePropChangedType = newValue as IMyNotifyPropertyChanged;
                                if (
                                    ProcessMyNotifyPropertyChangedProperty(
                                        typeof (T1).GetProperty(propName),
                                        newValuePropChangedType
                                    )
                                )
                                    return null; //suppress second property assignment
                            }
                        }
                        info.TargetMethod.Invoke(_impl, info.Arguments);
                        if (
                            ! (bool)(GetType().GetMethod("IsEqual").MakeGenericMethod(propType)
                                .Invoke(null, new [] {oldValue, newValue}))
                            )
                        {
                            _propertyChangedSubject.OnNext(
                                new MyNotifyPropertyChangedArgs
                                {
                                    Sender = _proxy,
                                    PropertyName = propName,
                                    InnerArgs = null,
                                    CastedNewProperty = newValue
                                }
                            );
                        }
                        return null;
                    }
                }
                return info.TargetMethod.Invoke(_impl, info.Arguments);
            }
        }

        public void AfterInvoke(InvocationInfo info, object returnValue)
        {
        }

        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        
        public async Task MyDisposeAsync()
        {
            await _stateHelper.MyDisposeAsync().ConfigureAwait(false);
            foreach (KeyValuePair<string, IDisposable> subscription in _subscriptions)
            {
                subscription.Value.Dispose();
            }
            _subscriptions.Clear();
        }
    }
    public class MyNotifyPropertyChangedArgs
    {
        public object Sender { get; set; }
        public string PropertyName { get; set; } = "";
        public MyNotifyPropertyChangedArgs InnerArgs { get; set; } = null;
        public object CastedNewProperty = null;

        public static void RaiseAllPropertiesChanged<T1>(
            T1 impl
        )
            where T1 : IMyNotifyPropertyChanged
        {
            Assert.NotNull(impl);
            var t1Type = typeof(T1);
            Assert.True(t1Type.IsInterface, "T1 is not interface");
            foreach (
                var propInfo
                    in typeof(T1).GetProperties(
                        BindingFlags.Public | BindingFlags.Instance
                    )
            )
            {
                var propName = propInfo.Name;
                RaiseProperyChanged(
                    impl,
                    propName
                );
            }
        }

        public static void RaiseProperyChanged<T1>(
            T1 impl,
            string propName
        )
            where T1 : IMyNotifyPropertyChanged
        {
            Assert.NotNull(impl);
            Assert.NotNull(propName);
            Assert.False(string.IsNullOrWhiteSpace(propName));
            Assert.True(typeof(T1).GetProperties().Any(_ => _.Name == propName));
            impl.PropertyChangedSubject.OnNext(
                new MyNotifyPropertyChangedArgs()
                {
                    InnerArgs = null,
                    PropertyName = propName,
                    Sender = impl,
                    CastedNewProperty = typeof(T1).GetProperty(propName).GetValue(impl)
                }
            );
        }

        public static void RaiseProperyChanged<T1,TProp>(
            T1 impl,
            Expression<Func<T1, TProp>> expression
        )
            where T1 : IMyNotifyPropertyChanged
        {
            Assert.NotNull(impl);
            Assert.NotNull(expression);
            var t1Type = typeof(T1);
            Assert.True(t1Type.IsInterface, "T1 is not interface");
            var propName = impl.MyNameOfProperty(expression);
            RaiseProperyChanged(impl, propName);
        }
        public static readonly string DefaultNotProxyExceptionString 
            = "Caller of PropertyChangedSubject getter is not proxy";
    }
    public interface IMyNotifyPropertyChanged
    {
        [JsonIgnore]
        Subject<MyNotifyPropertyChangedArgs> PropertyChangedSubject { get; }
    }
}