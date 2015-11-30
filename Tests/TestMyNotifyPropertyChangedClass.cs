using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using BtmI2p.MiscUtils;
using Xunit;
using Xunit.Abstractions;

namespace BtmI2p.MyNotifyPropertyChanged.Tests
{
    public interface ITest2 : IMyNotifyPropertyChanged
    {
        DateTime Prop4 { get; set; }
    }

    public class Test2Class : ITest2
    {
        public Subject<MyNotifyPropertyChangedArgs> PropertyChangedSubject
        {
            get { throw new NotImplementedException(); }
        }

        public DateTime Prop4 { get; set; }
    }

    public interface ITest1 : IMyNotifyPropertyChanged
    {
        ITest2 Prop5 { get; set; }
        string Prop1 { get; set; }
        Guid Prop2 { get; set; }
        int Prop3 { get; set; }
    }

    public class Test1Class : ITest1
    {
        public Subject<MyNotifyPropertyChangedArgs> PropertyChangedSubject
        {
            get { throw new NotImplementedException(); }
        }

        public ITest2 Prop5 { get; set; }

        public string Prop1 { get; set; }

        public Guid Prop2 { get; set; }

        public int Prop3 { get; set; }
    }
    public class TestMyNotifyPropertyChangedClass
    {
        private readonly ITestOutputHelper _output;
        public TestMyNotifyPropertyChangedClass(ITestOutputHelper output)
        {
	        _output = output;
        }

        [Fact]
        public async Task Test1()
        {
            var t1 = MyNotifyPropertyChangedImpl.GetProxy(
                (ITest1) new Test1Class());
            t1.PropertyChangedSubject
                .SelectMany(
                    async x =>
                    {
                        _output.WriteLine(
                            string.Format(
                                "{0} {1} {2}",
                                x.PropertyName,
                                x.CastedNewProperty,
                                (x.InnerArgs as MyNotifyPropertyChangedArgs).With(
                                    _ => _ != null ? $"{_.PropertyName} {_.CastedNewProperty}" : ""
                                )
                            )
                        );
                        await Task.Delay(10);
                        return 0;
                    }
                ).ObserveOn(TaskPoolScheduler.Default).Subscribe();
            _output.WriteLine("--- Prop1");
            t1.Prop1 = null;
            t1.Prop1 = "s1";
            t1.Prop1 = "s2";
            t1.Prop1 = "s2";
            /**/
            _output.WriteLine("--- Prop2");
            t1.Prop2 = new Guid();
            t1.Prop2 = Guid.NewGuid();
            t1.Prop2 = t1.Prop2;
            /**/
            _output.WriteLine("--- Prop3");
            t1.Prop3 = 0;
            t1.Prop3 = 1;
            t1.Prop3 = 1;
            /**/
            _output.WriteLine("--- Prop5 1");
            var t2Impl1 = MyNotifyPropertyChangedImpl.GetProxy(
                (ITest2)new Test2Class());
            t1.Prop5 = t2Impl1;
            t2Impl1.Prop4 = DateTime.UtcNow;
            /**/
            _output.WriteLine("--- Prop5 2");
            var t2Impl2 = MyNotifyPropertyChangedImpl.GetProxy(
                (ITest2)new Test2Class());
            t1.Prop5 = t2Impl2;
            t2Impl2.Prop4 = DateTime.MinValue;
            t2Impl2.Prop4 = DateTime.UtcNow;
            t2Impl1.Prop4 = DateTime.UtcNow;
            /**/
            _output.WriteLine("--- All Prop");
            MyNotifyPropertyChangedArgs.RaiseAllPropertiesChanged(
                t1);
            /**/
            await Task.Delay(1000).ConfigureAwait(false);
        }
    }
}
