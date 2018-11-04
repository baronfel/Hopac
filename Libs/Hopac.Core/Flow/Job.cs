// Copyright (C) by Housemarque, Inc.

namespace Hopac {
  using Microsoft.FSharp.Core;
  using Hopac.Core;
  using System.Runtime.CompilerServices;
  using System;

  /// <summary>Represents a lightweight thread of execution.</summary>
  public abstract class Job<T> {
    internal abstract void DoJob(ref Worker wr, Cont<T> aK);
    public static Job<U> op_GreaterGreaterEquals<U>(Job<T> source, FSharpFunc<T,Job<U>> f) {
      throw new Exception("bind");
    }
    public static Job<U> Return<U>(U item) {
      return new JobReturn<U>(item);
    }
    public static Job<U> Apply<U>(Job<FSharpFunc<T, U>> jF, Job<T> jJ) {
      throw new Exception("apply");
    }
    public static Job<Unit> Iterate(Job<T> job, FSharpFunc<T, Unit> action) {
      throw new Exception("iterate");
    }
    public static Job<U> Map<U>(Job<T> job, FSharpFunc<T, U> mapper) {
      throw new Exception("map");
    }
    public static Job<T> op_LessBarGreater(Job<T> left, Job<T> right) {
      throw new Exception("append");
    }
    public static Job<T> Delay(FSharpFunc<Unit, T> activator) {
      throw new Exception("delay");
    }
    public static Job<T> TryWith(Job<T> job, FSharpFunc<Exception, Job<T>> recover) {
      throw new Exception("trywith");
    }
    public static Job<T> TryFinally(Job<T> job, FSharpFunc<Unit, Unit> compensation) {
      throw new Exception("tryfinally");
    }
    public static Job<U> Using<R,U>(R resource, FSharpFunc<R, Job<U>> body) where R: IDisposable {
      throw new Exception("using");
    }
    public static Job<Tuple<T,U>> Zip<U>(Job<T> j, Job<U> u) {
      throw new Exception("zip");
    }
    public static T Extract(Job<T> j){
      throw new Exception("extract");
    }
  }

  namespace Core {
    public class JobReturn<T> : Job<T>
    {
      private readonly T _value;
      public JobReturn(T v)
      {
        this._value = v;
      }
      internal override void DoJob(ref Worker wr, Cont<T> aK)
      {
        aK.DoCont(ref wr, this._value);
      }
    }
    ///
    public abstract class JobCont<X, Y> : Job<Y> {
      private Job<X> xJ;
      ///
      [MethodImpl(AggressiveInlining.Flag)]
      public Job<Y> InternalInit(Job<X> xJ) { this.xJ = xJ; return this; }
      ///
      public abstract JobContCont<X, Y> Do();
      internal override void DoJob(ref Worker wr, Cont<Y> yK) {
        var xK = Do();
        xK.yK = yK;
        xJ.DoJob(ref wr, xK);
      }
    }

    ///
    public abstract class JobContCont<X, Y> : Cont<X> {
      internal Cont<Y> yK;
    }

    ///
    public abstract class ContBind<X, Y> : JobContCont<X, Y> {
      ///
      public abstract Job<Y> Do(X x);
      internal override Proc GetProc(ref Worker wr) {
        return Handler.GetProc(ref wr, ref yK);
      }
      internal override void DoHandle(ref Worker wr, Exception e) {
        Handler.DoHandle(yK, ref wr, e);
      }
      internal override void DoWork(ref Worker wr) {
        Do(this.Value).DoJob(ref wr, yK);
      }
      internal override void DoCont(ref Worker wr, X x) {
        Do(x).DoJob(ref wr, yK);
      }
    }

    ///
    public abstract class ContMap<X, Y> : JobContCont<X, Y> {
      ///
      public abstract Y Do(X x);
      internal override Proc GetProc(ref Worker wr) {
        return Handler.GetProc(ref wr, ref yK);
      }
      internal override void DoHandle(ref Worker wr, Exception e) {
        Handler.DoHandle(yK, ref wr, e);
      }
      internal override void DoWork(ref Worker wr) {
        yK.DoCont(ref wr, Do(this.Value));
      }
      internal override void DoCont(ref Worker wr, X x) {
        yK.DoCont(ref wr, Do(x));
      }
    }

    ///
    public abstract class JobUsing<X, Y> : Job<Y> where X : IDisposable {
      private X x;
      ///
      [MethodImpl(AggressiveInlining.Flag)]
      public Job<Y> InternalInit(X x) { this.x = x; return this; }
      ///
      public abstract Job<Y> Do(X x);
      internal override void DoJob(ref Worker wr, Cont<Y> yK) {
        var yKK = new ContUsing(this, yK);
        wr.Handler = yKK;
        Do(this.x).DoJob(ref wr, yKK);
      }
      private sealed class ContUsing : Cont<Y> {
        private JobUsing<X, Y> yJ;
        private Cont<Y> yK;
        internal ContUsing(JobUsing<X, Y> yJ, Cont<Y> yK) {
          this.yJ = yJ;
          this.yK = yK;
        }
        [MethodImpl(AggressiveInlining.Flag)]
        internal Cont<Y> Dispose(ref Worker wr) {
          var yK = this.yK;
          wr.Handler = yK;
          var x = this.yJ.x;
          if (null != x)
            x.Dispose();
          return yK;
        }
        internal override Proc GetProc(ref Worker wr) {
          return Handler.GetProc(ref wr, ref yK);
        }
        internal override void DoHandle(ref Worker wr, Exception e) {
          Handler.DoHandle(Dispose(ref wr), ref wr, e);
        }
        internal override void DoWork(ref Worker wr) {
          Dispose(ref wr).DoCont(ref wr, this.Value);
        }
        internal override void DoCont(ref Worker wr, Y y) {
          Dispose(ref wr).DoCont(ref wr, y);
        }
      }
    }

    ///
    public abstract class JobFor<S, R, F> : Job<F> {
      ///
      public abstract Job<R> Init(out S s);
      ///
      public virtual void Uninit(ref S s) { }
      ///
      public abstract Job<R> Next(R r, ref S s);
      ///
      public virtual void Finish(ref S s, ref F f) { }
      ///
      internal override void DoJob(ref Worker wr, Cont<F> fK) {
        S s;
        var rJ = Init(out s);
        if (null != rJ) {
          var rK = new ContFor(this, fK, s);
          wr.Handler = rK;
          rJ.DoJob(ref wr, rK);
        } else {
          Finish(ref s, ref fK.Value);
          Uninit(ref s);
          Work.Do(fK, ref wr);
        }
      }
      private sealed class ContFor : Cont<R> {
        private JobFor<S, R, F> fJ;
        private Cont<F> fK;
        private S s;
        internal ContFor(JobFor<S, R, F> fJ, Cont<F> fK, S s) {
          this.fJ = fJ;
          this.fK = fK;
          this.s = s;
        }
        internal override Proc GetProc(ref Worker wr) {
          return Handler.GetProc(ref wr, ref fK);
        }
        internal override void DoHandle(ref Worker wr, Exception e) {
          wr.Handler = fK;
          fJ.Uninit(ref s);
          Handler.DoHandle(fK, ref wr, e);
        }
        internal override void DoWork(ref Worker wr) {
          var rJ = fJ.Next(Value, ref s);
          if (null != rJ) {
            Job.Do(rJ, ref wr, this);
          } else {
            fJ.Finish(ref s, ref fK.Value);
            wr.Handler = fK;
            fJ.Uninit(ref s);
            Work.Do(fK, ref wr);
          }
        }
        internal override void DoCont(ref Worker wr, R r) {
          var rJ = fJ.Next(r, ref s);
          if (null != rJ) {
            Job.Do(rJ, ref wr, this);
          } else {
            fJ.Finish(ref s, ref fK.Value);
            wr.Handler = fK;
            fJ.Uninit(ref s);
            Work.Do(fK, ref wr);
          }
        }
      }
    }

    ///
    public abstract class JobWhileDoDelay<X> : Job<Unit> {
      ///
      public abstract Job<X> Do();
      internal override void DoJob(ref Worker wr, Cont<Unit> uK) {
        var xJ = Do();
        if (null != xJ)
          xJ.DoJob(ref wr, new ContWhile(this, uK));
        else
          Work.Do(uK, ref wr);
      }
      private sealed class ContWhile : Cont<X> {
        private JobWhileDoDelay<X> uJ;
        private Cont<Unit> uK;
        internal ContWhile(JobWhileDoDelay<X> uJ, Cont<Unit> uK) {
          this.uJ = uJ;
          this.uK = uK;
        }
        internal override Proc GetProc(ref Worker wr) {
          return uK.GetProc(ref wr);
        }
        internal override void DoHandle(ref Worker wr, Exception e) {
          uK.DoHandle(ref wr, e);
        }
        internal override void DoWork(ref Worker wr) {
          var xJ = uJ.Do();
          if (null != xJ)
            Job.Do(xJ, ref wr, this);
          else
            Work.Do(uK, ref wr);
        }
        internal override void DoCont(ref Worker wr, X value) {
          var xJ = uJ.Do();
          if (null != xJ)
            Job.Do(xJ, ref wr, this);
          else
            Work.Do(uK, ref wr);
        }
      }
    }

    ///
    public sealed class JobJoin<X, JX> : Job<X> where JX : Job<X> {
      private Job<JX> xJJ;
      ///
      [MethodImpl(AggressiveInlining.Flag)]
      public Job<X> InternalInit(Job<JX> xJJ) { this.xJJ = xJJ; return this; }
      internal override void DoJob(ref Worker wr, Cont<X> xK) {
        xJJ.DoJob(ref wr, new ContJoin(xK));
      }
      private sealed class ContJoin : Cont<JX> {
        private Cont<X> xK;
        internal ContJoin(Cont<X> xK) { this.xK = xK; }
        internal override Proc GetProc(ref Worker wr) {
          return Handler.GetProc(ref wr, ref xK);
        }
        internal override void DoHandle(ref Worker wr, Exception e) {
          Handler.DoHandle(xK, ref wr, e);
        }
        internal override void DoWork(ref Worker wr) {
          this.Value.DoJob(ref wr, xK);
        }
        internal override void DoCont(ref Worker wr, JX xJ) {
          xJ.DoJob(ref wr, xK);
        }
      }
    }

    ///
    public abstract class JobTryInCont<X, Y> : Cont<X> {
      internal Cont<Y> yK;
      ///
      public abstract Job<Y> DoIn(X x);
      ///
      public abstract Job<Y> DoExn(Exception e);
      internal override Proc GetProc(ref Worker wr) {
          return Handler.GetProc(ref wr, ref yK);
      }
      internal override void DoHandle(ref Worker wr, Exception e) {
        var yK = this.yK;
        wr.Handler = yK;
        this.DoExn(e).DoJob(ref wr, yK);
      }
      internal override void DoWork(ref Worker wr) {
        var yK = this.yK;
        wr.Handler = yK;
        this.DoIn(this.Value).DoJob(ref wr, yK);
      }
      internal override void DoCont(ref Worker wr, X x) {
        var yK = this.yK;
        wr.Handler = yK;
        this.DoIn(x).DoJob(ref wr, yK);
      }
    }

    ///
    public abstract class JobTryInBase<X, Y> : Job<Y> {
      ///
      public abstract JobTryInCont<X, Y> DoCont();
    }

    ///
    public abstract class JobTryIn<X, Y> : JobTryInBase<X, Y> {
      private Job<X> xJ;
      ///
      [MethodImpl(AggressiveInlining.Flag)]
      public Job<Y> InternalInit(Job<X> xJ) { this.xJ = xJ; return this; }
      internal override void DoJob(ref Worker wr, Cont<Y> yK) {
        var xK = this.DoCont();
        xK.yK = yK;
        wr.Handler = xK;
        xJ.DoJob(ref wr, xK);
      }
    }

    ///
    public abstract class JobTryInDelay<X, Y> : JobTryInBase<X, Y> {
      ///
      public abstract Job<X> DoDelay();
      internal override void DoJob(ref Worker wr, Cont<Y> yK) {
        var xK = this.DoCont();
        xK.yK = yK;
        wr.Handler = xK;
        DoDelay().DoJob(ref wr, xK);
      }
    }

    ///
    public abstract class ContTryWith<X> : Cont<X> {
      internal Cont<X> xK;
      ///
      public abstract Job<X> DoExn(Exception e);
      internal override Proc GetProc(ref Worker wr) {
        return Handler.GetProc(ref wr, ref xK);
      }
      internal override void DoHandle(ref Worker wr, Exception e) {
        var xK = this.xK;
        wr.Handler = xK;
        this.DoExn(e).DoJob(ref wr, xK);
      }
      internal override void DoWork(ref Worker wr) {
        var xK = this.xK;
        wr.Handler = xK;
        xK.DoCont(ref wr, this.Value);
      }
      internal override void DoCont(ref Worker wr, X x) {
        var xK = this.xK;
        wr.Handler = xK;
        xK.DoCont(ref wr, x);
      }
    }

    ///
    public abstract class JobTryWithBase<X> : Job<X> {
      ///
      public abstract ContTryWith<X> DoCont();
    }

    ///
    public abstract class JobTryWith<X> : JobTryWithBase<X> {
      private Job<X> xJ;
      ///
      [MethodImpl(AggressiveInlining.Flag)]
      public Job<X> InternalInit(Job<X> xJ) {
        this.xJ = xJ;
        return this;
      }
      internal override void DoJob(ref Worker wr, Cont<X> xK_) {
        var xK = DoCont();
        xK.xK = xK_;
        wr.Handler = xK;
        xJ.DoJob(ref wr, xK);
      }
    }

    ///
    public abstract class JobTryWithDelay<X> : JobTryWithBase<X> {
      ///
      public abstract Job<X> Do();
      internal override void DoJob(ref Worker wr, Cont<X> xK_) {
        var xK = DoCont();
        xK.xK = xK_;
        wr.Handler = xK;
        Do().DoJob(ref wr, xK);
      }
    }

    ///
    public abstract class JobRun<X> : Job<X> {
      ///
      public abstract Work Do(Cont<X> xK);
      internal override void DoJob(ref Worker wr, Cont<X> xK) {
        Work.Do(Do(xK), ref wr);
      }
    }

    ///
    public abstract class JobStart : Job<Unit> {
      ///
      public abstract Work Do();
      internal override void DoJob(ref Worker wr, Cont<Unit> uK) {
        Worker.PushNew(ref wr, Do());
        Work.Do(uK, ref wr);
      }
    }

    ///
    public abstract class JobDelay<X> : Job<X> {
      ///
      public abstract Job<X> Do();
      internal override void DoJob(ref Worker wr, Cont<X> xK) {
        Do().DoJob(ref wr, xK);
      }
    }

    ///
    public abstract class JobThunk<X> : Job<X> {
      ///
      public abstract X Do();
      internal override void DoJob(ref Worker wr, Cont<X> xK) {
        Cont.Do(xK, ref wr, Do());
      }
    }

    ///
    public abstract class JobIsolate<X> : Job<X> {
      ///
      public abstract X Do();
      internal override void DoJob(ref Worker wr, Cont<X> xK) {
        var work = wr.WorkStack;
        wr.WorkStack = null;
        Scheduler.PushAll(wr.Scheduler, work);
        Cont.Do(xK, ref wr, Do());
      }
    }

    internal static class Job {
      [MethodImpl(AggressiveInlining.Flag)]
      internal static void Do<T>(Job<T> tJ, ref Worker wr, Cont<T> tK) {
#if TRAMPOLINE
        unsafe {
          if (Unsafe.GetStackPtr() < wr.StackLimit) {
            var w = new JobWork<T>(tJ, tK);
            w.Next = wr.WorkStack;
            wr.WorkStack = w;
          } else {
            wr.Handler = tK;
            tJ.DoJob(ref wr, tK);
          }
        }
#else
        wr.Handler = tK;
        tJ.DoJob(ref wr, tK);
#endif
      }
    }

    internal sealed class JobWork<T> : Work {
      internal Job<T> tJ;
      internal Cont<T> tK;

      [MethodImpl(AggressiveInlining.Flag)]
      internal JobWork(Job<T> tJ, Cont<T> tK) {
        this.tJ = tJ;
        this.tK = tK;
      }

      internal override Proc GetProc(ref Worker wr) {
        return Handler.GetProc(ref wr, ref tK);
      }

      internal override void DoHandle(ref Worker wr, Exception e) {
        Handler.DoHandle(tK, ref wr, e);
      }

      internal override void DoWork(ref Worker wr) {
        tJ.DoJob(ref wr, tK);
      }
    }

    ///
    public abstract class JobSchedulerBind<X> : Job<X> {
      ///
      public abstract Job<X> Do(Scheduler scheduler);
      internal override void DoJob(ref Worker wr, Cont<X> xK) {
        Do(wr.Scheduler).DoJob(ref wr, xK);
      }
    }

    ///
    public abstract class JobProcBind<X> : Job<X> {
      ///
      public abstract Job<X> Do(Proc proc);
      internal override void DoJob(ref Worker wr, Cont<X> xK) {
        Do(xK.GetProc(ref wr)).DoJob(ref wr, xK);
      }
    }

    ///
    public abstract class JobProcMap<X> : Job<X> {
      ///
      public abstract X Do(Proc proc);
      internal override void DoJob(ref Worker wr, Cont<X> xK) {
        Cont.Do(xK, ref wr, Do(xK.GetProc(ref wr)));
      }
    }

    ///
    public abstract class JobRandomBind<X> : Job<X> {
      ///
      public abstract Job<X> Do(ulong random);
      internal override void DoJob(ref Worker wr, Cont<X> xK) {
        Do(Randomizer.Next(ref wr.RandomLo, ref wr.RandomHi)).DoJob(ref wr, xK);
      }
    }

    ///
    public abstract class JobRandomMap<X> : Job<X> {
      ///
      public abstract X Do(ulong random);
      internal override void DoJob(ref Worker wr, Cont<X> xK) {
        Cont.Do(xK, ref wr, Do(Randomizer.Next(ref wr.RandomLo, ref wr.RandomHi)));
      }
    }

    ///
    public sealed class JobRandomGet : Job<ulong> {
      ///
      internal override void DoJob(ref Worker wr, Cont<ulong> rK) {
        Cont.Do(rK, ref wr, Randomizer.Next(ref wr.RandomLo, ref wr.RandomHi));
      }
    }
  }
}
