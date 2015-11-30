using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using BtmI2p.MiscUtils;
using BtmI2p.Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace BtmI2p.MyNotifyPropertyChanged.Tests
{
    public interface ITestMyPropertyChanged1 : IMyNotifyPropertyChanged
    {
        int P1 { get; set; }
        [JsonConverter(typeof(ConcreteTypeConverter<
            TestMyPropertyChanged2Impl, 
            ITestMyPropertyChanged2
        >))]
        ITestMyPropertyChanged2 P3 { get; set; }
    }

    public class TestMyPropertyChanged1Impl : ITestMyPropertyChanged1
    {
        public TestMyPropertyChanged1Impl()
        {
        }

        public int P1 { get; set; }
        public ITestMyPropertyChanged2 P3 { get; set; }
        /**/
        public Subject<MyNotifyPropertyChangedArgs> PropertyChangedSubject
        {
            get
            {
                throw new Exception(MyNotifyPropertyChangedArgs.DefaultNotProxyExceptionString);
            }
        }
    }

    public interface ITestMyPropertyChanged2 : IMyNotifyPropertyChanged
    {
        int P1 { get; set; }
    }
    public class TestMyPropertyChanged2Impl : ITestMyPropertyChanged2
    {
        public Subject<MyNotifyPropertyChangedArgs> PropertyChangedSubject {
            get
            {
                throw new Exception(MyNotifyPropertyChangedArgs.DefaultNotProxyExceptionString);
            }
        }
        public int P1 { get; set; }
    }
    /**/
    public class TestMyNotifyPropertyChanged
    {
        private readonly ITestOutputHelper _output;
        public TestMyNotifyPropertyChanged(ITestOutputHelper output)
        {
            _output = output;
        }
        [Fact]
        public async Task TestPropertyChangedSubjectGetter()
        {
            var test2 = new TestMyPropertyChanged2Impl();
            var proxy2 = MyNotifyPropertyChangedImpl.GetProxy((ITestMyPropertyChanged2)test2);
            /**/
            var test1 = new TestMyPropertyChanged1Impl();
            var proxy1 = MyNotifyPropertyChangedImpl.GetProxy((ITestMyPropertyChanged1)test1);
            /**/
            //var temp1 = test2.PropertyChangedSubject;
            var temp2 = proxy2.PropertyChangedSubject;
            await Task.Delay(500).ConfigureAwait(false);
        }

        [Fact]
        public async Task Test1()
        {
            var test2 = new TestMyPropertyChanged2Impl();
            var proxy2 = MyNotifyPropertyChangedImpl.GetProxy((ITestMyPropertyChanged2)test2);
            /**/
            var test1 = new TestMyPropertyChanged1Impl();
            var proxy1 = MyNotifyPropertyChangedImpl.GetProxy((ITestMyPropertyChanged1)test1);
            //var proxyProxy1 = MyNotifyPropertyChangedImpl.GetProxy(proxy1);
            using (proxy1.PropertyChangedSubject.ObserveOn(TaskPoolScheduler.Default).Subscribe(i =>
            {
                _output.WriteLine(
                    $"proxy1 {i.PropertyName} changed {(i.InnerArgs as MyNotifyPropertyChangedArgs).With(_ => _ == null ? "" : _.PropertyName)}"
                    );
            }))
            {
                _output.WriteLine("----------0");
                proxy1.P1 = 10;
                _output.WriteLine("----------1");
                proxy1.P3 = test2;
                _output.WriteLine("----------2");
                using (proxy2.PropertyChangedSubject.ObserveOn(TaskPoolScheduler.Default).Subscribe(i =>
                {
                    _output.WriteLine(string.Format("proxy2 {0} changed", i.PropertyName));
                }))
                {
                    proxy1.P3.P1 = 150;
                    _output.WriteLine("----------3");
                    proxy1.P3 = proxy2;
                    _output.WriteLine("----------4");
                    proxy2.P1 = 200;
                    _output.WriteLine("----------5");
                    proxy2.P1 = 10;
                    _output.WriteLine("----------6");
                    
                }
            }
        }

        [Fact]
        public async Task TestSerialization()
        {
            var test2 = new TestMyPropertyChanged2Impl();
            var proxy2 = MyNotifyPropertyChangedImpl.GetProxy((ITestMyPropertyChanged2)test2);
            /**/
            var test1 = new TestMyPropertyChanged1Impl();
            var proxy1 = MyNotifyPropertyChangedImpl.GetProxy((ITestMyPropertyChanged1)test1);
            proxy1.P3 = proxy2;
            /**/
            _output.WriteLine(
                proxy2.WriteObjectToJson()
            );
            _output.WriteLine(
                proxy1.WriteObjectToJson()
            );
            var desTest1 = proxy1.WriteObjectToJson()
                .ParseJsonToType<TestMyPropertyChanged1Impl>();
            _output.WriteLine(desTest1.WriteObjectToJson());
        }
    }
    /*************************************/
    
    public interface ITestPropertyProxyFixture1
    {
        int P1 { get; set; }
        int P2 { get; set; }
        int P3 { get; set; }
    }

    public class TestPropertyProxyFixture1 : ITestPropertyProxyFixture1
    {
        public int P1 { get; set; }
        public int P2 { get; set; }
        public int P3 { get; set; }
    }
    public class TestPropertyProxyFixture2
    {
        public int P46 { get; set; }
    }
    public class TestPropertyProxyFixture3
    {
        public int P152 { get; set; }
    }
    public class TestPropertyProxy
    {
        private readonly ITestOutputHelper _output;
        public TestPropertyProxy(ITestOutputHelper output)
        {
            _output = output;
        }
        [Fact]
        public void Test1()
        {
            var fixture1 = new TestPropertyProxyFixture1();
            var fixture2 = new TestPropertyProxyFixture2
            {
                P46 = 10
            };
            var fixture3 = new TestPropertyProxyFixture3
            {
                P152 = 15
            };
            var proxy1 = new MyPropertiesProxy<ITestPropertyProxyFixture1>(
                fixture1,
                new List<PropertyBindingInfo>()
                {
                    PropertyBindingInfo.Create(
                        fixture2,
                        new List<Tuple<string, string>>()
                        {
                            new Tuple<string, string>(
                                fixture1.MyNameOfProperty(e => e.P1), 
                                fixture2.MyNameOfProperty(e => e.P46)
                            )
                        }
                    ),
                    PropertyBindingInfo.Create(
                        fixture3,
                        new List<Tuple<string, string>>()
                        {
                            new Tuple<string, string>(
                                fixture1.MyNameOfProperty(e => e.P2), 
                                fixture3.MyNameOfProperty(e => e.P152)
                            )
                        }
                    )
                }
            ).GetProxy();
            _output.WriteLine("{0}", proxy1.P1);
            _output.WriteLine("{0}", proxy1.P2);
            _output.WriteLine("{0}", proxy1.P3);
            /**/
            proxy1.P1 = 1000;
            _output.WriteLine("{0}", fixture2.P46);
            proxy1.P2 = 2000;
            _output.WriteLine("{0}", fixture3.P152);
            proxy1.P3 = 52700;
            _output.WriteLine(proxy1.WriteObjectToJson());
            _output.WriteLine(fixture2.WriteObjectToJson());
            _output.WriteLine(fixture3.WriteObjectToJson());
        }
    }
}
