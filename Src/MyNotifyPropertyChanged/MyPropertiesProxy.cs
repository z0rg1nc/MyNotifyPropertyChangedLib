using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BtmI2p.JsonRpcHelpers.Client;
using BtmI2p.MiscUtils;
using BtmI2p.ObjectStateLib;
using LinFu.DynamicProxy;

namespace BtmI2p.MyNotifyPropertyChanged
{
    public class PropertyBindingInfo
    {
        public static PropertyBindingInfo Create<T1>(
            T1 impl,
            List<Tuple<string, string>> propertyNamesMapping
        ) where T1 : class
        {
            if(impl == null)
                throw new ArgumentNullException(
                    MyNameof.GetLocalVarName(() => impl));

            return new PropertyBindingInfo
            {
                Impl = impl,
                ImplType = typeof(T1),
                PropertyNamesMapping = propertyNamesMapping
            };
        }

        public Type ImplType;
        public object Impl;
        // (proxy property name, impl property name) 
        public List<Tuple<string, string>> PropertyNamesMapping;
    }
    public class MyPropertiesProxy<T1> : IInvokeWrapper, IMyAsyncDisposable
        where T1 : class
    {
        private readonly T1 _impl;
        public MyPropertiesProxy(
            T1 impl,
            List<PropertyBindingInfo> bindings
        )
        {
            var t1Type = typeof (T1);
            if(impl == null)
                throw new ArgumentNullException(
                    MyNameof.GetLocalVarName(() => impl));
            if(bindings == null)
                throw new ArgumentNullException(
                    MyNameof.GetLocalVarName(() => bindings));
            _impl = impl;
            if (!t1Type.IsInterface)
            {
                throw new ArgumentException("MyPropertiesProxy ctor: T1 must be interface");
            }
            var pf = new ProxyFactory();
            _proxy = pf.CreateProxy<T1>(this);
            foreach (var binding in bindings)
            {
                if(binding.Impl == null)
                    throw new ArgumentNullException(
                        MyNameof.GetLocalVarName(() => binding.Impl));
                var bindingImplType = binding.ImplType;
                if(!bindingImplType.IsInstanceOfType(binding.Impl))
                    throw new ArgumentException("binding.Impl is not bindingImplType");
                foreach (var propNameTuple in binding.PropertyNamesMapping)
                {
                    if(_bindingsInfo.ContainsKey(propNameTuple.Item1))
                        throw new ArgumentException("Property info already exist");
                    var propInfo = t1Type.GetProperty(propNameTuple.Item1);
                    var bindingImplPropInfo = bindingImplType.GetProperty(propNameTuple.Item2);
                    if(propInfo.PropertyType != bindingImplPropInfo.PropertyType)
                        throw new ArgumentException("PropertyType's don't match");
                    if (
                        (propInfo.CanRead && !bindingImplPropInfo.CanRead)
                        || (propInfo.CanWrite && !bindingImplPropInfo.CanWrite)
                    )
                    {
                        throw new ArgumentException("Property get or set don't match");
                    }
                    _bindingsInfo.Add(
                        propNameTuple.Item1,
                        new Tuple<object, PropertyInfo>(
                            binding.Impl,
                            bindingImplPropInfo
                        )
                    );
                }
            }
            _stateHelper.SetInitializedState();
        }
        private readonly Dictionary<string,Tuple<object,PropertyInfo>> _bindingsInfo
            = new Dictionary<string, Tuple<object, PropertyInfo>>();
        private readonly T1 _proxy;
        public T1 GetProxy()
        {
            using (_stateHelper.GetFuncWrapper())
            {
                return _proxy;
            }
        }

        private readonly DisposableObjectStateHelper _stateHelper
            = new DisposableObjectStateHelper("MyPropertiesProxy");
        public void BeforeInvoke(InvocationInfo info)
        {
        }

        public object DoInvoke(InvocationInfo info)
        {
            return JsonRpcClientProcessor.DoInvokeHelper(
                info,
                DoInvokeImpl
            );
        }

        private async Task<object> DoInvokeImpl(InvocationInfo info)
        {
            using (_stateHelper.GetFuncWrapper())
            {
                var curMethodName = this.MyNameOfMethod(e => e.DoInvokeImpl(null));
                var targetMthdName = info.TargetMethod.Name;
                if (targetMthdName.StartsWith("set_"))
                {
                    var propName = targetMthdName.Substring(4, targetMthdName.Length - 4);
                    if (_bindingsInfo.ContainsKey(propName))
                    {
                        var bindingInfo = _bindingsInfo[propName];
                        bindingInfo.Item2.SetValue(bindingInfo.Item1, info.Arguments[0]);
                        return null;
                    }
                }
                else if (targetMthdName.StartsWith("get_"))
                {
                    var propName = targetMthdName.Substring(4, targetMthdName.Length - 4);
                    if (_bindingsInfo.ContainsKey(propName))
                    {
                        var bindingInfo = _bindingsInfo[propName];
                        return bindingInfo.Item2.GetValue(bindingInfo.Item1);
                    }
                }
                return await JsonRpcClientProcessor.GetInvokeResultFromImpl(
                    _impl,
                    info
                ).ConfigureAwait(false);
            }
        }

        public
            void AfterInvoke(InvocationInfo info, object returnValue)
        {
        }

        public async Task MyDisposeAsync()
        {
            await _stateHelper.MyDisposeAsync().ConfigureAwait(false);
        }
    }
}
