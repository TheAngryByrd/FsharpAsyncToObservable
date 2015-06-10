namespace AsyncToObservableBenchmarks

open Xunit
open System.Threading.Tasks
open System.Reactive.Linq
open System
open System.Threading

module Algorithm1 = 
    let private oneAndDone (obs : IObserver<_>) value = 
        obs.OnNext value
        obs.OnCompleted()
    
    let ofAsync a : IObservable<'a> = 
        { new IObservable<'a> with
              member __.Subscribe obs = 
                  let oneAndDone' = oneAndDone obs
                  let token = new CancellationTokenSource()
                  Async.StartWithContinuations(a, oneAndDone', obs.OnError, obs.OnError, token.Token)
                  { new IDisposable with
                        member __.Dispose() = 
                            token.Cancel |> ignore
                            token.Dispose() } }

module Algorithm2 = 
    let ofAsync asyncOperation = 
        Observable.FromAsync
            (fun (token : Threading.CancellationToken) -> Async.StartAsTask(asyncOperation, cancellationToken = token))

module Algorithm3 = 
    let ofAsync computation = 
        Observable.Create<'a>(Func<IObserver<'a>, Action>(fun o -> 
                                  if o = null then nullArg "observer"
                                  let cts = new System.Threading.CancellationTokenSource()
                                  let invoked = ref 0
                                  
                                  let cancelOrDispose cancel = 
                                      if System.Threading.Interlocked.CompareExchange(invoked, 1, 0) = 0 then 
                                          if cancel then cts.Cancel()
                                          else cts.Dispose()
                                  
                                  let wrapper = 
                                      async { 
                                          try 
                                              try 
                                                  let! result = computation
                                                  o.OnNext(result)
                                              with e -> o.OnError(e)
                                              o.OnCompleted()
                                          finally
                                              cancelOrDispose false
                                      }
                                  
                                  Async.Start(wrapper, cts.Token)
                                  Action(fun () -> cancelOrDispose true)))

module Algorithm4 = 
    let private createWithTask subscribe = 
        Observable.Create(Func<IObserver<'Result>, CancellationToken, Task> subscribe)
    
    let ofAsync (comp : Async<'T>) = 
        let subscribe (observer : IObserver<'T>) (ct : CancellationToken) = 
            let computation = 
                async { 
                    let! result = comp |> Async.Catch
                    match result with
                    | Choice1Of2 result -> observer.OnNext(result)
                    | Choice2Of2 exn -> observer.OnError exn
                }
            Async.StartAsTask(computation, cancellationToken = ct) :> Task
        createWithTask subscribe

module FsharpAsyncToObservableTests = 
    open System.Diagnostics
    
    let obsMerge (count : int) (obs : IObservable<_>) = Observable.Merge(obs, count)
    
    let GCReset() = 
        GC.Collect()
        GC.WaitForPendingFinalizers()
    
    let asyncWork value = 
        async { 
            for i = 0 to 10 do
                ()
            return value
        }
    
    let xs = Observable.Range(0, 10000)
    
    let createObs xs numberOfConcurrent asyncToObservable = 
        xs
        |> Observable.map asyncWork
        |> Observable.map asyncToObservable
        |> obsMerge numberOfConcurrent
    
    let createObs' = createObs xs
    
    module Seq = 
        let doSideEffect f s = 
            seq { 
                for x in s do
                    f x
                    yield x
            }
    
    let getEllapsedTimeForFunction (sw : Stopwatch) (f : unit -> IObservable<_>) = 
        GCReset()
        sw.Restart()
        f().Wait() |> ignore
        sw.Stop()
        sw.ElapsedMilliseconds
    
    let runAndPrintAverages times name f = 
        [ 1..times ]
        |> Seq.map (fun _ -> f())
        |> Seq.doSideEffect (fun time -> printfn "%s ms Ellapsed : %d" name time)
        |> Seq.map double
        |> Seq.average
        |> printfn "%s sum ms Ellapsed : %f" name
        printfn ""
    
    [<Fact>]
    let ``Benchmark Async to Observable patterns``() = 
        let stopWatch = new Stopwatch()
        let getEllapsedTimeForFunction' = getEllapsedTimeForFunction stopWatch
        for concurrentOperations = 1 to Environment.ProcessorCount do
            let createObs'' = createObs' concurrentOperations
            printfn ""
            printfn "Running with %d concurrent async" concurrentOperations
            printfn ""
            let alg1Obs() = createObs'' Algorithm1.ofAsync
            let alg2Obs() = createObs'' Algorithm2.ofAsync
            let alg3Obs() = createObs'' Algorithm3.ofAsync
            let alg4Obs() = createObs'' Algorithm4.ofAsync
            let seperateTimes = 10
            runAndPrintAverages seperateTimes "Algorithm1" (fun _ -> getEllapsedTimeForFunction' alg1Obs)
            runAndPrintAverages seperateTimes "Algorithm2" (fun _ -> getEllapsedTimeForFunction' alg2Obs)
            runAndPrintAverages seperateTimes "Algorithm3" (fun _ -> getEllapsedTimeForFunction' alg3Obs)
            runAndPrintAverages seperateTimes "Algorithm4" (fun _ -> getEllapsedTimeForFunction' alg4Obs)
        ()
(* 

Output on my machine:

    Running with 1 concurrent async
    
    Algorithm1 ms Ellapsed : 96
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 57
    Algorithm1 sum ms Ellapsed : 61.400000
    
    Algorithm2 ms Ellapsed : 233
    Algorithm2 ms Ellapsed : 204
    Algorithm2 ms Ellapsed : 268
    Algorithm2 ms Ellapsed : 267
    Algorithm2 ms Ellapsed : 261
    Algorithm2 ms Ellapsed : 264
    Algorithm2 ms Ellapsed : 274
    Algorithm2 ms Ellapsed : 254
    Algorithm2 ms Ellapsed : 259
    Algorithm2 ms Ellapsed : 265
    Algorithm2 sum ms Ellapsed : 254.900000
    
    Algorithm3 ms Ellapsed : 142
    Algorithm3 ms Ellapsed : 126
    Algorithm3 ms Ellapsed : 127
    Algorithm3 ms Ellapsed : 130
    Algorithm3 ms Ellapsed : 129
    Algorithm3 ms Ellapsed : 125
    Algorithm3 ms Ellapsed : 128
    Algorithm3 ms Ellapsed : 129
    Algorithm3 ms Ellapsed : 132
    Algorithm3 ms Ellapsed : 128
    Algorithm3 sum ms Ellapsed : 129.600000
    
    Algorithm4 ms Ellapsed : 246
    Algorithm4 ms Ellapsed : 244
    Algorithm4 ms Ellapsed : 241
    Algorithm4 ms Ellapsed : 247
    Algorithm4 ms Ellapsed : 239
    Algorithm4 ms Ellapsed : 247
    Algorithm4 ms Ellapsed : 246
    Algorithm4 ms Ellapsed : 245
    Algorithm4 ms Ellapsed : 247
    Algorithm4 ms Ellapsed : 255
    Algorithm4 sum ms Ellapsed : 245.700000
    
    
    Running with 2 concurrent async
    
    Algorithm1 ms Ellapsed : 61
    Algorithm1 ms Ellapsed : 60
    Algorithm1 ms Ellapsed : 60
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 62
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 58
    Algorithm1 sum ms Ellapsed : 59.600000
    
    Algorithm2 ms Ellapsed : 241
    Algorithm2 ms Ellapsed : 240
    Algorithm2 ms Ellapsed : 240
    Algorithm2 ms Ellapsed : 241
    Algorithm2 ms Ellapsed : 242
    Algorithm2 ms Ellapsed : 243
    Algorithm2 ms Ellapsed : 233
    Algorithm2 ms Ellapsed : 240
    Algorithm2 ms Ellapsed : 237
    Algorithm2 ms Ellapsed : 234
    Algorithm2 sum ms Ellapsed : 239.100000
    
    Algorithm3 ms Ellapsed : 94
    Algorithm3 ms Ellapsed : 94
    Algorithm3 ms Ellapsed : 91
    Algorithm3 ms Ellapsed : 96
    Algorithm3 ms Ellapsed : 90
    Algorithm3 ms Ellapsed : 90
    Algorithm3 ms Ellapsed : 92
    Algorithm3 ms Ellapsed : 89
    Algorithm3 ms Ellapsed : 95
    Algorithm3 ms Ellapsed : 90
    Algorithm3 sum ms Ellapsed : 92.100000
    
    Algorithm4 ms Ellapsed : 222
    Algorithm4 ms Ellapsed : 219
    Algorithm4 ms Ellapsed : 224
    Algorithm4 ms Ellapsed : 226
    Algorithm4 ms Ellapsed : 221
    Algorithm4 ms Ellapsed : 225
    Algorithm4 ms Ellapsed : 228
    Algorithm4 ms Ellapsed : 223
    Algorithm4 ms Ellapsed : 229
    Algorithm4 ms Ellapsed : 222
    Algorithm4 sum ms Ellapsed : 223.900000
    
    
    Running with 3 concurrent async
    
    Algorithm1 ms Ellapsed : 62
    Algorithm1 ms Ellapsed : 60
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 60
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 60
    Algorithm1 sum ms Ellapsed : 59.400000
    
    Algorithm2 ms Ellapsed : 208
    Algorithm2 ms Ellapsed : 208
    Algorithm2 ms Ellapsed : 211
    Algorithm2 ms Ellapsed : 212
    Algorithm2 ms Ellapsed : 207
    Algorithm2 ms Ellapsed : 210
    Algorithm2 ms Ellapsed : 211
    Algorithm2 ms Ellapsed : 202
    Algorithm2 ms Ellapsed : 204
    Algorithm2 ms Ellapsed : 205
    Algorithm2 sum ms Ellapsed : 207.800000
    
    Algorithm3 ms Ellapsed : 59
    Algorithm3 ms Ellapsed : 58
    Algorithm3 ms Ellapsed : 57
    Algorithm3 ms Ellapsed : 53
    Algorithm3 ms Ellapsed : 59
    Algorithm3 ms Ellapsed : 59
    Algorithm3 ms Ellapsed : 61
    Algorithm3 ms Ellapsed : 65
    Algorithm3 ms Ellapsed : 58
    Algorithm3 ms Ellapsed : 60
    Algorithm3 sum ms Ellapsed : 58.900000
    
    Algorithm4 ms Ellapsed : 201
    Algorithm4 ms Ellapsed : 200
    Algorithm4 ms Ellapsed : 200
    Algorithm4 ms Ellapsed : 198
    Algorithm4 ms Ellapsed : 202
    Algorithm4 ms Ellapsed : 200
    Algorithm4 ms Ellapsed : 199
    Algorithm4 ms Ellapsed : 198
    Algorithm4 ms Ellapsed : 200
    Algorithm4 ms Ellapsed : 197
    Algorithm4 sum ms Ellapsed : 199.500000
    
    
    Running with 4 concurrent async
    
    Algorithm1 ms Ellapsed : 61
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 60
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 59
    Algorithm1 sum ms Ellapsed : 59.300000
    
    Algorithm2 ms Ellapsed : 181
    Algorithm2 ms Ellapsed : 179
    Algorithm2 ms Ellapsed : 182
    Algorithm2 ms Ellapsed : 180
    Algorithm2 ms Ellapsed : 182
    Algorithm2 ms Ellapsed : 183
    Algorithm2 ms Ellapsed : 178
    Algorithm2 ms Ellapsed : 182
    Algorithm2 ms Ellapsed : 181
    Algorithm2 ms Ellapsed : 177
    Algorithm2 sum ms Ellapsed : 180.500000
    
    Algorithm3 ms Ellapsed : 50
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 50
    Algorithm3 ms Ellapsed : 49
    Algorithm3 ms Ellapsed : 50
    Algorithm3 ms Ellapsed : 49
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 50
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 47
    Algorithm3 sum ms Ellapsed : 48.800000
    
    Algorithm4 ms Ellapsed : 179
    Algorithm4 ms Ellapsed : 179
    Algorithm4 ms Ellapsed : 180
    Algorithm4 ms Ellapsed : 187
    Algorithm4 ms Ellapsed : 180
    Algorithm4 ms Ellapsed : 180
    Algorithm4 ms Ellapsed : 183
    Algorithm4 ms Ellapsed : 178
    Algorithm4 ms Ellapsed : 179
    Algorithm4 ms Ellapsed : 178
    Algorithm4 sum ms Ellapsed : 180.300000
    
    
    Running with 5 concurrent async
    
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 57
    Algorithm1 sum ms Ellapsed : 56.800000
    
    Algorithm2 ms Ellapsed : 168
    Algorithm2 ms Ellapsed : 164
    Algorithm2 ms Ellapsed : 160
    Algorithm2 ms Ellapsed : 162
    Algorithm2 ms Ellapsed : 161
    Algorithm2 ms Ellapsed : 160
    Algorithm2 ms Ellapsed : 166
    Algorithm2 ms Ellapsed : 167
    Algorithm2 ms Ellapsed : 165
    Algorithm2 ms Ellapsed : 166
    Algorithm2 sum ms Ellapsed : 163.900000
    
    Algorithm3 ms Ellapsed : 50
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 50
    Algorithm3 ms Ellapsed : 49
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 51
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 49
    Algorithm3 sum ms Ellapsed : 48.700000
    
    Algorithm4 ms Ellapsed : 162
    Algorithm4 ms Ellapsed : 160
    Algorithm4 ms Ellapsed : 162
    Algorithm4 ms Ellapsed : 155
    Algorithm4 ms Ellapsed : 159
    Algorithm4 ms Ellapsed : 158
    Algorithm4 ms Ellapsed : 163
    Algorithm4 ms Ellapsed : 161
    Algorithm4 ms Ellapsed : 166
    Algorithm4 ms Ellapsed : 161
    Algorithm4 sum ms Ellapsed : 160.700000
    
    
    Running with 6 concurrent async
    
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 56
    Algorithm1 sum ms Ellapsed : 56.300000
    
    Algorithm2 ms Ellapsed : 146
    Algorithm2 ms Ellapsed : 148
    Algorithm2 ms Ellapsed : 142
    Algorithm2 ms Ellapsed : 154
    Algorithm2 ms Ellapsed : 159
    Algorithm2 ms Ellapsed : 154
    Algorithm2 ms Ellapsed : 147
    Algorithm2 ms Ellapsed : 147
    Algorithm2 ms Ellapsed : 144
    Algorithm2 ms Ellapsed : 152
    Algorithm2 sum ms Ellapsed : 149.300000
    
    Algorithm3 ms Ellapsed : 51
    Algorithm3 ms Ellapsed : 50
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 56
    Algorithm3 ms Ellapsed : 52
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 52
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 52
    Algorithm3 ms Ellapsed : 50
    Algorithm3 sum ms Ellapsed : 50.500000
    
    Algorithm4 ms Ellapsed : 153
    Algorithm4 ms Ellapsed : 157
    Algorithm4 ms Ellapsed : 152
    Algorithm4 ms Ellapsed : 168
    Algorithm4 ms Ellapsed : 155
    Algorithm4 ms Ellapsed : 155
    Algorithm4 ms Ellapsed : 153
    Algorithm4 ms Ellapsed : 152
    Algorithm4 ms Ellapsed : 154
    Algorithm4 ms Ellapsed : 154
    Algorithm4 sum ms Ellapsed : 155.300000
    
    
    Running with 7 concurrent async
    
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 55
    Algorithm1 ms Ellapsed : 56
    Algorithm1 ms Ellapsed : 58
    Algorithm1 sum ms Ellapsed : 56.700000
    
    Algorithm2 ms Ellapsed : 132
    Algorithm2 ms Ellapsed : 124
    Algorithm2 ms Ellapsed : 126
    Algorithm2 ms Ellapsed : 125
    Algorithm2 ms Ellapsed : 127
    Algorithm2 ms Ellapsed : 124
    Algorithm2 ms Ellapsed : 126
    Algorithm2 ms Ellapsed : 130
    Algorithm2 ms Ellapsed : 129
    Algorithm2 ms Ellapsed : 127
    Algorithm2 sum ms Ellapsed : 127.000000
    
    Algorithm3 ms Ellapsed : 50
    Algorithm3 ms Ellapsed : 52
    Algorithm3 ms Ellapsed : 49
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 46
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 47
    Algorithm3 sum ms Ellapsed : 48.200000
    
    Algorithm4 ms Ellapsed : 149
    Algorithm4 ms Ellapsed : 147
    Algorithm4 ms Ellapsed : 146
    Algorithm4 ms Ellapsed : 151
    Algorithm4 ms Ellapsed : 153
    Algorithm4 ms Ellapsed : 147
    Algorithm4 ms Ellapsed : 151
    Algorithm4 ms Ellapsed : 146
    Algorithm4 ms Ellapsed : 156
    Algorithm4 ms Ellapsed : 146
    Algorithm4 sum ms Ellapsed : 149.200000
    
    
    Running with 8 concurrent async
    
    Algorithm1 ms Ellapsed : 61
    Algorithm1 ms Ellapsed : 62
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 61
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 59
    Algorithm1 ms Ellapsed : 57
    Algorithm1 ms Ellapsed : 58
    Algorithm1 ms Ellapsed : 57
    Algorithm1 sum ms Ellapsed : 59.000000
    
    Algorithm2 ms Ellapsed : 119
    Algorithm2 ms Ellapsed : 119
    Algorithm2 ms Ellapsed : 119
    Algorithm2 ms Ellapsed : 119
    Algorithm2 ms Ellapsed : 122
    Algorithm2 ms Ellapsed : 116
    Algorithm2 ms Ellapsed : 127
    Algorithm2 ms Ellapsed : 117
    Algorithm2 ms Ellapsed : 122
    Algorithm2 ms Ellapsed : 119
    Algorithm2 sum ms Ellapsed : 119.900000
    
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 46
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 49
    Algorithm3 ms Ellapsed : 47
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 48
    Algorithm3 ms Ellapsed : 48
    Algorithm3 sum ms Ellapsed : 47.500000
    
    Algorithm4 ms Ellapsed : 157
    Algorithm4 ms Ellapsed : 150
    Algorithm4 ms Ellapsed : 145
    Algorithm4 ms Ellapsed : 151
    Algorithm4 ms Ellapsed : 147
    Algorithm4 ms Ellapsed : 152
    Algorithm4 ms Ellapsed : 148
    Algorithm4 ms Ellapsed : 150
    Algorithm4 ms Ellapsed : 144
    Algorithm4 ms Ellapsed : 148
    Algorithm4 sum ms Ellapsed : 149.200000

*)
